using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LOMAssistant.Features.Extras;
using LOMAssistant.Features.Story;
using Mortal.Core;
using UnityEngine;

namespace LOMAssistant.Features.Ai;

/// <summary>
/// 给 AI 的 item 工具：按名字查物品（书、杂物、贵重品、消耗品）和铁铺、炼丹房、制毒的开发项目。
/// 物品：说明、这一局有几个、使用效果、哪些剧情给它、判定它、收走它、哪家店卖、哪个开发项目练满送它。
/// 开发项目：类型、门派规模要求、每点花费、各级点数、这一局开发到哪、从哪个项目进化来、能进化成什么（要几级、其他条件）。
/// </summary>
public static class AiItems
{
	private const int MaxStoryLines = 12;

	public static string Lookup(string query)
	{
		string q = TextConverter.ToSimplified((query ?? "").Trim());
		if (q.Length == 0)
		{
			return "请给物品或开发项目的名字。";
		}
		var sb = new StringBuilder();
		List<UpgradeItemData> upgrades = Resources.FindObjectsOfTypeAll<UpgradeItemData>().Where(u => u != null).Distinct().ToList();
		var items = Resources.FindObjectsOfTypeAll<ItemData>().Where(i => i != null && Name(i).Contains(q)).GroupBy(i => (i.ItemType, i.Id)).Select(g => g.First()).Take(4).ToList();
		foreach (ItemData item in items)
		{
			AppendItem(sb, item, upgrades);
		}
		foreach (UpgradeItemData u in upgrades.Where(u => Name(u).Contains(q)).Take(4))
		{
			AppendUpgrade(sb, u, upgrades);
		}
		return sb.Length > 0 ? sb.ToString() : "没有叫「" + query + "」的物品或开发项目。换个说法（游戏里显示的名字），或者用 search 搜剧情。";
	}

	private static string Text(string key)
	{
		try
		{
			string s = LocalizationManager.Instance.LocaleResolver.GetString(key);
			return string.IsNullOrEmpty(s) || s == key ? "" : AiKnowledge.Clean(TextConverter.ToSimplified(RichText.Strip(s))).Replace("\n", " ").Trim();
		}
		catch
		{
			return "";
		}
	}

	private static string Name(IGameItem item) => Text(item.GetNameKey());

	private static string Name(UpgradeItemData u) => Text(u.GetNameKey());

	private static bool InGame() => PlayerStatManagerData.Instance != null && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Title";

	private static string TypeName(GameItemType t) => t switch
	{
		GameItemType.書籍 => "书籍",
		GameItemType.雜物 => "杂物",
		GameItemType.貴重品 => "贵重品（剧情物品）",
		GameItemType.消耗品 => "消耗品",
		_ => t.ToString(),
	};

	private static void AppendItem(StringBuilder sb, ItemData item, List<UpgradeItemData> upgrades)
	{
		sb.Append("\n【物品「").Append(Name(item)).Append("」】").Append(TypeName(item.ItemType)).Append("，编号 ").Append(item.Id);
		if (item.Cost > 0)
		{
			sb.Append("，价格 ").Append(item.Cost);
		}
		if (InGame())
		{
			sb.Append("；这一局身上有 ").Append(item.CurrentCount).Append(" 个");
		}
		sb.Append('\n');
		string desc = Text(item.GetDescKey());
		if (desc.Length > 0)
		{
			sb.Append("说明：").Append(desc).Append('\n');
		}
		// 使用效果、剧情里判定、收走（同背包说明里的「用途」）
		string uses = TextConverter.ToSimplified(ItemUsePatch.Uses(item)).Trim();
		if (uses.Length > 0)
		{
			sb.Append(uses).Append('\n');
		}
		List<StoryUse> all = StoryUsage.Item(item.ItemType, item.Id) ?? new List<StoryUse>();
		var gains = all.Where(u => u.Kind == "获得").ToList();
		if (gains.Count > 0)
		{
			sb.Append("剧情里给它的地方：\n");
			foreach (StoryUse u in gains.Take(MaxStoryLines))
			{
				sb.Append("- ").Append(AiKnowledge.Clean(u.Detail)).Append("｜剧本 ").Append(u.Script)
					.Append(u.Mission != null ? "（任务 " + u.Mission.Key + "「" + AiKnowledge.Clean(u.Mission.Title) + "」" + (u.Mission.When.Length > 0 ? " " + AiKnowledge.Clean(u.Mission.When) : "") + "）" : "（地点事件、随机事件等）");
				if (u.Path.Count > 0)
				{
					sb.Append("｜走：").Append(string.Join(" → ", u.Path.Select(s => AiKnowledge.Clean(StoryScripts.Describe(s))).ToArray()));
				}
				sb.Append('\n');
			}
			if (gains.Count > MaxStoryLines)
			{
				sb.Append("- ……还有 ").Append(gains.Count - MaxStoryLines).Append(" 处\n");
			}
		}
		var shops = new List<string>();
		foreach (ShopItemsData shop in Resources.FindObjectsOfTypeAll<ShopItemsData>())
		{
			var list = (shop.Books ?? new ShopItem[0]).Concat(shop.Miscs ?? new ShopItem[0]).Concat(shop.Specials ?? new ShopItem[0]).Concat(shop.Consumables ?? new ShopItem[0]);
			ShopItem hit = list.FirstOrDefault(s => s?.Data != null && s.Data.ItemType == item.ItemType && s.Data.Id == item.Id);
			if (hit != null)
			{
				shops.Add(ShopName(shop.name) + "（进货 " + hit.DefaultCount + " 个）");
			}
		}
		if (shops.Count > 0)
		{
			sb.Append("商店有卖：").Append(string.Join("、", shops.Distinct().ToArray())).Append('\n');
		}
		var makers = upgrades.Where(u => (u.ItemModify ?? new UpgradeItemModify[0]).Any(m => m != null && Modified(m) == item)).Select(Name).Where(n => n.Length > 0).ToList();
		if (makers.Count > 0)
		{
			sb.Append("开发练满级时送：").Append(string.Join("、", makers.ToArray())).Append("（详情用 item 查这个开发项目）\n");
		}
		if (gains.Count == 0 && shops.Count == 0 && makers.Count == 0)
		{
			sb.Append("没查到剧情、商店、开发项目给它的地方（可能来自场所事件里的随机结果，可再用 events 或 search 查）。\n");
		}
	}

