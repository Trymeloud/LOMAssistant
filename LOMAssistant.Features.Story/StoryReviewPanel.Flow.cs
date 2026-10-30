using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LOMAssistant.Features.Story;

// 剧情图：一个任务的剧情从开始剧本往下画成流程图。一路只有一条去处、又没有分岔的剧本合成一个方块；
// 选项、掷骰、条件各连一条线到下一段，线上写着选了什么；走出这个任务的（回地图、主线推进、结局）画成灰色小方块。
public partial class StoryReviewPanel
{
	private sealed class FlowBox
	{
		public string Id = "";
		public List<string> Chain = new List<string>();
		public StoryScript Last;
		public int Layer, Row;
		public Rect Rect;
		public string Title = "", Sub = "";
		public int? Read;
		public StoryNode Kept;
		/// <summary>走出本任务的去处（回地图、主线推进、结局、别的任务），没有剧本。</summary>
		public bool Terminal;
		public bool Ending;
		/// <summary>这一段最后一个剧本的每个去处，方块里一行一个：选项、掷骰、条件、接着；To 为 null 的是选了还留在本段、之后合回来的选项。</summary>
		public readonly List<FlowWay> Ways = new List<FlowWay>();
		/// <summary>方块里第一行去处的纵坐标（行高 RowHeight），排版时算。</summary>
		public float RowsTop;
	}

	/// <summary>方块里的一行去处。</summary>
	private sealed class FlowWay
	{
		public FlowBox To;
		/// <summary>行里显示的短说明；Full 是完整的条件和选项（悬停时看）。</summary>
		public string Short = "";
		public readonly List<string> Full = new List<string>();
		public string Effects = "";
		public readonly List<StoryExit> Exits = new List<StoryExit>();
		public bool Back;
	}

	private sealed class FlowLink
	{
		public FlowBox From, To;
		public List<string> Labels = new List<string>();
		public List<StoryExit> Exits = new List<StoryExit>();
		public bool Back;
		/// <summary>线上的标签（每项一行、完整显示）、它占的高度，以及离线多高（同一个方块进来几条线时往上叠）。</summary>
		public string Text = "";
		public float Height, Lift;
	}

	private sealed class FlowGraph
	{
		public readonly List<FlowBox> Boxes = new List<FlowBox>();
		public readonly List<FlowLink> Links = new List<FlowLink>();
		public float Width, Height;
		public bool Cut;
	}

	private const int FlowLimit = 160;

	// 方块里去处那一行的文字：左对齐、超出截掉（悬停看完整的）
	private GUIStyle _flowRow, _flowRowMuted;
	private float _flowRowScale;
	private bool _flowRowDark;

	private StoryMission _flowMission;
	private FlowGraph _flow;
	private float _flowBuiltScale;
	private int _flowBuiltVersion;
	private FlowBox _flowPicked;
	private Vector2 _flowScroll, _flowListScroll, _flowDetailScroll;
	private string _flowSearch = "";

	/// <summary>打开某个任务的剧情图；pickScript 给了就选中含这个剧本的那一段并滚过去。</summary>
	private void OpenFlow(StoryMission m, string pickScript = null)
	{
		_tab = 2;
		_flowMission = m;
		_flow = null;
		_flowPicked = null;
		_flowScroll = Vector2.zero;
		_flowPickScript = pickScript;
	}

	private string _flowPickScript;
	/// <summary>此刻页点「在剧情图里看」：剧本读完后打开它所在任务的剧情图，选中这一段。</summary>
	private string _flowJumpScript;
	private string _flowListQuery;
	private HashSet<StoryMission> _flowDialogueHits = new HashSet<StoryMission>();

	/// <summary>任务的剧情图（路线图的小节也用它），按任务缓存。</summary>
	private readonly Dictionary<StoryMission, FlowGraph> _flowCache = new Dictionary<StoryMission, FlowGraph>();
	private int _flowCacheVersion = -1;

	private FlowGraph FlowOf(StoryMission m)
	{
		if (_flowCacheVersion != StoryAtlas.Version + StoryNodes.Version * 7919)
		{
			_flowCache.Clear();
			_flowCacheVersion = StoryAtlas.Version + StoryNodes.Version * 7919;
		}
		if (!_flowCache.TryGetValue(m, out FlowGraph graph))
		{
			_flowCache[m] = graph = BuildFlow(m);
		}
		return graph;
	}

