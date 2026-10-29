using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using LOMAssistant.Features.Ai;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Newtonsoft.Json.Linq;

internal static class AiChecks
{
    internal static void Run(string root, Action<bool, string> check)
    {
        if (string.IsNullOrEmpty(root)) return;
        var han = new Regex("[一-鿿]");
        foreach (string id in new[] { "sister1", "girl1", "girl2", "girl4", "girl5", "girl6", "girl7", "girl8", "girl9", "brother1" })
        {
            string traits = KoreanAiPrompt.Traits(id);
            check(traits.Length > 60 && !han.IsMatch(traits), "Korean persona background: " + id);
            var voice = AiKoreanVoice.Find(id);
            check(voice != null && !han.IsMatch(voice.Name + voice.Address + voice.Style), "Official Korean voice retained: " + id);
            foreach (bool hints in new[] { true, false })
                for (int spoiler = 0; spoiler <= 2; spoiler++)
                    check(!han.IsMatch(KoreanAiPrompt.PersonaRules(voice.Name, hints, KoreanAiPrompt.Spoiler(spoiler), "normal(평소)")), "Native persona rules: " + id);
        }
        for (int mode = 0; mode <= 2; mode++)
            check(!han.IsMatch(KoreanAiPrompt.Assistant(KoreanAiPrompt.Spoiler(mode))), "Native assistant spoiler mode " + mode);
        check(!han.IsMatch(KoreanAiPrompt.Summary + KoreanAiPrompt.Deep), "Native compression/deep instructions");
        foreach (string marker in new[] { KoreanAiPrompt.HintMarker, KoreanAiPrompt.LegacyHintMarker })
        {
            string text = "대사\n" + marker + "첫 글자 보존";
            int index = KoreanAiPrompt.HintIndex(text);
            check(text.Substring(index + KoreanAiPrompt.HintLength(text, index)) == "첫 글자 보존", "Hint length compatibility: " + marker);
            check(KoreanAiPrompt.NormalizeHints(text, "힌트") == "대사\n힌트첫 글자 보존", "Streaming/export hint compatibility");
        }
        check(KoreanAiPrompt.HintIndex("일상 대화") == -1, "No hint in casual conversation");
        foreach (string name in new[] { "호감도·욱죽", "욱죽 호감도", "욱죽의 호감도", "好感·욱죽", "욱죽的好感度" })
            check(KoreanAiQuery.AffectionPerson(name) == "욱죽", "Korean and legacy affection name: " + name);
        check(KoreanAiQuery.AffectionPerson("심상") == "심상", "Unrelated stat is not affection");
        check(KoreanAiQuery.Contains("욱죽호감", "욱죽 호감도") && !KoreanAiQuery.Contains("위국호감", "욱죽"), "Korean focus matching preserves person");
        check(KoreanAiQuery.Contains("욱죽호감", "호감도·욱죽") && KoreanAiQuery.Contains("욱죽호감", "욱죽의 호감도"), "Affection focus supports prefix and possessive forms");
        check(KoreanAiQuery.EndingQuery("생사부 3번") == "生死簿3" && KoreanAiQuery.EndingQuery("결말 4") == "结局4"
            && KoreanAiQuery.EndingQuery("인간처럼 살다") == "인간처럼 살다", "Numbered Korean catalogue query preserves title queries");

        // Check every explicit source translation, including format placeholders.
        int fragments = 0;
        foreach (string file in new[] { "LOMAssistant.Features.Ai/AiChat.cs", "LOMAssistant.Features.Ai/AiPersonas.cs", "LOMAssistant.Features.Ai/AiKnowledge.cs", "LOMAssistant.Features.Ai/AiItems.cs", "LOMAssistant.Features.Ai/AiAnalysis.cs", "LOMAssistant.Features.Story/StoryReviewPanel.Ai.cs", "LOMAssistant.Features.Ai/LlmClient.cs" })
        {
            foreach (var call in Parse(Path.Combine(root, file)).DescendantNodes().OfType<InvocationExpressionSyntax>().Where(n => n.Expression.ToString() == "AiText.T"))
            {
                var args = call.ArgumentList.Arguments;
                if (args.Count != 2 || args[1].Expression is not LiteralExpressionSyntax ko) continue;
                fragments++;
                check(!han.IsMatch(ko.Token.ValueText), "Chinese in Korean AI fragment: " + file + ": " + ko.Token.ValueText);
                if (args[0].Expression is LiteralExpressionSyntax original)
                    check(Regex.Matches(original.Token.ValueText, @"\{\d+(?:[^}]*)\}").Select(m => m.Value).OrderBy(x => x)
                        .SequenceEqual(Regex.Matches(ko.Token.ValueText, @"\{\d+(?:[^}]*)\}").Select(m => m.Value).OrderBy(x => x)), "AI format arguments changed: " + original.Token.ValueText);
            }
        }
        check(fragments >= 470, "AI text audit unexpectedly lost coverage");
        CheckUnchangedLogic(root, check);
        RunGameFixtures(root, check);
        Console.WriteLine("AI: Korean prompts/voices, legacy hints, tool schema, branch totals and confirmation tested offline; no API requests");
    }