	// 商店进货表按月份命名：「1-10-ShopItems」「3-05」是第 1 年 10 月、第 3 年 5 月的货
	private static string ShopName(string name)
	{
		var m = System.Text.RegularExpressions.Regex.Match(name ?? "", @"^(\d+)-(\d+)");
		if (m.Success)
		{
			return "第" + int.Parse(m.Groups[1].Value) + "年" + int.Parse(m.Groups[2].Value) + "月的商店";
		}
		return name == "DefaultShopItems" ? "平时的商店" : "商店 " + TextConverter.ToSimplified(name);
	}

	private static ItemData Modified(UpgradeItemModify m)
	{
		try
		{
			return m.GetFieldValue<ItemData>("_item");
		}
		catch
		{
			return null;
		}
	}

	private static string UpgradeType(UpgradeItemType t) => TextConverter.ToSimplified(t.ToString());

	private static void AppendUpgrade(StringBuilder sb, UpgradeItemData u, List<UpgradeItemData> upgrades)
	{
		string kind = UpgradeType(u.ItemType);
		string where = u.ItemType == UpgradeItemType.毒術 ? "制毒" : u.ItemType == UpgradeItemType.煉丹 ? "炼丹" : "铁铺（锻冶）";
		sb.Append("\n【开发项目「").Append(Name(u)).Append("」】").Append(where).Append(" · ").Append(kind)
			.Append("；门派规模要求 ").Append(u.RequiredSectLevel).Append("；每点花 ").Append(u.CostMoney).Append(" 门派资产\n");
		string desc = Text(u.GetDescKey());
		if (desc.Length > 0)
		{
			sb.Append("说明：").Append(desc).Append('\n');
		}
		UpgradeItemLevel[] levels = u.UpgradeLevels ?? new UpgradeItemLevel[0];
		if (levels.Length > 0)
		{
			sb.Append("共 ").Append(levels.Length).Append(" 级，各级要的点数：").Append(string.Join("、", levels.Select(l => l.LevelPoints.ToString()).ToArray())).Append('\n');
		}
		if (InGame())
		{
			sb.Append("这一局：").Append(u.AlreadyDevelop ? "已开发，" + (u.IsMaxLevel ? "已练满" : "第 " + u.CurrentLevel + " 级，这一级已投 " + u.CurrentPoints + " 点") : "还没开发出来").Append('\n');
		}
		// 从哪个项目进化来：开发项目不是一开始都列着，要先把前一个练到几级才会出现
		var from = new List<string>();
		foreach (UpgradeItemData prev in upgrades)
		{
			foreach (UpgradeDevelopItem evo in Evolutions(prev).Where(e => e.NextItem == u))
			{
				from.Add("「" + Name(prev) + "」练到 " + Required(evo) + " 级" + Conditions(evo));
			}
		}
		sb.Append(from.Count > 0 ? "怎么出现在开发列表里：" + string.Join("；或 ", from.ToArray()) + "（满足后在开发页进化出来）\n" : "怎么出现在开发列表里：不是从别的项目进化来的（一开始就有，或由剧情加进来）\n");
		var next = Evolutions(u).Where(e => e.NextItem != null).Select(e => "「" + Name(e.NextItem) + "」（这项练到 " + Required(e) + " 级" + Conditions(e) + "）").ToList();
		if (next.Count > 0)
		{
			sb.Append("能进化成：").Append(string.Join("、", next.ToArray())).Append('\n');
		}
		var gifts = (u.ItemModify ?? new UpgradeItemModify[0]).Where(m => m != null && Modified(m) != null).Select(m => Name(Modified(m)) + " ×" + m.GetFieldValue<int>("_count")).ToList();
		if (gifts.Count > 0)
		{
			sb.Append("练满级送：").Append(string.Join("、", gifts.ToArray())).Append('\n');
		}
	}

	private static IEnumerable<UpgradeDevelopItem> Evolutions(UpgradeItemData u)
	{
		try
		{
			return u.GetFieldValue<UpgradeDevelopItem[]>("_evolutionItems") ?? new UpgradeDevelopItem[0];
		}
		catch
		{
			return new UpgradeDevelopItem[0];
		}
	}

	private static int Required(UpgradeDevelopItem e)
	{
		try
		{
			return e.GetFieldValue<int>("_requiredLevel");
		}
		catch
		{
			return e.RequiredLevel;
		}
	}

	private static string Conditions(UpgradeDevelopItem e)
	{
		var parts = new List<string>();
		foreach (ConditionResultItem c in e.Conditions ?? new ConditionResultItem[0])
		{
			if (c == null)
			{
				continue;
			}
			string text = InGame() ? DataHelper.ExtractConditions(c, out _) : DataHelper.ConditionTemplate(c);
			text = AiKnowledge.Clean(TextConverter.ToSimplified(RichText.Strip(text ?? ""))).Trim();
			if (text.Length > 0)
			{
				parts.Add(text);
			}
		}
		return parts.Count > 0 ? "，另外要 " + string.Join(" 且 ", parts.ToArray()) : "";
	}
}
