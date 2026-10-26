using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using LOMAssistant;

internal static class FlagAudit
{
    private static readonly Regex Han = new Regex("[一-鿿]");
    private static string T(string value) => UiText.Translate(value, UiText.Korean);
    private static string Cell(string text) => text.Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n");
    public static void Run(string flagsFile, string conditionsFile)
    {
        string folder = AppContext.BaseDirectory;
        using var document = JsonDocument.Parse(File.ReadAllText(flagsFile));
        var rows = new List<string> { "id\tasset\tname_status\tfull_description_status\toriginal_name\ttranslated_name\toriginal_description\ttranslated_description" };
        var failures = new List<string> { "id\tcontext\tinput\toutput" };
        int total = 0, described = 0, nameMissing = 0, descriptionMissing = 0, variants = 0, variantFailures = 0;
        var table = UiTextKorean.Flags.ToDictionary(x => x[0], x => x[1]);
        int absentFromTable = 0;
        foreach (var item in document.RootElement.GetProperty("flags").EnumerateArray())
        {
            total++;
            string id = item.GetProperty("id").GetString();
            string original = item.GetProperty("simplified").GetString();
            if (string.IsNullOrWhiteSpace(original)) continue;
            described++;
            // Mirror DataHelper.GetFlagName and StoryReviewPanel.Stats separately.
            string name = original.Split('\n')[0].TrimEnd('\r');
            string description = original.Replace("\n", " ").Trim();
            string translatedName = T(name), translatedDescription = T(description);
            bool missingName = Han.IsMatch(translatedName), missingDescription = Han.IsMatch(translatedDescription);
            if (missingName) nameMissing++;
            if (missingDescription) descriptionMissing++;
            if (!table.ContainsKey(name.Trim())) absentFromTable++;
            rows.Add(string.Join("\t", new[] { id, item.GetProperty("asset").GetString(), missingName ? "CHINESE_REMAINS" : "OK", missingDescription ? "CHINESE_REMAINS" : "OK", name, translatedName, description, translatedDescription }.Select(Cell)));
            var examples = new Dictionary<string, string>
            {
                ["condition"] = name + " (" + id + ") 需>=1",
                ["coloured_condition"] = "<color=#FF0000>" + name + "</color> (" + id + ") 需>=1",
                ["notification"] = "【旗标】" + name + "  0 → 1",
                ["choice_set"] = "设置" + name + "=1",
                ["choice_add"] = name + " +1",
                ["quoted_lookup"] = "「" + name + "」在哪些剧情里用到",
            };
            foreach (var pair in examples)
            {
                variants++;
                string translated = T(pair.Value);
                bool failed = Han.IsMatch(translated);
                if (pair.Key.Contains("condition") && (!translated.Contains("(" + id + ")") || !translated.Contains(">=1"))) failed = true;
                if (pair.Key == "coloured_condition" && (!translated.Contains("<color=#FF0000>") || !translated.Contains("</color>"))) failed = true;
                if (table.TryGetValue(name.Trim(), out string expected) && !translated.Contains(expected)) failed = true;
                if (failed)
                {
                    variantFailures++;
                    failures.Add(string.Join("\t", new[] { id, pair.Key, pair.Value, translated }.Select(Cell)));
                }
            }
        }
        File.WriteAllLines(Path.Combine(folder, "flag-translations.tsv"), rows);
        File.WriteAllLines(Path.Combine(folder, "flag-context-failures.tsv"), failures);
        int liveTotal = 0;
        var liveMissing = new List<string> { "section\tkey\tinput\toutput" };
        if (File.Exists(conditionsFile))
        {
            using var live = JsonDocument.Parse(File.ReadAllText(conditionsFile));
            foreach (string section in new[] { "Conditions", "Switches" })
                foreach (var entry in live.RootElement.GetProperty(section).EnumerateObject())
                    foreach (string text in entry.Value.ValueKind == JsonValueKind.Array ? entry.Value.EnumerateArray().Select(x => x.GetString()) : new[] { entry.Value.GetString() })
                    {
                        liveTotal++;
                        string translated = T(text);
                        // Strict scan: do not conceal untranslated text in arbitrary parentheses.
                        if (Han.IsMatch(translated)) liveMissing.Add(string.Join("\t", new[] { section, entry.Name, text, translated }.Select(Cell)));
                    }
        }
        File.WriteAllLines(Path.Combine(folder, "live-condition-failures.tsv"), liveMissing);
        string summary = $"Flag assets: {total}; with descriptions: {described}; without descriptions: {total-described}\n" +
            $"Names with Chinese remaining: {nameMissing}; F9 full descriptions with Chinese remaining: {descriptionMissing}\n" +
            $"Names absent from exact flag table: {absentFromTable}\n" +
            $"Flag display contexts: {variants}; failed: {variantFailures}\n" +
            $"Actual cached game conditions: {liveTotal}; Chinese remaining (strict): {liveMissing.Count-1}\n";
        File.WriteAllText(Path.Combine(folder, "flag-audit-summary.txt"), summary);
        Console.WriteLine(summary);
    }
}
