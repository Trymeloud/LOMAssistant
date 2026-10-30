using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace LOMAssistant;

internal enum KoreanStoryMessage
{
    Main, Side, CurrentMain, Rebuild, Search, Year, YearMonth, MonthStage, AnyTime, Monthly,
    UndeterminedTime, AnyMonthly, UpperStage, MiddleStage, LowerStage, MainArrow, NowStamp, EndStamp, SaveStamp,
    ReadPercent, DialogueReadPercent, Visited, NotVisited, VisitedShort, EndingCount, TriggerConditions,
    BeforeAfter, ReplayHere, RomanceRoute, Preparing, Affection, Lover, RomanceProgress,
    StepNotFound, CurrentConditions, AffectionScenes, ElsewhereCount, ReturnRomance, NoRomanceEffect,
    RelatedPerson, StepMet, StepConfession, StepBonded, StepTeacher, StepPrerequisite,
    ContinueMain, Incoming, IncomingFrom, Sections, Trunk, Branch, SectionCount, FlagHits,
    FirstFlagsOnly, MoreFlagUses, PreparingDialogue, NoDialogueHits, DialogueHits, FirstDialogueOnly,
    All, Achieved, Unachieved, SortNumber, SortTime, Endings, Deaths, Achievements, EndingCg, Story,
    AllRuns, Award, NoSearchResults, NoAchieved, AllAchieved, ReadComplete, NoEndingSource,
    MissionHeading, ScriptHeading, LostDuel, ReachedScene, MoreSources, EndingNumber, OpenFlow,
    OpenFlowTitle, UnreadMark, PartialReadMark, AfterBattle, FlowCut, OptionTag, DiceTag, BattleTag,
    ContinueOnMap, Ending, EndingNamed, NextMission, BackToMap, ContinueTo, DeathNamed,
    Next, NoNextScript, Credits, CreditsHint, CreditsUnavailable, SelectOption, Requires,
    DiceResult, IfCondition, UnknownCondition, Otherwise, OtherwiseOne, OtherwiseAll,
    ReturnTitle, ContinueScript, ContinueAfterBattle, AdvanceMain, ContinueScene,
    PreparingScripts, ScriptsProgress, PreparingMissions, MissionsProgress, LoadBeforeBuild, PreparingEndings, BuildFailed,
}

