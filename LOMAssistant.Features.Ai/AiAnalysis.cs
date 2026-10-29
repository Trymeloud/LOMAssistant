using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using LOMAssistant.Features.Story;
using Mortal.Core;

namespace LOMAssistant.Features.Ai;

/// <summary>
/// 给 AI 的分析类工具：反查（旗标、好感在哪些剧情里被改、被判定）、整条路线算账（一个任务从开头走到底每条路一共加减多少），
/// 以及全部剧本的对白搜索（任务剧本之外的地点事件、随机事件也搜）。这些是 AI 自己没法从零散资料里算对的东西，由程序算好给它。
/// </summary>
public static class AiAnalysis
{
	private const int MaxRoutes = 60;
	private const int MaxUses = 40;

	// ---------- 反查 ----------

	public static string Usage(string name)
	{
		string q = TextConverter.ToSimplified((name ?? "").Trim());
		if (q.Length == 0)
		{
			return AiText.T("请给旗标名（或编号）、人名。", "플래그 이름·ID 또는 인물 이름을 입력해 주세요.");
		}
		var sb = new StringBuilder();
		// 好感：「郁竹」「郁竹好感」「好感·郁竹」
		string who = KoreanAiQuery.AffectionPerson(q);
		string person = AiKnowledge.FindHeroine(who) is StoryRomance.Heroine h ? TextConverter.ToSimplified(h.Relation.ToString()) : who;
		if (NativeKorean.Enabled) person = StoryUsage.RelationNames.FirstOrDefault(n => KoreanAiQuery.Equal(AiText.Label(n), person)) ?? person;
		List<StoryUse> rel = StoryUsage.Relation(person);
		if (rel == null)
		{
			return AiText.T("剧情数据还在整理，请稍后再问。", "스토리 자료를 정리하는 중입니다. 잠시 후 다시 질문해 주세요.");
		}
		if (rel.Count > 0)
		{
			sb.Append("【").Append(AiText.Label(person)).Append(AiText.T("的好感在这些地方被判定】（加减好感的地方太多不列，要知道具体加减用 routes 或 script）\n", " 호감도 판정 지점】(변경 지점은 너무 많아 생략합니다. 구체적인 변화는 routes 또는 script로 확인하세요.)\n"));
			AppendUses(sb, rel);
		}
		// 旗标：编号完全相同，或游戏里旗标说明里有这几个字
		var flags = StoryUsage.FlagIds.Where(id => string.Equals(id, q, StringComparison.OrdinalIgnoreCase)).ToList();
		if (flags.Count == 0)
		{
			flags = StoryUsage.FlagIds.Where(id => FlagName(id).Contains(q)).Take(6).ToList();
		}
		foreach (string id in flags)
		{
			List<StoryUse> uses = StoryUsage.Flag(id) ?? new List<StoryUse>();
			sb.Append(AiText.T("\n【旗标 ", "\n[플래그 ")).Append(id).Append("「").Append(FlagName(id)).Append(AiText.T("」】改的地方 ", "」] 변경 지점 ")).Append(uses.Count(u => u.Changes)).Append(AiText.T(" 处，判定的地方 ", "곳, 판정 지점 ")).Append(uses.Count(u => !u.Changes)).Append(AiText.T(" 处", "곳"))
				.Append(AiKnowledge.FlagNow(id) is int now ? AiText.T("；这一局现在 = ", "; 이번 회차 현재 값 = ") + now : "").Append('\n');
			AppendUses(sb, uses.OrderByDescending(u => u.Changes));
		}
		// 数值、技能、天赋：名字完全相同的优先，其次是包含这几个字的（「南宫护心诀」找到「技能_南宫护心诀」）
		string bare = Regex.Replace(q, @"^(技能|天赋)[_·:：]?", "");
		var stats = StoryUsage.StatNames.Where(n => n == q || n == "技能_" + bare || n == "天赋_" + bare
			|| NativeKorean.Enabled && KoreanAiQuery.Equal(AiText.Label(n), q)).ToList();
		if (stats.Count == 0 && bare.Length >= 2)
		{
			stats = StoryUsage.StatNames.Where(n => n.Contains(bare) || NativeKorean.Enabled && KoreanAiQuery.Contains(AiText.Label(n), bare)).OrderBy(n => n.Length).Take(3).ToList();
		}
		foreach (string stat in stats)
		{
			AppendStat(sb, stat);
		}
		return sb.Length > 0 ? sb.ToString() : AiText.T("没找到「", "「") + name + AiText.T("」的旗标、数值、技能或好感判定。换个说法（旗标说明里的词、数值名、人名），或者先用 search。", "」 플래그·수치·기술·호감도 판정 지점을 찾지 못했습니다. 플래그 설명의 단어·수치명·인물명으로 다시 검색하거나 search를 먼저 이용하세요.");
	}