	/// <summary>主干：从开头到这个任务的每个出口都必经的段落；其余是岔路。</summary>
	private static HashSet<FlowBox> Trunk(FlowGraph graph)
	{
		var boxes = graph.Boxes.Where(b => !b.Terminal).ToList();
		var trunk = new HashSet<FlowBox>();
		if (boxes.Count == 0)
		{
			return trunk;
		}
		FlowBox start = boxes[0];
		var preds = boxes.ToDictionary(b => b, b => graph.Links.Where(l => !l.Back && l.To == b && !l.From.Terminal).Select(l => l.From).ToList());
		var dom = boxes.ToDictionary(b => b, b => b == start ? new HashSet<FlowBox> { start } : new HashSet<FlowBox>(boxes));
		for (bool changed = true; changed;)
		{
			changed = false;
			foreach (FlowBox b in boxes)
			{
				if (b == start || preds[b].Count == 0)
				{
					continue;
				}
				var next = new HashSet<FlowBox>(dom[preds[b][0]]);
				foreach (FlowBox p in preds[b].Skip(1))
				{
					next.IntersectWith(dom[p]);
				}
				next.Add(b);
				if (!next.SetEquals(dom[b]))
				{
					dom[b] = next;
					changed = true;
				}
			}
		}
		// 出口：连到「走出本任务」小方块的，或者没有往下走的段落
		var exits = boxes.Where(b => graph.Links.Any(l => l.From == b && l.To.Terminal) || !graph.Links.Any(l => l.From == b && !l.Back)).ToList();
		if (exits.Count == 0)
		{
			exits = boxes;
		}
		trunk.UnionWith(dom[exits[0]]);
		foreach (FlowBox e in exits.Skip(1))
		{
			trunk.IntersectWith(dom[e]);
		}
		return trunk;
	}

	private void DrawFlow(Rect r)
	{
		if (!EnsureAtlas(r))
		{
			return;
		}
		if (_flowJumpScript != null)
		{
			string script = _flowJumpScript;
			_flowJumpScript = null;
			if (StoryAtlas.Owner.TryGetValue(script, out StoryMission owner))
			{
				OpenFlow(owner, script);
			}
		}
		// 左边：任务列表
		float listWidth = Mathf.Min(S(330), r.width * 0.24f);
		string query = SearchBox(new Rect(r.x, r.y, listWidth, S(36)), ref _flowSearch);
		var listArea = new Rect(r.x, r.y + S(44), listWidth, r.height - S(44));
		DragScroll("flowList", listArea, ref _flowListScroll);
		GUILayout.BeginArea(listArea);
		_flowListScroll = GUILayout.BeginScrollView(_flowListScroll);
		// 搜索也看台词：台词里出现这几个字的任务一起列出来
		if (query != _flowListQuery)
		{
			_flowListQuery = query;
			_flowDialogueHits = query.Length >= 2
				? new HashSet<StoryMission>(StoryAtlas.SearchDialogue(query, 500).Select(h => StoryAtlas.Owner.TryGetValue(h.Script, out StoryMission o) ? o : null).Where(o => o != null))
				: new HashSet<StoryMission>();
		}
		// 按剧情时间从早到晚，主线和支线排在一起；同一旬里主线在前。按年份分段，「任意时间」的放最后
		string group = null;
		foreach (StoryMission m in StoryAtlas.Missions.Where(m => m.StartScript.Length > 0 && (Matches(query, m.Title, m.Key, m.SeriesTitle, m.Place, m.Summary) || _flowDialogueHits.Contains(m)))
			.OrderBy(m => m.From ?? int.MaxValue).ThenBy(m => m.Main ? 0 : 1).ThenBy(m => m.Key, StringComparer.Ordinal))
		{
			string g = m.From != null ? StoryUi.Text(KoreanStoryMessage.Year, "第{0}年", m.From.Value / 36 + 1) : StoryUi.Text(KoreanStoryMessage.AnyMonthly, "任意时间 / 每月");
			if (g != group)
			{
				group = g;
				GUILayout.Label(g, _muted);
			}
			string when = m.From != null ? (NativeKorean.Enabled ? KoreanStoryText.Get(KoreanStoryMessage.MonthStage, m.From.Value % 36 / 3 + 1, KoreanStoryText.Stage(m.From.Value % 3 + 1)) : $"{m.From.Value % 36 / 3 + 1}月{StoryAtlas.StageText(m.From.Value % 3 + 1)}") : StoryUi.When(m);
			string kind = m.Main ? StoryUi.Text(KoreanStoryMessage.Main, "主线") : Short(StoryUi.Series(m), 10);
			var style = new GUIStyle(m == _flowMission ? _primary : _button) { alignment = TextAnchor.MiddleLeft, wordWrap = true, richText = true };
			string whenColor = m == _flowMission ? "#F3E6D6" : "#73644F";
			if (GUILayout.Button($"<size={Mathf.RoundToInt(13 * _scale)}><color={whenColor}>{when} · {kind}</color></size>\n{Short(m.Title, 16)}", style))
			{
				OpenFlow(m);
			}
		}
		GUILayout.EndScrollView();
		GUILayout.EndArea();

		var area = new Rect(r.x + listWidth + S(12), r.y, r.width - listWidth - S(12), r.height);
		if (_flowMission == null)
		{
			return;
		}
		if (_flow == null || !Mathf.Approximately(_flowBuiltScale, _scale) || _flowBuiltVersion != StoryNodes.Version)
		{
			_flow = BuildFlow(_flowMission);
			_flowBuiltScale = _scale;
			_flowBuiltVersion = StoryNodes.Version;
			_flowPicked = _flowPicked == null ? null : _flow.Boxes.FirstOrDefault(b => b.Id == _flowPicked.Id);
		}
		if (_flowPickScript != null)
		{
			FlowBox target = _flow.Boxes.FirstOrDefault(b => b.Chain.Any(c => string.Equals(c, _flowPickScript, StringComparison.OrdinalIgnoreCase)));
			if (target != null)
			{
				_flowPicked = target;
				_flowScroll = new Vector2(Mathf.Max(0, target.Rect.x - S(80)), Mathf.Max(0, target.Rect.y - S(120)));
			}
			_flowPickScript = null;
		}
		GUI.Label(new Rect(area.x, area.y, area.width, S(36)), $"<b>{_flowMission.Title}</b>　<color=#73644F>{StoryUi.When(_flowMission)}{(_flowMission.Place.Length > 0 ? " · " + _flowMission.Place : "")}{(_flow.Cut ? " · " + StoryUi.Text(KoreanStoryMessage.FlowCut, "太长，只画了前 {0} 段", FlowLimit) : "")}</color>", _label);
		float detailWidth = _flowPicked != null ? Mathf.Min(S(460), area.width * 0.36f) : 0;
		var canvas = new Rect(area.x, area.y + S(44), area.width - detailWidth - (detailWidth > 0 ? S(12) : 0), area.height - S(44));
		Fill(canvas, PaperLight);
		Frame(canvas, Edge, 1);
		DrawFlowCanvas(canvas);
		if (_flowPicked != null)
		{
			var detail = new Rect(canvas.xMax + S(12), canvas.y, detailWidth, canvas.height);
			Fill(detail, PaperDeep);
			Frame(detail, Edge, 1);
			DragScroll("flowDetail", detail, ref _flowDetailScroll);
			GUILayout.BeginArea(new Rect(detail.x + S(12), detail.y + S(10), detail.width - S(24), detail.height - S(20)));
			_flowDetailScroll = GUILayout.BeginScrollView(_flowDetailScroll);
			DrawBoxDetail(_flowPicked);
			GUILayout.EndScrollView();
			GUILayout.EndArea();
		}
	}