internal static class KoreanStoryText
{
    internal static readonly IReadOnlyDictionary<KoreanStoryMessage, string> Messages = new Dictionary<KoreanStoryMessage, string>
    {
        [KoreanStoryMessage.Main] = "주요 이야기", [KoreanStoryMessage.Side] = "곁가지 이야기",
        [KoreanStoryMessage.CurrentMain] = "현재 주요 이야기", [KoreanStoryMessage.Rebuild] = "다시 생성",
        [KoreanStoryMessage.Search] = "검색", [KoreanStoryMessage.Year] = "{0}년차",
        [KoreanStoryMessage.YearMonth] = "{0}년차 {1}월", [KoreanStoryMessage.MonthStage] = "{0}월 {1}",
        [KoreanStoryMessage.AnyTime] = "언제든지", [KoreanStoryMessage.Monthly] = "매달 {0}",
        [KoreanStoryMessage.UndeterminedTime] = "시기 미정", [KoreanStoryMessage.UpperStage] = "상순",
        [KoreanStoryMessage.AnyMonthly] = "언제든지 / 매달",
        [KoreanStoryMessage.MiddleStage] = "중순", [KoreanStoryMessage.LowerStage] = "하순",
        [KoreanStoryMessage.MainArrow] = "→ 주요 이야기", [KoreanStoryMessage.NowStamp] = "현",
        [KoreanStoryMessage.EndStamp] = "끝", [KoreanStoryMessage.SaveStamp] = "저장",
        [KoreanStoryMessage.ReadPercent] = "{0}% 읽음", [KoreanStoryMessage.DialogueReadPercent] = "대사 {0}% 읽음",
        [KoreanStoryMessage.Visited] = "이번 회차에 지나옴", [KoreanStoryMessage.NotVisited] = "이번 회차에 지나오지 않음",
        [KoreanStoryMessage.VisitedShort] = "지나옴", [KoreanStoryMessage.EndingCount] = "결말 {0}개",
        [KoreanStoryMessage.TriggerConditions] = "발생 조건", [KoreanStoryMessage.BeforeAfter] = "전후 관계",
        [KoreanStoryMessage.ReplayHere] = "여기서 다시 플레이", [KoreanStoryMessage.RomanceRoute] = "인연 경로",
        [KoreanStoryMessage.Preparing] = "스토리 데이터를 정리하는 중입니다", [KoreanStoryMessage.Affection] = "호감도 {0}",
        [KoreanStoryMessage.Lover] = "마음에 둔 사람", [KoreanStoryMessage.RomanceProgress] = "이번 회차의 인연 진행도",
        [KoreanStoryMessage.StepNotFound] = "스크립트에서 이 단계를 설정하는 위치를 찾지 못했습니다",
        [KoreanStoryMessage.CurrentConditions] = "발생 조건(이번 회차 수치): {0}",
        [KoreanStoryMessage.AffectionScenes] = "호감도가 변하는 스토리",
        [KoreanStoryMessage.ElsewhereCount] = "임무에 속하지 않는 육성 랜덤 이벤트 등의 스토리에 {0}곳 더 있습니다",
        [KoreanStoryMessage.ReturnRomance] = "‹ {0}의 인연 경로로 돌아가기",
        [KoreanStoryMessage.NoRomanceEffect] = "이 구간에서는 {0}의 호감도와 인연 진행 상태가 바뀌지 않습니다",
        [KoreanStoryMessage.RelatedPerson] = "{0} 관련:", [KoreanStoryMessage.StepMet] = "만남",
        [KoreanStoryMessage.StepConfession] = "고백", [KoreanStoryMessage.StepBonded] = "인연 성립",
        [KoreanStoryMessage.StepTeacher] = "제자 입문", [KoreanStoryMessage.StepPrerequisite] = "선행 단계",
        [KoreanStoryMessage.ContinueMain] = "완료 후 주요 이야기 「{0}」로 이어짐",
        [KoreanStoryMessage.Incoming] = "다른 구간에서 이어지는 경로",
        [KoreanStoryMessage.IncomingFrom] = "{0}에서 이어짐",
        [KoreanStoryMessage.Sections] = "구간", [KoreanStoryMessage.Trunk] = "주요 경로", [KoreanStoryMessage.Branch] = "분기",
        [KoreanStoryMessage.SectionCount] = "… 총 {0}구간", [KoreanStoryMessage.FlagHits] = "플래그 {0}개 검색됨",
        [KoreanStoryMessage.FirstFlagsOnly] = "(앞의 {0}개만 표시 · 검색어를 구체적으로 입력해 주세요)",
        [KoreanStoryMessage.MoreFlagUses] = "{0}곳 더 있습니다. 수치 탭에서 플래그 이름을 누르면 모두 볼 수 있습니다",
        [KoreanStoryMessage.PreparingDialogue] = "대사를 정리하는 중입니다…",
        [KoreanStoryMessage.NoDialogueHits] = "일치하는 대사가 없습니다", [KoreanStoryMessage.DialogueHits] = "대사 {0}곳 검색됨",
        [KoreanStoryMessage.FirstDialogueOnly] = "(앞의 {0}곳만 표시)",
        [KoreanStoryMessage.All] = "전체", [KoreanStoryMessage.Achieved] = "달성", [KoreanStoryMessage.Unachieved] = "미달성",
        [KoreanStoryMessage.SortNumber] = "번호순", [KoreanStoryMessage.SortTime] = "시기순",
        [KoreanStoryMessage.Endings] = "결말(한청서)", [KoreanStoryMessage.Deaths] = "사망(생사부)",
        [KoreanStoryMessage.Achievements] = "업적(풍운사)", [KoreanStoryMessage.EndingCg] = "결말 CG", [KoreanStoryMessage.Story] = "스토리",
        [KoreanStoryMessage.AllRuns] = "모든 회차의 기록 · 다른 저장을 불러와도 유지됩니다",
        [KoreanStoryMessage.Award] = "보상: {0}", [KoreanStoryMessage.NoSearchResults] = "일치하는 항목이 없습니다",
        [KoreanStoryMessage.NoAchieved] = "이 분류에는 아직 달성한 항목이 없습니다", [KoreanStoryMessage.AllAchieved] = "이 분류는 모두 달성했습니다",
        [KoreanStoryMessage.ReadComplete] = "모두 읽음",
        [KoreanStoryMessage.NoEndingSource] = "스크립트·결투 데이터에서 발생 위치를 찾지 못했습니다(난전·미니게임 등에서 직접 발생할 수 있습니다)",
        [KoreanStoryMessage.MissionHeading] = "{0} 「{1}」", [KoreanStoryMessage.ScriptHeading] = "스크립트 {0}",
        [KoreanStoryMessage.LostDuel] = "결투에서 패배", [KoreanStoryMessage.ReachedScene] = "해당 구간에 도달하면 발생",
        [KoreanStoryMessage.MoreSources] = "추가 발생 위치 {0}곳", [KoreanStoryMessage.EndingNumber] = "결말 {0}",
        [KoreanStoryMessage.OpenFlow] = "스토리 흐름도 보기", [KoreanStoryMessage.OpenFlowTitle] = "스토리 흐름도 보기: {0}",
        [KoreanStoryMessage.UnreadMark] = "〔안 읽음〕", [KoreanStoryMessage.PartialReadMark] = "〔{0}% 읽음〕",
        [KoreanStoryMessage.AfterBattle] = "전투 후", [KoreanStoryMessage.FlowCut] = "너무 길어 앞의 {0}구간만 표시",
        [KoreanStoryMessage.OptionTag] = "선택지", [KoreanStoryMessage.DiceTag] = "주사위", [KoreanStoryMessage.BattleTag] = "전투",
        [KoreanStoryMessage.ContinueOnMap] = "지도에서 계속", [KoreanStoryMessage.Ending] = "결말",
        [KoreanStoryMessage.EndingNamed] = "결말: {0}", [KoreanStoryMessage.NextMission] = "주요 이야기 → {0}",
        [KoreanStoryMessage.BackToMap] = "지도로 돌아감", [KoreanStoryMessage.ContinueTo] = "이어짐: {0}",
        [KoreanStoryMessage.DeathNamed] = "사망: {0}", [KoreanStoryMessage.Next] = "다음 전개",
        [KoreanStoryMessage.NoNextScript] = "이 구간 뒤에 바로 이어지는 스크립트가 없습니다",
        [KoreanStoryMessage.Credits] = "엔딩 크레딧 보기", [KoreanStoryMessage.CreditsHint] = "클리어 시의 엔딩 크레딧을 재생합니다. Esc로 종료합니다",
        [KoreanStoryMessage.CreditsUnavailable] = "스토리 장면에서 재생할 수 있습니다(스토리 저장을 불러온 뒤 확인해 주세요)",
        [KoreanStoryMessage.SelectOption] = "선택 「{0}」", [KoreanStoryMessage.Requires] = "필요: {0}",
        [KoreanStoryMessage.DiceResult] = "주사위 판정 「{0}」", [KoreanStoryMessage.IfCondition] = "조건: {0}",
        [KoreanStoryMessage.UnknownCondition] = "조건 불명", [KoreanStoryMessage.Otherwise] = "그 외",
        [KoreanStoryMessage.OtherwiseOne] = "그 외 (불충족: {0})", [KoreanStoryMessage.OtherwiseAll] = "그 외 (모두 불충족: {0})",
        [KoreanStoryMessage.ReturnTitle] = "타이틀 화면으로 돌아감 (결말)",
        [KoreanStoryMessage.ContinueScript] = "「{0}」로 이어짐", [KoreanStoryMessage.ContinueAfterBattle] = "전투 후 「{0}」로 이어짐",
        [KoreanStoryMessage.AdvanceMain] = "주요 이야기 「{0}」로 진행", [KoreanStoryMessage.ContinueScene] = "이 구간에서 계속 진행",
        [KoreanStoryMessage.PreparingScripts] = "스토리 데이터를 불러오는 중",
        [KoreanStoryMessage.ScriptsProgress] = "스토리 데이터를 불러오는 중 {0}/{1}",
        [KoreanStoryMessage.PreparingMissions] = "임무를 정리하는 중",
        [KoreanStoryMessage.MissionsProgress] = "임무를 정리하는 중 {0}/{1}",
        [KoreanStoryMessage.LoadBeforeBuild] = "게임을 불러온 뒤 다시 생성해 주세요",
        [KoreanStoryMessage.PreparingEndings] = "결말 정보를 정리하는 중",
        [KoreanStoryMessage.BuildFailed] = "생성 실패: {0}",
    };