	// 数值改的地方往往上百处：判定的地方全列，改的地方按任务合计（每个任务列出加减了哪些数）
	private static void AppendStat(StringBuilder sb, string stat)
	{
		List<StoryUse> uses = StoryUsage.Stat(stat) ?? new List<StoryUse>();
		var checks = uses.Where(u => !u.Changes).ToList();
		var changes = uses.Where(u => u.Changes).ToList();
		sb.Append(AiText.T("\n【数值「", "\n[수치 「")).Append(AiText.Label(stat)).Append(AiText.T("」】判定的地方 ", "」] 판정 지점 ")).Append(checks.Count).Append(AiText.T(" 处，改的地方 ", "곳, 변경 지점 ")).Append(changes.Count).Append(AiText.T(" 处（这一局现在的值用 player_state 看）\n", "곳(현재 값은 player_state로 확인)\n"));
		AppendUses(sb, checks);
		if (changes.Count > 0)
		{
			sb.Append(AiText.T("改的地方（按任务，同一任务分岔互斥，不能直接加起来）：\n", "변경 지점(임무별 목록이며 같은 임무의 분기는 서로 배타적이므로 단순 합산할 수 없습니다):\n"));
			int n = 0;
			foreach (var g in changes.GroupBy(u => u.Mission))
			{
				if (++n > MaxUses)
				{
					sb.Append(AiText.T("- ……还有更多任务，没列完\n", "- …추가 임무가 있어 일부만 표시합니다.\n"));
					break;
				}
				string where = g.Key != null ? AiText.T("任务 ", "임무 ") + g.Key.Key + "「" + AiKnowledge.Clean(g.Key.Title) + "」" + (g.Key.When.Length > 0 ? " " + AiKnowledge.Clean(g.Key.When) : "") : AiText.T("地点事件、随机事件等（剧本 ", "장소·랜덤 이벤트 등(스크립트 ") + string.Join("、", g.Select(u => u.Script).Distinct().Take(4).ToArray()) + "）";
				sb.Append("- ").Append(where).Append("：").Append(string.Join("、", g.Select(u => AiKnowledge.Clean(u.Detail)).Distinct().Take(8).ToArray())).Append('\n');
			}
		}
	}

	private static string FlagName(string id)
	{
		try
		{
			return NativeKorean.Enabled ? KoreanLabels.Flag(id) : AiKnowledge.Clean(DataHelper.GetFlagName(id));
		}
		catch
		{
			return "";
		}
	}

	private static void AppendUses(StringBuilder sb, IEnumerable<StoryUse> uses)
	{
		int n = 0;
		foreach (StoryUse u in uses)
		{
			if (++n > MaxUses)
			{
				sb.Append(AiText.T("- ……还有更多，没列完\n", "- …추가 결과가 있어 일부만 표시합니다.\n"));
				break;
			}
			sb.Append("- ").Append(u.Changes ? AiText.T("改：", "변경: ") : AiText.T("判定：", "판정: ")).Append(AiKnowledge.Clean(u.Kind)).Append(' ').Append(AiKnowledge.Clean(u.Detail))
				.Append(AiText.T("｜剧本 ", " | 스크립트 ")).Append(u.Script)
				.Append(u.Mission != null ? AiText.T("（任务 ", "(임무 ") + u.Mission.Key + "「" + AiKnowledge.Clean(u.Mission.Title) + "」" + (u.Mission.When.Length > 0 ? " " + AiKnowledge.Clean(u.Mission.When) : "") + "）" : AiText.T("（不属于任务：地点事件、随机事件等）", "(임무 외 장소·랜덤 이벤트 등)"));
			if (u.Path.Count > 0)
			{
				sb.Append(AiText.T("｜走：", " | 경로: ")).Append(string.Join(" → ", u.Path.Select(s => AiKnowledge.Clean(StoryScripts.Describe(s))).ToArray()));
			}
			string after = ArmEffects(u);
			if (after.Length > 0)
			{
				sb.Append(AiText.T("｜满足时这一支里：", " | 조건 충족 시 해당 분기의 효과: ")).Append(after);
			}
			sb.Append('\n');
		}
	}

