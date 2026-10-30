using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using LOMAssistant.LuaParse;
using Mortal.Core;

namespace LOMAssistant;

// Game adapter. Reads data only; does not change flags, random draws or selections.
internal static class NativeKorean
{
    static NativeKorean() => KoreanLabels.ReportMissing = name => FileLogger.LogWarning("[한국어] 이름 번역 미등록: " + name);
    internal static bool Enabled => TextConverter.DisplayLanguage == UiText.Korean;

    internal static string Text(KoreanMessage key, string legacy, params object[] values) =>
        Enabled ? KoreanText.Get(key, values) : string.Format(CultureInfo.CurrentCulture, legacy, values);

    internal static string DescribeAction(ActionType type, string content)
    {
        if (type == ActionType.BattleScene) return KoreanText.Get(content.Contains("Combat") ? KoreanMessage.Duel : KoreanMessage.Battle);
        if (type != ActionType.ModifyAction) return "";
        if (!KoreanActionCall.TryParse(content, out KoreanActionCall call)) return "";
        string method = call.Method, id = call.Id;
        if (method == "RemoveMisc" && !call.HasValue)
            return KoreanText.Get(KoreanMessage.Discard, DataHelper.GetItemName(GameItemType.雜物, id));
        int value = call.Value;
        string name = method switch
        {
            "Player" or "UpdateSetPlayerStat" => DataHelper.TryParseEnum<GameStatType>(id, out _)
                ? DataHelper.GetGameStatLocalizedName(id) : id,
            "Character" => DataHelper.TryParseEnum<RelationshipStatType>(id, out _) ? DataHelper.GetCharacterLocalizedName(id) : id,
            "SetFlag" or "AddFlag" => KoreanLabels.Flag(id) + " (" + id + ")",
            "AddBook" => DataHelper.GetItemName(GameItemType.書籍, id),
            "AddMisc" => DataHelper.GetItemName(GameItemType.雜物, id),
            "AddSpecial" => DataHelper.GetItemName(GameItemType.貴重品, id),
            "AddTalent" => DataHelper.GetTalentName(id),
            "ModifyEnemyTeam" => DataHelper.GetTeamName(id),
            _ => id,
        };
        // Official game names are resolved separately from the sentence template.
        if (string.IsNullOrEmpty(name)) name = id;
        string text = KoreanText.Effect(method, KoreanLabels.Variable(name), value);
        return method is "Player" or "Character" or "ModifyEnemyTeam" ? RichText.ColorText(text) : text;
    }

    internal static string Variable(StatGroupVariable variable, string canonicalName)
    {
        // _value may describe a sum, product or several flags. Only replace a
        // simple direct flag name here; keep compound group names from the reviewed table.
        string id = variable.name;
        foreach (string prefix in new[] { "标记_", "旗标_", "標記_", "旗標_" })
            if (id.StartsWith(prefix)) { id = id.Substring(prefix.Length); break; }
        if (KoreanLabels.HasFlag(id)) return KoreanLabels.Flag(id) + " (" + id + ")";
        return KoreanLabels.Variable(canonicalName);
    }
}
