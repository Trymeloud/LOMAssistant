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
			return AiText.T("请给物品或开发项目的名字。", "물품 또는 개발 프로젝트 이름을 입력해 주세요.");
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
		return sb.Length > 0 ? sb.ToString() : AiText.T("没有叫「", "「") + query + AiText.T("」的物品、开发项目或天赋。换个说法（游戏里显示的名字），或者用 search 搜剧情。", "」 물품·개발 프로젝트·재능을 찾지 못했습니다. 게임에 표시된 이름으로 검색하거나 search로 이야기를 검색해 보세요.");
	}

	// ---------- 武学秘笈 ----------

	private static string RefName(MartialStatReference r)
	{
		if (r == null)
		{
			return "";
		}
		string name = Text(r.GetNameKey());
		return name.Length > 0 ? name : AiText.Label(TextConverter.ToSimplified(r.StatType.ToString()));
	}

	private static string ModifyText(MartialModifyStat m) =>
		RefName(m.Stat) + " " + Signed(m.ModifyValue) + (m.Center ? AiText.T("（往 50 靠拢，最多改这么多）", "(50에 가까워지는 방향으로, 최대 이 수치만큼 변화)") : "");

	private static void AppendMartial(StringBuilder sb, Book book)
	{
		var factions = (book.MartialFaction ?? new MartialFactionType[0]).Where(f => f != MartialFactionType.無).Select(f => AiText.Label(TextConverter.ToSimplified(f.ToString()))).ToArray();
		sb.Append(AiText.T("武学：", "무학: ")).Append(AiText.Label(TextConverter.ToSimplified(book.MartialType.ToString()))).Append(factions.Length > 0 ? "，" + string.Join("、", factions) + AiText.T("的功夫", " 소속 무공") : "")
			.Append(book.Complete ? "" : AiText.T("，残篇（凑齐后合成完整秘笈 ", ", 잔편(모으면 완성 비급으로 합성: ") + book.CompleteId + "）").Append(AiText.T("，共 ", ", 총 ")).Append(book.MaxLevel).Append(AiText.T(" 级", "레벨"));
		if (InGame())
		{
			sb.Append(AiText.T("；这一局练到第 ", "; 이번 회차 현재 레벨 ")).Append(book.CurrentMartialLevel).Append(AiText.T(" 级", "레벨")).Append(book.IsMaxLevel ? AiText.T("（已满）", "(최대)") : AiText.T("，这一级已投 ", ", 현재 레벨에 투자한 점수 ") + book.CurrentLevelPoints + AiText.T(" 点", "점"));
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
			sb.Append(AiText.T("- 第 ", "- 레벨 ")).Append(i + 1).Append(AiText.T(" 级：要 ", ": 필요 점수 ")).Append(lv.Points).Append(AiText.T(" 点", "점"));
			var groups = new List<string>();
			foreach (MartialLearnCompareGroup g in lv.Condition?.List ?? new List<MartialLearnCompareGroup>())
			{
				var items = (g.ItemList ?? new MartialLearnCompareItem[0]).Where(c => c?.Stat != null)
					.Select(c => RefName(c.Stat) + " " + DataHelper.GetCompareOperator(c.CompareOp) + c.Value).ToArray();
				if (items.Length > 0)
				{
					groups.Add(items.Length > 1 ? "（" + string.Join(AiText.T(" 或 ", " 또는 "), items) + "）" : items[0]);
				}
			}
			if (groups.Count > 0)
			{
				sb.Append(AiText.T("，修练条件 ", ", 수련 조건 ")).Append(string.Join(AiText.T(" 且 ", " 그리고 "), groups.ToArray()));
			}
			var up = (lv.LevelUpStat ?? new List<MartialModifyStat>()).Where(m => m?.Stat != null).Select(ModifyText).ToArray();
			if (up.Length > 0)
			{
				sb.Append(AiText.T("，练成时 ", ", 수련 성취 시 ")).Append(string.Join("、", up));
			}
			foreach (MartialLearnConsumable c in lv.ConsumableList ?? new List<MartialLearnConsumable>())
			{
				var cost = (c.ConsumableList ?? new List<MartialModifyStat>()).Where(m => m?.Stat != null).Select(ModifyText).ToArray();
				if (cost.Length > 0)
				{
					sb.Append(AiText.T("，每次修练消耗 ", ", 매번 수련 시 소모 ")).Append(string.Join(c.LogicType == LogicOperatorType.OR ? AiText.T(" 或 ", " 또는 ") : "、", cost));
				}
			}
			sb.Append('\n');
		}
		var related = (book.RelationStatConvert ?? new BookLevelStatConvertData[0]).Where(r => r != null).Select(r => Text(r.GetNameKey())).Where(n => n.Length > 0).Distinct().ToArray();
		if (related.Length > 0)
		{
			sb.Append(AiText.T("等级越高加成越多的：", "레벨이 높아질수록 증가하는 보너스: ")).Append(string.Join("、", related)).Append('\n');
		}
	}

	// ---------- 天赋 ----------

	private static void AppendTalent(StringBuilder sb, PlayerTalentData t, List<UpgradeItemData> upgrades)
	{
		string name = Text(t.GetIdKey());
		sb.Append(AiText.T("\n【天赋「", "\n[재능 「")).Append(name).Append(AiText.T("」】最高 ", "」] 최대 ")).Append(t.GetFieldValue<int>("_maxLevel")).Append(AiText.T(" 级", "레벨"))
			.Append(t.CombatSkill ? AiText.T("，决斗里能用的技能", ", 결투에서 사용 가능한 기술") : "").Append(t.BattleSkill ? AiText.T("，战役里能用的技能", ", 전투에서 사용 가능한 기술") : "");
		if (InGame())
		{
			sb.Append(AiText.T("；这一局 ", "; 이번 회차: ")).Append(t.Level > 0 ? AiText.T("有，", "보유, ") + t.Level + AiText.T(" 级", "레벨") : AiText.T("还没有", "미보유"));
		}
		sb.Append('\n');
		string desc = Text(t.GetDescKey());
		if (desc.Length > 0)
		{
			sb.Append(AiText.T("说明：", "설명: ")).Append(desc).Append('\n');
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
					from.Add(AiText.T("秘笈「", "비급 「") + Name(book) + AiText.T("」练到第 ", "」 레벨 ") + (i + 1) + AiText.T(" 级（", " 달성(") + Signed(hit.ModifyValue) + "）");
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
					from.Add(AiText.T("开发项目「", "개발 프로젝트 「") + Name(u) + AiText.T("」练到第 ", "」 레벨 ") + (i + 1) + AiText.T(" 级", " 달성"));
				}
			}
		}
		sb.Append(from.Count > 0 ? AiText.T("从哪来：", "획득 방법: ") + string.Join("；", from.Distinct().ToArray()) + "\n" : "")
			.Append(AiText.T("剧情里在哪给、在哪判定：用 usage 查「", "이야기에서 획득·판정되는 곳: usage 검색어 「")).Append(name).Append("」\n");
	}

	/// <summary>四师兄的商店：现在架上有什么（价格、剩几个、吃了加什么），以及之后哪个月会进什么新货。</summary>
	public static string Shop()
	{
		if (!InGame())
		{
			return AiText.T("玩家还没读档进游戏，看不到商店。", "저장을 불러오기 전에는 상점을 확인할 수 없습니다.");
		}
		ShopDatabase db = ShopDatabase.Instance;
		if (db == null || db.Miscs == null)
		{
			return AiText.T("商店还没进货（进过一次唐门、开过一次商店后才有）。", "상점이 아직 입고되지 않았습니다(당문에 들어가 상점을 한 번 열어야 합니다).");
		}
		var sb = new StringBuilder(AiText.T("【四师兄的商店，现在架上】（价格是原价，好感、身份会打折）\n", "[사사형 상점의 현재 물품](원가이며 호감도와 신분에 따라 할인됩니다.)\n"));
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
				sb.Append("- ").Append(Name(s.Data)).Append("，").Append(s.Data.Cost).Append(AiText.T(" 两，剩 ", "냥, 남은 수량 ")).Append(s.Count).Append(AiText.T(" 个", "개"));
				string effect = Effect(s.Data);
				if (effect.Length > 0)
				{
					sb.Append("｜").Append(effect);
				}
				sb.Append('\n');
			}
		}
		Section(AiText.T("杂物", "잡화"), db.Miscs);
		Section(AiText.T("消耗品", "소모품"), db.Consumables);
		Section(AiText.T("书籍", "서적"), db.Books);
		Section(AiText.T("贵重品", "귀중품"), db.Specials);
		GameTime now = PlayerStatManagerData.Instance.GameTime;
		var later = Resources.FindObjectsOfTypeAll<ShopItemTimeData>()
			.Where(t => t != null && t.ItemData != null && !t.IsTrigger && (t.Year > now.Year || (t.Year == now.Year && t.Month > now.Month)))
			.OrderBy(t => t.Year).ThenBy(t => t.Month).Take(8).ToList();
		if (later.Count > 0)
		{
			sb.Append(AiText.T("之后会进的新货：\n", "추후 입고 물품:\n"));
			foreach (ShopItemTimeData t in later)
			{
				ShopItemsData d = t.ItemData;
				var names = (d.Books ?? new ShopItem[0]).Concat(d.Miscs ?? new ShopItem[0]).Concat(d.Specials ?? new ShopItem[0]).Concat(d.Consumables ?? new ShopItem[0])
					.Where(s => s?.Data != null).Select(s => Name(s.Data)).Where(n => n.Length > 0).Distinct().ToArray();
				if (names.Length > 0)
				{
					sb.Append(AiText.T("- 第", "- ")).Append(t.Year).Append(AiText.T("年", "년차 ")).Append(t.Month).Append(AiText.T("月", "월")).Append(t.CheckMissionStat ? AiText.T("（还要看任务进度）", "(임무 진행도도 필요)") : "").Append("：").Append(string.Join("、", names)).Append('\n');
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
			return AiText.T("玩家还没读档进游戏，看不到门派。", "저장을 불러오기 전에는 문파를 확인할 수 없습니다.");
		}
		var sb = new StringBuilder(AiText.T("【门派】", "[문파]"));
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
		sb.Append(string.Join("，", shown.ToArray())).Append(AiText.T("\n门派规模决定开发项目能不能开（开发项目的「门派规模要求」），在剧情里提升，用 usage 查「门派规模」看在哪加。\n", "\n문파 규모는 개발 프로젝트 해금에 필요하며 이야기에서 높일 수 있습니다. usage에서 '문파 규모'를 검색해 상승 지점을 확인하세요.\n"));
		FacilityConfigData config = player.GetFieldValue<FacilityConfigData>("_facility");
		FacilityItemData[] facilities = config?.Data ?? new FacilityItemData[0];
		if (facilities.Length == 0)
		{
			return sb.ToString();
		}
		sb.Append(AiText.T("设施（每旬结束时，开着的设施把收入加进门派资产，并结算下面的数值变化）：\n", "시설(매 순 종료 시 가동 중인 시설의 수입이 문파 자산에 반영되고 아래 수치 변화가 정산됩니다):\n"));
		foreach (FacilityItemData f in facilities.Where(f => f != null))
		{
			FlagData levelFlag = f.GetFieldValue<FlagData>("_level");
			FacilityLevelData[] levels = f.GetFieldValue<FacilityLevelData[]>("_levelData") ?? new FacilityLevelData[0];
			int level = levelFlag != null ? levelFlag.State : 0;
			string name = Text(f.GetNameKey());
			sb.Append("\n「").Append(name.Length > 0 ? name : f.name).Append(AiText.T("」现在 ", "」 현재 ")).Append(level > 0 ? level + AiText.T(" 级", "레벨") : AiText.T("还没建", "미건설")).Append(AiText.T("（共 ", "(총 ")).Append(levels.Length).Append(AiText.T(" 级", "레벨"))
				.Append(levelFlag != null ? AiText.T("，等级记在旗标 ", ", 레벨 기록 플래그 ") + levelFlag.name + AiText.T("，用 usage 查它在哪升级", ", usage로 상승 지점 확인") : "").Append("）");
			if (level > 0)
			{
				sb.Append(AiText.T("，这一旬收入 ", ", 이번 순 수입 ")).Append(f.GetTotalIncome());
				string effect = Text(f.GetEffectKey());
				if (effect.Length > 0)
				{
					sb.Append(AiText.T("；效果：", "; 효과: ")).Append(effect);
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
				sb.Append("- ").Append(i + 1).Append(AiText.T(" 级：每旬收入 ", "레벨: 매 순 수입 ")).Append(levels[i].BaseIncome).Append(mods.Length > 0 ? AiText.T("，每旬 ", ", 매 순 ") + mods : "").Append('\n');
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
				sb.Append(AiText.T("- 追加", "- 추가 효과")).Append(note.Length > 0 ? "「" + note + "」" : "").Append(add.IsActive() ? AiText.T("（已开）", "(활성)") : AiText.T("（没开）", "(비활성)"))
					.Append(flag != null ? AiText.T("，要旗标 ", ", 필요 플래그 ") + DataHelper.GetFlagName(flag) + " (" + flag.name + ")" : "")
					.Append(AiText.T("：收入 +", ": 수입 +")).Append(add.Income).Append(add.RateIncome != 0f ? AiText.T("、总收入 +", ", 총수입 +") + Mathf.RoundToInt(add.RateIncome * 100) + "%" : "")
					.Append(mods.Length > 0 ? AiText.T("，每旬 ", ", 매 순 ") + mods : "").Append('\n');
			}
		}
		return sb.ToString();
	}

	/// <summary>
	/// 图鉴（讲经堂）奖励：开二周目（新游戏+）时按收集了几条生死簿、汗青书加命运等，几条成就另有奖励。
	/// 数据同游戏的 SaveSystem.ExecuteLibraryAwardStat：每个 LibraryAwardData 收集数到了门槛就给，成就条目自带奖励。
	/// </summary>
	public static string LibraryAwards()
	{
		var sb = new StringBuilder(AiText.T("【图鉴奖励】开二周目（新游戏+）时一次性给，按全部存档累计收集的条数算：\n", "[도감 보상] 새 게임+ 시작 시 한 번 지급됩니다. 모든 저장의 누적 수집 개수 기준입니다:\n"));
		foreach (LibraryAwardData award in Resources.FindObjectsOfTypeAll<LibraryAwardData>().Where(a => a?.Data != null).Distinct())
		{
			int have = award.Data.GetAchieveCount();
			string what = award.Data == LibrarySystem.Instance?.Dead ? AiText.T("生死簿", "생사부") : award.Data == LibrarySystem.Instance?.EndGame ? AiText.T("汗青书（结局）", "한청서(결말)") : TextConverter.ToSimplified(award.Data.name);
			var tiers = (award.AwardList ?? new LibraryAwardItem[0]).Where(t => t != null).ToList();
			if (tiers.Count == 0)
			{
				continue;
			}
			var gifts = tiers.Select(t => AiText.T("收集 ", "수집 ") + t.Count + AiText.T(" 条：", "개: ") + string.Join("、", (t.Items ?? new LibraryAwardStatItem[0]).Where(i => i?.Stat != null).Select(i => StatName(i.Stat) + " " + Signed(i.Value)).ToArray())).ToList();
			// 档位都一样时合起来说
			var first = tiers[0].Items ?? new LibraryAwardStatItem[0];
			bool same = tiers.All(t => (t.Items ?? new LibraryAwardStatItem[0]).Length == first.Length && (t.Items ?? new LibraryAwardStatItem[0]).Select((i, k) => i?.Stat == first[k]?.Stat && i?.Value == first[k]?.Value).All(x => x));
			int step = tiers.Count > 1 ? tiers[1].Count - tiers[0].Count : tiers[0].Count;
			sb.Append("- ").Append(what).Append("：");
			if (same && tiers.Select((t, k) => t.Count == tiers[0].Count + k * step).All(x => x))
			{
				sb.Append(AiText.T("每收集 ", "수집할 때마다: ")).Append(step).Append(AiText.T(" 条 ", "개 → ")).Append(string.Join("、", first.Where(i => i?.Stat != null).Select(i => StatName(i.Stat) + " " + Signed(i.Value)).ToArray()))
					.Append(AiText.T("，最多算到 ", ", 최대 인정 개수 ")).Append(tiers[tiers.Count - 1].Count).Append(AiText.T(" 条", "개"));
			}
			else
			{
				sb.Append(string.Join("；", gifts.ToArray()));
			}
			sb.Append(AiText.T("。现在收集了 ", ". 현재 수집 ")).Append(have).Append(AiText.T(" 条，二周目能拿 ", "개, 새 게임+에서 받을 보상 단계 ")).Append(tiers.Count(t => t.Count <= have)).Append(AiText.T(" 档\n", "단계\n"));
		}
		LibraryItemCollection achievements = LibrarySystem.Instance?.Achievement;
		var special = Resources.FindObjectsOfTypeAll<LibraryItemData>().Where(i => i != null && (i.AwardStatItems ?? new LibraryAwardStatItem[0]).Length > 0).Distinct().ToList();
		if (special.Count > 0)
		{
			sb.Append(AiText.T("- 成就（风云史）自带的奖励：\n", "- 업적(풍운사) 자체 보상:\n"));
			foreach (LibraryItemData item in special)
			{
				string title = Text(item.GetTitleKey());
				sb.Append("  · 「").Append(title.Length > 0 ? title : item.name).Append("」").Append(item.Count > 0 ? AiText.T("（已达成）", "(달성)") : AiText.T("（未达成）", "(미달성)")).Append("：")
					.Append(string.Join("、", item.AwardStatItems.Where(i => i?.Stat != null).Select(i => StatName(i.Stat) + " " + Signed(i.Value)).ToArray())).Append('\n');
			}
		}
		sb.Append(AiText.T("额外行动次数_1、_2、_3 分别是每月上旬、中旬、下旬多一次行动。\n", "추가 행동 횟수의 _1·_2·_3은 각각 매월 상순·중순·하순의 행동 1회 추가를 뜻합니다.\n"));
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
					one.Add(AiText.Label(TextConverter.ToSimplified(r.Data.Type.ToString())) + AiText.T("好感 ", " 호감도 ") + Signed(r.Value));
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
				parts.Add(random > 0 ? AiText.T("从「", "「") + string.Join("、", one.ToArray()) + AiText.T("」里随机 ", "」 중 무작위 선택 ") + random + AiText.T(" 项", "개") : string.Join("、", one.ToArray()));
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
			string line = uses.Split('\n').FirstOrDefault(l => l.StartsWith("使用效果")
				|| NativeKorean.Enabled && l.StartsWith(TextConverter.ForDisplay("使用效果"))) ?? "";
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
		GameItemType.書籍 => AiText.T("书籍", "서적"),
		GameItemType.雜物 => AiText.T("杂物", "잡화"),
		GameItemType.貴重品 => AiText.T("贵重品（剧情物品）", "귀중품(스토리 물품)"),
		GameItemType.消耗品 => AiText.T("消耗品", "소모품"),
		_ => t.ToString(),
	};

	private static void AppendItem(StringBuilder sb, ItemData item, List<UpgradeItemData> upgrades)
	{
		sb.Append(AiText.T("\n【物品「", "\n【물품 「")).Append(Name(item)).Append("」】").Append(TypeName(item.ItemType)).Append(AiText.T("，编号 ", ", ID ")).Append(item.Id);
		if (item.Cost > 0)
		{
			sb.Append(AiText.T("，价格 ", ", 가격 ")).Append(item.Cost);
		}
		if (InGame())
		{
			sb.Append(AiText.T("；这一局身上有 ", "; 이번 회차 보유 ")).Append(item.CurrentCount).Append(AiText.T(" 个", "개"));
		}
		sb.Append('\n');
		string desc = Text(item.GetDescKey());
		if (desc.Length > 0)
		{
			sb.Append(AiText.T("说明：", "설명: ")).Append(desc).Append('\n');
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
			sb.Append(AiText.T("剧情里给它的地方：\n", "이야기에서 받는 곳:\n"));
			foreach (StoryUse u in gains.Take(MaxStoryLines))
			{
				sb.Append("- ").Append(AiKnowledge.Clean(u.Detail)).Append(AiText.T("｜剧本 ", " | 스크립트 ")).Append(u.Script)
					.Append(u.Mission != null ? AiText.T("（任务 ", "(임무 ") + u.Mission.Key + "「" + AiKnowledge.Clean(u.Mission.Title) + "」" + (u.Mission.When.Length > 0 ? " " + AiKnowledge.Clean(u.Mission.When) : "") + "）" : AiText.T("（地点事件、随机事件等）", "(장소·랜덤 이벤트 등)"));
				if (u.Path.Count > 0)
				{
					sb.Append(AiText.T("｜走：", " | 경로: ")).Append(string.Join(" → ", u.Path.Select(s => AiKnowledge.Clean(StoryScripts.Describe(s))).ToArray()));
				}
				sb.Append('\n');
			}
			if (gains.Count > MaxStoryLines)
			{
				sb.Append(AiText.T("- ……还有 ", "- …추가 ")).Append(gains.Count - MaxStoryLines).Append(AiText.T(" 处\n", "곳\n"));
			}
		}
		var shops = new List<string>();
		foreach (ShopItemsData shop in Resources.FindObjectsOfTypeAll<ShopItemsData>())
		{
			var list = (shop.Books ?? new ShopItem[0]).Concat(shop.Miscs ?? new ShopItem[0]).Concat(shop.Specials ?? new ShopItem[0]).Concat(shop.Consumables ?? new ShopItem[0]);
			ShopItem hit = list.FirstOrDefault(s => s?.Data != null && s.Data.ItemType == item.ItemType && s.Data.Id == item.Id);
			if (hit != null)
			{
				shops.Add(ShopName(shop.name) + AiText.T("（进货 ", "(입고 ") + hit.DefaultCount + AiText.T(" 个）", "개)"));
			}
		}
		if (shops.Count > 0)
		{
			sb.Append(AiText.T("商店有卖：", "판매 상점: ")).Append(string.Join("、", shops.Distinct().ToArray())).Append('\n');
		}
		var makers = upgrades.Where(u => (u.ItemModify ?? new UpgradeItemModify[0]).Any(m => m != null && Modified(m) == item)).Select(Name).Where(n => n.Length > 0).ToList();
		if (makers.Count > 0)
		{
			sb.Append(AiText.T("开发练满级时送：", "최대 레벨 달성 보상 프로젝트: ")).Append(string.Join("、", makers.ToArray())).Append(AiText.T("（详情用 item 查这个开发项目）\n", "(item으로 해당 개발 프로젝트 상세 확인)\n"));
		}
		if (gains.Count == 0 && shops.Count == 0 && makers.Count == 0)
		{
			sb.Append(AiText.T("没查到剧情、商店、开发项目给它的地方（可能来自场所事件里的随机结果，可再用 events 或 search 查）。\n", "이야기·상점·개발 프로젝트에서 획득처를 찾지 못했습니다. 장소 이벤트의 무작위 결과일 수 있으므로 events 또는 search로 추가 확인할 수 있습니다.\n"));
		}
	}

	// 商店进货表按月份命名：「1-10-ShopItems」「3-05」是第 1 年 10 月、第 3 年 5 月的货
	private static string ShopName(string name)
	{
		var m = System.Text.RegularExpressions.Regex.Match(name ?? "", @"^(\d+)-(\d+)");
		if (m.Success)
		{
			return AiText.T("第", "") + int.Parse(m.Groups[1].Value) + AiText.T("年", "년차 ") + int.Parse(m.Groups[2].Value) + AiText.T("月的商店", "월 상점");
		}
		return name == "DefaultShopItems" ? AiText.T("平时的商店", "평소 상점") : AiText.T("商店 ", "상점 ") + TextConverter.ToSimplified(name);
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

	private static string UpgradeType(UpgradeItemType t) => AiText.Label(TextConverter.ToSimplified(t.ToString()));

	private static void AppendUpgrade(StringBuilder sb, UpgradeItemData u, List<UpgradeItemData> upgrades)
	{
		string kind = UpgradeType(u.ItemType);
		string where = u.ItemType == UpgradeItemType.毒術 ? AiText.T("制毒", "독 제조") : u.ItemType == UpgradeItemType.煉丹 ? AiText.T("炼丹", "연단") : AiText.T("铁铺（锻冶）", "대장간(단야)");
		sb.Append(AiText.T("\n【开发项目「", "\n【개발 프로젝트 「")).Append(Name(u)).Append("」】").Append(where).Append(" · ").Append(kind)
			.Append(AiText.T("；门派规模要求 ", "; 필요 문파 규모 ")).Append(u.RequiredSectLevel).Append(AiText.T("；每投 1 点开发点数花 ", "; 개발 점수 1점당 비용 ")).Append(u.CostMoney).Append(AiText.T(" 两银子\n", "냥\n"));
		string desc = Text(u.GetDescKey());
		if (desc.Length > 0)
		{
			sb.Append(AiText.T("说明：", "설명: ")).Append(desc).Append('\n');
		}
		UpgradeItemLevel[] levels = u.UpgradeLevels ?? new UpgradeItemLevel[0];
		if (levels.Length > 0)
		{
			sb.Append(AiText.T("共 ", "총 ")).Append(levels.Length).Append(AiText.T(" 级，各级要的点数：", "레벨, 레벨별 필요 점수: ")).Append(string.Join("、", levels.Select(l => l.LevelPoints.ToString()).ToArray())).Append('\n');
		}
		if (InGame())
		{
			sb.Append(AiText.T("这一局：", "이번 회차: ")).Append(u.AlreadyDevelop ? AiText.T("已开发，", "개발됨, ") + (u.IsMaxLevel ? AiText.T("已练满", "최대 레벨 달성") : AiText.T("第 ", "레벨 ") + u.CurrentLevel + AiText.T(" 级，这一级已投 ", ", 현재 레벨에 투자한 점수 ") + u.CurrentPoints + AiText.T(" 点", "점")) : AiText.T("还没开发出来", "아직 개발되지 않음")).Append('\n');
		}
		// 从哪个项目进化来：开发项目不是一开始都列着，要先把前一个练到几级才会出现
		var from = new List<string>();
		foreach (UpgradeItemData prev in upgrades)
		{
			foreach (UpgradeDevelopItem evo in Evolutions(prev).Where(e => e.NextItem == u))
			{
				from.Add("「" + Name(prev) + AiText.T("」练到 ", "」 레벨 ") + Required(evo) + AiText.T(" 级", " 달성") + Conditions(evo));
			}
		}
		sb.Append(from.Count > 0 ? AiText.T("怎么出现在开发列表里：", "개발 목록에 나타나는 조건: ") + string.Join(AiText.T("；或 ", "; 또는 "), from.ToArray()) + AiText.T("（满足后在开发页进化出来）\n", "(충족 후 개발 화면에서 파생)\n") : AiText.T("怎么出现在开发列表里：不是从别的项目进化来的（一开始就有，或由剧情加进来）\n", "개발 목록에 나타나는 조건: 다른 프로젝트에서 파생되지 않음(처음부터 존재하거나 이야기에서 추가됨)\n"));
		var next = Evolutions(u).Where(e => e.NextItem != null).Select(e => "「" + Name(e.NextItem) + AiText.T("」（这项练到 ", "」(이 프로젝트의 레벨 ") + Required(e) + AiText.T(" 级", " 달성") + Conditions(e) + "）").ToList();
		if (next.Count > 0)
		{
			sb.Append(AiText.T("能进化成：", "파생 프로젝트: ")).Append(string.Join("、", next.ToArray())).Append('\n');
		}
		var gifts = (u.ItemModify ?? new UpgradeItemModify[0]).Where(m => m != null && Modified(m) != null).Select(m => Name(Modified(m)) + " ×" + m.GetFieldValue<int>("_count")).ToList();
		if (gifts.Count > 0)
		{
			sb.Append(AiText.T("练满级送：", "최대 레벨 달성 보상: ")).Append(string.Join("、", gifts.ToArray())).Append('\n');
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
		return parts.Count > 0 ? AiText.T("，另外要 ", ", 추가 필요 조건 ") + string.Join(AiText.T(" 且 ", " 그리고 "), parts.ToArray()) : "";
	}
}
