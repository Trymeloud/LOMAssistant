using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using LOMAssistant;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// Execute upstream and current code with the same inputs. No game/API calls.
internal static class NonKoreanChecks
{
    private const string Revision = "84b0527";

    private static string Source(string root, string file)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("show"); start.ArgumentList.Add(Revision + ":" + file);
        using var process = Process.Start(start);
        string text = process.StandardOutput.ReadToEnd(), error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new Exception("Cannot read upstream language baseline: " + error);
        return text;
    }

    internal static void Run(string root, Action<bool, string> check)
    {
        var trees = new List<SyntaxTree>();
        foreach (string name in new[] { "UiText", "UiTextEnglish", "UiQuestNames", "UiConditionNames", "UiFlagNames" })
            trees.Add(CSharpSyntaxTree.ParseText(Source(root, "LOMAssistant/" + name + ".cs").Replace("namespace LOMAssistant;", "namespace UpstreamLanguageBaseline;")));
        trees.Add(CSharpSyntaxTree.ParseText(Source(root, "LOMAssistant.Features.Ai/AiPrompts.cs").Replace("internal static class AiPrompts", "internal static class UpstreamAiPrompts")));
        trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, "LOMAssistant.Features.Ai/AiPrompts.cs"))));
        trees.Add(CSharpSyntaxTree.ParseText(@"
namespace UpstreamLanguageBaseline {
    internal static class XUnityTranslations { public static System.Collections.Generic.Dictionary<string,string> Load()=>new System.Collections.Generic.Dictionary<string,string>(); }
    internal static class JapanesePatchCompat { public const string NoticeText=""Japanese patch notice""; }
}
namespace LOMAssistant {
    internal static class UiText { public const string Korean=""Korean"", Japanese=""Japanese"", English=""English""; }
    internal static class TextConverter { public static string DisplayLanguage; public static string ForDisplay(string t)=>""display:""+t; }
    internal static class NativeKorean { public static bool Enabled=>TextConverter.DisplayLanguage==UiText.Korean; }
}
namespace LOMAssistant.Features.Ai {
    internal sealed class AiPersona { public string Id,Name; public object Heroine; public string KoreanPrompt(string s,bool fast)=>throw new System.Exception(""Korean persona entered from another language""); }
    internal static class KoreanAiPrompt { public const string LanguageRule=""Korean"",Summary=""summary""; public static string Spoiler(int i)=>""Korean""; public static string Assistant(string s)=>""Korean""; }
    internal static class AiReplyLanguage { public static string Apply(string s,string l,string id)=>throw new System.Exception(""Korean prompt entered from another language""); public static string KoreanRules(string id)=>throw new System.Exception(""Korean rewrite entered from another language""); }
}"));
        var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Distinct().Select(p => MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create("NonKoreanLanguageBaseline", trees, refs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        check(result.Success, "Non-Korean differential fixture compilation: " + string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var assembly = Assembly.Load(output.ToArray());
        var upstream = assembly.GetType("UpstreamLanguageBaseline.UiText");
        var translate = upstream.GetMethod("Translate");
        var inputs = new HashSet<string>(StringComparer.Ordinal) { "", " ", "일반 사용자 입력", "arbitrary input" };
        string[] values = { "7", "小师妹", "好感 7", "<color=green>5</color>", "甲 · 乙", "甲\n乙" };
        foreach (string field in new[] { "Table", "Terms" })
            foreach (string[] row in (string[][])upstream.GetField(field, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).GetValue(null))
            {
                inputs.Add(row[0]);
                if (row[0].Contains("{0}"))
                    foreach (string value in values) inputs.Add(Regex.Replace(row[0], @"\{\d+\}", _ => value));
            }
        foreach (var spec in new[] { ("UiQuestNames", "Table"), ("UiFlagNames", "Japanese"), ("UiConditionNames", "Japanese") })
        {
            var type = assembly.GetType("UpstreamLanguageBaseline." + spec.Item1);
            foreach (string[] row in (string[][])type.GetField(spec.Item2, BindingFlags.Static | BindingFlags.Public).GetValue(null))
            {
                inputs.Add(row[0]); inputs.Add(row[0] + " 需>=40 → 当前1");
                inputs.Add("设置" + row[0] + "=1");
            }
        }
        string Outcome(Func<string> call)
        {
            try { return "OK:" + call(); }
            catch (TargetInvocationException e) { return "ERR:" + e.InnerException.GetType().FullName; }
            catch (Exception e) { return "ERR:" + e.GetType().FullName; }
        }
        int count = 0;
        foreach (string language in new[] { "ChineseSimplified", "ChineseTraditional", UiText.Japanese, UiText.English, "unknown" })
            foreach (string input in inputs)
            {
                if ((count & 63) == 0) UiText.Translate("嘴力 需>=40", UiText.Korean);
                string expected = Outcome(() => (string)translate.Invoke(null, new object[] { input, language }));
                string actual = Outcome(() => UiText.Translate(input, language));
                check(actual == expected, "Non-Korean rendering changed: " + language + " / " + input + " / " + expected + " / " + actual);
                count++;
            }
        var originalPrompts = assembly.GetType("LOMAssistant.Features.Ai.UpstreamAiPrompts");
        var currentPrompts = assembly.GetType("LOMAssistant.Features.Ai.AiPrompts");
        var languageField = assembly.GetType("LOMAssistant.TextConverter").GetField("DisplayLanguage");
        int prompts = 0;
        foreach (string language in new[] { "ChineseSimplified", "ChineseTraditional", UiText.Japanese, UiText.English, "unknown" })
        {
            languageField.SetValue(null, language);
            foreach (string property in new[] { "Hero", "LanguageRule", "RewriteRule", "DeepRule", "FastRule", "RoundsUsedUp", "AlreadyLooked", "DigestNone", "DigestHead" })
            {
                check(Equals(originalPrompts.GetProperty(property).GetValue(null), currentPrompts.GetProperty(property).GetValue(null)), "Non-Korean prompt changed: " + language + "/" + property);
                prompts++;
            }
            for (int level = 0; level < 3; level++)
            {
                string spoiler = (string)originalPrompts.GetMethod("Spoiler").Invoke(null, new object[] { level });
                check(spoiler == (string)currentPrompts.GetMethod("Spoiler").Invoke(null, new object[] { level }), "Non-Korean spoiler setting changed");
                foreach (string method in new[] { "Assistant", "FastAssistant" })
                {
                    check(Equals(originalPrompts.GetMethod(method).Invoke(null, new object[] { spoiler }), currentPrompts.GetMethod(method).Invoke(null, new object[] { spoiler })), "Non-Korean assistant prompt changed: " + language + "/" + method);
                    prompts++;
                }
                prompts++;
            }
        }
        Console.WriteLine("Language isolation: " + count + " renderer cases and " + prompts + " prompt cases match upstream for non-Korean languages, with interleaved Korean lookups");
    }
}
