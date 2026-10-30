using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace LOMAssistant;

// Stable message IDs: Korean sentences are composed from data, never Chinese prose.
internal enum KoreanMessage
{
    LoadGame, Refresh, OpenFlow, NotAMission, CurrentOptions, OtherOptions, SceneOptions,
    Dice, Other, ChoiceNumber, NextRoutes, RandomEvents, Unmet, Selected, SelectNext,
    Missions, Main, Ready, ReadyAt, Missing, EmptyScene, SnapshotError,
    Condition, ConditionOnly, All, Any, UnknownCondition, Otherwise,
    SetValue, Affection, Talent, Cohesion, Discard, Quantity, Followup, Flag,
    Duel, Battle, PossibleResults, CertainResults, ConditionalResults, Weight,
    Activation, RateConditions, Location, Period, Conditions, UnlimitedTime,
    AfterUnlock, ConditionalStory, RandomRange, UnknownLabel, EffectWhen, PlaceEvent, FlagChange,
    BranchDestination, BranchEnding, BranchNumber, WarningLine, ActionModeUnlimitedWarning, ActionModeWarning,
    EnemyPlanned, EnemyAdaptive, BookStage, BookBonusTitle, CenterBonus, MemoHeading,
    SkipBusy, SkipLate, HotkeyConflict, FixedHotkeyConflict, GeneralHotkeyConflict,
}