	// 分支判定满足后走的那一支里加减了什么（不用再翻剧本就知道这个判定管什么）
	private static string ArmEffects(StoryUse u)
	{
		if (u.Arm == null || u.Changes)
		{
			return "";
		}
		StoryScript script = StoryScripts.Get(u.Script);
		if (script == null)
		{
			return "";
		}
		var prefix = new List<StoryStep>(u.Path) { u.Arm };
		var effects = (NativeKorean.Enabled
			? script.DisplayEffects.Where(e => StoryScripts.IsPrefix(prefix, e.Path) && !e.Action.CanonicalDescription.Contains("暂存")).Select(e => AiKnowledge.Clean(e.Action.Description))
			: script.Effects.Where(e => StoryScripts.IsPrefix(prefix, e.Path) && !e.Text.Contains("暂存")).Select(e => AiKnowledge.Clean(e.Text)))
			.Concat(script.Fights.Where(f => StoryScripts.IsPrefix(prefix, f.Path)).Select(f => AiText.T("决斗 ", "결투 ") + f.Level))
			.Concat(script.Exits.Where(x => StoryScripts.IsPrefix(prefix, x.Path) && x.Target.Length > 0).Select(x => AiText.T("接剧本 ", "다음 스크립트 ") + x.Target))
			.Distinct().ToList();
		if (effects.Count == 0)
		{
			return "";
		}
		return string.Join("、", effects.Take(6).ToArray()) + (effects.Count > 6 ? AiText.T("等 ", " 등 총 ") + effects.Count + AiText.T(" 项", "개") : "");
	}

	// ---------- 整条路线算账 ----------

	// 「郁竹好感 +2」「心相 -30」，有的不带正号（「夺魄门好感 1」）
	private static readonly Regex Numeric = new Regex(@"^(\S.*?) ([+-]?\d+)$");

	private sealed class Route
	{
		public readonly List<string> Steps = new List<string>();
		public readonly Dictionary<string, int> Totals = new Dictionary<string, int>();
		public readonly List<string> Sets = new List<string>();
		public string End = "";
	}