	/// <summary>从 name 开始，一路只有一条去处、没有分岔的剧本合在一起。</summary>
	private static List<string> Chain(string name, out StoryScript last)
	{
		var chain = new List<string>();
		StoryScript script = StoryScripts.Get(name);
		while (script != null && !script.Branches && script.Exits.Count == 1 && script.Exits[0].Path.Count == 0 && script.Exits[0].Target.Length > 0
			&& !script.Exits[0].ToMap && !script.Exits[0].ToTitle && script.Exits[0].MainMission.Length == 0 && !chain.Contains(script.Name) && chain.Count < 30)
		{
			chain.Add(script.Name);
			script = StoryScripts.Get(script.Exits[0].Target);
		}
		if (script != null && !chain.Contains(script.Name))
		{
			chain.Add(script.Name);
		}
		last = script;
		return chain;
	}

	/// <summary>线上的短标签：这条路最后一步分岔。</summary>
	/// <summary>线上的标签：这条路上的每一步分岔，用「→」连起来。</summary>
	private static string LinkLabel(StoryExit exit) => exit.Path.Count == 0 ? (exit.AfterScene ? StoryUi.Text(KoreanStoryMessage.AfterBattle, "战斗后") : "") : string.Join(" → ", exit.Path.Select(StepLabel).ToArray());

	/// <summary>线上的短标签：选项写「选项文字」，掷骰写「掷出 &lt;33」，条件写「如果 小梅好感>=35」。</summary>
	private static string StepLabel(StoryStep step)
	{
		switch (step.Kind)
		{
			case StoryStep.Option:
				return "「" + StoryScripts.OptionText(step.Key) + "」";
			case StoryStep.Dice:
				return (NativeKorean.Enabled ? KoreanStoryText.Get(KoreanStoryMessage.DiceTag) + " " : "掷出 ")
					+ (step.Range.Length > 0 ? step.Range : Short(StoryScripts.OptionText(step.Key), 6));
			default:
				if (step.Arm == 0)
				{
					return StoryScripts.Otherwise(step.Key, false);
				}
				// 「小梅好感 需>=35 → 当前40」只留「小梅好感>=35」
				string text = RichText.Strip(DataHelper.SwitchBranchCondition(step.Key, step.Arm)).Trim();
				if (text == "以上条件都不满足")
				{
					return StoryScripts.Otherwise(step.Key, false);
				}
				text = KoreanText.CompactCondition(text);
				return text.Length > 0
					? StoryUi.Text(KoreanStoryMessage.IfCondition, "如果 {0}", NativeKorean.Enabled ? TextConverter.ForDisplay(text) : text)
					: StoryUi.Text(KoreanStoryMessage.UnknownCondition, "条件不明");
		}
	}

	/// <summary>这个去处触发的是哪个结局（汗青书），找不到返回空。</summary>
	private static string EndingName(StoryScript script, StoryExit exit)
	{
		foreach (var (path, death, id) in script.Endings)
		{
			if (!death && (StoryScripts.IsPrefix(path, exit.Path) || StoryScripts.IsPrefix(exit.Path, path)))
			{
				StoryEnding ending = StoryAtlas.Endings.FirstOrDefault(e => !e.Death && e.Id == id);
				return ending != null ? ending.Title : id;
			}
		}
		return "";
	}

