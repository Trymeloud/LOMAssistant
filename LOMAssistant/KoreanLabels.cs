using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace LOMAssistant;

// Only names enter this compatibility boundary. Complete sentences never do.
internal static class KoreanLabels
{
    private static readonly Regex Random = new Regex(@"^随机值_(-?\d+)_(-?\d+)$");
    private static readonly Regex Han = new Regex(@"[一-鿿]");
    private static readonly Dictionary<string, string> Variables = MakeVariables();
    private static readonly Dictionary<string, string> Flags = LoadFlags();
    private static readonly Dictionary<string, string> Missions = UiQuestNames.Table.ToDictionary(row => row[0], row => row[1], StringComparer.Ordinal);
    internal static Action<string> ReportMissing { get; set; }
    private static readonly HashSet<string> Missing = new HashSet<string>(StringComparer.Ordinal);
    // Place IDs from the game; names checked against its official Korean menu.
    internal static readonly IReadOnlyDictionary<string, string> SectionPlaces = new Dictionary<string, string>
    {
        ["Section_01_00"] = "공동·휴식",
        ["Section_01_01"] = "공동·비천전",
        ["Section_01_02"] = "공동·탈백삼림",
        ["Section_01_03"] = "공동·철권항",
        ["Section_01_04"] = "공동·현공동",
        ["Section_01_05"] = "공동·탈백봉",
        ["Section_01_06"] = "공동·무색광장",
        ["Section_01_07"] = "공동·비천전(우소매 만남)",
        ["Section_01_08"] = "공동·학애",
        ["Section_02_00"] = "청성·휴식",
        ["Section_02_01"] = "청성·상풍관",
        ["Section_02_02"] = "청성·상청궁",
        ["Section_02_03"] = "청성·청성산진",
    };

    private static Dictionary<string, string> MakeVariables()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string[] row in UiTextKorean.Flags.Concat(UiTextKorean.Names)
            .Concat(UiTextKorean.Conditions).Concat(UiTextKorean.SupplementalConditions)) map[row[0]] = row[1];
        return map;
    }

    private static Dictionary<string, string> LoadFlags()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        using (Stream stream = typeof(KoreanLabels).Assembly.GetManifestResourceStream("LOMAssistant.flag-names-ko.tsv"))
        {
            if (stream == null) throw new InvalidOperationException("Missing flag-names-ko.tsv resource");
            using var reader = new StreamReader(stream);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0 || line.StartsWith("#")) continue;
                string[] cells = line.Split('\t');
                if (cells.Length != 2 || string.IsNullOrWhiteSpace(cells[0]) || string.IsNullOrWhiteSpace(cells[1])
                    || Han.IsMatch(cells[1]) || map.ContainsKey(cells[0]))
                    throw new InvalidOperationException("Invalid Korean flag resource: " + line);
                map.Add(cells[0], cells[1]);
            }
        }
        return map;
    }

    internal static string Flag(string id) => Flags.TryGetValue(id ?? "", out string value)
        ? value : KoreanText.Get(KoreanMessage.Flag, id);

    internal static bool HasFlag(string id) => Flags.ContainsKey(id ?? "");

    internal static string MissionName(string raw)
    {
        raw = (raw ?? "").Trim();
        if (Missions.TryGetValue(raw, out string exact)) return exact;
        return string.Join(" - ", raw.Split(new[] { " - " }, StringSplitOptions.None).Select(s => Variable(s.Trim())));
    }

    internal static string Variable(string name)
    {
        name = (name ?? "").Trim();
        Match random = Random.Match(name);
        if (random.Success) return KoreanText.Get(KoreanMessage.RandomRange, random.Groups[1].Value, random.Groups[2].Value);
        if (Variables.TryGetValue(name, out string value)) return value;
        // Individual game names still use the reviewed legacy dictionary while screens migrate.
        string translated = UiText.Translate(name, UiText.Korean);
        if (Han.IsMatch(translated) && Missing.Add(name)) ReportMissing?.Invoke(name);
        return translated;
    }
}
