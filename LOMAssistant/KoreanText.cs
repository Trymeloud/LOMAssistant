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
}

internal static class KoreanText
{
    internal static readonly IReadOnlyDictionary<KoreanMessage, string> Messages = new Dictionary<KoreanMessage, string>
    {
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
