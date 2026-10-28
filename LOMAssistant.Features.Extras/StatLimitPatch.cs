using System.Collections.Generic;
using HarmonyLib;
using LOMAssistant.Config;
using Mortal.Core;
using UnityEngine;

namespace LOMAssistant.Features.Extras;

/// <summary>
/// 突破属性上限（设置页「养成」，默认关）：体力、内力、心相、刀剑、暗器、拳掌、儒学道学释学等属性原本最高 100，打开后可以加到 9999。
/// 不能直接改 GameStat 的上限（Max）：游戏把属性折算成战斗加成、属性等级时用的是「数值 ÷ 上限」，
/// 上限改成 9999 后 100 点只算 1%，实际效果反而变低。所以上限照旧是 100，折算时超过 100 的部分另外接着往上算（见 GetResult_Prefix），只放开两处截断：
/// 加值、设值时临时把上限换成 9999 再截；最终值的截断（上限 + 额外上限）把额外上限抬高。
/// 额外上限是游戏数据，不进存档，所以每次读档前、新开局后、开关变化时都重新套一次；关掉就改回原来的额外上限。
/// </summary>
public static class StatLimitPatch
{
	private const int RaisedMax = 9999;

	private static readonly GameStatType[] Types =
	{
		GameStatType.體力, GameStatType.內力, GameStatType.輕功, GameStatType.學問, GameStatType.嘴力, GameStatType.心理衛生,
		GameStatType.性情, GameStatType.處世, GameStatType.修養, GameStatType.道德, GameStatType.陰陽內功, GameStatType.命運,
		GameStatType.魅力, GameStatType.廚藝, GameStatType.抗毒, GameStatType.抗麻,
		// 武功三项：决斗的普攻、暗器、绝招、格挡都按「武功 × 倍率」算，超过 100 照样往上加；儒学、道学、释学用在剧情和秘笈的判定
		GameStatType.武功刀劍, GameStatType.武功暗器, GameStatType.武功拳掌, GameStatType.儒學, GameStatType.道學, GameStatType.佛學,
	};

	private static readonly AccessTools.FieldRef<GameStat, int> MaxField = AccessTools.FieldRefAccess<GameStat, int>("_max");
	private static readonly AccessTools.FieldRef<GameStat, int> LimitAdditionField = AccessTools.FieldRefAccess<GameStat, int>("_limitAddition");

	/// <summary>放开了的属性和它原来的额外上限（关掉时改回去）。</summary>
	private static readonly Dictionary<GameStat, int> Raised = new Dictionary<GameStat, int>();

	public static void Apply()
	{
		PlayerStatManagerData player = PlayerStatManagerData.Instance;
		if (player == null || player.Stats == null)
		{
			return;
		}
		bool raise = ConfigManager.BreakStatLimit.Value;
		foreach (GameStatType type in Types)
		{
			GameStat stat;
			try
			{
				stat = player.Stats.Get(type);
			}
			catch
			{
				continue;
			}
			if (stat == null)
			{
				continue;
			}
			if (raise)
			{
				if (!Raised.ContainsKey(stat))
				{
					Raised[stat] = LimitAdditionField(stat);
				}
				LimitAdditionField(stat) = Raised[stat] + RaisedMax - stat.Max;
			}
			else if (Raised.TryGetValue(stat, out int original))
			{
				LimitAdditionField(stat) = original;
				Raised.Remove(stat);
			}
		}
	}

	// 加值、设值时游戏按上限截断：这一下临时换成 9999，完了再换回来
	[HarmonyPrefix]
	[HarmonyPatch(typeof(GameStat), "AddValue")]
	public static void AddValue_Prefix(GameStat __instance, out int __state) => __state = Lift(__instance);

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(GameStat), "AddValue")]
	public static void AddValue_Finalizer(GameStat __instance, int __state) => Restore(__instance, __state);

	[HarmonyPrefix]
	[HarmonyPatch(typeof(GameStat), "SetValue")]
	public static void SetValue_Prefix(GameStat __instance, out int __state) => __state = Lift(__instance);

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(GameStat), "SetValue")]
	public static void SetValue_Finalizer(GameStat __instance, int __state) => Restore(__instance, __state);

	private static int Lift(GameStat stat)
	{
		if (!Raised.ContainsKey(stat))
		{
			return -1;
		}
		int max = MaxField(stat);
		MaxField(stat) = RaisedMax;
		return max;
	}

	private static void Restore(GameStat stat, int max)
	{
		if (max >= 0)
		{
			MaxField(stat) = max;
		}
	}

	private static readonly AccessTools.FieldRef<CurveStatConvertData, AnimationCurve> CurveField = AccessTools.FieldRefAccess<CurveStatConvertData, AnimationCurve>("_curve");
	private static readonly AccessTools.FieldRef<GameStatConvertData, GameStat> SourceField = AccessTools.FieldRefAccess<GameStatConvertData, GameStat>("_statData");

	// 属性折算成战斗数值：游戏按「(数值 - 下限) ÷ (上限 - 下限)」在曲线上取值，超过上限就停在曲线末端。
	// 突破上限后超过 100 的部分沿曲线末端的斜率继续往上加，数值越高效果越强；100 以内和原版一样
	[HarmonyPrefix]
	[HarmonyPatch(typeof(CurveStatConvertData), "GetResult", typeof(int))]
	public static bool GetResult_Prefix(CurveStatConvertData __instance, int value, ref float __result)
	{
		if (!(__instance is GameStatConvertData data) || !(SourceField(data) is GameStat stat) || !Raised.ContainsKey(stat) || value <= stat.Max)
		{
			return true;
		}
		AnimationCurve curve = CurveField(__instance);
		if (curve == null || stat.Max <= stat.Min)
		{
			return true;
		}
		float t = (float)(value - stat.Min) / (stat.Max - stat.Min);
		float end = curve.Evaluate(1f);
		float slope = (end - curve.Evaluate(0.95f)) / 0.05f;
		__result = end + slope * (t - 1f);
		return false;
	}

	// 属性等级（「出类拔萃」这类文字）按「数值 ÷ 每级多少」算，游戏只处理了刚好等于满级的情况；
	// 数值超过 100 时会算出比最高级还高的等级，按最高级算
	[HarmonyPostfix]
	[HarmonyPatch(typeof(GameStatUtils), "GetGameStatLevel")]
	public static void GetGameStatLevel_Postfix(int levelLength, ref int __result)
	{
		if (levelLength > 0 && __result >= levelLength)
		{
			__result = levelLength - 1;
		}
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(PlayerStatManagerData), "Load")]
	public static void Load_Prefix() => Apply();

	[HarmonyPostfix]
	[HarmonyPatch(typeof(PlayerStatManagerData), "ResetData")]
	public static void ResetData_Postfix() => Apply();
}
