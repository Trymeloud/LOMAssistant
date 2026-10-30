using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using LOMAssistant;

internal static class StoryChecks
{
    internal static void Run(Action<bool, string> check, string flagsFile)
    {
        foreach (KoreanStoryMessage key in Enum.GetValues(typeof(KoreanStoryMessage)))
        {
            check(KoreanStoryText.Messages.ContainsKey(key), "Missing story message: " + key);
            string text = KoreanStoryText.Get(key, "소사매", 52, "M50009", 84);
            check(!Regex.IsMatch(text, "[一-鿿]"), "Chinese in story message: " + key);
            check(!Regex.IsMatch(text, @"\{\d+\}"), "Unexpanded story message: " + key);
        }
        check(KoreanStoryText.Date(0) == "1년차 1월 상순", "First date");
        check(KoreanStoryText.Date(2) == "1년차 1월 하순", "Month end");
        check(KoreanStoryText.Date(3) == "1년차 2월 상순", "Next month");
        check(KoreanStoryText.Date(35) == "1년차 12월 하순", "Year end");
        check(KoreanStoryText.Date(36) == "2년차 1월 상순", "Next year");
        check(KoreanStoryText.Date(107) == "3년차 12월 하순", "Third year end");
        check(KoreanStoryText.RouteClause("현재 시점 필요 >=1 → 현재 30 그리고 행동력 필요 >=1 → 현재 2 그리고 구력 필요 >=40 → 현재 50", _ => null)
            == "구력>=40", "Route clause omits dates/action counts and retains native comparator");
        check(KoreanStoryText.RouteClause("현재 시점 필요 >=1 → 현재 30\n또는 소사매 호감도 필요 >=60 → 현재 84", _ => null)
            == "", "Route clause must not confuse another OR group with the first group");
        check(KoreanStoryText.RouteClause("당신의 결정 (M80100) 필요 =4 → 현재 0", id => id == "M80100" ? "1. 다른 결정\n4. 서무림맹" : null)
            == "당신의 결정: 서무림맹", "Route flag value resolves the selected numbered explanation");
        check(KoreanStoryText.Period(null, null, null, false) == "언제든지", "Unlimited mission period");
        check(KoreanStoryText.Period(null, null, null, true) == "시기 미정", "Missing date must not imply an unlimited period");
        check(KoreanStoryText.Period(null, null, 2, true) == "매달 중순", "Monthly mission");
        check(KoreanStoryText.Period(36, 36, null, true) == "2년차 1월 상순", "Exact mission date");
        check(KoreanStoryText.Period(35, 36, null, true) == "1년차 12월 하순 ～ 2년차 1월 상순", "Mission range crossing year boundary");
        check(KoreanStoryText.ReadMark(null) == "" && KoreanStoryText.ReadMark(100) == "", "No unread marker for completed or uncounted dialogue");
        check(KoreanStoryText.ReadMark(0) == "〔안 읽음〕" && KoreanStoryText.ReadMark(52) == "〔52% 읽음〕", "Unread and partly read are distinct");
        foreach (var pair in new[] { ("G10004", "만남"), ("G00005", "고백"), ("G11004", "인연 성립"), ("S0014_02", "제자 입문"), ("M50024", "선행 단계") })
            check(KoreanStoryText.RomanceStep(pair.Item1) == pair.Item2, "Romance step meaning: " + pair.Item1);
        var steps = new[] { "선택 「싸운다」", "조건: 구력>=40" };
        string lost = KoreanStoryText.EndingWay(false, "산책", "1년차 2월 상순", "script_01", steps, true);
        check(lost == "곁가지 이야기 「산책」 (1년차 2월 상순): 선택 「싸운다」 → 조건: 구력>=40 → 결투에서 패배", "Ending must preserve branch order and loss requirement");
        check(steps.Length == 2 && steps[1] == "조건: 구력>=40", "Formatting must not mutate the source path");
        check(KoreanStoryText.EndingWay(true, null, "", "S9999_01", Array.Empty<string>(), false)
            == "스크립트 S9999_01: 해당 구간에 도달하면 발생", "Unassigned ending retains its script ID");
        check(KoreanStoryText.Get(KoreanStoryMessage.Achieved) != KoreanStoryText.Get(KoreanStoryMessage.ReadComplete)
            && KoreanStoryText.Get(KoreanStoryMessage.Visited) != KoreanStoryText.Get(KoreanStoryMessage.ReadComplete), "Cross-run achievement, dialogue reading and current-run visits must stay distinct");
        foreach (string[] row in UiQuestNames.Table)
            check(!Regex.IsMatch(KoreanLabels.MissionName(row[0]), "[一-鿿]"), "Reviewed mission label: " + row[0]);
        string missionFile = Path.Combine(Path.GetDirectoryName(flagsFile ?? "") ?? "", "game-mission-labels-3.0.9.json");
        if (File.Exists(missionFile))
        {
            var missing = new SortedSet<string>();
            using var doc = JsonDocument.Parse(File.ReadAllText(missionFile));
            foreach (var row in doc.RootElement.EnumerateArray())
            {
                string original = row.GetProperty("description").GetString().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
                if (original.Length == 0) original = row.GetProperty("name").GetString();
                string translated = KoreanLabels.MissionName(original);
                if (Regex.IsMatch(translated, "[一-鿿]")) missing.Add(row.GetProperty("id").GetString() + "\t" + original + "\t" + translated);
            }
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "untranslated-mission-labels.tsv"), missing);
            Console.WriteLine("Local mission metadata inspected: " + doc.RootElement.GetArrayLength() + "; Chinese label candidates: " + missing.Count);
            check(missing.Count == 0, "Untranslated local mission labels; see untranslated-mission-labels.tsv");
        }
    }
}
