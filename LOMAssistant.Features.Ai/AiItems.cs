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
		foreach (PlayerTalentData t in Resources.FindObjectsOfTypeAll<PlayerTalentData>().Where(t => t != null && Text(t.GetIdKey()).Contains(q)).GroupBy(t => t.Id).Select(g => g.First()).Take(4))
		{
			AppendTalent(sb, t, upgrades);
		}
		return sb.Length > 0 ? sb.ToString() : "没有叫「" + query + "」的物品、开发项目或天赋。换个说法（游戏里显示的名字），或者用 search 搜剧情。";
	}

	// ---------- 武学秘笈 ----------

	private static string RefName(MartialStatReference r)
	{
		if (r == null)
		{
			return "";
		}
		string name = Text(r.GetNameKey());
		return name.Length > 0 ? name : TextConverter.ToSimplified(r.StatType.ToString());
	}

	private static string ModifyText(MartialModifyStat m) =>
		RefName(m.Stat) + " " + Signed(m.ModifyValue) + (m.Center ? "（往 50 靠拢，最多改这么多）" : "");

	private static void AppendMartial(StringBuilder sb, Book book)
	{
		var factions = (book.MartialFaction ?? new MartialFactionType[0]).Where(f => f != MartialFactionType.無).Select(f => TextConverter.ToSimplified(f.ToString())).ToArray();
		sb.Append("武学：").Append(TextConverter.ToSimplified(book.MartialType.ToString())).Append(factions.Length > 0 ? "，" + string.Join("、", factions) + "的功夫" : "")
			.Append(book.Complete ? "" : "，残篇（凑齐后合成完整秘笈 " + book.CompleteId + "）").Append("，共 ").Append(book.MaxLevel).Append(" 级");
		if (InGame())
		{
			sb.Append("；这一局练到第 ").Append(book.CurrentMartialLevel).Append(" 级").Append(book.IsMaxLevel ? "（已满）" : "，这一级已投 " + book.CurrentLevelPoints + " 点");
		}
		sb.Append('\n');
		MartialLevelData[] levels = book.LevelData ?? new MartialLevelData[0];
		for (int i = 0; i < levels.Length; i++)
		{
			MartialLevelData lv = levels[i];
			if (lv == null)
			{
				continue;
			}
			sb.Append("- 第 ").Append(i + 1).Append(" 级：要 ").Append(lv.Points).Append(" 点");
			var groups = new List<string>();
			foreach (MartialLearnCompareGroup g in lv.Condition?.List ?? new List<MartialLearnCompareGroup>())
			{
				var items = (g.ItemList ?? new MartialLearnCompareItem[0]).Where(c => c?.Stat != null)
					.Select(c => RefName(c.Stat) + " " + DataHelper.GetCompareOperator(c.CompareOp) + c.Value).ToArray();
				if (items.Length > 0)
				{
					groups.Add(items.Length > 1 ? "（" + string.Join(" 或 ", items) + "）" : items[0]);
				}
			}
			if (groups.Count > 0)
			{
				sb.Append("，修练条件 ").Append(string.Join(" 且 ", groups.ToArray()));
			}
			var up = (lv.LevelUpStat ?? new List<MartialModifyStat>()).Where(m => m?.Stat != null).Select(ModifyText).ToArray();
			if (up.Length > 0)
			{
				sb.Append("，练成时 ").Append(string.Join("、", up));
			}
			foreach (MartialLearnConsumable c in lv.ConsumableList ?? new List<MartialLearnConsumable>())
			{
				var cost = (c.ConsumableList ?? new List<MartialModifyStat>()).Where(m => m?.Stat != null).Select(ModifyText).ToArray();
				if (cost.Length > 0)
				{
					sb.Append("，每次修练消耗 ").Append(string.Join(c.LogicType == LogicOperatorType.OR ? " 或 " : "、", cost));
				}
			}
			sb.Append('\n');
		}
		var related = (book.RelationStatConvert ?? new BookLevelStatConvertData[0]).Where(r => r != null).Select(r => Text(r.GetNameKey())).Where(n => n.Length > 0).Distinct().ToArray();
		if (related.Length > 0)
		{
			sb.Append("等级越高加成越多的：").Append(string.Join("、", related)).Append('\n');
		}
	}

	// ---------- 天赋 ----------

	private static void AppendTalent(StringBuilder sb, PlayerTalentData t, List<UpgradeItemData> upgrades)
	{
		string name = Text(t.GetIdKey());
		sb.Append("\n【天赋「").Append(name).Append("」】最高 ").Append(t.GetFieldValue<int>("_maxLevel")).Append(" 级")
			.Append(t.CombatSkill ? "，决斗里能用的技能" : "").Append(t.BattleSkill ? "，战役里能用的技能" : "");
		if (InGame())
		{
			sb.Append("；这一局 ").Append(t.Level > 0 ? "有，" + t.Level + " 级" : "还没有");
		}
		sb.Append('\n');
		string desc = Text(t.GetDescKey());
		if (desc.Length > 0)
		{
			sb.Append("说明：").Append(desc).Append('\n');
		}
		var from = new List<string>();
		foreach (Book book in Resources.FindObjectsOfTypeAll<Book>().Where(b => b != null && b.IsMartial).GroupBy(b => b.Id).Select(g => g.First()))
		{
			MartialLevelData[] levels = book.LevelData ?? new MartialLevelData[0];
			for (int i = 0; i < levels.Length; i++)
			{
				MartialModifyStat hit = (levels[i]?.LevelUpStat ?? new List<MartialModifyStat>()).FirstOrDefault(m => m?.Stat != null && m.Stat.StatType == MartialLevelupStatType.天賦 && RefName(m.Stat) == name);
				if (hit != null)
				{
					from.Add("秘笈「" + Name(book) + "」练到第 " + (i + 1) + " 级（" + Signed(hit.ModifyValue) + "）");
				}
			}
		}
		foreach (UpgradeItemData u in upgrades)
		{
			UpgradeItemLevel[] levels = u.UpgradeLevels ?? new UpgradeItemLevel[0];
			for (int i = 0; i < levels.Length; i++)
			{
				PlayerTalentData[] talents = levels[i]?.GetFieldValue<PlayerTalentData[]>("_talents") ?? new PlayerTalentData[0];
				if (talents.Any(x => x != null && x.Id == t.Id))
				{
					from.Add("开发项目「" + Name(u) + "」练到第 " + (i + 1) + " 级");
				}
			}
		}
		sb.Append(from.Count > 0 ? "从哪来：" + string.Join("；", from.Distinct().ToArray()) + "\n" : "")
			.Append("剧情里在哪给、在哪判定：用 usage 查「").Append(name).Append("」\n");
	}

	/// <summary>四师兄的商店：现在架上有什么（价格、剩几个、吃了加什么），以及之后哪个月会进什么新货。</summary>
	public static string Shop()
	{
		if (!InGame())
		{
			return "玩家还没读档进游戏，看不到商店。";
		}
		ShopDatabase db = ShopDatabase.Instance;
		if (db == null || db.Miscs == null)
		{
			return "商店还没进货（进过一次唐门、开过一次商店后才有）。";
		}
		var sb = new StringBuilder("【四师兄的商店，现在架上】（价格是原价，好感、身份会打折）\n");
		void Section(string title, List<ShopItem> list)
		{
			var rows = (list ?? new List<ShopItem>()).Where(s => s?.Data != null && s.Count > 0).ToList();
			if (rows.Count == 0)
			{
				return;
			}
			sb.Append(title).Append("：\n");
			foreach (ShopItem s in rows)
			{
				sb.Append("- ").Append(Name(s.Data)).Append("，").Append(s.Data.Cost).Append(" 两，剩 ").Append(s.Count).Append(" 个");
				string effect = Effect(s.Data);
				if (effect.Length > 0)
				{
					sb.Append("｜").Append(effect);
				}
				sb.Append('\n');
			}
		}
		Section("杂物", db.Miscs);
		Section("消耗品", db.Consumables);
		Section("书籍", db.Books);
		Section("贵重品", db.Specials);
		GameTime now = PlayerStatManagerData.Instance.GameTime;
		var later = Resources.FindObjectsOfTypeAll<ShopItemTimeData>()
			.Where(t => t != null && t.ItemData != null && !t.IsTrigger && (t.Year > now.Year || (t.Year == now.Year && t.Month > now.Month)))
			.OrderBy(t => t.Year).ThenBy(t => t.Month).Take(8).ToList();
		if (later.Count > 0)
		{
			sb.Append("之后会进的新货：\n");
			foreach (ShopItemTimeData t in later)
			{
				ShopItemsData d = t.ItemData;
				var names = (d.Books ?? new ShopItem[0]).Concat(d.Miscs ?? new ShopItem[0]).Concat(d.Specials ?? new ShopItem[0]).Concat(d.Consumables ?? new ShopItem[0])
					.Where(s => s?.Data != null).Select(s => Name(s.Data)).Where(n => n.Length > 0).Distinct().ToArray();
				if (names.Length > 0)
				{
					sb.Append("- 第").Append(t.Year).Append("年").Append(t.Month).Append("月").Append(t.CheckMissionStat ? "（还要看任务进度）" : "").Append("：").Append(string.Join("、", names)).Append('\n');
				}
			}
		}
		return sb.ToString();
	}

	/// <summary>
	/// 门派经营：门派规模、资产、名声、人数、向心力，各设施的等级、每旬收入和每旬加减的数值（设施等级是一面旗标，剧情里升级，用 usage 查在哪升）。
	/// 每旬结束时（行动次数用完进下一旬）所有开着的设施把收入加进门派资产，再结算各自的数值变化。
	/// </summary>
	public static string Sect()
	{
		PlayerStatManagerData player = PlayerStatManagerData.Instance;
		if (!InGame() || player == null)
		{
			return "玩家还没读档进游戏，看不到门派。";
		}
		var sb = new StringBuilder("【门派】");
		var shown = new List<string>();
		foreach (GameStatType t in new[] { GameStatType.門派規模, GameStatType.門派資產, GameStatType.門派名聲, GameStatType.門派人數, GameStatType.向心力 })
		{
			try
			{
				GameStat stat = player.Stats.Get(t);
				if (stat != null)
				{
					shown.Add(StatName(stat) + " " + stat.FinalValue);
				}
			}
			catch
			{
			}
		}
		sb.Append(string.Join("，", shown.ToArray())).Append("\n门派规模决定开发项目能不能开（开发项目的「门派规模要求」），在剧情里提升，用 usage 查「门派规模」看在哪加。\n");
		FacilityConfigData config = player.GetFieldValue<FacilityConfigData>("_facility");
		FacilityItemData[] facilities = config?.Data ?? new FacilityItemData[0];
		if (facilities.Length == 0)
		{
			return sb.ToString();
		}
		sb.Append("设施（每旬结束时，开着的设施把收入加进门派资产，并结算下面的数值变化）：\n");
		foreach (FacilityItemData f in facilities.Where(f => f != null))
		{
			FlagData levelFlag = f.GetFieldValue<FlagData>("_level");
			FacilityLevelData[] levels = f.GetFieldValue<FacilityLevelData[]>("_levelData") ?? new FacilityLevelData[0];
			int level = levelFlag != null ? levelFlag.State : 0;
			string name = Text(f.GetNameKey());
			sb.Append("\n「").Append(name.Length > 0 ? name : f.name).Append("」现在 ").Append(level > 0 ? level + " 级" : "还没建").Append("（共 ").Append(levels.Length).Append(" 级")
				.Append(levelFlag != null ? "，等级记在旗标 " + levelFlag.name + "，用 usage 查它在哪升级" : "").Append("）");
			if (level > 0)
			{
				sb.Append("，这一旬收入 ").Append(f.GetTotalIncome());
				string effect = Text(f.GetEffectKey());
				if (effect.Length > 0)
				{
					sb.Append("；效果：").Append(effect);
				}
			}
			sb.Append('\n');
			for (int i = 0; i < levels.Length; i++)
			{
				if (levels[i] == null)
				{
					continue;
				}
				string mods = Modifies(levels[i].ModifyData);
				sb.Append("- ").Append(i + 1).Append(" 级：每旬收入 ").Append(levels[i].BaseIncome).Append(mods.Length > 0 ? "，每旬 " + mods : "").Append('\n');
			}
			foreach (FacilityAdditionData add in f.GetFieldValue<FacilityAdditionData[]>("_additionData") ?? new FacilityAdditionData[0])
			{
				if (add == null)
				{
					continue;
				}
				FlagData flag = add.GetFieldValue<FlagData>("_flag");
				string note = AiKnowledge.Clean(TextConverter.ToSimplified(add.GetFieldValue<string>("_note") ?? ""));
				string mods = Modifies(add.ModifyItem);
				sb.Append("- 追加").Append(note.Length > 0 ? "「" + note + "」" : "").Append(add.IsActive() ? "（已开）" : "（没开）")
					.Append(flag != null ? "，要旗标 " + DataHelper.GetFlagName(flag) + " (" + flag.name + ")" : "")
					.Append("：收入 +").Append(add.Income).Append(add.RateIncome != 0f ? "、总收入 +" + Mathf.RoundToInt(add.RateIncome * 100) + "%" : "")
					.Append(mods.Length > 0 ? "，每旬 " + mods : "").Append('\n');
			}
		}
		return sb.ToString();
	}

	private static string StatName(GameStat stat) => AiKnowledge.Clean(TextConverter.ToSimplified(string.IsNullOrEmpty(stat.Name) ? stat.StatType.ToString() : stat.Name));

	private static string Modifies(FacilityModifyItem[] items)
	{
		var parts = new List<string>();
		foreach (FacilityModifyItem m in items ?? new FacilityModifyItem[0])
		{
			if (m == null)
			{
				continue;
			}
			var one = new List<string>();
			foreach (FacilityModifyStatItem s in m.Stat ?? new FacilityModifyStatItem[0])
			{
				if (s?.Data != null)
				{
					one.Add(StatName(s.Data) + " " + Signed(s.Value));
				}
			}
			foreach (FacilityModifyRelationshipItem r in m.Relationship ?? new FacilityModifyRelationshipItem[0])
			{
				if (r?.Data != null)
				{
					one.Add(TextConverter.ToSimplified(r.Data.Type.ToString()) + "好感 " + Signed(r.Value));
				}
			}
			foreach (FacilityModifyFlagItem f in m.Flag ?? new FacilityModifyFlagItem[0])
			{
				if (f?.Data != null)
				{
					one.Add(AiKnowledge.Clean(DataHelper.GetFlagName(f.Data)) + " (" + f.Data.name + ") " + Signed(f.Value));
				}
			}
			int random = m.GetFieldValue<int>("_randomCount");
			if (one.Count > 0)
			{
				parts.Add(random > 0 ? "从「" + string.Join("、", one.ToArray()) + "」里随机 " + random + " 项" : string.Join("、", one.ToArray()));
			}
		}
		return string.Join("；", parts.ToArray());
	}

	private static string Signed(int v) => v >= 0 ? "+" + v : v.ToString();

	// 背包说明里的「使用效果：……」那一行（吃喝、使用时一并发生的数值变化）
	private static string Effect(ItemData item)
	{
		if (item.ItemType == GameItemType.書籍)
		{
			return "";
		}
		try
		{
			string uses = TextConverter.ToSimplified(ItemUsePatch.Uses(item));
			string line = uses.Split('\n').FirstOrDefault(l => l.StartsWith("使用效果")) ?? "";
			return AiKnowledge.Clean(line);
		}
		catch
		{
			return "";
		}
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
		if (item is Book book && book.IsMartial)
		{
			AppendMartial(sb, book);
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
			.Append("；门派规模要求 ").Append(u.RequiredSectLevel).Append("；每投 1 点开发点数花 ").Append(u.CostMoney).Append(" 两银子\n");
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