	/// <summary>
	/// 从任务的开头剧本（或给的剧本）走到底：每到一个分岔分开走，接到同一任务的剧本就接着走，接到别的任务、回地图、回标题算一条路走完。
	/// 每条路把一路上的数值变化（「郁竹好感 +2」「心相 -30」）加总，设的旗标列出来。分岔互斥，玩家一次只走其中一条。
	/// </summary>
	public static string Routes(string key, string focus)
	{
		StoryMission mission = StoryAtlas.Find(key);
		string start = mission != null ? mission.StartScript : key;
		if (mission == null && StoryAtlas.Owner.TryGetValue(key, out StoryMission owner))
		{
			mission = owner;
		}
		if (StoryScripts.Get(start) == null)
		{
			return AiText.T("没有任务或剧本「", "임무 또는 스크립트 「") + key + AiText.T("」。先用 search 找编号。", "」를 찾지 못했습니다. search로 ID를 확인하세요.");
		}
		var routes = new List<Route>();
		bool truncated = false;
		void Walk(string name, Route sofar, HashSet<string> seen, int depth)
		{
			if (routes.Count >= MaxRoutes)
			{
				truncated = true;
				return;
			}
			StoryScript s = StoryScripts.Get(name);
			if (s == null || depth > 40)
			{
				sofar.End = AiText.T("（剧本 ", "(스크립트 ") + name + AiText.T(" 读不到）", " 읽기 실패)");
				routes.Add(sofar);
				return;
			}
			var exits = s.Exits.Count > 0 ? s.Exits : new List<StoryExit> { new StoryExit() };
			foreach (StoryExit exit in exits)
			{
				var r = Copy(sofar);
				foreach (StoryStep step in exit.Path)
				{
					r.Steps.Add(AiKnowledge.Clean(StoryScripts.Describe(step)));
				}
				foreach (string effect in s.CanonicalEffectsOn(exit).Select(AiKnowledge.CanonicalClean))
				{
					foreach (string part in effect.Split('；', ';'))
					{
						Match m = Numeric.Match(part.Trim());
						if (m.Success && int.TryParse(m.Groups[2].Value, out int v))
						{
							string k = m.Groups[1].Value.Trim();
							r.Totals[k] = (r.Totals.TryGetValue(k, out int old) ? old : 0) + v;
						}
						else if (part.Trim().Length > 0 && !r.Sets.Contains(part.Trim()))
						{
							r.Sets.Add(part.Trim());
						}
					}
				}
				foreach (var (path, death, id) in s.Endings.Where(e => StoryScripts.IsPrefix(e.Path, exit.Path) || StoryScripts.IsPrefix(exit.Path, e.Path)))
				{
					StoryEnding e = StoryAtlas.Endings.FirstOrDefault(x => x.Id == id);
					r.Sets.Add((death ? AiText.T("死亡：", "사망: ") : AiText.T("结局：", "결말: ")) + (e != null ? AiKnowledge.Clean(e.Title) : id));
				}
				string target = exit.Target;
				bool sameMission = target.Length > 0 && (mission == null || (StoryAtlas.Owner.TryGetValue(target, out StoryMission t) && t == mission));
				if (sameMission && !seen.Contains(target))
				{
					var next = new HashSet<string>(seen) { target };
					Walk(target, r, next, depth + 1);
				}
				else
				{
					r.End = exit.ToTitle ? AiText.T("回到标题（结局）", "제목 화면으로(결말)") : exit.ToMap ? AiText.T("回地图", "지도로") : target.Length > 0
						? AiText.T("接到剧本 ", "다음 스크립트 ") + target + (StoryAtlas.Owner.TryGetValue(target, out StoryMission other) ? AiText.T("（任务 ", "(임무 ") + other.Key + "「" + AiKnowledge.Clean(other.Title) + "」）" : "")
						: AiText.T("剧本结束", "스크립트 종료");
					if (exit.MainMission.Length > 0)
					{
						r.End += AiText.T("，主线推进到 ", ", 주요 임무 진행: ") + exit.MainMission;
					}
					routes.Add(r);
				}
				if (routes.Count >= MaxRoutes)
				{
					truncated = true;
					return;
				}
			}
		}
		Walk(start, new Route(), new HashSet<string> { start }, 0);

		string f = TextConverter.ToSimplified((focus ?? "").Trim());
		int Score(Route r) => f.Length == 0 ? 0 : r.Totals.Where(p => p.Key.Contains(f)
			|| NativeKorean.Enabled && KoreanAiQuery.Contains(AiText.Label(p.Key), f)).Sum(p => p.Value);
		var ordered = f.Length > 0 ? routes.OrderByDescending(Score).ToList() : routes;
		var sb = new StringBuilder();
		sb.Append(mission != null ? AiText.T("任务 ", "임무 ") + mission.Key + "「" + AiKnowledge.Clean(mission.Title) + "」" : AiText.T("剧本 ", "스크립트 ") + start).Append(AiText.T(" 从头走到底一共 ", " 시작부터 끝까지 총 ")).Append(routes.Count).Append(AiText.T(" 条路", "개 경로"))
			.Append(truncated ? AiText.T("（太多，只列前 ", "(너무 많아 처음 ") + MaxRoutes + AiText.T(" 条）", "개만 표시)") : "").Append(AiText.T("。每条路互斥，玩家一次只走一条；数字是这一条路上的合计。\n", ". 경로는 서로 배타적이며 한 번에 하나만 지납니다. 수치는 각 경로의 합계입니다.\n"));
		if (f.Length > 0 && routes.Count > 0)
		{
			sb.Append("「").Append(f).Append(AiText.T("」合计：最多 ", "」 합계: 최대 ")).Append(Score(ordered[0])).Append(AiText.T("，最少 ", ", 최소 ")).Append(Score(ordered[ordered.Count - 1])).Append(AiText.T("（按这个从多到少排）\n", "(내림차순 정렬)\n"));
		}
		int i = 0;
		foreach (Route r in ordered)
		{
			sb.Append("\n").Append(++i).Append(". ").Append(r.Steps.Count > 0 ? string.Join(" → ", r.Steps.ToArray()) : AiText.T("（不经分岔）", "(분기 없음)")).Append(" ⇒ ").Append(r.End).Append('\n');
			if (r.Totals.Count > 0)
			{
				sb.Append(AiText.T("   合计：", "   합계: ")).Append(string.Join("，", r.Totals.Where(p => p.Value != 0).Select(p => AiText.Label(p.Key) + " " + (p.Value > 0 ? "+" : "") + p.Value).ToArray())).Append('\n');
			}
			if (r.Sets.Count > 0)
			{
				sb.Append(AiText.T("   另外：", "   그 외: ")).Append(string.Join("；", r.Sets.Take(12).Select(AiText.Label).ToArray())).Append(r.Sets.Count > 12 ? "……" : "").Append('\n');
			}
		}
		return sb.ToString();
	}