	private FlowGraph BuildFlow(StoryMission mission)
	{
		var graph = new FlowGraph();
		var otherStarts = new HashSet<string>(StoryAtlas.Missions.Where(m => m != mission && m.StartScript.Length > 0).Select(m => m.StartScript), StringComparer.OrdinalIgnoreCase);
		var byScript = new Dictionary<string, FlowBox>(StringComparer.OrdinalIgnoreCase);
		var terminals = new Dictionary<string, FlowBox>(StringComparer.Ordinal);
		var queue = new Queue<FlowBox>();

		FlowBox Box(string head, int layer)
		{
			List<string> chain = Chain(head, out StoryScript last);
			string key = last != null ? last.Name : head;
			if (byScript.TryGetValue(key, out FlowBox known))
			{
				return known;
			}
			if (graph.Boxes.Count >= FlowLimit)
			{
				graph.Cut = true;
				return null;
			}
			var scripts = chain.Select(StoryScripts.Get).Where(s => s != null).ToList();
			var tags = new List<string>();
			if (last != null && last.Menu) tags.Add(StoryUi.Text(KoreanStoryMessage.OptionTag, "选项"));
			if (last != null && last.Dice) tags.Add(StoryUi.Text(KoreanStoryMessage.DiceTag, "掷骰"));
			if (last != null && last.Fight) tags.Add(StoryUi.Text(KoreanStoryMessage.BattleTag, "战斗"));
			var box = new FlowBox
			{
				Id = key,
				Chain = chain,
				Last = last,
				Layer = layer,
				Read = StoryScripts.ReadPercent(scripts.SelectMany(s => s.Lines)),
				Kept = StoryNodes.Latest(key),
				Title = scripts.SelectMany(s => s.Records).Select(StoryScripts.Legend).FirstOrDefault(t => t.Length > 0) ?? ""
			};
			// 没有剧情记录的段落用第一句台词，再没有才用剧本名
			if (box.Title.Length == 0)
			{
				box.Title = scripts.SelectMany(s => s.Lines).Take(6).Select(StoryAtlas.LineText).FirstOrDefault(t => t.Length > 0) ?? "";
			}
			if (box.Title.Length == 0)
			{
				box.Title = chain.Count > 1 ? chain[0] + " … " + key : key;
			}
			box.Sub = string.Join("・", tags.ToArray()) + (box.Read != null ? (tags.Count > 0 ? "　" : "") + StoryUi.Text(KoreanStoryMessage.ReadPercent, "读过 {0}%", box.Read) : "");
			foreach (string name in chain)
			{
				byScript[name] = box;
			}
			graph.Boxes.Add(box);
			queue.Enqueue(box);
			return box;
		}

		FlowBox Terminal(FlowBox from, string text, bool ending)
		{
			string key = from.Id + "|" + text;
			if (!terminals.TryGetValue(key, out FlowBox box))
			{
				box = new FlowBox { Id = key, Title = text, Terminal = true, Ending = ending, Layer = from.Layer + 1 };
				terminals[key] = box;
				graph.Boxes.Add(box);
			}
			return box;
		}

		Box(mission.StartScript, 0);
		// 回地图以后由地图事件接着演的段落（襄阳行这类）：顺着剧本走不到，演完前面的再从这些段落接着画，
		// 前面放一个「地图上接着」的小方块
		var later = new Queue<string>(mission.Scripts.Where(n => !string.Equals(n, mission.StartScript, StringComparison.OrdinalIgnoreCase)));
		while (true)
		{
			Drain();
			string root = null;
			while (later.Count > 0 && root == null)
			{
				string name = later.Dequeue();
				if (byScript.ContainsKey(name) || otherStarts.Contains(name))
				{
					continue;
				}
				// 只从没有别的段落接进来的那几段开始
				bool reached = mission.Scripts.Any(n => !string.Equals(n, name, StringComparison.OrdinalIgnoreCase) && (StoryScripts.Get(n)?.Exits.Any(x => string.Equals(x.Target, name, StringComparison.OrdinalIgnoreCase)) ?? false));
				if (!reached)
				{
					root = name;
				}
			}
			if (root == null || graph.Boxes.Count >= FlowLimit)
			{
				break;
			}
			int layer = graph.Boxes.Max(b => b.Layer) + 1;
			var map = new FlowBox { Id = "map|" + root, Title = StoryUi.Text(KoreanStoryMessage.ContinueOnMap, "地图上接着"), Terminal = true, Layer = layer };
			graph.Boxes.Add(map);
			FlowBox next = Box(root, layer + 1);
			if (next != null)
			{
				graph.Links.Add(new FlowLink { From = map, To = next });
			}
		}
		return Layout();

		void Drain()
		{
			while (queue.Count > 0)
			{
				FlowBox from = queue.Dequeue();
				if (from.Last == null)
				{
					continue;
				}
				foreach (StoryExit exit in from.Last.Exits)
				{
					FlowBox to;
					if (exit.ToTitle)
					{
						string name = EndingName(from.Last, exit);
						to = Terminal(from, name.Length > 0 ? StoryUi.Text(KoreanStoryMessage.EndingNamed, "结局：{0}", Short(name, 9)) : StoryUi.Text(KoreanStoryMessage.Ending, "结局"), true);
					}
					else if (exit.MainMission.Length > 0)
					{
						StoryMission next = StoryAtlas.Find(exit.MainMission);
						to = Terminal(from, StoryUi.Text(KoreanStoryMessage.NextMission, "主线 → {0}", next != null ? Short(next.Title, 10) : exit.MainMission), false);
					}
					else if (exit.ToMap)
					{
						to = Terminal(from, StoryUi.Text(KoreanStoryMessage.BackToMap, "回到地图"), false);
					}
					else if (exit.Target.Length == 0)
					{
						continue;
					}
					else if (otherStarts.Contains(exit.Target))
					{
						StoryMission other = StoryAtlas.Missions.FirstOrDefault(m => string.Equals(m.StartScript, exit.Target, StringComparison.OrdinalIgnoreCase));
						to = Terminal(from, StoryUi.Text(KoreanStoryMessage.ContinueTo, "接到 {0}", other != null ? Short(other.Title, 10) : exit.Target), false);
					}
					else
					{
						to = Box(exit.Target, from.Layer + 1);
						if (to == null)
						{
							continue;
						}
					}
					FlowLink link = graph.Links.Find(l => l.From == from && l.To == to);
					if (link == null)
					{
						link = new FlowLink { From = from, To = to, Back = to.Layer <= from.Layer };
						graph.Links.Add(link);
					}
					link.Exits.Add(exit);
					string label = LinkLabel(exit);
					if (label.Length > 0 && !link.Labels.Contains(label))
					{
						link.Labels.Add(label);
					}
					AddWay(from, to, ShortLabel(exit), label, exit, link.Back);
				}
				// 选了还留在本段、之后合回来的选项：也列成一行，写上它改什么
				var leaving = new HashSet<string>(from.Last.Exits.SelectMany(x => x.Path).Where(st => st.Kind == StoryStep.Option).Select(st => st.Key));
				foreach (StoryStep option in from.Last.Menus.SelectMany(m => m).Where(st => st.Kind == StoryStep.Option && !leaving.Contains(st.Key)))
				{
					if (from.Ways.Any(w => w.To == null && w.Exits.Count == 0 && w.Full.Contains(option.Key)))
					{
						continue;
					}
					var stay = new FlowWay { Short = "「" + Short(StoryScripts.OptionText(option.Key), 12) + "」" };
					stay.Full.Add(option.Key);
					var effects = NativeKorean.Enabled
						? from.Last.DisplayEffects.Where(x => !x.Action.CanonicalDescription.Contains("暂存"))
							.Select(x => (Path: x.Path, Text: RichText.Strip(x.Action.Description)))
						: from.Last.Effects;
					stay.Effects = string.Join("　", effects.Where(x => x.Path.Any(st => st.Key == option.Key) && !x.Text.Contains("暂存")).Select(x => x.Text).Distinct().ToArray());
					from.Ways.Add(stay);
				}
				// 输了会死的决斗
				foreach (var (path, level) in from.Last.Fights)
				{
					if (!ConditionBook.CombatDeaths.TryGetValue(level, out string deadId))
					{
						continue;
					}
					StoryEnding ending = StoryAtlas.Endings.FirstOrDefault(e => e.Death && e.Id == deadId);
					FlowBox to = Terminal(from, StoryUi.Text(KoreanStoryMessage.DeathNamed, "死亡：{0}", Short(ending != null ? ending.Title : deadId, 9)), true);
					FlowLink link = graph.Links.Find(l => l.From == from && l.To == to);
					if (link == null)
					{
						link = new FlowLink { From = from, To = to };
						graph.Links.Add(link);
					}
					string label = (path.Count > 0 ? StepLabel(path[path.Count - 1]) + "，" : "") + StoryUi.Text(KoreanStoryMessage.LostDuel, "决斗输了");
					if (!link.Labels.Contains(label))
					{
						link.Labels.Add(label);
					}
					AddWay(from, to, (path.Count > 0 ? StepShort(path[path.Count - 1]) + " · " : "") + "决斗输了", label, null, false);
				}
				// 死亡（生死簿）不是「回到标题」，单独画出来
				foreach (var (path, death, id) in from.Last.Endings.Where(e => e.Death))
				{
					StoryEnding ending = StoryAtlas.Endings.FirstOrDefault(e => e.Death && e.Id == id);
					FlowBox to = Terminal(from, StoryUi.Text(KoreanStoryMessage.DeathNamed, "死亡：{0}", Short(ending != null ? ending.Title : id, 9)), true);
					FlowLink link = graph.Links.Find(l => l.From == from && l.To == to);
					if (link == null)
					{
						link = new FlowLink { From = from, To = to };
						graph.Links.Add(link);
					}
					string label = path.Count > 0 ? StepLabel(path[path.Count - 1]) : "";
					if (label.Length > 0 && !link.Labels.Contains(label))
					{
						link.Labels.Add(label);
					}
					AddWay(from, to, path.Count > 0 ? StepShort(path[path.Count - 1]) : "接着", label, null, false);
				}
			}
		}

		// 同一个去处、同一句短说明的合成一行
		void AddWay(FlowBox from, FlowBox to, string shortText, string full, StoryExit exit, bool back)
		{
			FlowWay way = from.Ways.Find(w => w.To == to && w.Short == shortText);
			if (way == null)
			{
				way = new FlowWay { To = to, Short = shortText, Back = back };
				from.Ways.Add(way);
			}
			if (full.Length > 0 && !way.Full.Contains(full))
			{
				way.Full.Add(full);
			}
			if (exit != null)
			{
				way.Exits.Add(exit);
			}
		}

		FlowGraph Layout()
		{
			foreach (FlowLink link in graph.Links)
			{
				link.Text = string.Join("\n", link.Labels.ToArray());
			}
			// 分列：每一段放在「最长的一条路」走到它的那一列（往回接的线不算），岔开以后再合流的段落排在合流处，不会挤在前面
			for (int pass = 0; pass < graph.Boxes.Count; pass++)
			{
				bool moved = false;
				foreach (FlowLink link in graph.Links.Where(l => !l.Back))
				{
					if (link.To.Layer < link.From.Layer + 1)
					{
						link.To.Layer = link.From.Layer + 1;
						moved = true;
					}
				}
				if (!moved)
				{
					break;
				}
			}
			float w = S(290), gapX = S(120), gapY = S(22);
			// 一列一列往右排：每个方块对着连进来的那几行去处的平均高度放，同一列里按这个高度排先后、互不重叠
			var incoming = new Dictionary<FlowBox, List<float>>();
			float bottom = 0;
			foreach (var layer in graph.Boxes.GroupBy(b => b.Layer).OrderBy(g => g.Key))
			{
				float Wanted(FlowBox b) => incoming.TryGetValue(b, out var ys) && ys.Count > 0 ? ys.Average() : 0f;
				var sorted = layer.OrderBy(Wanted).ThenBy(b => b.Terminal ? 1 : 0).ToList();
				float y = S(24);
				int row = 0;
				foreach (FlowBox b in sorted)
				{
					b.Row = row++;
					float bh = BoxHeight(b);
					float wanted = layer.Key == 0 ? y : Wanted(b) - InPort(b, bh);
					y = Mathf.Max(y, wanted);
					b.Rect = new Rect(S(24) + layer.Key * (w + gapX), y, b.Terminal ? w * 0.72f : w, bh);
					b.RowsTop = b.Rect.y + HeaderHeight;
					y += bh + gapY;
					for (int k = 0; k < b.Ways.Count; k++)
					{
						FlowWay way = b.Ways[k];
						if (way.To != null && !way.Back)
						{
							if (!incoming.TryGetValue(way.To, out var ys))
							{
								incoming[way.To] = ys = new List<float>();
							}
							ys.Add(b.RowsTop + (k + 0.5f) * RowHeight);
						}
					}
				}
				bottom = Mathf.Max(bottom, y);
			}
			graph.Width = S(48) + (graph.Boxes.Count == 0 ? 0 : graph.Boxes.Max(b => b.Layer) + 1) * (w + gapX);
			graph.Height = bottom + S(40);
			return graph;
		}
	}

