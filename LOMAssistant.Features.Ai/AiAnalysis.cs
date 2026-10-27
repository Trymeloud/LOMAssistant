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
			return "请给旗标名（或编号）、人名。";
		}
		var sb = new StringBuilder();
		// 好感：「郁竹」「郁竹好感」「好感·郁竹」
		string who = Regex.Replace(q, @"^好感[·:：]?|的?好感度?$", "");
		string person = AiKnowledge.FindHeroine(who) is StoryRomance.Heroine h ? StoryRomance.Name(h) : who;
		List<StoryUse> rel = StoryUsage.Relation(person);
		if (rel == null)
		{
			return "剧情数据还在整理，请稍后再问。";
		}
		if (rel.Count > 0)
		{
			sb.Append("【").Append(person).Append("的好感在这些地方被判定】（加减好感的地方太多不列，要知道具体加减用 routes 或 script）\n");
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
			sb.Append("\n【旗标 ").Append(id).Append("「").Append(FlagName(id)).Append("」】改的地方 ").Append(uses.Count(u => u.Changes)).Append(" 处，判定的地方 ").Append(uses.Count(u => !u.Changes)).Append(" 处")
				.Append(AiKnowledge.FlagNow(id) is int now ? "；这一局现在 = " + now : "").Append('\n');
			AppendUses(sb, uses.OrderByDescending(u => u.Changes));
		}
		return sb.Length > 0 ? sb.ToString() : "没找到「" + name + "」的旗标或好感判定。换个说法（旗标说明里的词、人名），或者先用 search。";
	}

	private static string FlagName(string id)
	{
		try
		{
			return AiKnowledge.Clean(DataHelper.GetFlagName(id));
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
				sb.Append("- ……还有更多，没列完\n");
				break;
			}
			sb.Append("- ").Append(u.Changes ? "改：" : "判定：").Append(AiKnowledge.Clean(u.Kind)).Append(' ').Append(AiKnowledge.Clean(u.Detail))
				.Append("｜剧本 ").Append(u.Script)
				.Append(u.Mission != null ? "（任务 " + u.Mission.Key + "「" + AiKnowledge.Clean(u.Mission.Title) + "」" + (u.Mission.When.Length > 0 ? " " + AiKnowledge.Clean(u.Mission.When) : "") + "）" : "（不属于任务：地点事件、随机事件等）");
			if (u.Path.Count > 0)
			{
				sb.Append("｜走：").Append(string.Join(" → ", u.Path.Select(s => AiKnowledge.Clean(StoryScripts.Describe(s))).ToArray()));
			}
			sb.Append('\n');
		}
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
			return "没有任务或剧本「" + key + "」。先用 search 找编号。";
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
				sofar.End = "（剧本 " + name + " 读不到）";
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
				foreach (string effect in s.EffectsOn(exit).Select(AiKnowledge.Clean))
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
					r.Sets.Add((death ? "死亡：" : "结局：") + (e != null ? AiKnowledge.Clean(e.Title) : id));
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
					r.End = exit.ToTitle ? "回到标题（结局）" : exit.ToMap ? "回地图" : target.Length > 0
						? "接到剧本 " + target + (StoryAtlas.Owner.TryGetValue(target, out StoryMission other) ? "（任务 " + other.Key + "「" + AiKnowledge.Clean(other.Title) + "」）" : "")
						: "剧本结束";
					if (exit.MainMission.Length > 0)
					{
						r.End += "，主线推进到 " + exit.MainMission;
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
		int Score(Route r) => f.Length == 0 ? 0 : r.Totals.Where(p => p.Key.Contains(f)).Sum(p => p.Value);
		var ordered = f.Length > 0 ? routes.OrderByDescending(Score).ToList() : routes;
		var sb = new StringBuilder();
		sb.Append(mission != null ? "任务 " + mission.Key + "「" + AiKnowledge.Clean(mission.Title) + "」" : "剧本 " + start).Append(" 从头走到底一共 ").Append(routes.Count).Append(" 条路")
			.Append(truncated ? "（太多，只列前 " + MaxRoutes + " 条）" : "").Append("。每条路互斥，玩家一次只走一条；数字是这一条路上的合计。\n");
		if (f.Length > 0 && routes.Count > 0)
		{
			sb.Append("「").Append(f).Append("」合计：最多 ").Append(Score(ordered[0])).Append("，最少 ").Append(Score(ordered[ordered.Count - 1])).Append("（按这个从多到少排）\n");
		}
		int i = 0;
		foreach (Route r in ordered)
		{
			sb.Append("\n").Append(++i).Append(". ").Append(r.Steps.Count > 0 ? string.Join(" → ", r.Steps.ToArray()) : "（不经分岔）").Append(" ⇒ ").Append(r.End).Append('\n');
			if (r.Totals.Count > 0)
			{
				sb.Append("   合计：").Append(string.Join("，", r.Totals.Where(p => p.Value != 0).Select(p => p.Key + " " + (p.Value > 0 ? "+" : "") + p.Value).ToArray())).Append('\n');
			}
			if (r.Sets.Count > 0)
			{
				sb.Append("   另外：").Append(string.Join("；", r.Sets.Take(12).ToArray())).Append(r.Sets.Count > 12 ? "……" : "").Append('\n');
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