    private static SyntaxNode Parse(string path) => CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();
    private static string Baseline(string root, string file)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("show"); start.ArgumentList.Add("c381992:" + file);
        using var process = Process.Start(start);
        string text = process.StandardOutput.ReadToEnd(); string error = process.StandardError.ReadToEnd(); process.WaitForExit();
        if (process.ExitCode != 0) throw new Exception("Cannot read ko.2 source: " + error);
        return text;
    }
    private static void CheckUnchangedLogic(string root, Action<bool, string> check)
    {
        const string client = "LOMAssistant.Features.Ai/LlmClient.cs";
        string[] Tokens(SyntaxNode node) => node.DescendantTokens().Select(t => t.RawKind + ":" + t.ValueText).ToArray();
        var before = Tokens(CSharpSyntaxTree.ParseText(Baseline(root, client)).GetRoot());
        var after = Tokens(new OriginalText().Visit(Parse(Path.Combine(root, client))));
        check(before.SequenceEqual(after), "AI transport, endpoints, retries, limits and provider schemas changed beyond text");
        foreach (var spec in new[] {
            ("LOMAssistant.Features.Ai/AiPersonas.cs", new[] {"ReadLines", "Bonded", "Intros", "LatestCall", "ScriptOrder"}),
            ("LOMAssistant.Features.Story/StoryScripts.cs", new[] {"Parse"}),
            ("LOMAssistant.Features.Story/StoryUsage.cs", new[] {"Build", "AddConditions"}),
        })
        {
            var oldTree = CSharpSyntaxTree.ParseText(Baseline(root, spec.Item1)).GetRoot();
            var newTree = Parse(Path.Combine(root, spec.Item1));
            foreach (string name in spec.Item2)
            {
                string[] Body(SyntaxNode tree) => Tokens(tree.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.Text == name));
                check(Body(oldTree).SequenceEqual(Body(newTree)), "Game parsing or spoiler/history gate changed: " + spec.Item1 + ":" + name);
            }
        }
    }
    private sealed class OriginalText : CSharpSyntaxRewriter
    {
        public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node) => node.Expression.ToString() == "AiText.T"
            ? Visit(node.ArgumentList.Arguments[0].Expression) : base.VisitInvocationExpression(node);
    }
    private static string Members(string root, string file, params string[] names)
    {
        var tree = Parse(Path.Combine(root, file));
        return string.Join("\n", tree.DescendantNodes().Where(n => n is MethodDeclarationSyntax m && names.Contains(m.Identifier.Text)
            || n is FieldDeclarationSyntax f && f.Declaration.Variables.Any(v => names.Contains(v.Identifier.Text))
            || n is ClassDeclarationSyntax c && names.Contains(c.Identifier.Text)
            || n is EnumDeclarationSyntax e && names.Contains(e.Identifier.Text))
            .Where(n => !n.Ancestors().OfType<ClassDeclarationSyntax>().Any(c => names.Contains(c.Identifier.Text))).Select(n => n.ToFullString()));
    }

    private static void RunGameFixtures(string root, Action<bool, string> check)
    {
        string analysis = Members(root, "LOMAssistant.Features.Ai/AiAnalysis.cs", "MaxRoutes", "Numeric", "Route", "Routes", "Copy");
        string script = Members(root, "LOMAssistant.Features.Story/StoryScripts.cs", "StoryStep", "StoryExit", "StoryScript");
        string schema = Members(root, "LOMAssistant.Features.Ai/AiKnowledge.cs", "CanonicalTools", "Schema", "Current", "Clean", "CanonicalClean");
        string proposal = Members(root, "LOMAssistant.Features.Ai/AiChat.cs", "Kind", "Entry", "ProposalState", "Change", "Propose", "Resolve");
        string imports = "using System;using System.Collections.Generic;using System.Linq;using System.Text;using System.Text.RegularExpressions;using Newtonsoft.Json.Linq;using LOMAssistant;using LOMAssistant.Features.Story;";
        string code = imports
            + "namespace LOMAssistant.Features.Ai {public static class AiAnalysis {" + analysis + "public static string ExplainTime(string t)=>t;}"
            + "public static class AiKnowledge {" + schema + "} public static class AiChat {" + proposal + "}}"
            + "namespace LOMAssistant.Features.Story {" + script + "}"
            + File.ReadAllText(Path.Combine(root, "tools/ko-check/ai-fixtures.cs.txt"));
        var trees = new[] { CSharpSyntaxTree.ParseText(code), CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "LOMAssistant.Features.Ai/AiText.cs"))),
            CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "LOMAssistant.Features.Ai/KoreanAiQuery.cs"))),
            CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "LOMAssistant.Features.Ai/KoreanAiTools.cs"))) };
        var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
            .Append(typeof(JObject).Assembly.Location).Distinct().Select(p => MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create("AiOfflineFixtures", trees, refs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        var emitted = compilation.Emit(output);
        check(emitted.Success, "AI offline fixture compilation: " + string.Join("\n", emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var assembly = Assembly.Load(output.ToArray());
        assembly.GetType("AiFixtureTests").GetMethod("Run").Invoke(null, new object[] { check });
    }
}