	private static Route Copy(Route r)
	{
		var c = new Route { End = r.End };
		c.Steps.AddRange(r.Steps);
		foreach (var p in r.Totals)
		{
			c.Totals[p.Key] = p.Value;
		}
		c.Sets.AddRange(r.Sets);
		return c;
	}

	// ---------- 全部剧本的对白 ----------

	private static List<(string Script, string Text)> _extra;
	private static int _extraVersion = -1;

	/// <summary>不属于任务的剧本（地点事件、随机事件……）里的对白：路线图的台词索引只建了任务剧本，这里补上。第一次用时建，之后记住。</summary>
	public static List<(string Script, string Snippet)> SearchOtherDialogue(string query, int max)
	{
		if (_extra == null || _extraVersion != StoryAtlas.Version)
		{
			_extraVersion = StoryAtlas.Version;
			_extra = new List<(string, string)>();
			foreach (StoryScript s in StoryScripts.All)
			{
				if (StoryAtlas.Owner.ContainsKey(s.Name))
				{
					continue;
				}
				foreach (var (_, _, lineKey) in s.Talk)
				{
					string text = StoryAtlas.LineText(lineKey);
					if (text.Length > 0)
					{
						_extra.Add((s.Name, text));
					}
				}
			}
			FileLogger.Log("[AI] 任务以外剧本的台词索引：" + _extra.Count + " 句");
		}
		var found = new List<(string, string)>();
		foreach (var (script, text) in _extra)
		{
			int at = text.IndexOf(query, StringComparison.Ordinal);
			if (at < 0)
			{
				continue;
			}
			int from = Math.Max(0, at - 20);
			found.Add((script, (from > 0 ? "…" : "") + text.Substring(from, Math.Min(text.Length - from, 60))));
			if (found.Count >= max)
			{
				break;
			}
		}
		return found;
	}

	/// <summary>名字或说明里有这几个字的旗标（编号：说明），search 用。</summary>
	public static List<string> FlagsNamed(string query, int max)
	{
		// 剧情里用得多的旗标排前面：「大师兄幸存」「大师兄归来」这类关键旗标不会被零星用到的挤掉
		return StoryUsage.FlagIds.Select(id => (Id: id, Name: FlagName(id))).Where(x => x.Name.Contains(query) || x.Id.Equals(query, StringComparison.OrdinalIgnoreCase))
			.OrderByDescending(x => StoryUsage.Flag(x.Id)?.Count ?? 0)
			.Take(max).Select(x => x.Id + "「" + x.Name + "」").ToList();
	}

	// ---------- 条件里的时间 ----------

	private static readonly Regex TimeCondition = new Regex(@"(目前时间 需(?:>=|<=|!=|=|>|<))(\d+)");

	/// <summary>「目前时间 需>=46」后面补上日期：目前时间是从开局数的第几旬（第 1 年 1 月上旬 = 1）。</summary>
	public static string ExplainTime(string text)
	{
		return TimeCondition.Replace(text, m => int.TryParse(m.Groups[2].Value, out int n) && n >= 1
			? m.Value + "（第" + ((n - 1) / 36 + 1) + "年" + ((n - 1) % 36 / 3 + 1) + "月" + StoryAtlas.StageText((n - 1) % 3 + 1) + "）"
			: m.Value);
	}
}
