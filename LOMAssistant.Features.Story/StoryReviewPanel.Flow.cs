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
			string g = m.From != null ? $"第{m.From.Value / 36 + 1}年" : "任意时间 / 每月";
			if (g != group)
			{
				group = g;
				GUILayout.Label(g, _muted);
			}
			string when = m.From != null ? $"{m.From.Value % 36 / 3 + 1}月{StoryAtlas.StageText(m.From.Value % 3 + 1)}" : m.When;
			string kind = m.Main ? "主线" : Short(m.SeriesTitle, 10);
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
		GUI.Label(new Rect(area.x, area.y, area.width, S(36)), $"<b>{_flowMission.Title}</b>　<color=#73644F>{_flowMission.When}{(_flowMission.Place.Length > 0 ? " · " + _flowMission.Place : "")}{(_flow.Cut ? " · 太长，只画了前 " + FlowLimit + " 段" : "")}</color>", _label);
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
	private static string LinkLabel(StoryExit exit) => exit.Path.Count == 0 ? (exit.AfterScene ? "战斗后" : "") : string.Join(" → ", exit.Path.Select(StepLabel).ToArray());

	/// <summary>线上的短标签：选项写「选项文字」，掷骰写「掷出 &lt;33」，条件写「如果 小梅好感>=35」。</summary>
	private static string StepLabel(StoryStep step)
	{
		switch (step.Kind)
		{
			case StoryStep.Option:
				return "「" + StoryScripts.OptionText(step.Key) + "」";
			case StoryStep.Dice:
				return "掷出 " + (step.Range.Length > 0 ? step.Range : Short(StoryScripts.OptionText(step.Key), 6));
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
				text = System.Text.RegularExpressions.Regex.Replace(text, @" → 当前[^ ]*", "").Replace(" 需", "").Trim();
				return text.Length > 0 ? "如果 " + text : "条件不明";
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
			if (last != null && last.Menu) tags.Add("选项");
			if (last != null && last.Dice) tags.Add("掷骰");
			if (last != null && last.Fight) tags.Add("战斗");
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
			box.Sub = string.Join("・", tags.ToArray()) + (box.Read != null ? (tags.Count > 0 ? "　" : "") + "读过 " + box.Read + "%" : "");
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
			var map = new FlowBox { Id = "map|" + root, Title = "地图上接着", Terminal = true, Layer = layer };
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
						to = Terminal(from, name.Length > 0 ? "结局：" + Short(name, 9) : "结局", true);
					}
					else if (exit.MainMission.Length > 0)
					{
						StoryMission next = StoryAtlas.Find(exit.MainMission);
						to = Terminal(from, "主线 → " + (next != null ? Short(next.Title, 10) : exit.MainMission), false);
					}
					else if (exit.ToMap)
					{
						to = Terminal(from, "回到地图", false);
					}
					else if (exit.Target.Length == 0)
					{
						continue;
					}
					else if (otherStarts.Contains(exit.Target))
					{
						StoryMission other = StoryAtlas.Missions.FirstOrDefault(m => string.Equals(m.StartScript, exit.Target, StringComparison.OrdinalIgnoreCase));
						to = Terminal(from, "接到 " + (other != null ? Short(other.Title, 10) : exit.Target), false);
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
				}
				// 输了会死的决斗
				foreach (var (path, level) in from.Last.Fights)
				{
					if (!ConditionBook.CombatDeaths.TryGetValue(level, out string deadId))
					{
						continue;
					}
					StoryEnding ending = StoryAtlas.Endings.FirstOrDefault(e => e.Death && e.Id == deadId);
					FlowBox to = Terminal(from, "死亡：" + Short(ending != null ? ending.Title : deadId, 9), true);
					FlowLink link = graph.Links.Find(l => l.From == from && l.To == to);
					if (link == null)
					{
						link = new FlowLink { From = from, To = to };
						graph.Links.Add(link);
					}
					string label = (path.Count > 0 ? StepLabel(path[path.Count - 1]) + "，" : "") + "决斗输了";
					if (!link.Labels.Contains(label))
					{
						link.Labels.Add(label);
					}
				}
				// 死亡（生死簿）不是「回到标题」，单独画出来
				foreach (var (path, death, id) in from.Last.Endings.Where(e => e.Death))
				{
					StoryEnding ending = StoryAtlas.Endings.FirstOrDefault(e => e.Death && e.Id == id);
					FlowBox to = Terminal(from, "死亡：" + Short(ending != null ? ending.Title : id, 9), true);
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
				}
			}
		}

		FlowGraph Layout()
		{
			// 排版：同一层从上往下排；每条线的标签完整显示（每项一行，太长就换行），标签写在进方块的那段线上方，
			// 所以每个方块上面按进来的标签留出空间
			float w = S(230), h = S(62), gapX = S(260), gapY = S(24), labelWidth = gapX - S(60);
			var labelStyle = new GUIStyle(_tag) { wordWrap = true, alignment = TextAnchor.LowerRight };
			var reserve = new Dictionary<FlowBox, float>();
			foreach (FlowLink link in graph.Links.Where(l => !l.Back && l.Labels.Count > 0))
			{
				link.Text = string.Join("\n", link.Labels.ToArray());
				link.Height = labelStyle.CalcHeight(new GUIContent(link.Text), labelWidth) + S(4);
				reserve.TryGetValue(link.To, out float used);
				link.Lift = used;
				reserve[link.To] = used + link.Height + S(2);
			}
			// 一层一层往右排：同一层里按上一层「父方块」的位置排先后（父方块在上的排上面，同一个父方块下按去处原本的顺序），
			// 剧情段在前、走出本任务的小方块在后；每个方块尽量和父方块对齐，放不下就往下挪
			var parents = graph.Links.Where(l => !l.Back).GroupBy(l => l.To).ToDictionary(g => g.Key, g => g.Select(l => l.From).ToList());
			var order = graph.Links.Select((l, i) => (l, i)).GroupBy(x => x.l.To).ToDictionary(g => g.Key, g => g.Min(x => x.i));
			float bottom = 0;
			foreach (var layer in graph.Boxes.GroupBy(b => b.Layer).OrderBy(g => g.Key))
			{
				float ParentY(FlowBox b) => parents.TryGetValue(b, out var ps) && ps.Count > 0 ? ps.Average(p => p.Rect.center.y) : 0f;
				var sorted = layer.OrderBy(ParentY).ThenBy(b => b.Terminal ? 1 : 0).ThenBy(b => order.TryGetValue(b, out int i) ? i : int.MaxValue).ToList();
				float y = S(24);
				int row = 0;
				foreach (FlowBox b in sorted)
				{
					b.Row = row++;
					float bh = b.Terminal ? h * 0.62f : h;
					reserve.TryGetValue(b, out float above);
					y += Mathf.Max(0, above - bh / 2f + S(6));
					float wanted = layer.Key == 0 ? y : ParentY(b) - bh / 2f;
					y = Mathf.Max(y, wanted);
					b.Rect = new Rect(S(24) + layer.Key * (w + gapX), y, b.Terminal ? w * 0.8f : w, bh);
					y += bh + gapY;
				}
				bottom = Mathf.Max(bottom, y);
			}
			graph.Width = S(48) + (graph.Boxes.Count == 0 ? 0 : graph.Boxes.Max(b => b.Layer) + 1) * (w + gapX);
			graph.Height = bottom + S(40);
			return graph;
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
			// 线：先画线，标签在线的末段上，同一个方块进来的多条线标签错开
			var incoming = new Dictionary<FlowBox, int>();
			foreach (FlowLink link in graph.Links)
			{
				Rect a = link.From.Rect, b = link.To.Rect;
				bool hot = _flowPicked != null && (link.From == _flowPicked || link.To == _flowPicked);
				Color color = hot ? Cinnabar : InkLine;
				if (link.Back)
				{
					// 往回接的线从下面绕过去
					float below = Mathf.Max(a.yMax, b.yMax) + S(14);
					var p1 = new Vector2(a.xMax, a.center.y);
					Segment(p1, new Vector2(a.xMax + S(14), p1.y), color, S(2), true);
					Segment(new Vector2(a.xMax + S(14), p1.y), new Vector2(a.xMax + S(14), below), color, S(2), true);
					Segment(new Vector2(a.xMax + S(14), below), new Vector2(b.center.x, below), color, S(2), true);
					Segment(new Vector2(b.center.x, below), new Vector2(b.center.x, b.yMax), color, S(2), true);
					continue;
				}
				var from = new Vector2(a.xMax, a.center.y);
				var to = new Vector2(b.x, b.center.y);
				float bend = from.x + S(24);
				Elbow(from, to, color, hot ? S(3) : S(2), false, bend);
				Fill(new Rect(to.x - S(7), to.y - S(4), S(7), S(8)), color);
				if (link.Text.Length > 0)
				{
					// 标签贴在进方块那段线的上方；同一个方块进来几条线时往上叠
					float lw = to.x - bend - S(20);
					var lr = new Rect(to.x - S(12) - lw, to.y - S(4) - link.Lift - link.Height, lw, link.Height);
					GUI.Label(lr, link.Text, new GUIStyle(_tag) { wordWrap = true, alignment = TextAnchor.LowerRight, normal = { background = null, textColor = hot ? Cinnabar : Ink } });
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
				// 剧情段：宣纸卡片，墨边；底下一条读过多少的进度
				Fill(box.Rect, picked ? Paper : PaperLight);
				Frame(box.Rect, picked ? Cinnabar : Ink, picked ? S(2) : 1);
				GUI.Label(new Rect(box.Rect.x + S(12), box.Rect.y + S(8), box.Rect.width - S(20), S(24)), Short(box.Title, 13), _boxTitle);
				GUI.Label(new Rect(box.Rect.x + S(12), box.Rect.y + S(32), box.Rect.width - S(20), S(20)), box.Sub, _boxSmall);
				ReadBar(new Rect(box.Rect.x + S(12), box.Rect.yMax - S(7), box.Rect.width - S(24), S(3)), box.Read);
				if (box.Kept != null)
				{
					var stamp = new Rect(box.Rect.xMax - S(26), box.Rect.y + S(6), S(20), S(20));
					Round(stamp, Cinnabar, S(3));
					GUI.Label(stamp, "存", new GUIStyle(_seal) { fontSize = Mathf.Max(8, Mathf.RoundToInt(_seal.fontSize * 0.65f)) });
				}
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
			ReplayButton(box.Kept, "从这里重玩", _primary, GUILayout.Height(S(38)));
		}
		if (box.Last == null)
		{
			return;
		}
		GUILayout.Space(S(10));
		GUILayout.Label("接下来", _heading);
		if (box.Last.Exits.Count == 0)
		{
			GUILayout.Label("这一段之后没有直接接着的剧本", _muted);
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