	private float HeaderHeight => S(60);
	private float RowHeight => S(28);

	private float BoxHeight(FlowBox b) => b.Terminal ? S(40) : HeaderHeight + Mathf.Max(1, b.Ways.Count) * RowHeight + S(8);

	// 连进来的线接在方块的哪个高度：剧情段接在标题那一行，走出本任务的小方块接在正中
	private float InPort(FlowBox b, float height) => b.Terminal ? height / 2f : S(22);

	/// <summary>行里的短说明：这条路上每一步分岔，选项写选项文字，掷骰写范围，条件只写个大概（完整的悬停看）。</summary>
	private static string ShortLabel(StoryExit exit) => exit.Path.Count == 0
		? (exit.AfterScene ? StoryUi.Text(KoreanStoryMessage.AfterBattle, "战斗后") : (NativeKorean.Enabled ? TextConverter.ForDisplay("接着") : "接着"))
		: string.Join(" · ", exit.Path.Select(StepShort).ToArray());

	private static string StepShort(StoryStep step)
	{
		switch (step.Kind)
		{
			case StoryStep.Option:
				return "「" + Short(StoryScripts.OptionText(step.Key), 12) + "」";
			case StoryStep.Dice:
				return (NativeKorean.Enabled ? "주사위 " : "掷出 ") + (step.Range.Length > 0 ? step.Range : Short(StoryScripts.OptionText(step.Key), 6));
			default:
				string full = StepLabel(step);
				// Translate complete condition labels before making a compact row preview.
				return full.StartsWith("如果 ") ? "若 " + Short(full.Substring(3), 14) : Short(full, 12);
		}
	}

