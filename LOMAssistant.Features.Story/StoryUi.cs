using System.Globalization;
using LOMAssistant;

namespace LOMAssistant.Features.Story;

// Presentation only. Mission IDs, analysis text, dates and unlock checks stay canonical.
internal static class StoryUi
{
    internal static string Text(KoreanStoryMessage key, string legacy, params object[] args) =>
        NativeKorean.Enabled ? KoreanStoryText.Get(key, args) : string.Format(CultureInfo.CurrentCulture, legacy, args);
    internal static string Name(string raw) => NativeKorean.Enabled ? KoreanLabels.MissionName(TextConverter.ToSimplified(raw ?? "")) : raw;
    internal static string Series(StoryMission mission) => !NativeKorean.Enabled ? (mission.Main ? "主线" : mission.SeriesTitle)
        : mission.Main ? KoreanStoryText.Get(KoreanStoryMessage.Main) : Name(mission.FullSeriesTitle.Length > 0 ? mission.FullSeriesTitle : mission.SeriesTitle);
    internal static string When(StoryMission mission) => NativeKorean.Enabled
        ? KoreanStoryText.Period(mission.From, mission.To, mission.MonthlyStage, mission.HasTimeLimit) : mission.When;
    internal static string Category(int section, string legacy) => NativeKorean.Enabled ? KoreanStoryText.Get(new[]
        { KoreanStoryMessage.Endings, KoreanStoryMessage.Deaths, KoreanStoryMessage.Achievements, KoreanStoryMessage.EndingCg, KoreanStoryMessage.Story }[section]) : legacy;
    internal static string Filter(int filter, string legacy) => NativeKorean.Enabled ? KoreanStoryText.Get(new[]
        { KoreanStoryMessage.All, KoreanStoryMessage.Achieved, KoreanStoryMessage.Unachieved }[filter]) : legacy;
    internal static string Kind(bool main) => Text(main ? KoreanStoryMessage.Main : KoreanStoryMessage.Side, main ? "主线" : "支线");
    internal static string Mission(bool main, string title) => NativeKorean.Enabled
        ? KoreanStoryText.Get(KoreanStoryMessage.MissionHeading, Kind(main), Name(title)) : Kind(main) + "「" + title + "」";
    internal static string Visited(bool visited) => Text(visited ? KoreanStoryMessage.Visited : KoreanStoryMessage.NotVisited, visited ? "这一局走过" : "这一局没走过");
}
