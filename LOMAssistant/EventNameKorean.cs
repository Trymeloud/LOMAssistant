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
    private static readonly Dictionary<string, string> Overrides = new Dictionary<string, string>(StringComparer.Ordinal);
    private static readonly SortedDictionary<string, string> Seen = new SortedDictionary<string, string>(StringComparer.Ordinal);
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
            if (!File.Exists(OverridesFile)) return;
            foreach (string line in File.ReadAllLines(OverridesFile, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal)) continue;
                string[] fields = line.TrimStart('\uFEFF').Split('\t');
                // Format: event ID <tab> Chinese original <tab> Korean translation
                if (fields.Length < 3 || string.IsNullOrWhiteSpace(fields[0]) || string.IsNullOrWhiteSpace(fields[2])) continue;
                Overrides[fields[0]] = fields[2].Trim();
            }
        }
        catch (Exception ex)
        {
            FileLogger.LogWarning("[한국어 이벤트명] 번역표 읽기 실패: " + ex.Message);
        }
    }

    internal static string Resolve(PositionEventData data, string original)
    {
        if (TextConverter.DisplayLanguage != UiText.Korean) return original;
        Load();
        string id = data != null ? data.name : null;
        if (!string.IsNullOrEmpty(id) && Overrides.TryGetValue(id, out string korean)) return korean;

        string text = TextConverter.ToSimplified(original ?? "");
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
                    Seen[data.name] = Clean(Raw(data));
                }
            }
            if (Seen.Count == previous) return;
            var lines = new List<string> { "# event ID\tChinese developer label\tKorean translation (fill this column in event-names-ko.tsv)" };
            foreach (var item in Seen)
                lines.Add(Clean(item.Key) + "\t" + item.Value + "\t");
            File.WriteAllLines(AuditFile, lines, new UTF8Encoding(true));
        }
        catch (Exception ex)
        {
            FileLogger.LogWarning("[한국어 이벤트명] 번역 후보 목록 저장 실패: " + ex.Message);
        }
    }
}