internal static class KoreanText
{
    internal static readonly IReadOnlyDictionary<KoreanMessage, string> Messages = new Dictionary<KoreanMessage, string>
    {
        [KoreanMessage.BranchDestination] = "「{0}」(으)로 이어짐",
        [KoreanMessage.BranchEnding] = "타이틀로 돌아감 (결말)",
        [KoreanMessage.BranchNumber] = "{0}번째 분기",
        [KoreanMessage.WarningLine] = "【주의】{0}",
        [KoreanMessage.ActionModeUnlimitedWarning] = "행동력이 1 남으면 더 이상 소모되지 않습니다. ESC의 「턴 넘기기」로 이번 순을 끝내세요. ",
        [KoreanMessage.ActionModeWarning] = "행동력이 「보통」 모드가 아니면 일부 주요·곁가지 이야기가 발생하지 않거나 일찍 발생할 수 있습니다. 모드를 바꾸기 전에 저장하세요.",
        [KoreanMessage.EnemyPlanned] = "적 예정 행동",
        [KoreanMessage.EnemyAdaptive] = "대응을 바꿀 수 있음",
        [KoreanMessage.BookStage] = "{0}단계",
        [KoreanMessage.BookBonusTitle] = "수련 단계별 능력치 보너스:",
        [KoreanMessage.CenterBonus] = "{0} 중간 쪽으로 {1}",
        [KoreanMessage.MemoHeading] = "메모: ",
        [KoreanMessage.SkipBusy] = "지금은 해야 할 중요한 일이 있어 시간을 허비할 수 없다.",
        [KoreanMessage.SkipLate] = "지금은 시간이 늦었으니 다음에 하자.",
        [KoreanMessage.HotkeyConflict] = "「{0}」 모드와 단축키({1})가 겹쳐, 이 모드의 {2} 단축키를 해제했습니다. Tab으로 창을 열거나 설정의 「단축키」에서 다른 키를 지정할 수 있습니다.",
        [KoreanMessage.FixedHotkeyConflict] = "일러스트 교체 모드의 {0} 단축키는 변경할 수 없습니다. 단축키가 겹쳐 이 모드의 {1} 단축키를 해제했습니다. Tab으로 창을 열거나 설정의 「단축키」에서 다른 키를 지정할 수 있습니다.",
        [KoreanMessage.GeneralHotkeyConflict] = "「{0}」 모드와 단축키가 겹쳐, 이 모드의 {1} 단축키를 해제했습니다. 설정의 「단축키」에서 다른 키를 지정할 수 있습니다.",
        [KoreanMessage.LoadGame] = "게임을 불러온 뒤 확인할 수 있습니다",
        [KoreanMessage.Refresh] = "새로고침",
        [KoreanMessage.OpenFlow] = "스토리 흐름도에서 보기",
        [KoreanMessage.NotAMission] = "이 구간은 임무에 속하지 않습니다",
        [KoreanMessage.CurrentOptions] = "지금 선택할 수 있는 항목",
        [KoreanMessage.OtherOptions] = "이 구간의 다른 선택지",
        [KoreanMessage.SceneOptions] = "이 구간의 선택지",
        [KoreanMessage.Dice] = "주사위 판정",
        [KoreanMessage.Other] = "기타",
        [KoreanMessage.ChoiceNumber] = "{0}번째 선택",
        [KoreanMessage.NextRoutes] = "이후 진행 경로",
        [KoreanMessage.RandomEvents] = "이 장소의 랜덤 이벤트",
        [KoreanMessage.Unmet] = "조건 미충족",
        [KoreanMessage.Selected] = "지정됨",
        [KoreanMessage.SelectNext] = "다음 이벤트로 지정",
        [KoreanMessage.Missions] = "현재 임무",
        [KoreanMessage.Main] = "주요 이야기",
        [KoreanMessage.Ready] = "조건 충족",
        [KoreanMessage.ReadyAt] = "조건 충족 · 「{0}」 방문 시 발생",
        [KoreanMessage.Missing] = "조건 미충족",
        [KoreanMessage.EmptyScene] = "이 장면에는 표시할 선택지·판정·임무가 없습니다",
        [KoreanMessage.SnapshotError] = "현재 상황을 읽는 중 오류가 발생했습니다. 새로고침하거나 로그를 확인해 주세요.",
        [KoreanMessage.Condition] = "{0} 필요 {1}{2} → 현재 {3}",
        [KoreanMessage.ConditionOnly] = "{0} 필요 {1}{2}",
        [KoreanMessage.All] = " 그리고 ",
        [KoreanMessage.Any] = " 또는 ",
        [KoreanMessage.UnknownCondition] = "조건 불명",
        [KoreanMessage.Otherwise] = "위 조건을 모두 충족하지 않음",
        [KoreanMessage.SetValue] = "{0} = {1}",
        [KoreanMessage.Affection] = "{0} 호감도 {1}",
        [KoreanMessage.Talent] = "천부 「{0}」 {1}단계",
        [KoreanMessage.Cohesion] = "{0} 단합 {1}",
        [KoreanMessage.Discard] = "{0} 버림",
        [KoreanMessage.Quantity] = "{0} ×{1}",
        [KoreanMessage.Followup] = "조건에 따라: {0}",
        [KoreanMessage.Flag] = "플래그 {0}",
        [KoreanMessage.FlagChange] = "【플래그】{0}  {1} → {2}",
        [KoreanMessage.Duel] = "결투",
        [KoreanMessage.Battle] = "난전",
        [KoreanMessage.PossibleResults] = "발생 가능한 결과:",
        [KoreanMessage.CertainResults] = "항상 적용: {0}",
        [KoreanMessage.ConditionalResults] = "조건에 따라 적용: {0}",
        [KoreanMessage.Weight] = "추첨 가중치: {0} (기본 {1})",
        [KoreanMessage.Activation] = "활성화 조건: {0}",
        [KoreanMessage.RateConditions] = "가중치 변경 조건:",
        [KoreanMessage.Location] = "위치: {0}",
        [KoreanMessage.Period] = "시기: {0}",
        [KoreanMessage.Conditions] = "조건:",
        [KoreanMessage.UnlimitedTime] = "제한 없음",
        [KoreanMessage.AfterUnlock] = "{0}; 해금 후: {1}",
        [KoreanMessage.ConditionalStory] = "{0}이면 스토리 발생",
        [KoreanMessage.RandomRange] = "난수({0}~{1})",
        [KoreanMessage.UnknownLabel] = "번역 미등록: {0}",
        [KoreanMessage.EffectWhen] = "{0}: {1}",
        [KoreanMessage.PlaceEvent] = "{0} 이벤트: {1}",
    };

    internal static string Get(KoreanMessage key, params object[] values) =>
        string.Format(CultureInfo.InvariantCulture, Messages[key], values);