	private void DrawFlowCanvas(Rect canvas)
	{
		FlowGraph graph = _flow;
		Event e = Event.current;
		if (e.type == EventType.ScrollWheel && canvas.Contains(e.mousePosition) && e.shift)
		{
			_flowScroll.x += e.delta.y * S(30);
			e.Use();
		}
		// 四周留出余量：图比窗口小时也能上下左右拖
		_flowScroll = GUI.BeginScrollView(canvas, _flowScroll, new Rect(0, 0, Mathf.Max(graph.Width, canvas.width) + canvas.width * 0.4f, Mathf.Max(graph.Height, canvas.height) + canvas.height * 0.4f));
		int pan = GUIUtility.GetControlID(FocusType.Passive);
		var view = new Rect(_flowScroll.x, _flowScroll.y, canvas.width, canvas.height);
		if (e.type == EventType.Repaint)
		{
			// 线：从方块里那一行去处的右边伸出去，在目标方块左边的竖道里拐弯，进目标方块；进同一个方块的几条线各占一条竖道
			var slots = new Dictionary<FlowBox, int>();
			foreach (FlowBox box in graph.Boxes)
			{
				for (int k = 0; k < box.Ways.Count; k++)
				{
					FlowWay way = box.Ways[k];
					if (way.To == null)
					{
						continue;
					}
					bool hot = _flowPicked != null && (box == _flowPicked || way.To == _flowPicked);
					Color color = hot ? Cinnabar : InkLine;
					float width = hot ? S(3) : S(2);
					var from = new Vector2(box.Rect.xMax, box.RowsTop + (k + 0.5f) * RowHeight);
					Rect b = way.To.Rect;
					if (way.Back)
					{
						// 往回接的线从下面绕过去
						float below = Mathf.Max(box.Rect.yMax, b.yMax) + S(14);
						Segment(from, new Vector2(from.x + S(14), from.y), color, width, true);
						Segment(new Vector2(from.x + S(14), from.y), new Vector2(from.x + S(14), below), color, width, true);
						Segment(new Vector2(from.x + S(14), below), new Vector2(b.center.x, below), color, width, true);
						Segment(new Vector2(b.center.x, below), new Vector2(b.center.x, b.yMax), color, width, true);
						continue;
					}
					slots.TryGetValue(way.To, out int slot);
					slots[way.To] = slot + 1;
					var to = new Vector2(b.x, b.y + InPort(way.To, b.height));
					float lane = b.x - S(18) - (slot % 8) * S(7);
					Elbow(from, to, color, width, false, Mathf.Max(from.x + S(10), lane));
					Fill(new Rect(to.x - S(7), to.y - S(4), S(7), S(8)), color);
				}
			}
		}
		foreach (FlowBox box in graph.Boxes)
		{
			if (!box.Rect.Overlaps(view))
			{
				continue;
			}
			if (e.type == EventType.Repaint)
			{
				bool picked = box == _flowPicked;
				if (box.Terminal)
				{
					// 走出本任务的去处：虚线框；结局用朱砂
					Color c = box.Ending ? Cinnabar : InkMuted;
					Fill(box.Rect, Paper);
					Segment(new Vector2(box.Rect.x, box.Rect.y), new Vector2(box.Rect.xMax, box.Rect.y), c, 1, true);
					Segment(new Vector2(box.Rect.x, box.Rect.yMax), new Vector2(box.Rect.xMax, box.Rect.yMax), c, 1, true);
					Segment(new Vector2(box.Rect.x, box.Rect.y), new Vector2(box.Rect.x, box.Rect.yMax), c, 1, true);
					Segment(new Vector2(box.Rect.xMax, box.Rect.y), new Vector2(box.Rect.xMax, box.Rect.yMax), c, 1, true);
					var t = new GUIStyle(_boxSmall) { alignment = TextAnchor.MiddleCenter };
					t.normal.textColor = c;
					GUI.Label(box.Rect, box.Ending ? "<b>" + box.Title + "</b>" : box.Title, t);
					continue;
				}
				// 剧情段：宣纸卡片，墨边；标题下一条读过多少的进度，再往下一行一个去处
				Fill(box.Rect, picked ? Paper : PaperLight);
				Frame(box.Rect, picked ? Cinnabar : Ink, picked ? S(2) : 1);
				GUI.Label(new Rect(box.Rect.x + S(12), box.Rect.y + S(8), box.Rect.width - S(20), S(24)), Short(box.Title, 15), _boxTitle);
				GUI.Label(new Rect(box.Rect.x + S(12), box.Rect.y + S(32), box.Rect.width - S(20), S(20)), box.Sub, _boxSmall);
				ReadBar(new Rect(box.Rect.x + S(12), box.RowsTop - S(6), box.Rect.width - S(24), S(3)), box.Read);
				if (box.Ways.Count == 0)
				{
					GUI.Label(new Rect(box.Rect.x + S(12), box.RowsTop, box.Rect.width - S(24), RowHeight), "这一段之后没有直接接着的剧本", _boxSmall);
				}
				for (int k = 0; k < box.Ways.Count; k++)
				{
					FlowWay way = box.Ways[k];
					var row = new Rect(box.Rect.x + S(1), box.RowsTop + k * RowHeight, box.Rect.width - S(2), RowHeight);
					if (k > 0)
					{
						Fill(new Rect(row.x + S(10), row.y, row.width - S(20), 1), new Color(InkLine.r, InkLine.g, InkLine.b, InkLine.a * 0.35f));
					}
					bool stays = way.To == null;
					string text = stays ? way.Short + "　<color=#8A7E6E>留在本段</color>" : way.Short;
					if (_flowRow == null || !Mathf.Approximately(_flowRowScale, _scale) || _flowRowDark != PanelTheme.Dark)
					{
						_flowRowScale = _scale;
						_flowRowDark = PanelTheme.Dark;
						_flowRow = new GUIStyle(_tag) { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
						_flowRow.normal.background = null;
						_flowRowMuted = new GUIStyle(_boxSmall) { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip, richText = true };
					}
					GUI.Label(new Rect(row.x + S(11), row.y, row.width - S(28), row.height), text, stays ? _flowRowMuted : _flowRow);
					if (!stays)
					{
						bool hot = _flowPicked != null && (box == _flowPicked || way.To == _flowPicked);
						Fill(new Rect(row.xMax - S(6), row.center.y - S(3), S(6), S(6)), hot ? Cinnabar : Ink);
					}
				}
				if (box.Kept != null)
				{
					var stamp = new Rect(box.Rect.xMax - S(26), box.Rect.y + S(6), S(20), S(20));
					Round(stamp, Cinnabar, S(3));
					GUI.Label(stamp, StoryUi.Text(KoreanStoryMessage.SaveStamp, "存"), new GUIStyle(_seal) { fontSize = Mathf.Max(8, Mathf.RoundToInt(_seal.fontSize * 0.65f)) });
				}
			}
			for (int k = 0; k < box.Ways.Count && !box.Terminal; k++)
			{
				FlowWay way = box.Ways[k];
				var row = new Rect(box.Rect.x, box.RowsTop + k * RowHeight, box.Rect.width, RowHeight);
				string full = way.Full.Count > 0 && way.To == null ? "「" + StoryScripts.OptionText(way.Full[0]) + "」" : string.Join("\n", way.Full.ToArray());
				string tip = (full.Length > 0 ? full : way.Short) + (way.To != null && !way.To.Terminal ? "\n→ " + way.To.Title : way.To != null ? "\n→ " + way.To.Title : "\n选了以后还在这一段里，之后合回来") + (way.Effects.Length > 0 ? "\n" + way.Effects : "");
				Hover(row, tip);
			}
			if (!box.Terminal && GUI.Button(box.Rect, GUIContent.none, GUIStyle.none))
			{
				_flowPicked = box;
				_flowDetailScroll = Vector2.zero;
			}
		}
		switch (e.GetTypeForControl(pan))
		{
			// 右边、下边的滚动条留给滚动条自己拖
			case EventType.MouseDown when e.button == 0 && new Rect(view.x, view.y, view.width - S(22), view.height - S(22)).Contains(e.mousePosition):
				GUIUtility.hotControl = pan;
				e.Use();
				break;
			case EventType.MouseDrag when GUIUtility.hotControl == pan:
				_flowScroll -= e.delta;
				e.Use();
				break;
			case EventType.MouseUp when GUIUtility.hotControl == pan:
				GUIUtility.hotControl = 0;
				e.Use();
				break;
		}
		GUI.EndScrollView();
	}

	private void DrawBoxDetail(FlowBox box)
	{
		GUILayout.Label(string.Join(" → ", box.Chain.ToArray()), _muted);
		GUILayout.Label(box.Title, _heading);
		if (box.Sub.Length > 0)
		{
			GUILayout.Label(box.Sub, _label);
		}
		if (box.Kept != null)
		{
			ReplayButton(box.Kept, StoryUi.Text(KoreanStoryMessage.ReplayHere, "从这里重玩"), _primary, GUILayout.Height(S(38)));
		}
		if (!box.Terminal && box.Chain.Count > 0)
		{
			JumpButton(_flowMission, box.Chain[0]);
		}
		if (box.Last == null)
		{
			return;
		}
		GUILayout.Space(S(10));
		GUILayout.Label(StoryUi.Text(KoreanStoryMessage.Next, "接下来"), _heading);
		if (box.Last.Exits.Count == 0)
		{
			GUILayout.Label(StoryUi.Text(KoreanStoryMessage.NoNextScript, "这一段之后没有直接接着的剧本"), _muted);
		}
		foreach (StoryExit exit in box.Last.Exits)
		{
			GUILayout.BeginVertical(_card);
			GUILayout.Label(Cached(exit, () => OnPaper(StoryScripts.Describe(exit)) + "<color=#A3302A>" + ReadMark(exit) + "</color>"), _rich);
			string effects = Cached((exit, "效果", PanelTheme.Dark, LOMAssistant.Config.ConfigManager.ColorOptionDeltas.Value), () => PanelDeltas(string.Join("　", box.Last.EffectsOn(exit).ToArray())));
			if (effects.Length > 0)
			{
				GUILayout.Label(effects, _muted);
			}
			GUILayout.EndVertical();
		}
	}
}
