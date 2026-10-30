using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using LOMAssistant;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Inventory/probes, not a proof of runtime reachability. Never modify game files.
internal static class CompositionAudit
{
    private static readonly Regex Han = new Regex("[一-鿿]");
    private static bool IsComposition(SyntaxNode n) => n is BinaryExpressionSyntax b && b.IsKind(SyntaxKind.AddExpression)
        || n is InterpolatedStringExpressionSyntax
        || n is InvocationExpressionSyntax c && (c.Expression.ToString() is "string.Format" or "string.Join"
            || c.Expression is MemberAccessExpressionSyntax m && m.Name.Identifier.Text is "Append" or "AppendLine" or "AppendFormat")
        || n is ObjectCreationExpressionSyntax o && o.Type.ToString() == "StringBuilder";
    private static bool HasHan(SyntaxNode n) => n.DescendantNodesAndSelf().OfType<LiteralExpressionSyntax>()
        .Any(s => s.IsKind(SyntaxKind.StringLiteralExpression) && Han.IsMatch(s.Token.ValueText))
        || n.DescendantNodesAndSelf().OfType<InterpolatedStringTextSyntax>().Any(s => Han.IsMatch(s.TextToken.ValueText));
    private static string Cell(string s) => s.Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n");

    internal static void Run(string root)
    {
        var rows = new List<object>();
        var failures = new List<string> { "file\tline\tmethod\tcontext\tinput\ttranslated" };
        int files = 0, expressions = 0, protectedCount = 0, logs = 0, candidates = 0, probes = 0;
        foreach (string directory in Directory.GetDirectories(root, "LOMAssistant*"))
        foreach (string file in Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories))
        {
            files++;
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file));
            var syntax = tree.GetRoot();
            foreach (var e in syntax.DescendantNodes().OfType<ExpressionSyntax>().Where(n => IsComposition(n) && HasHan(n)
                && !n.Ancestors().TakeWhile(a => a is not StatementSyntax && a is not ArrowExpressionClauseSyntax).Any(IsComposition)))
            {
                expressions++;
                var method = e.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
                string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                int line = tree.GetLineSpan(e.Span).StartLinePosition.Line + 1;
                string context = Context(e);
                if (context == "LOG") logs++;
                else if (context == "LOCALIZED_FALLBACK") protectedCount++;
                var samples = new List<object>();
                var seen = new HashSet<string>();
                bool missing = false;
                if (context != "LOG" && context != "LOCALIZED_FALLBACK" && context != "INTERNAL_MATCH")
                for (int scenario = 0; scenario < 4; scenario++)
                {
                    string input = Render(e, method, scenario, 0, new HashSet<string>());
                    if (!seen.Add(input) || !Han.IsMatch(input)) continue;
                    string output = UiText.Translate(input, UiText.Korean);
                    bool remaining = Han.IsMatch(output);
                    probes++;
                    samples.Add(new { input, output, chineseRemaining = remaining });
                    if (remaining)
                    {
                        missing = true;
                        failures.Add(string.Join("\t", new[] { relative, line.ToString(), method?.Identifier.Text ?? "(field)", context, input, output }.Select(Cell)));
                    }
                }
                if (missing) candidates++;
                rows.Add(new { file = relative, line, method = method?.Identifier.Text, context, source = e.ToString(), samples, candidate = missing });
            }
        }
        string folder = AppContext.BaseDirectory;
        File.WriteAllText(Path.Combine(folder, "chinese-compositions.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllLines(Path.Combine(folder, "chinese-composition-candidates.tsv"), failures);
        var summary = new { sourceFiles = files, expressions, nativeFallbackExpressions = protectedCount, logExpressions = logs,
            generatedProbes = probes, remainingCandidateExpressions = candidates,
            limitation = "Synthetic probes and source inventory; candidates require manual verification of data flow and Korean runtime branches." };
        File.WriteAllText(Path.Combine(folder, "chinese-composition-summary.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Composition audit: {files} source files, {expressions} Chinese compositions, {probes} probes, {candidates} candidates (not confirmed UI failures)");
        ExactProbes();
    }

    private static void ExactProbes()
    {
        var rows = new List<string> { "case\tsource\tinput\ttranslated\tactual_display_path_output\tchinese_remaining\tpath" };
        void Probe(string name, string source, string input, string path = "display_translation")
        {
            string translated = UiText.Translate(input, UiText.Korean);
            string actual = path == "game_SayDialog_bypasses_ForDisplay" ? input : translated;
            rows.Add(string.Join("\t", new[] { name, source, input, translated, actual, Han.IsMatch(actual).ToString(), path }.Select(Cell)));
        }
        Probe("settings_warning_prefix", "StoryReviewPanel.Settings.cs:286", "【注意】호환성 안내");
        Probe("settings_action_mode", "StoryReviewPanel.Settings.cs:314", "行动点剩 1 点后不再消耗，用 ESC「空过」结束本旬。行动点不是「正常」时可能影响剧情触发（部分剧情、支线不触发或提前触发），请谨慎使用，切换前先存档。");
        Probe("settings_action_mode_fixed", "StoryReviewPanel.Settings.cs:315", "行动点不是「正常」时可能影响剧情触发（部分剧情、支线不触发或提前触发），请谨慎使用，切换前先存档。");
        Probe("compatibility_notice", "StoryWorkbenchCompat.cs:67", "检测到「剧情随行」，它的 F6、F8、F9 快捷键和本 Mod 撞了，本 Mod 的F8、F9热键已让给它（改成不绑定）。可以按 Tab 打开窗口再点对应的页，或者在设置页「热键」里另绑别的键。");
        Probe("upstream_update_banner", "StoryReviewPanel.Updates.cs:82", "<color=#A3302A>有新版本 3.2.2</color>：退出游戏后运行新版的「咖啡伴侣安装器 Installer.exe」更新，存档和设置不受影响。");
        Probe("branch_destination", "SwitchRandom.cs:207", "接到「소사매와 함께 수련…」");
        Probe("branch_combined_destination", "SwitchRandom.cs:207", "接到「소사매와 함께 수련…」；回到地图");
        Probe("replay_progress", "AnytimeSavePanel.cs:74", "正在读取随时存档 3/10");
        Probe("book_warning", "StoryReviewPanel.Stats.cs:623", "【注意】提前拿到会让六月后山事件出 bug，剧情需要时游戏会自己给。\n공식 한국어 비급 설명");
        Probe("book_bonus", "StoryReviewPanel.Stats.cs:690", "\n每层练成时的加成：\n✓ 第3层：구력 +1、심상 往中间 2");
        Probe("book_bonus_without_checkmark", "StoryReviewPanel.Stats.cs:690", "\n每层练成时的加成：\n第3层：구력 +1");
        Probe("duel_vertical", "DuelActionForecast.cs:104", "敌方预选\n\n捅人");
        Probe("duel_horizontal", "DuelActionForecast.cs:105", "敌方预选 捅人");
        Probe("duel_horizontal_adaptive", "DuelActionForecast.cs:114", "敌方预选 捅人 · 可能临场应变");
        Probe("martial_points_warning", "StoryReviewPanel.Stats.cs:713", "本层修炼点数，满 30 点升到下一层，和在修炼界面升层一样加上这层的加成；多的点数算到下一层。\n只能升层不能降层：游戏没有撤回加成的办法，减点只减本层点数。");
        Probe("save_new_slot", "SaveBrowser.cs:762", "存在空栏位 3");
        Probe("save_detail", "SaveBrowser.cs:921", "手动存档 · 3\n存于 2026/10/30 15:30");
        Probe("quick_save_memo_tooltip", "StoryReviewPanel.Anytime.cs:335", "이전 대사\n\n备注：분기 확인용 저장");
        Probe("jump_title", "StoryReviewPanel.Jump.cs:39", "跳到「무림맹을 첫 공격에 무려 백…」");
        Probe("jump_mission", "StoryReviewPanel.Jump.cs:49", "任务：당문 회의（主线）");
        Probe("jump_intro", "StoryReviewPanel.Jump.cs:44", "<color=#A3302A><b>正常游玩不建议这样做。</b></color>中间的剧情没有真的演过，只按剧本把一路上会设的旗标、剧情记录、任务进度、物品、好感和数值补上，有些状态补不全，之后可能出现对话对不上、分支判断出错，甚至卡住。跳之前会先存一份随时存档，跳过去以后可以回到跳转前。");
        Probe("jump_bond_warning", "StoryReviewPanel.Jump.cs:117", "<color=#A3302A>已经和소사매结缘：她的结缘旗标不会清掉，等于两位都结缘了，之后的剧情可能对不上</color>");
        Probe("jump_fork_option", "StoryJump.cs:399", "전투 후　→ 主线 → 당문 회의");
        Probe("jump_legacy_dice_reproduction", "v3.2.1-ko.1 StoryJump.cs:302", "· 「무림맹을 첫 공격에 무려 백…」这一步要靠掷骰，按选的这条路补", "old_source_reproduction_only");
        foreach (var key in new[] { KoreanStoryMessage.JumpDiceNote, KoreanStoryMessage.JumpBattleNote, KoreanStoryMessage.JumpUnreachableNote,
            KoreanStoryMessage.JumpUnlinkedNote, KoreanStoryMessage.JumpPastNote })
            Probe("native_" + key, "StoryJump.cs ko.2", "· " + KoreanStoryText.Get(key, "무림맹을 첫 공격에 무려 백…"), "native_korean");
        Probe("skip_game_dialog_busy", "SkipActionPatch.cs:92", "眼下还有要事在身，不能虚度光阴。", "game_SayDialog_bypasses_ForDisplay");
        Probe("skip_game_dialog_late", "SkipActionPatch.cs:97", "這會兒有些晚了，改日再說吧。", "game_SayDialog_bypasses_ForDisplay");
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "chinese-exact-display-probes.tsv"), rows);
        Console.WriteLine("Exact display probes: " + (rows.Count - 1) + " concrete compositions (including fixed/native controls and game-dialog bypasses)");
    }

    private static string Context(ExpressionSyntax e)
    {
        foreach (var call in e.Ancestors().OfType<InvocationExpressionSyntax>())
        {
            string name = call.Expression.ToString();
            if (name.StartsWith("FileLogger.") || new[] { ".LogWarning", ".LogError", ".LogInfo", ".LogDebug", ".LogMessage" }.Any(name.EndsWith)) return "LOG";
            if (name is "AiText.T" or "StoryUi.Text" or "NativeKorean.Text") return "LOCALIZED_FALLBACK";
            if (name.StartsWith("Regex.") || name.Contains(".Contains") || name.EndsWith(".StartsWith") || name.EndsWith(".IndexOf")) return "INTERNAL_MATCH";
            if (name.StartsWith("GUI.") || name.StartsWith("GUILayout.") || name == "Hover" || name == "new GUIContent") return "IMGUI";
            if (name.EndsWith("DisplayMessageText")) return "GAME_NOTICE";
        }
        foreach (var conditional in e.Ancestors().OfType<ConditionalExpressionSyntax>())
            if (NativeCondition(conditional.Condition) && conditional.WhenFalse.Span.Contains(e.Span)) return "LOCALIZED_FALLBACK";
        // Only an unconditional early return in the same statement container proves the remainder is a fallback.
        foreach (var statement in e.Ancestors().OfType<StatementSyntax>())
        {
            var siblings = statement.Parent?.ChildNodes().OfType<StatementSyntax>().TakeWhile(s => s != statement);
            if (siblings != null && siblings.OfType<IfStatementSyntax>().Any(i => NativeCondition(i.Condition)
                && i.Else == null && (i.Statement is ReturnStatementSyntax || i.Statement is BlockSyntax b && b.Statements.LastOrDefault() is ReturnStatementSyntax)))
                return "LOCALIZED_FALLBACK";
        }
        var assignment = e.Ancestors().OfType<AssignmentExpressionSyntax>().FirstOrDefault();
        if (assignment?.Left.ToString().EndsWith(".text") == true) return "GAME_TEXT";
        return "COMPOSED_VALUE";
    }
    private static bool NativeCondition(ExpressionSyntax e) => e.ToString() is "NativeKorean.Enabled" or "korean"
        || e.ToString() == "TextConverter.DisplayLanguage == UiText.Korean";

    private static string Render(ExpressionSyntax e, MethodDeclarationSyntax method, int scenario, int depth, HashSet<string> resolving)
    {
        if (depth > 9) return Unknown(e, scenario);
        string R(ExpressionSyntax x) => Render(x, method, scenario, depth + 1, new HashSet<string>(resolving));
        if (e is LiteralExpressionSyntax literal) return literal.IsKind(SyntaxKind.NullLiteralExpression) ? "" : literal.Token.ValueText;
        if (e is ParenthesizedExpressionSyntax p) return R(p.Expression);
        if (e is BinaryExpressionSyntax add && add.IsKind(SyntaxKind.AddExpression))
        {
            bool hasString = add.DescendantNodes().OfType<LiteralExpressionSyntax>().Any(l => l.IsKind(SyntaxKind.StringLiteralExpression));
            if (!hasString && Regex.IsMatch(add.ToString(), @"\b(?:i|j|k|n|r|rounds|cursor|target|count)\b|\.Count|\.Length|\.Value")) return scenario < 2 ? "3" : "42";
            return R(add.Left) + R(add.Right);
        }
        if (e is BinaryExpressionSyntax coalesce && coalesce.IsKind(SyntaxKind.CoalesceExpression)) return R(coalesce.Left);
        if (e is ConditionalExpressionSyntax conditional) return R(scenario % 2 == 0 ? conditional.WhenTrue : conditional.WhenFalse);
        if (e is ObjectCreationExpressionSyntax builder && builder.Type.ToString() == "StringBuilder")
            return builder.ArgumentList?.Arguments.Count > 0 ? R(builder.ArgumentList.Arguments[0].Expression) : "";
        if (e is InterpolatedStringExpressionSyntax interpolated) return string.Concat(interpolated.Contents.Select(c => c is InterpolatedStringTextSyntax t ? t.TextToken.ValueText : R(((InterpolationSyntax)c).Expression)));
        if (e is InvocationExpressionSyntax call)
        {
            string name = call.Expression.ToString();
            var args = call.ArgumentList.Arguments;
            if (name == "AiText.T" && args.Count > 1) return R(args[1].Expression);
            if (name is "StoryUi.Text" or "NativeKorean.Text") return "한국어 안내";
            if (name == "TextConverter.ForDisplay" && args.Count > 0) return UiText.Translate(R(args[0].Expression), UiText.Korean);
            if (name is "Short" or "Fit" or "OnPaper" or "PanelDeltas" or "RichText.Golden" or "RichText.Strip" && args.Count > 0) return R(args[0].Expression);
            if (call.Expression is MemberAccessExpressionSyntax append && append.Name.Identifier.Text is "Append" or "AppendLine")
                return (append.Expression is InvocationExpressionSyntax ? R(append.Expression) : "")
                    + (args.Count > 0 ? R(args[0].Expression) : "") + (append.Name.Identifier.Text == "AppendLine" ? "\n" : "");
            if (name == "string.Join" && args.Count > 0) return "소사매" + R(args[0].Expression) + "욱죽";
            if (name == "string.Format" && args.Count > 0)
            {
                string format = R(args[0].Expression);
                return Regex.Replace(format, @"\{(\d+)(?:[^{}]*)\}", m => int.TryParse(m.Groups[1].Value, out int i) && i + 1 < args.Count ? R(args[i+1].Expression) : "한국어 항목");
            }
        }
        if (e is IdentifierNameSyntax identifier && method != null && resolving.Add(identifier.Identifier.Text))
        {
            var local = method.DescendantNodes().OfType<VariableDeclaratorSyntax>()
                .LastOrDefault(v => v.Identifier.Text == identifier.Identifier.Text && v.Initializer != null && v.SpanStart < e.SpanStart);
            if (local != null) return Render(local.Initializer.Value, method, scenario, depth + 1, resolving);
        }
        return Unknown(e, scenario);
    }

    private static string Unknown(ExpressionSyntax e, int scenario)
    {
        string s = e.ToString().ToLowerInvariant();
        if (new[] { "count", "length", "value", "year", "month", "cursor", "target", "percent", "slots", "number", "remain" }.Any(s.Contains))
            return scenario < 2 ? "3" : "42";
        return scenario < 2 ? "소사매" : "무림맹을 첫 공격에 무려 백…";
    }
}