    // Compatibility notices are captured before the game language may be available.
    // Resolve their presentation at draw time without changing keys or compatibility decisions.
    internal static string Warning(string detail)
    {
        detail ??= "";
        Match keys = Regex.Match(detail, @"本 Mod 的(.+?)热键已让给它（改成不绑定）");
        string keyNames = keys.Success ? string.Join(", ", keys.Groups[1].Value.Split('、').Select(n => UiText.Translate(n, UiText.Korean))) : "";
        if (keys.Success && detail.StartsWith("检测到「剧情随行」", StringComparison.Ordinal))
            return Get(KoreanMessage.WarningLine, Get(KoreanMessage.HotkeyConflict, "스토리 동행", "F6·F8·F9", keyNames));
        if (keys.Success && detail.StartsWith("检测到立绘替换 Mod「邪神认知污染ON」", StringComparison.Ordinal))
            return Get(KoreanMessage.WarningLine, Get(KoreanMessage.FixedHotkeyConflict, "F8·F9", keyNames));
        Match other = Regex.Match(detail, @"^检测到「(.+?)」的快捷键和本 Mod 撞了，");
        if (keys.Success && other.Success)
            return Get(KoreanMessage.WarningLine, Get(KoreanMessage.GeneralHotkeyConflict, other.Groups[1].Value, keyNames));
        // Preserve whole-notice translations (e.g. Japanese patch compatibility),
        // then translate the detail separately so the warning prefix cannot hide its sentence.
        string whole = UiText.Translate("【注意】" + detail, UiText.Korean);
        if (whole.StartsWith("【주의】", StringComparison.Ordinal)) return whole;
        return Get(KoreanMessage.WarningLine, UiText.Translate(detail, UiText.Korean));
    }

    internal static string ActionModeWarning(bool unlimited) =>
        (unlimited ? Get(KoreanMessage.ActionModeUnlimitedWarning) : "") + Get(KoreanMessage.ActionModeWarning);

    internal static string BookBonusLine(int stage, bool learned, IEnumerable<string> parts) =>
        (learned ? "✓ " : "") + Get(KoreanMessage.BookStage, stage) + ": " + string.Join(", ", parts);

    internal static string DuelForecast(string action, bool horizontal, bool adaptive) =>
        Get(KoreanMessage.EnemyPlanned) + (horizontal ? " " : "\n\n") + UiText.Translate(action, UiText.Korean)
        + (adaptive ? (horizontal ? " · " : "\n\n") + Get(KoreanMessage.EnemyAdaptive) : "");

    internal static string Condition(string name, string op, string required, string current = null) =>
        current == null ? Get(KoreanMessage.ConditionOnly, name, op, required)
            : Get(KoreanMessage.Condition, name, op, required, current);

    // Compatibility for compact flow edges; comparison operators and IDs stay intact.
    internal static string CompactCondition(string text) => Regex.Replace(text, @" → (?:当前|현재\s*)[^\s]+", "")
        .Replace(" 需", "").Replace(" 필요 ", "").Trim();

    internal static string Effect(string method, string name, int value)
    {
        string delta = value > 0 ? "+" + value.ToString(CultureInfo.InvariantCulture) : value.ToString(CultureInfo.InvariantCulture);
        return method switch
        {
            "Player" => name + " " + delta,
            "Character" => Get(KoreanMessage.Affection, name, delta),
            "ModifyEnemyTeam" => Get(KoreanMessage.Cohesion, name, delta),
            "SetFlag" or "UpdateSetPlayerStat" => Get(KoreanMessage.SetValue, name, value),
            "AddFlag" => name + " " + delta,
            "AddTalent" => Get(KoreanMessage.Talent, name, value),
            "AddBook" or "AddMisc" or "AddSpecial" => Get(KoreanMessage.Quantity, name, value),
            _ => "",
        };
    }

    // Keep branch grouping and order supplied by the existing parser.
    internal static string Effects(IEnumerable<string> certain, IEnumerable<IEnumerable<string>> conditional)
    {
        var parts = new List<string>();
        string guaranteed = string.Join(", ", certain.Where(s => !string.IsNullOrEmpty(s)).Distinct());
        if (guaranteed.Length > 0) parts.Add(guaranteed);
        var branches = conditional.Select(g => string.Join(", ", g.Where(s => !string.IsNullOrEmpty(s)).Distinct()))
            .Where(s => s.Length > 0).ToList();
        if (branches.Count > 0)
        {
            const string circled = "①②③④⑤⑥⑦⑧⑨⑩⑪⑫⑬⑭⑮⑯⑰⑱⑲⑳";
            parts.Add(Get(KoreanMessage.Followup, branches.Count == 1 ? branches[0] :
                string.Join(" ", branches.Select((s, i) => (i < circled.Length ? circled[i].ToString() : (i + 1) + ".") + s))));
        }
        return string.Join("; ", parts);
    }
}
