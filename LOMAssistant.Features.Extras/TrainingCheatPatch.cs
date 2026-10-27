using System.Collections;
using HarmonyLib;
using LOMAssistant.Config;
using Mortal.Core;

namespace LOMAssistant.Features.Extras;

/// <summary>
/// 修炼 / 锻造 / 炼丹 相关修改：
/// 无限：投点时不消耗本次可用次数和武学点（锻造点、炼丹点），投完换回投之前的值；不会一打开就补满。
/// 点数是 0 时照样投不了，要加点数在数值页改。（以前的「快速：投 1 点直接升级」游戏自己已经支持了，删掉了。）
/// 开关在「设置」页，即时生效。
/// </summary>
public static class TrainingCheatPatch
{
	private static readonly AccessTools.FieldRef<GameStat, int> ValueField = AccessTools.FieldRefAccess<GameStat, int>("_value");

	// ---- 修炼（MartialPanel）：投点在 StartHandleUpgarde 协程开头扣武学点和次数 ----

	[HarmonyPostfix]
	[HarmonyPatch(typeof(MartialPanel), "StartHandleUpgarde")]
	public static void Martial_Postfix(MartialPanel __instance, ref IEnumerator __result)
	{
		if (ConfigManager.CheatUnlimitedTraining.Value)
		{
			__result = NoCost(__result, __instance, "_martialPoint");
		}
	}

	// ---- 锻造 / 炼丹（UpgradePanel）：同样在 StartHandleUpgarde 开头扣点数和次数（银两由下面的开关管） ----

	[HarmonyPostfix]
	[HarmonyPatch(typeof(UpgradePanel), "StartHandleUpgarde")]
	public static void Upgrade_Postfix(UpgradePanel __instance, ref IEnumerator __result)
	{
		if (ConfigManager.CheatUnlimitedUpgrade.Value)
		{
			__result = NoCost(__result, __instance, "_pointData");
		}
	}

	// 锻造 / 炼丹不消耗银两：花费读取的都是 CostMoney，返回 0 即不扣钱、不受银两限制。
	[HarmonyPostfix]
	[HarmonyPatch(typeof(UpgradeItemData), "CostMoney", MethodType.Getter)]
	public static void UpgradeItemData_CostMoney_Postfix(ref int __result)
	{
		if (ConfigManager.CheatFreeUpgradeMoney.Value)
		{
			__result = 0;
		}
	}

	// 开发新装备、新药不看剧情条件：开发项上「其他条件」（多是剧情旗标）一律算满足，界面上的条件也显示成满足
	[HarmonyPostfix]
	[HarmonyPatch(typeof(UpgradeDevelopItem), nameof(UpgradeDevelopItem.CheckConditions))]
	public static void Develop_Postfix(ref bool __result)
	{
		if (ConfigManager.CheatDevelopIgnoreStory.Value)
		{
			__result = true;
		}
	}

	/// <summary>原协程第一步扣点数和次数：记下扣之前的值，第一步走完换回去，后面的动画照常。</summary>
	private static IEnumerator NoCost(IEnumerator inner, object panel, string pointField)
	{
		var tr = Traverse.Create(panel);
		GameStat point = tr.Field(pointField).GetValue<GameStat>();
		int value = point != null ? ValueField(point) : 0;
		int count = tr.Field("_currentCount").GetValue<int>();
		bool first = true;
		while (inner.MoveNext())
		{
			if (first)
			{
				first = false;
				if (point != null)
				{
					ValueField(point) = value;
				}
				tr.Field("_currentCount").SetValue(count);
			}
			yield return inner.Current;
		}
		if (first)
		{
			if (point != null)
			{
				ValueField(point) = value;
			}
			tr.Field("_currentCount").SetValue(count);
		}
	}
}
