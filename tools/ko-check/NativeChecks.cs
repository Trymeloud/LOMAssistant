using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using LOMAssistant;

internal static class NativeChecks
{
    internal static void Run(Action<bool, string> check, string flagsFile)
    {
        foreach (KoreanMessage key in Enum.GetValues(typeof(KoreanMessage)))
        {
            check(KoreanText.Messages.ContainsKey(key), "Missing native message: " + key);
            string text = KoreanText.Get(key, "소사매", ">=", "35", "40");
            check(!Regex.IsMatch(text, "[一-鿿]"), "Chinese in native message: " + key);
            check(!Regex.IsMatch(text, @"\{\d+\}"), "Unexpanded native placeholder: " + key);
        }
        foreach (string op in new[] { "=", "!=", ">", "<", ">=", "<=" })
        {
            string condition = KoreanText.Condition("소사매 호감도 (M50009)", op, "60", "84");
            check(condition == "소사매 호감도 (M50009) 필요 " + op + "60 → 현재 84", "Native condition operator: " + op);
            check(KoreanText.CompactCondition(condition) == "소사매 호감도 (M50009)" + op + "60", "Native compact condition: " + op);
        }
        check(KoreanText.Condition("구력", "<", "40") == "구력 필요 <40", "Template must omit current value");
        check(KoreanText.Effect("Player", "심상", -30) == "심상 -30", "Negative stat delta");
        check(KoreanText.Effect("Character", "우소매", 3) == "우소매 호감도 +3", "Positive affection delta");
        check(KoreanText.Effect("Character", "우소매", 0) == "우소매 호감도 0", "Zero affection delta");
        check(KoreanText.Effect("AddFlag", "만난 횟수 (M50009)", -1) == "만난 횟수 (M50009) -1", "Flag delta must preserve minus sign and ID");
        check(KoreanText.Effect("SetFlag", "진행 (M50009)", -1) == "진행 (M50009) = -1", "Set flag is assignment, not delta");
        check(KoreanText.Effect("UpdateSetPlayerStat", "체력", 50) == "체력 = 50", "Stat assignment");
        check(KoreanText.Effect("ModifyEnemyTeam", "공동파", -1) == "공동파 단합 -1", "Team effect");
        check(KoreanText.Effect("AddTalent", "불굴", 2) == "천부 「불굴」 2단계", "Talent level");
        foreach (string method in new[] { "AddBook", "AddMisc", "AddSpecial" })
            check(KoreanText.Effect(method, "비급", 2) == "비급 ×2", "Item quantity: " + method);
        check(KoreanText.Effect("UnknownMethod", "설명", 3) == "", "Unknown action must not masquerade as a known effect");
        check(KoreanText.Effects(new[] { "호감도 +3", "호감도 +3" }, new[] { new[] { "심상 -5" }, new[] { "은자 +10" } })
            == "호감도 +3; 조건에 따라: ①심상 -5 ②은자 +10", "Effect grouping, deduplication and order");
        check(KoreanText.Effects(Array.Empty<string>(), new[] { new[] { "" }, new[] { "은자 +10" } })
            == "조건에 따라: 은자 +10", "Empty conditional groups");
        check(KoreanLabels.Variable("随机值_0_100") == "난수(0~100)", "Random variable range");
        check(KoreanLabels.Variable("嘴力") == "구력", "Reviewed variable name");
        check(KoreanLabels.Flag("NEW_UNKNOWN_FLAG") == "플래그 NEW_UNKNOWN_FLAG", "Unknown flag retains its ID");
        check(KoreanText.Get(KoreanMessage.FlagChange, "소사매 호감도", 0, 1) == "【플래그】소사매 호감도  0 → 1", "Flag notification label and state order");
        check(KoreanText.Get(KoreanMessage.FlagChange, "진행 상태", 3, -1) == "【플래그】진행 상태  3 → -1", "Flag notification preserves decreases and negative values");
        check(KoreanText.Get(KoreanMessage.FlagChange, KoreanLabels.Flag("NEW_UNKNOWN_FLAG"), -1, 0)
            == "【플래그】플래그 NEW_UNKNOWN_FLAG  -1 → 0", "Unknown notification flag retains ID without untranslated developer prose");
        check(KoreanActionCall.TryParse("statmodifymanager.Player(\"money\", -100, \"\", 1)", out var spend)
            && spend.Method == "Player" && spend.Id == "money" && spend.Value == -100 && spend.HasValue, "Lua negative amount and target");
        check(KoreanActionCall.TryParse("statmodifymanager.SetFlag(\"M50009\", 1)", out var setFlag)
            && setFlag.Id == "M50009" && setFlag.Method == "SetFlag" && setFlag.Value == 1, "Lua flag identity");
        check(KoreanActionCall.TryParse("statmodifymanager.RemoveMisc(\"item_001\")", out var discard)
            && discard.Id == "item_001" && !discard.HasValue, "Item removal without count");
        check(!KoreanActionCall.TryParse("statmodifymanager.Player(\"money\", amount)", out _), "Unevaluated Lua must not become a numeric result");
        check(!KoreanActionCall.TryParse("statmodifymanager.Player(\"money\", 1.5)", out _), "Preserve integer action semantics");
        check(!KoreanActionCall.TryParse("not_an_action()", out _), "Ignore unrelated Lua calls");
        foreach (string place in KoreanLabels.SectionPlaces.Values)
            check(!Regex.IsMatch(place, "[一-鿿]"), "Untranslated study-abroad place");
        foreach (string[] row in UiTextKorean.Conditions.Concat(UiTextKorean.SupplementalConditions))
            check(!Regex.IsMatch(KoreanLabels.Variable(row[0]), "[一-鿿]"), "Native variable name: " + row[0]);
        if (File.Exists(flagsFile))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(flagsFile));
            foreach (var flag in document.RootElement.GetProperty("flags").EnumerateArray())
            {
                string id = flag.GetProperty("id").GetString();
                check(KoreanLabels.HasFlag(id), "Native flag ID missing: " + id);
                check(!Regex.IsMatch(KoreanLabels.Flag(id), "[一-鿿]"), "Untranslated native flag: " + id);
                string original = flag.GetProperty("simplified").GetString().Split('\n')[0].TrimEnd('\r');
                if (!string.IsNullOrWhiteSpace(original))
                    check(KoreanLabels.Flag(id) == UiText.Translate(original, UiText.Korean).Trim(), "Native flag disagrees with reviewed translation: " + id
                        + " actual=[" + KoreanLabels.Flag(id) + "] expected=[" + UiText.Translate(original, UiText.Korean).Trim() + "]");
            }
        }
    }
}
