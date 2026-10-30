using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LOMAssistant.Config;
using Mortal.Story;
using OBB.Framework.Data;
using OBB.Framework.Utils;

namespace LOMAssistant;

/// <summary>
/// Display-only Korean names for random event choices. The game uses Chinese
/// developer notes for these labels; official Korean dialogue keys do not map
/// directly to them. The original identifiers and game decisions are untouched.
/// </summary>
internal static class EventNameKorean
{
    private static readonly EventNameTable Overrides = new EventNameTable();
    private static readonly SortedSet<string> Seen = new SortedSet<string>(StringComparer.Ordinal);
    private static bool loaded;
    private static PositionResultConfig lastExported;
    private static readonly string Folder = Path.GetDirectoryName(typeof(EventNameKorean).Assembly.Location) ?? ".";
    private static readonly string OverridesFile = Path.Combine(Folder, "event-names-ko.tsv");
    private static readonly string AuditFile = Path.Combine(Folder, "event-names-to-translate.tsv");

    internal static string Raw(PositionEventData data)
    {
        if (data == null) return string.Empty;
        StoryMappingItem mapping = data.GetFieldValue<StoryMappingItem>("_config");
        string text = mapping != null ? mapping.name : null;
        string note = data.GetFieldValue<string>("_devNote");
        if (!string.IsNullOrWhiteSpace(note)) text = note;
        return text ?? data.name ?? string.Empty;
    }

    private static string Clean(string text)
    {
        return (text ?? "").Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
    }

    private static void Load()
    {
        if (loaded) return;
        loaded = true;
        try
        {
            using (Stream embedded = typeof(EventNameKorean).Assembly.GetManifestResourceStream("LOMAssistant.event-names-ko.tsv"))
                if (embedded != null)
                    using (var reader = new StreamReader(embedded, Encoding.UTF8)) ReadTable(reader);
            // An optional user file overrides the embedded defaults; a normal upstream
            // installation needs only the DLL and does not depend on a loose TSV file.
            if (File.Exists(OverridesFile))
                using (var reader = new StreamReader(OverridesFile, Encoding.UTF8)) ReadTable(reader);
        }
        catch (Exception ex)
        {
            FileLogger.LogWarning("[한국어 이벤트명] 번역표 읽기 실패: " + ex.Message);
        }
    }

    private static void ReadTable(TextReader reader)
    {
        string line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal)) continue;
            string[] fields = line.TrimStart('\uFEFF').Split('\t');
            // IDs alone are reused in other event pools: preserve the original label too.
            if (fields.Length < 3 || string.IsNullOrWhiteSpace(fields[0]) || string.IsNullOrWhiteSpace(fields[2])) continue;
            Overrides.Add(fields[0], TextConverter.ToSimplified(Clean(fields[1])), fields[2].Trim());
        }
    }

    internal static string Resolve(PositionEventData data, string original)
    {
        if (TextConverter.DisplayLanguage != UiText.Korean) return original;
        Load();
        string id = data != null ? data.name : null;
        string text = TextConverter.ToSimplified(original ?? "");
        if (Overrides.TryGet(id, Clean(text), out string korean)) return korean;
        // Keep contextual overrides first; upstream adds defaults for new labels and P_* results.
        string upstream = UiEventNames.Display(original);
        if (upstream != null) return upstream;
        // Small, reviewed defaults; all other original names are exported for manual review.
        // Do not interpret or replace game IDs, random-event probability or selection keys.
        text = text.Replace("排队的", "대기 중인 ")
                   .Replace("大宋闲汉", "송나라 한량")
                   .Replace("买饮料", "음료 구매");
        if (text.StartsWith("P_", StringComparison.Ordinal) || text.StartsWith("p_", StringComparison.Ordinal))
            return "이벤트 [" + text + "]";
        return UiText.Translate(text, UiText.Korean);
    }

    /// <summary>
    /// Export available event IDs and raw developer names to TSV for translation.
    /// Called after game event tables become available; never modifies those tables.
    /// </summary>
    internal static void Export(PositionResultConfig config)
    {
        if (TextConverter.DisplayLanguage != UiText.Korean || config == null || ReferenceEquals(lastExported, config)) return;
        lastExported = config;
        try
        {
            if (config.List == null) return;
            int previous = Seen.Count;
            foreach (PositionResultData pool in config.List)
            {
                if (pool == null || pool.List == null) continue;
                foreach (PositionEventData data in pool.List)
                {
                    if (data == null || string.IsNullOrWhiteSpace(data.name)) continue;
                    Seen.Add(Clean(data.name) + "\t" + Clean(Raw(data)));
                }
            }
            if (Seen.Count == previous) return;
            var lines = new List<string> { "# event ID\tChinese developer label\tKorean translation (fill this column in event-names-ko.tsv)" };
            foreach (string item in Seen)
                lines.Add(item + "\t");
            File.WriteAllLines(AuditFile, lines, new UTF8Encoding(true));
        }
        catch (Exception ex)
        {
            FileLogger.LogWarning("[한국어 이벤트명] 번역 후보 목록 저장 실패: " + ex.Message);
        }
    }
}