    internal static string Get(KoreanStoryMessage key, params object[] args) => string.Format(CultureInfo.InvariantCulture, Messages[key], args);
    internal static string Stage(int stage) => Get(stage == 1 ? KoreanStoryMessage.UpperStage : stage == 2 ? KoreanStoryMessage.MiddleStage : KoreanStoryMessage.LowerStage);
    internal static string Date(int rounds) => Get(KoreanStoryMessage.YearMonth, rounds / 36 + 1, rounds % 36 / 3 + 1) + " " + Stage(rounds % 3 + 1);
    // Native route labels must not feed Korean conditions back through the Chinese prose parser.
    internal static string RouteClause(string conditions, Func<string, string> flagNote)
    {
        string first = Regex.Split(conditions ?? "", @"\n\s*또는\s+")[0].Replace("\r", "").Replace("\n", " ");
        string clause = first.Split(new[] { " 그리고 " }, StringSplitOptions.None).Select(s => s.Trim())
            .FirstOrDefault(s => s.Length > 0 && !s.Contains("시점") && !s.Contains("행동력") && !s.Contains("행동 횟수"));
        if (clause == null) return "";
        Match flag = Regex.Match(clause, @"^(.+?) \(([A-Za-z0-9_]+)\) 필요 =(\d+)");
        string note = flag.Success ? flagNote(flag.Groups[2].Value) : null;
        Match pick = note != null ? Regex.Match(note, @"(?:^|\s)" + flag.Groups[3].Value + @"[.．、:：]\s*([^\s\r\n]+)") : Match.Empty;
        return pick.Success ? flag.Groups[1].Value.Trim() + ": " + KoreanLabels.Variable(pick.Groups[1].Value)
            : Regex.Replace(clause, @" → 현재.*$| \([A-Za-z0-9_]+\)", "").Replace(" 필요 ", "").Trim();
    }
    internal static string Period(int? from, int? to, int? monthly, bool limited)
    {
        if (!limited) return Get(KoreanStoryMessage.AnyTime);
        if (monthly != null) return Get(KoreanStoryMessage.Monthly, Stage(monthly.Value));
        if (from == null || to == null) return Get(KoreanStoryMessage.UndeterminedTime);
        return from == to ? Date(from.Value) : Date(from.Value) + " ～ " + Date(to.Value);
    }
    internal static string RomanceStep(string flag) => Get(flag.StartsWith("G10") ? KoreanStoryMessage.StepMet
        : flag.StartsWith("G00") ? KoreanStoryMessage.StepConfession : flag.StartsWith("G11") ? KoreanStoryMessage.StepBonded
        : flag == "S0014_02" ? KoreanStoryMessage.StepTeacher : KoreanStoryMessage.StepPrerequisite);
    internal static string ReadMark(int? read) => read == null || read == 100 ? "" : read == 0 ? Get(KoreanStoryMessage.UnreadMark) : Get(KoreanStoryMessage.PartialReadMark, read);
    internal static string EndingWay(bool main, string missionTitle, string when, string script, IEnumerable<string> path, bool lostDuel)
    {
        string head = missionTitle == null ? Get(KoreanStoryMessage.ScriptHeading, script)
            : Get(KoreanStoryMessage.MissionHeading, Get(main ? KoreanStoryMessage.Main : KoreanStoryMessage.Side), missionTitle) + " (" + when + ")";
        var steps = path.ToList();
        if (lostDuel) steps.Add(Get(KoreanStoryMessage.LostDuel));
        return head + ": " + (steps.Count > 0 ? string.Join(" → ", steps) : Get(KoreanStoryMessage.ReachedScene));
    }
}
