using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LOMAssistant.Config;
using LOMAssistant.Features.Story;
using Mortal.Core;
using Mortal.Story;
using UnityEngine.UI;

namespace LOMAssistant.Features.Extras;

/// <summary>
/// 物品用途：背包（物品页）选中贵重品、杂物、书，和商店里鼠标停在商品上时，说明后面接一段「用途」，
/// 列出哪些剧情会判定要不要有它、会把它收走（数据同数值页的反查 StoryUsage）。剧本还没读完时不加。
/// 开关在设置页（ShowItemUses），即时生效。
/// </summary>
public static class ItemUsePatch
{
	private const int MaxLines = 4;

	[HarmonyPostfix]
	[HarmonyPatch(typeof(ItemStatPanel), nameof(ItemStatPanel.OnItemSelected))]
	public static void Bag_Postfix(ItemStatPanel __instance, IGameItem item)
	{
		Append(Traverse.Create(__instance).Field("_descText").GetValue<Text>(), item);
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(ShopPanel), nameof(ShopPanel.OnItemHover))]
	public static void Shop_Postfix(ShopPanel __instance, IGameItem item)
	{
		Append(Traverse.Create(__instance).Field("_dialogDesc").GetValue<Text>(), item);
	}

	private static void Append(Text desc, IGameItem item)
	{
		try
		{
			if (!ConfigManager.ShowItemUses.Value || desc == null || item == null || item.ItemType == GameItemType.消耗品)
			{
				return;
			}
			string uses = Uses(item);
			if (uses.Length > 0)
			{
				desc.text = (desc.text ?? "").TrimEnd() + "\n\n" + uses;
			}
		}
		catch (Exception ex)
		{
			FileLogger.Log("[物品用途] 出错（已忽略）：" + ex.Message);
		}
	}

	/// <summary>
	/// 说明后面接的两段：「使用效果」——吃喝、使用它的那条分岔上一并发生的数值变化（养成地图「食用」菜单这类）；
	/// 「剧情里用到」——哪些任务会判定要不要有它、会把它收走（使用菜单那几处不重复列）。都没有就写一句没有。
	/// </summary>
	internal static string Uses(IGameItem item)
	{
		List<StoryUse> all = StoryUsage.Item(item.ItemType, item.Id);
		if (all == null)
		{
			return "";
		}
		string name = DataHelper.GetItemName(item.ItemType, item.Id) ?? "";
		var effects = new List<string>();
		var effectUses = new HashSet<StoryUse>();
		foreach (StoryUse use in all.Where(u => u.Kind == "失去"))
		{
			StoryScript script = StoryScripts.Get(use.Script);
			if (script == null)
			{
				continue;
			}
			// 同一条分岔（路径完全一样）上的其他变化，去掉物品本身的增减
			var here = script.Effects.Where(e => SamePath(e.Path, use.Path) && (name.Length == 0 || !e.Text.Contains(name)))
				.Select(e => e.Text).Where(t => !t.Contains("暂存")).Distinct().ToList();
			if (here.Count > 0)
			{
				effectUses.Add(use);
				string line = string.Join("、", here.Select(TextConverter.ForDisplay).ToArray());
				if (!effects.Contains(line))
				{
					effects.Add(line);
				}
			}
		}
		var parts = new List<string>();
		if (effects.Count > 0)
		{
			parts.Add(TextConverter.ForDisplay("使用效果：") + string.Join("；", effects.Take(2).ToArray()));
		}
		var lines = all.Where(u => (!u.Changes || u.Kind == "失去") && !effectUses.Contains(u))
			.GroupBy(u => u.Mission?.Title ?? "")
			.Select(g =>
			{
				bool check = g.Any(u => !u.Changes);
				bool take = g.Any(u => u.Kind == "失去");
				string what = check && take ? "判定，会收走" : check ? "判定要不要有它" : "会收走";
				string where = g.Key.Length > 0 ? "「" + TextConverter.ForDisplay(g.Key) + "」" : TextConverter.ForDisplay("其他剧情");
				return "· " + where + TextConverter.ForDisplay(what);
			})
			.Distinct()
			.ToList();
		if (lines.Count > 0)
		{
			var shown = lines.Take(MaxLines).ToList();
			if (lines.Count > MaxLines)
			{
				shown.Add("· " + string.Format(TextConverter.ForDisplay("另有 {0} 处"), lines.Count - MaxLines));
			}
			parts.Add(TextConverter.ForDisplay("剧情里用到：") + "\n" + string.Join("\n", shown.ToArray()));
		}
		if (parts.Count == 0)
		{
			parts.Add(TextConverter.ForDisplay("用途：") + TextConverter.ForDisplay("剧情里没有判定或收走它的地方"));
		}
		return string.Join("\n", parts.ToArray());
	}

	private static bool SamePath(List<StoryStep> a, List<StoryStep> b) =>
		a.Count == b.Count && a.Zip(b, (x, y) => x.Kind == y.Kind && x.Key == y.Key && x.Arm == y.Arm).All(v => v);
}
