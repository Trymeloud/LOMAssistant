using System;
using System.Collections.Generic;
using System.Linq;
using Mortal.Core;
using UnityEngine;

namespace LOMAssistant.Features.Story;

// 一条贯穿全程的主线（这一局走过的优先）画在同一条车道上，岔路分到上下的车道、线上写触发条件。横轴是游戏日历，支线按任务组挂在下面。
// 一个站点往后岔出几条路时，第一条留在原车道，其余分到上下的车道。横轴是游戏日历，支线按任务组挂在下面。
public partial class StoryReviewPanel
{
	private sealed class RouteLayout
	{
		public readonly Dictionary<string, Vector2> Points = new Dictionary<string, Vector2>(StringComparer.Ordinal);
		public readonly List<(string From, string To, bool ByDate)> Edges = new List<(string, string, bool)>();
		public readonly List<(float X, int Date)> Columns = new List<(float, int)>();
		public readonly List<SideStory> Side = new List<SideStory>();
		public float Width, Height, MainBottom, LabelWidth;
		/// <summary>岔开的线上写的触发条件（短），按 (从, 到)。</summary>
		public readonly Dictionary<(string, string), string> EdgeLabels = new Dictionary<(string, string), string>();
		/// <summary>一条贯穿全程的主线（这一局走过的优先），画在同一条车道上。</summary>
		public readonly HashSet<string> Spine = new HashSet<string>(StringComparer.Ordinal);
	}

	private sealed class SideStory
	{
		public string Series = "", Title = "";
		public List<StoryMission> Missions = new List<StoryMission>();
		/// <summary>走完会把主线设到这些站（襄阳行这类）。</summary>
		public List<StoryMission> Joins = new List<StoryMission>();
		public Rect Pill;
		public int? Read;
		public bool Visited, Dated;
	}

	private RouteLayout _route;
	private int _routeBuiltFor = -1;
	private float _routeZoom = 1f, _routeBuiltZoom;
	private Vector2 _routeScroll, _routeDetailScroll;
	private string _routeSearch = "", _routeLastQuery = "";
	private StoryMission _routePicked;
	private SideStory _routeSide;
	// 结缘路线：选了一位，地图上和她有关的任务亮着，其余变淡，右边列出她的结缘进度
	private StoryRomance.Heroine _routeHeroine;
	private HashSet<StoryMission> _romanceMissions, _romanceSteps;
	private float _routeDetailInner; // 详情栏里能用的宽度（去掉边距和滚动条）

	private float Z(float v) => S(v) * _routeZoom;

	private void DrawRoute(Rect r)
	{
		if (!EnsureAtlas(r))
		{
			return;
		}
		if (_route == null || _routeBuiltFor != StoryAtlas.Version || !Mathf.Approximately(_routeBuiltZoom, _routeZoom * _scale))
		{
			_route = BuildRoute();
			_routeBuiltFor = StoryAtlas.Version;
			_routeBuiltZoom = _routeZoom * _scale;
		}
		// 顶上一行：搜索、缩放、跳到当前主线、重新生成
		string query = SearchBox(new Rect(r.x, r.y, S(360), S(36)), ref _routeSearch);
		// 输入新的搜索词时，右边改成显示搜索结果
		if (query != _routeLastQuery)
		{
			_routeLastQuery = query;
			if (query.Length >= 2)
			{
				_routePicked = null;
				_routeSide = null;
				_routeDetailScroll = Vector2.zero;
			}
		}
		float bx = r.x + S(376);
		if (GUI.Button(new Rect(bx, r.y, S(40), S(36)), "－", _button)) _routeZoom = Mathf.Max(0.6f, _routeZoom - 0.1f);
		GUI.Label(new Rect(bx + S(44), r.y, S(60), S(36)), Mathf.RoundToInt(_routeZoom * 100) + "%", _center);
		if (GUI.Button(new Rect(bx + S(108), r.y, S(40), S(36)), "＋", _button)) _routeZoom = Mathf.Min(1.6f, _routeZoom + 0.1f);
		if (GUI.Button(new Rect(bx + S(160), r.y, TextWidth(StoryUi.Text(KoreanStoryMessage.CurrentMain, "当前主线"), _button, S(130)), S(36)), StoryUi.Text(KoreanStoryMessage.CurrentMain, "当前主线"), _button) && _route.Points.TryGetValue(StoryAtlas.CurrentMain, out Vector2 now))
		{
			_routePicked = StoryAtlas.Find(StoryAtlas.CurrentMain);
			_routeSide = null;
			_routeScroll = new Vector2(Mathf.Max(0, now.x - r.width * 0.3f), Mathf.Max(0, now.y - r.height * 0.4f));
		}
		if (GUI.Button(new Rect(r.xMax - S(130), r.y, S(130), S(36)), StoryUi.Text(KoreanStoryMessage.Rebuild, "重新生成"), _button))
		{
			_reading.Clear();
			StoryAtlas.Start();
			return;
		}
		DrawRomanceBar(new Rect(r.x, r.y + S(44), r.width, S(34)));
		float detailWidth = Mathf.Min(S(460), r.width * 0.32f);
		var canvas = new Rect(r.x, r.y + S(86), r.width - detailWidth - S(12), r.height - S(86));
		var detail = new Rect(canvas.xMax + S(12), canvas.y, detailWidth, canvas.height);
		Fill(canvas, PaperLight);
		Frame(canvas, Edge, 1);
		DrawRouteCanvas(canvas, query);
		Fill(detail, PaperDeep);
		Frame(detail, Edge, 1);
		DragScroll("routeDetail", detail, ref _routeDetailScroll);
		GUILayout.BeginArea(new Rect(detail.x + S(12), detail.y + S(10), detail.width - S(24), detail.height - S(20)));
		_routeDetailInner = detail.width - S(24) - S(22);
		_routeDetailScroll = GUILayout.BeginScrollView(_routeDetailScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
		if (query.Length >= 2 && _routeSide == null && _routePicked == null)
		{
			DrawSearchResults(query);
		}
		else if (_routeSide != null)
		{
			DrawSideDetail(_routeSide);
		}
		else if (_routePicked != null)
		{
			DrawMissionDetail(_routePicked);
		}
		else if (_routeHeroine != null)
		{
			DrawRomanceDetail(_routeHeroine);
		}
		GUILayout.EndScrollView();
		GUILayout.EndArea();
	}

	private RouteLayout BuildRoute()
	{
		var layout = new RouteLayout { LabelWidth = Z(132) };
		var mains = StoryAtlas.Missions.Where(m => m.Main && m.StartScript.Length > 0).ToDictionary(m => m.Key, StringComparer.Ordinal);
		// 连线：剧本里写明的
		var edges = new List<(string From, string To, bool ByDate)>();
		foreach (StoryMission m in mains.Values)
		{
			foreach (string next in m.Next)
			{
				if (mains.ContainsKey(next))
				{
					edges.Add((m.Key, next, false));
				}
			}
		}
		// 日期：没有日期的任务跟在它前一站后面
		var date = mains.Values.ToDictionary(m => m.Key, m => m.From, StringComparer.Ordinal);
		for (int pass = 0; pass < 6; pass++)
		{
			foreach (var e in edges)
			{
				if (date[e.To] == null && date[e.From] != null)
				{
					date[e.To] = date[e.From];
				}
			}
		}
		var placed = mains.Values.Where(m => date[m.Key] != null).OrderBy(m => date[m.Key]).ThenBy(m => m.Key, StringComparer.Ordinal).ToList();
		// 没有任何剧本写明会走到的站点（按日期自动开始的），从日期最近的前一站接一条虚线
		var hasIncoming = new HashSet<string>(edges.Select(e => e.To), StringComparer.Ordinal);
		for (int i = 1; i < placed.Count; i++)
		{
			StoryMission m = placed[i];
			if (hasIncoming.Contains(m.Key))
			{
				continue;
			}
			int? before = placed.Take(i).Select(p => date[p.Key]).Where(d => d < date[m.Key]).DefaultIfEmpty(null).Max();
			if (before == null)
			{
				continue;
			}
			foreach (StoryMission p in placed.Take(i).Where(p => date[p.Key] == before).Take(2))
			{
				edges.Add((p.Key, m.Key, true));
			}
		}
		layout.Edges.AddRange(edges);

		// 横轴：每个出现过的日期一列，列距按相隔的旬数，有上下限
		float x = Z(40);
		int? last = null;
		foreach (int d in placed.Select(m => date[m.Key].Value).Distinct().OrderBy(v => v))
		{
			if (last != null)
			{
				x += Mathf.Clamp((d - last.Value) * Z(14), layout.LabelWidth + Z(16), Z(260));
			}
			layout.Columns.Add((x, d));
			last = d;
		}

		// 车道：先挑一条贯穿全程的主线放在第 0 道（每到一个岔口，走这一局走过的那条；都没走过就走后面还长的那条），
		// 其余的站从主线上下两边就近找空车道，岔路的第一条接着沿用岔出去的那条车道，合流时回到主线
		var children = edges.GroupBy(e => e.From).ToDictionary(g => g.Key, g => g.Select(e => e.To).Distinct().OrderBy(k => date[k] ?? int.MaxValue).ThenBy(k => k, StringComparer.Ordinal).ToList(), StringComparer.Ordinal);
		var depth = new Dictionary<string, int>(StringComparer.Ordinal);
		int Depth(string key, int guard)
		{
			if (depth.TryGetValue(key, out int d))
			{
				return d;
			}
			depth[key] = 0;
			int best = 0;
			if (guard < 200 && children.TryGetValue(key, out var next))
			{
				foreach (string n in next.Where(n => date[n] >= date[key]))
				{
					best = Mathf.Max(best, 1 + Depth(n, guard + 1));
				}
			}
			return depth[key] = best;
		}
		var placedKeys = new HashSet<string>(placed.Select(m => m.Key), StringComparer.Ordinal);
		string at = placed.Count > 0 ? placed[0].Key : null;
		while (at != null && layout.Spine.Add(at))
		{
			at = children.TryGetValue(at, out var next) ? next.Where(n => placedKeys.Contains(n) && date[n] >= date[at] && !layout.Spine.Contains(n))
				.OrderByDescending(n => mains[n].Visited ? 1 : 0).ThenByDescending(n => Depth(n, 0)).FirstOrDefault() : null;
		}
		// 名字按实际显示的宽度算，同一条车道上两站的名字不叠在一起
		var nameStyle = new GUIStyle(_boxTitle) { fontSize = Mathf.Max(9, Mathf.RoundToInt(_boxTitle.fontSize * _routeZoom)), wordWrap = false };
		var widths = placed.ToDictionary(m => m.Key, m => Mathf.Max(layout.LabelWidth * 0.6f, nameStyle.CalcSize(new GUIContent(TextConverter.ForDisplay("✓ " + Short(m.Title, 9)))).x), StringComparer.Ordinal);
		var usedWidth = new Dictionary<int, List<(float X, float W)>>();
		bool Free(int l, float px, float pw) => !usedWidth.TryGetValue(l, out var xs) || xs.All(v => Mathf.Abs(v.X - px) >= (v.W + pw) / 2f + Z(14));
		var lane = new Dictionary<string, int>(StringComparer.Ordinal);
		void Take(string key, int l)
		{
			lane[key] = l;
			if (!usedWidth.TryGetValue(l, out var list))
			{
				usedWidth[l] = list = new List<(float, float)>();
			}
			list.Add((XOf(layout, date[key].Value), widths[key]));
		}
		// 主线上同一天的两站会落在同一个横坐标：第一站占中间这道，后面的挪到旁边空着的车道
		int Nearest(float px, float pw, bool allowSpine)
		{
			for (int step = 0; step < 24; step++)
			{
				int candidate = step == 0 ? 0 : step % 2 == 1 ? (step + 1) / 2 : -(step / 2);
				if ((allowSpine || candidate != 0) && Free(candidate, px, pw))
				{
					return candidate;
				}
			}
			return allowSpine ? 0 : 1;
		}
		foreach (StoryMission m in placed.Where(m => layout.Spine.Contains(m.Key)))
		{
			Take(m.Key, Nearest(XOf(layout, date[m.Key].Value), widths[m.Key], true));
		}
		foreach (StoryMission m in placed.Where(m => !layout.Spine.Contains(m.Key)))
		{
			float px = XOf(layout, date[m.Key].Value);
			var preds = edges.Where(e => e.To == m.Key && lane.ContainsKey(e.From)).Select(e => e.From).ToList();
			// 接着前一站所在的岔路车道；从主线上岔出来的，从主线下面一道开始找
			string from = preds.FirstOrDefault(p => !layout.Spine.Contains(p)) ?? preds.FirstOrDefault();
			int want = from == null ? 1 : lane[from] == 0 ? 1 : lane[from];
			int chosen = want;
			for (int step = 0; step < 24; step++)
			{
				int candidate = want + (step % 2 == 0 ? step / 2 : -(step / 2 + 1));
				if (candidate != 0 && Free(candidate, px, widths[m.Key]))
				{
					chosen = candidate;
					break;
				}
			}
			Take(m.Key, chosen);
		}
		// 岔开的线上写触发条件：去掉每条都有的时间、行动次数，留第一条，写短
		foreach (var (from, to, byDate) in edges)
		{
			if (!children.TryGetValue(from, out var outs) || outs.Count < 2 || !mains.TryGetValue(to, out StoryMission target))
			{
				continue;
			}
			// 几组条件之间是「\n或 」；旗标说明里自己也会换行（「你的决策」后面一行一个选项），换成空格
			if (NativeKorean.Enabled)
			{
				string preview = KoreanStoryText.RouteClause(RichText.Strip(StoryAtlas.ConditionText(target)),
					id => TextConverter.ToSimplified(MissionManagerData.Instance?.GetFlag(id)?.DevNote ?? ""));
				if (preview.Length > 0) layout.EdgeLabels[(from, to)] = Short(preview, 12);
				continue;
			}
			string first = RichText.Strip(StoryAtlas.ConditionText(target)).Split(new[] { "\n或 " }, StringSplitOptions.None)[0].Replace("\r", "").Replace("\n", " ");
			string clause = first.Split(new[] { " 且 " }, StringSplitOptions.None).Select(c => c.Trim())
				.FirstOrDefault(c => c.Length > 0 && !c.Contains("时间") && !c.Contains("時間") && !c.Contains("行动次数") && !c.Contains("行動次數"));
			if (clause == null)
			{
				continue;
			}
			// 条件写成「名字 (编号) 需=4 → 当前0」，名字只是旗标说明的第一行。说明后面几行一行一个选项（「你的决策」：1. 退隐江湖 2. 战他娘亲 …）
			// 的，按要的数字查出那一项，写成「你的决策：西武林盟」
			var flag = System.Text.RegularExpressions.Regex.Match(clause, @"^(.+?) \(([A-Za-z0-9_]+)\) 需=(\d+)");
			string note = flag.Success ? MissionManagerData.Instance?.GetFlag(flag.Groups[2].Value)?.DevNote : null;
			var pick = note != null ? System.Text.RegularExpressions.Regex.Match(TextConverter.ToSimplified(note), @"(?:^|\s)" + flag.Groups[3].Value + @"[.．、:：]\s*([^\s\r\n]+)") : System.Text.RegularExpressions.Match.Empty;
			clause = pick.Success
				? flag.Groups[1].Value.Trim() + "：" + pick.Groups[1].Value
				: System.Text.RegularExpressions.Regex.Replace(clause, @" → 当前.*$| \([A-Za-z0-9_]+\)", "").Replace(" 需", "").Trim();
			if (clause.Length > 0)
			{
				layout.EdgeLabels[(from, to)] = Short(clause, 12);
			}
		}
		int minLane = lane.Values.DefaultIfEmpty(0).Min(), maxLane = lane.Values.DefaultIfEmpty(0).Max();
		float top = Z(70), laneHeight = Z(96);
		foreach (StoryMission m in placed)
		{
			layout.Points[m.Key] = new Vector2(XOf(layout, date[m.Key].Value), top + (lane[m.Key] - minLane) * laneHeight + Z(20));
		}
		layout.MainBottom = top + (maxLane - minLane + 1) * laneHeight;

		// 支线：每个任务组一颗胶囊，放在最早能触发的日期上，一行放不下就换行
		float y = layout.MainBottom + Z(56);
		var rows = new List<float>();
		var groups = StoryAtlas.Missions.Where(m => !m.Main && m.StartScript.Length > 0).GroupBy(m => m.Series)
			.Select(g => new SideStory
			{
				Series = g.Key,
				Title = SideTitle(g.ToList()),
				Missions = g.ToList(),
				Joins = g.SelectMany(m => m.Next).Distinct().Where(mains.ContainsKey).Select(k => mains[k]).ToList(),
				Dated = g.Any(m => m.From != null),
				Read = StoryScripts.ReadPercent(g.SelectMany(m => m.Scripts).Distinct().Select(StoryScripts.Get).Where(s => s != null).SelectMany(s => s.Lines)),
				Visited = g.Any(m => m.Visited)
			}).OrderBy(s => s.Dated ? s.Missions.Where(m => m.From != null).Min(m => m.From.Value) : int.MaxValue).ToList();
		var pillStyle = new GUIStyle(_boxTitle) { fontSize = Mathf.Max(9, Mathf.RoundToInt(_boxTitle.fontSize * _routeZoom)) };
		// 签条：左边一道墨条，前面留「✓ 」的位置
		foreach (SideStory side in groups)
		{
			float sx = side.Dated ? XOf(layout, side.Missions.Where(m => m.From != null).Min(m => m.From.Value)) - Z(10) : Z(20);
			float w = pillStyle.CalcSize(new GUIContent(TextConverter.ForDisplay("✓ " + side.Title + (side.Joins.Count > 0 ? " " + StoryUi.Text(KoreanStoryMessage.MainArrow, "→ 主线") : "")))).x + Z(26);
			int row = rows.FindIndex(end => end <= sx);
			if (row < 0)
			{
				row = rows.Count;
				rows.Add(0);
			}
			rows[row] = sx + w + Z(10);
			side.Pill = new Rect(sx, y + row * Z(30), w, Z(24));
			layout.Side.Add(side);
		}
		layout.Width = Mathf.Max(layout.Columns.Count > 0 ? layout.Columns[layout.Columns.Count - 1].X : 0, rows.DefaultIfEmpty(0).Max()) + Z(200);
		layout.Height = y + rows.Count * Z(30) + Z(30);
		return layout;
	}

	/// <summary>
	/// 支线签条的名字：任务组自己的名字；游戏里没起名（只有 S2321 这样的编号）的，
	/// 用最早那个任务的说明，再没有就用它的第一条剧情记录。
	/// </summary>
	private static string SideTitle(List<StoryMission> missions)
	{
		string title = missions[0].SeriesTitle;
		if (title.Any(c => c >= 128))
		{
			return NativeKorean.Enabled ? StoryUi.Name(missions[0].FullSeriesTitle.Length > 0 ? missions[0].FullSeriesTitle : title) : title;
		}
		var first = missions.OrderBy(m => m.From ?? int.MaxValue).ThenBy(m => m.Key, StringComparer.Ordinal).ToList();
		string named = first.Select(m => m.Desc).FirstOrDefault(d => d.Any(c => c >= 128))
			?? first.Select(m => m.Summary).FirstOrDefault(d => d.Length > 0);
		return named != null ? Short(named, 12) : title;
	}

	/// <summary>日期 → 横坐标：落在两列之间的按旬数插进去。</summary>
	private static float XOf(RouteLayout layout, int date)
	{
		var cols = layout.Columns;
		if (cols.Count == 0)
		{
			return 0;
		}
		if (date <= cols[0].Date)
		{
			return cols[0].X;
		}
		for (int i = 1; i < cols.Count; i++)
		{
			if (date <= cols[i].Date)
			{
				float t = (float)(date - cols[i - 1].Date) / Mathf.Max(1, cols[i].Date - cols[i - 1].Date);
				return Mathf.Lerp(cols[i - 1].X, cols[i].X, t);
			}
		}
		return cols[cols.Count - 1].X;
	}

	private void DrawRouteCanvas(Rect canvas, string query)
	{
		RouteLayout layout = _route;
		Event e = Event.current;
		// Ctrl + 滚轮：以鼠标所在的位置为中心缩放（地图按新比例重排后，鼠标下面还是同一个地方）
		if (e.type == EventType.ScrollWheel && canvas.Contains(e.mousePosition) && (e.control || e.command))
		{
			float before = _routeZoom;
			float after = Mathf.Clamp(Mathf.Round((before + (e.delta.y < 0 ? 0.1f : -0.1f)) * 10f) / 10f, 0.6f, 1.6f);
			if (!Mathf.Approximately(before, after))
			{
				Vector2 mouse = e.mousePosition - canvas.position;
				_routeScroll = Vector2.Max(Vector2.zero, (_routeScroll + mouse) * (after / before) - mouse);
				_routeZoom = after;
			}
			e.Use();
		}
		else if (e.type == EventType.ScrollWheel && canvas.Contains(e.mousePosition) && e.shift)
		{
			_routeScroll.x += e.delta.y * Z(30);
			e.Use();
		}
		_routeScroll = GUI.BeginScrollView(canvas, _routeScroll, new Rect(0, 0, Mathf.Max(layout.Width, canvas.width) + canvas.width * 0.3f, Mathf.Max(layout.Height, canvas.height) + canvas.height * 0.4f));
		int pan = GUIUtility.GetControlID(FocusType.Passive);
		var view = new Rect(_routeScroll.x, _routeScroll.y, canvas.width, canvas.height);
		bool repaint = e.type == EventType.Repaint;
		var tiny = new GUIStyle(_boxSmall) { fontSize = Mathf.Max(8, Mathf.RoundToInt(_boxSmall.fontSize * _routeZoom)), alignment = TextAnchor.UpperCenter, wordWrap = true, clipping = TextClipping.Overflow };
		var name = new GUIStyle(_boxTitle) { fontSize = Mathf.Max(9, Mathf.RoundToInt(_boxTitle.fontSize * _routeZoom)), alignment = TextAnchor.UpperCenter, wordWrap = true, clipping = TextClipping.Overflow };
		if (repaint)
		{
			// 日历：每年一条淡墨底色，每列标上月份
			int year = -1;
			for (int i = 0; i < layout.Columns.Count; i++)
			{
				var (cx, d) = layout.Columns[i];
				if (d / 36 != year)
				{
					year = d / 36;
					float end = layout.Columns.Skip(i).Where(c => c.Date / 36 != year).Select(c => c.X - layout.LabelWidth / 2).DefaultIfEmpty(layout.Width).First();
					Fill(new Rect(cx - layout.LabelWidth / 2, 0, end - cx + layout.LabelWidth / 2, layout.Height), new Color(Ink.r, Ink.g, Ink.b, year % 2 == 0 ? 0.02f : 0.05f));
					Fill(new Rect(cx - layout.LabelWidth / 2, 0, Z(2), layout.Height), new Color(Ink.r, Ink.g, Ink.b, 0.18f));
					GUI.Label(new Rect(cx - layout.LabelWidth / 2 + Z(8), Z(6), Z(140), Z(26)), "<b>" + StoryUi.Text(KoreanStoryMessage.Year, "第{0}年", year + 1) + "</b>", name);
				}
				Fill(new Rect(cx, Z(56), 1, layout.MainBottom - Z(56)), new Color(Ink.r, Ink.g, Ink.b, 0.07f));
				GUI.Label(new Rect(cx - Z(40), Z(34), Z(80), Z(20)), NativeKorean.Enabled
					? KoreanStoryText.Get(KoreanStoryMessage.MonthStage, d % 36 / 3 + 1, KoreanStoryText.Stage(d % 3 + 1).Substring(0, 1))
					: $"{d % 36 / 3 + 1}月{StoryAtlas.StageText(d % 3 + 1).Substring(0, 1)}", tiny);
			}
			Fill(new Rect(0, layout.MainBottom + Z(16), layout.Width, Z(2)), InkLine);
			GUI.Label(new Rect(view.x + Z(12), layout.MainBottom + Z(24), Z(200), Z(26)), "<b>" + StoryUi.Text(KoreanStoryMessage.Side, "支线") + "</b>", name);
			// 连线：这一局走过的两站之间用青色粗线
			foreach (var (from, to, byDate) in layout.Edges)
			{
				if (!layout.Points.TryGetValue(from, out Vector2 a) || !layout.Points.TryGetValue(to, out Vector2 b))
				{
					continue;
				}
				StoryMission ma = StoryAtlas.Find(from), mb = StoryAtlas.Find(to);
				bool walked = ma != null && mb != null && ma.Visited && mb.Visited;
				// 岔开的线在出发的那一站后面就拐，合流的线在到的那一站前面才拐，看得出从哪分、到哪合
				int outs = layout.Edges.Count(x => x.From == from), ins = layout.Edges.Count(x => x.To == to);
				float bend = outs > 1 && ins <= 1 ? a.x + Z(26) : b.x - Z(26);
				Elbow(new Vector2(a.x + Z(10), a.y), new Vector2(b.x - Z(10), b.y), walked ? Jade : InkLine, walked ? Z(3) : Z(2), byDate, bend);
				// 岔开的线上写它的触发条件，写在拐过去以后那段横线上面
				if (layout.EdgeLabels.TryGetValue((from, to), out string why) && Mathf.Abs(a.y - b.y) > 1f)
				{
					var whyStyle = new GUIStyle(tiny) { alignment = TextAnchor.LowerLeft, wordWrap = false, clipping = TextClipping.Overflow };
					float whyWidth = whyStyle.CalcSize(new GUIContent(TextConverter.ForDisplay(why))).x + Z(4);
					GUI.Label(new Rect(bend + Z(6), b.y - Z(22), whyWidth, Z(18)), "<color=#A3302A>" + why + "</color>", whyStyle);
				}
			}
		}
		// 站点：墨圈，读得越多圈里墨越浓；走过的名字前打勾，当前主线盖一枚朱砂印
		foreach (var pair in layout.Points)
		{
			StoryMission m = StoryAtlas.Find(pair.Key);
			Vector2 p = pair.Value;
			var hit = new Rect(p.x - layout.LabelWidth / 2, p.y - Z(14), layout.LabelWidth, Z(66));
			if (m == null || !hit.Overlaps(view))
			{
				continue;
			}
			bool dim = query.Length > 0 && !Matches(query, m.Title, m.Key, m.Place, m.Summary) || _romanceMissions != null && !_romanceMissions.Contains(m);
			if (repaint)
			{
				float rr = Z(9), alpha = dim ? 0.25f : 1f;
				var dot = new Rect(p.x - rr, p.y - rr, rr * 2, rr * 2);
				Round(dot, new Color(PaperLight.r, PaperLight.g, PaperLight.b, alpha), rr);
				if (m.Read != null)
				{
					Round(dot, new Color(Ink.r, Ink.g, Ink.b, alpha * (0.12f + 0.88f * m.Read.Value / 100f)), rr);
				}
				Round(dot, new Color(Ink.r, Ink.g, Ink.b, alpha), rr, Z(2));
				if (_routePicked == m)
				{
					Round(new Rect(dot.x - Z(4), dot.y - Z(4), dot.width + Z(8), dot.height + Z(8)), Cinnabar, rr + Z(4), Z(2));
				}
				if (StoryAtlas.CurrentMain == m.Key)
				{
					var stamp = new Rect(p.x + Z(9), p.y - Z(30), Z(22), Z(22));
					Round(stamp, Cinnabar, Z(3));
					GUI.Label(stamp, StoryUi.Text(KoreanStoryMessage.NowStamp, "今"), new GUIStyle(_seal) { fontSize = Mathf.Max(8, Mathf.RoundToInt(_seal.fontSize * 0.7f * _routeZoom)) });
				}
				if (m.Endings > 0)
				{
					GUI.Label(new Rect(p.x - Z(34), p.y - Z(22), Z(24), Z(18)), "<color=#A3302A><b>" + StoryUi.Text(KoreanStoryMessage.EndStamp, "终") + "</b></color>", tiny);
				}
				var label = new Rect(p.x - layout.LabelWidth / 2 - Z(20), p.y + Z(13), layout.LabelWidth + Z(40), Z(24));
				// 结缘路线：设她结缘旗标的站，名字后面用朱砂色标出是哪一步（【认识】【告白·攻略】…）
				string title = (m.Visited ? "<color=#2F6B3A>✓</color> " : "") + Short(m.Title, 9) + RomanceMark(m);
				GUI.Label(label, dim ? "<color=#B5AA98>" + RichText.Strip(title) + "</color>" : title, new GUIStyle(name) { wordWrap = false });
			}
			if (GUI.Button(hit, GUIContent.none, GUIStyle.none))
			{
				_routePicked = m;
				_routeSide = null;
				_routeDetailScroll = Vector2.zero;
			}
		}
		// 支线签条
		foreach (SideStory side in layout.Side)
		{
			if (!side.Pill.Overlaps(view))
			{
				continue;
			}
			bool dim = query.Length > 0 && !Matches(query, side.Title) && !side.Missions.Any(m => Matches(query, m.Title, m.Key, m.Place, m.Summary))
				|| _romanceMissions != null && !side.Missions.Any(_romanceMissions.Contains);
			if (repaint)
			{
				float alpha = dim ? 0.3f : 1f;
				Fill(side.Pill, new Color(PaperLight.r, PaperLight.g, PaperLight.b, alpha));
				Frame(side.Pill, _routeSide == side ? Cinnabar : new Color(Edge.r, Edge.g, Edge.b, alpha), _routeSide == side ? Z(2) : 1);
				Fill(new Rect(side.Pill.x, side.Pill.y, Z(4), side.Pill.height), new Color(Ink.r, Ink.g, Ink.b, alpha));
				if (side.Read != null)
				{
					ReadBar(new Rect(side.Pill.x + Z(6), side.Pill.yMax - Z(4), side.Pill.width - Z(10), Z(2)), side.Read);
				}
				var tagText = new GUIStyle(name) { alignment = TextAnchor.MiddleCenter, wordWrap = false };
				// 走完会接到某一站主线的，后面标「→ 主线」
				string text = (side.Visited ? "<color=#2F6B3A>✓</color> " : "") + side.Title + (side.Joins.Count > 0 ? " <color=#A3302A>" + StoryUi.Text(KoreanStoryMessage.MainArrow, "→ 主线") + "</color>" : "");
				GUI.Label(new Rect(side.Pill.x + Z(4), side.Pill.y - Z(1), side.Pill.width - Z(4), side.Pill.height), dim ? "<color=#B5AA98>" + side.Title + "</color>" : text, tagText);
			}
			if (GUI.Button(side.Pill, GUIContent.none, GUIStyle.none))
			{
				_routeSide = side;
				_routePicked = null;
				_routeDetailScroll = Vector2.zero;
			}
		}
		// 按住空白处拖动地图
		switch (e.GetTypeForControl(pan))
		{
			case EventType.MouseDown when e.button == 0 && new Rect(view.x, view.y, view.width - S(22), view.height - S(22)).Contains(e.mousePosition):
				GUIUtility.hotControl = pan;
				e.Use();
				break;
			case EventType.MouseDrag when GUIUtility.hotControl == pan:
				_routeScroll -= e.delta;
				e.Use();
				break;
			case EventType.MouseUp when GUIUtility.hotControl == pan:
				GUIUtility.hotControl = 0;
				e.Use();
				break;
		}
		GUI.EndScrollView();
	}

	private void DrawMissionDetail(StoryMission m)
	{
		DrawRomanceOnMission(m);
		GUILayout.Label(StoryUi.Series(m) + "　" + m.Key, _muted);
		GUILayout.Label(m.Title, _heading);
		if (m.Summary.Length > 0)
		{
			GUILayout.Label(m.Summary, _muted);
		}
		GUILayout.Space(S(6));
		GUILayout.Label(StoryUi.When(m) + (m.Place.Length > 0 ? "　·　" + m.Place : ""), _label);
		GUILayout.Label((m.Visited ? "<color=#2F6B3A>✓ " + StoryUi.Visited(true) + "</color>" : StoryUi.Visited(false))
			+ (m.Read != null ? "　·　" + StoryUi.Text(KoreanStoryMessage.DialogueReadPercent, "对白读过 {0}%", m.Read) : "")
			+ (m.Endings > 0 ? "　·　<color=#A3302A>" + StoryUi.Text(KoreanStoryMessage.EndingCount, "{0} 个结局", m.Endings) + "</color>" : ""), _label);
		if (StoryAtlas.CurrentMain == m.Key)
		{
			GUILayout.Label("<color=#A3302A><b>" + StoryUi.Text(KoreanStoryMessage.CurrentMain, "当前主线") + "</b></color>", _label);
		}
		if (GUILayout.Button(StoryUi.Text(KoreanStoryMessage.OpenFlow, "看剧情图"), _primary, GUILayout.Height(S(38))))
		{
			OpenFlow(m);
		}
		JumpButton(m, m.StartScript);
		string conditions = StoryAtlas.ConditionText(m);
		if (conditions.Length > 0)
		{
			GUILayout.Space(S(10));
			GUILayout.Label(StoryUi.Text(KoreanStoryMessage.TriggerConditions, "触发条件"), _heading);
			GUILayout.Label(OnPaper(conditions), _rich);
		}
		DrawSections(m);
		DrawEntries(m);
		var before = StoryAtlas.Missions.Where(x => x.Main && x.Next.Contains(m.Key)).ToList();
		var after = m.Next.Select(StoryAtlas.Find).Where(x => x != null).ToList();
		if (before.Count + after.Count > 0)
		{
			GUILayout.Space(S(10));
			GUILayout.Label(StoryUi.Text(KoreanStoryMessage.BeforeAfter, "前后"), _heading);
			foreach (StoryMission x in before)
			{
				if (GUILayout.Button("← " + x.Title, _button))
				{
					_routePicked = x;
				}
			}
			foreach (StoryMission x in after)
			{
				if (GUILayout.Button("→ " + x.Title, _button))
				{
					_routePicked = x;
				}
			}
		}
		var kept = m.Scripts.Select(StoryNodes.Latest).Where(n => n != null).ToList();
		if (kept.Count > 0)
		{
			GUILayout.Space(S(10));
			GUILayout.Label(StoryUi.Text(KoreanStoryMessage.ReplayHere, "可以从这里重玩"), _heading);
			foreach (StoryNode node in kept.Take(8))
			{
				ReplayButton(node, StoryScripts.SegmentName(node.Script) + "　" + StoryUi.Text(KoreanStoryMessage.YearMonth, "第{0}年{1}月", node.Year, node.Month), _button);
			}
		}
	}

	/// <summary>地图上方一行：九位可以结缘的人，点一位看她的结缘路线，再点一次取消。</summary>
	private void DrawRomanceBar(Rect r)
	{
		GUIStyle line = Middle(_label);
		float x = r.x;
		GUI.Label(new Rect(x, r.y, S(90), r.height), StoryUi.Text(KoreanStoryMessage.RomanceRoute, "结缘路线"), line);
		x += S(96);
		foreach (StoryRomance.Heroine h in StoryRomance.All)
		{
			string name = StoryRomance.Name(h);
			bool on = _routeHeroine == h;
			float w = _button.CalcSize(new GUIContent(name)).x + S(20);
			if (GUI.Button(new Rect(x, r.y, w, r.height), name, on ? _primary : _button))
			{
				_routeHeroine = on ? null : h;
				_routePicked = null;
				_routeSide = null;
				_routeDetailScroll = Vector2.zero;
			}
			x += w + S(6);
		}
		_romanceMissions = _romanceSteps = null;
		if (_routeHeroine != null && StoryRomance.StopsOf(_routeHeroine) is List<StoryRomance.Stop> stops)
		{
			_romanceMissions = new HashSet<StoryMission>(stops.Select(s => s.Mission));
			_romanceSteps = new HashSet<StoryMission>(stops.Where(s => s.Steps.Count > 0).Select(s => s.Mission));
		}
	}

	/// <summary>右边：她的好感、是不是心上人，结缘的每一步这一局做到没有、在哪拿到，以及加减她好感的剧情。</summary>
	private void DrawRomanceDetail(StoryRomance.Heroine h)
	{
		List<StoryRomance.Stop> stops = StoryRomance.StopsOf(h);
		if (stops == null)
		{
			GUILayout.Label(StoryUi.Text(KoreanStoryMessage.Preparing, "剧本还没整理好"), _muted);
			return;
		}
		GUILayout.Label(StoryUi.Text(KoreanStoryMessage.RomanceRoute, "结缘路线"), _muted);
		GUILayout.Label(StoryRomance.Name(h), _heading);
		GUILayout.Label(StoryUi.Text(KoreanStoryMessage.Affection, "好感 {0}", StoryRomance.Affection(h)) + (StoryRomance.IsLover(h) ? "　·　<color=#A3302A><b>" + StoryUi.Text(KoreanStoryMessage.Lover, "心上人") + "</b></color>" : ""), WrapLabel());
		GUILayout.Space(S(10));
		GUILayout.Label(StoryUi.Text(KoreanStoryMessage.RomanceProgress, "结缘进度（这一局）"), _heading);
		foreach (string flag in h.Steps)
		{
			GUILayout.BeginVertical(_card, GUILayout.Width(_routeDetailInner));
			GUILayout.Label((StoryRomance.StepDone(flag) ? "<color=#2F6B3A><b>✓</b></color> " : "<color=#A3302A>✗</color> ") + (NativeKorean.Enabled ? KoreanLabels.Flag(flag) : StoryRomance.StepName(flag)), WrapLabel());
			var where = stops.Where(s => s.Steps.Contains(flag)).ToList();
			if (where.Count == 0)
			{
				GUILayout.Label(StoryUi.Text(KoreanStoryMessage.StepNotFound, "剧本里没找到设这一步的地方"), WrapSmall());
			}
			bool done = StoryRomance.StepDone(flag);
			foreach (StoryRomance.Stop s in where.Take(4))
			{
				RomanceStopButton(s, _routeDetailInner - S(24));
				string conditions = done ? "" : StoryAtlas.ConditionText(s.Mission);
				if (conditions.Length > 0)
				{
					GUILayout.Label(StoryUi.Text(KoreanStoryMessage.CurrentConditions, "触发条件（这一局的值）：{0}", OnPaper(conditions)), WrapSmall(), GUILayout.Width(_routeDetailInner - S(24)));
				}
			}
			GUILayout.EndVertical();
		}
		GUILayout.Space(S(10));
		GUILayout.Label(StoryUi.Text(KoreanStoryMessage.AffectionScenes, "加减好感的剧情"), _heading);
		foreach (StoryRomance.Stop s in stops.Where(s => s.Affection != 0))
		{
			RomanceStopButton(s, _routeDetailInner);
		}
		int elsewhere = StoryRomance.ElsewhereOf(h);
		if (elsewhere > 0)
		{
			GUILayout.Label(StoryUi.Text(KoreanStoryMessage.ElsewhereCount, "另有 {0} 处在养成随机事件等不属于任务的剧情里", elsewhere), WrapSmall());
		}
	}

	/// <summary>站名后面的结缘步骤：「【认识·告白】」；没选人或这站不设她的旗标时为空。</summary>
	private string RomanceMark(StoryMission m)
	{
		if (_routeHeroine == null || _romanceSteps == null || !_romanceSteps.Contains(m))
		{
			return "";
		}
		StoryRomance.Stop stop = StoryRomance.StopsOf(_routeHeroine)?.FirstOrDefault(s => s.Mission == m);
		return stop == null ? "" : " <color=#A3302A>" + StepMarks(stop) + "</color>";
	}

	// 步骤名拼在一串里，界面翻译整句对不上：先各自换成界面语言
	private static string StepMarks(StoryRomance.Stop s) => "【" + string.Join("·", s.Steps.Select(f => NativeKorean.Enabled ? KoreanStoryText.RomanceStep(f) : TextConverter.ForDisplay(StoryRomance.StepKind(f))).ToArray()) + "】";

	// 「　好感 +3　【认识】」
	private static string RomanceParts(StoryRomance.Stop s)
	{
		string text = s.Affection != 0 ? "　" + StoryUi.Text(KoreanStoryMessage.Affection, "好感 {0}", (s.Affection > 0 ? "+" : "") + s.Affection) : "";
		return text + (s.Steps.Count > 0 ? "　<color=#A3302A>" + StepMarks(s) + "</color>" : "");
	}

	/// <summary>选了人时，站点详情最上面：返回她的结缘路线，以及这一站和她有什么关系。</summary>
	private void DrawRomanceOnMission(StoryMission m)
	{
		if (_routeHeroine == null)
		{
			return;
		}
		string name = StoryRomance.Name(_routeHeroine);
		if (GUILayout.Button(StoryUi.Text(KoreanStoryMessage.ReturnRomance, "‹ 返回{0}的结缘路线", name), _button))
		{
			_routePicked = null;
			_routeSide = null;
			_routeDetailScroll = Vector2.zero;
			return;
		}
		StoryRomance.Stop stop = StoryRomance.StopsOf(_routeHeroine)?.FirstOrDefault(s => s.Mission == m);
		GUILayout.Label(stop == null ? StoryUi.Text(KoreanStoryMessage.NoRomanceEffect, "这一站不改{0}的好感，也不设她的结缘旗标", name) : StoryUi.Text(KoreanStoryMessage.RelatedPerson, "和{0}有关：", name) + RomanceParts(stop).TrimStart('　'), WrapSmall(), GUILayout.Width(_routeDetailInner));
		GUILayout.Space(S(6));
	}

	private GUIStyle _wrapLabel, _wrapSmall, _wrapButton;
	private float _wrapScale;

	// 详情栏里的字和按钮：宽度固定、长了换行，不把详情栏撑宽
	private void EnsureWrapStyles()
	{
		if (_wrapLabel == null || _wrapScale != _scale)
		{
			_wrapScale = _scale;
			_wrapLabel = new GUIStyle(_label) { wordWrap = true, richText = true };
			_wrapSmall = new GUIStyle(_small) { wordWrap = true, richText = true };
			_wrapButton = new GUIStyle(_button) { wordWrap = true, richText = true, alignment = TextAnchor.MiddleLeft };
		}
	}

	private GUIStyle WrapLabel() { EnsureWrapStyles(); return _wrapLabel; }
	private GUIStyle WrapSmall() { EnsureWrapStyles(); return _wrapSmall; }

	// 一个任务：名字一行，时间和好感加减一行；点了在地图上选中它
	private void RomanceStopButton(StoryRomance.Stop s, float width)
	{
		EnsureWrapStyles();
		StoryMission m = s.Mission;
		string detail = StoryUi.When(m) + RomanceParts(s);
		string text = (m.Visited ? "✓ " : "") + m.Title + "\n<size=" + _small.fontSize + ">" + detail + "</size>";
		if (GUILayout.Button(text, _wrapButton, GUILayout.Width(width)))
		{
			_routePicked = m;
			_routeSide = null;
			_routeDetailScroll = Vector2.zero;
			if (_route != null && _route.Points.TryGetValue(m.Key, out Vector2 p))
			{
				_routeScroll = new Vector2(Mathf.Max(0, p.x - S(400)), Mathf.Max(0, p.y - S(250)));
			}
		}
	}

	private void DrawSideDetail(SideStory side)
	{
		if (_routeHeroine != null && GUILayout.Button(StoryUi.Text(KoreanStoryMessage.ReturnRomance, "‹ 返回{0}的结缘路线", StoryRomance.Name(_routeHeroine)), _button))
		{
			_routeSide = null;
			_routePicked = null;
			_routeDetailScroll = Vector2.zero;
			return;
		}
		GUILayout.Label(StoryUi.Text(KoreanStoryMessage.Side, "支线"), _muted);
		GUILayout.Label(side.Title, _heading);
		foreach (StoryMission m in side.Joins)
		{
			if (GUILayout.Button(StoryUi.Text(KoreanStoryMessage.ContinueMain, "走完接到主线「{0}」", m.Title), _button))
			{
				_routePicked = m;
				_routeSide = null;
			}
		}
		GUILayout.Label((side.Visited ? "<color=#2F6B3A>✓ " + StoryUi.Visited(true) + "</color>" : StoryUi.Visited(false)) + (side.Read != null ? "　·　" + StoryUi.Text(KoreanStoryMessage.DialogueReadPercent, "对白读过 {0}%", side.Read) : ""), _label);
		foreach (StoryMission m in side.Missions.OrderBy(m => m.From ?? int.MaxValue))
		{
			GUILayout.Space(S(8));
			GUILayout.BeginVertical(_card);
			GUILayout.Label(m.Title + (m.Visited ? "　<color=#2F6B3A>✓ " + StoryUi.Text(KoreanStoryMessage.VisitedShort, "走过") + "</color>" : ""), _label);
			GUILayout.Label(StoryUi.When(m) + (m.Place.Length > 0 ? "　·　" + m.Place : "") + (m.Read != null ? "　·　" + StoryUi.Text(KoreanStoryMessage.ReadPercent, "读过 {0}%", m.Read) : ""), _muted);
			string conditions = StoryAtlas.ConditionText(m);
			if (conditions.Length > 0)
			{
				GUILayout.Label(OnPaper(conditions), _small);
			}
			if (m.Entries.Count > 0)
			{
				GUILayout.Label(Cached((m, "entries"), () => string.Join("\n", m.Entries.Select(EntryText).ToArray())), _small);
			}
			if (GUILayout.Button(StoryUi.Text(KoreanStoryMessage.OpenFlow, "看剧情图"), _button))
			{
				OpenFlow(m);
			}
			GUILayout.EndVertical();
		}
	}

	/// <summary>从别的任务接进来的一处：「从「某任务」：选… → 如果…」。</summary>
	private static string EntryText(StoryEntry e)
	{
		if (NativeKorean.Enabled)
			return OnPaper(KoreanStoryText.Get(KoreanStoryMessage.IncomingFrom, StoryUi.Mission(e.From.Main, e.From.Title))
				+ (e.Path.Count > 0 ? ": " + string.Join(" → ", e.Path.Select(StoryScripts.Describe)) : ""));
		string from = (e.From.Main ? "主线" : "支线") + "「" + e.From.Title + "」";
		return OnPaper("从" + from + "接进来" + (e.Path.Count > 0 ? "：" + string.Join(" → ", e.Path.Select(StoryScripts.Describe).ToArray()) : ""));
	}

	private void DrawEntries(StoryMission m)
	{
		if (m.Entries.Count == 0)
		{
			return;
		}
		GUILayout.Space(S(10));
		GUILayout.Label(StoryUi.Text(KoreanStoryMessage.Incoming, "从别处接进来"), _heading);
		GUILayout.Label(Cached((m, "entries"), () => string.Join("\n", m.Entries.Select(EntryText).ToArray())), _rich);
	}

	/// <summary>站内小节：这个任务的剧情段，每条路都会经过的是主干，其余是岔路（写出怎么进去）。</summary>
	private void DrawSections(StoryMission m)
	{
		FlowGraph graph = FlowOf(m);
		var boxes = graph.Boxes.Where(b => !b.Terminal).OrderBy(b => b.Layer).ThenBy(b => b.Row).ToList();
		if (boxes.Count == 0)
		{
			return;
		}
		HashSet<FlowBox> trunk = Trunk(graph);
		GUILayout.Space(S(10));
		GUILayout.Label(StoryUi.Text(KoreanStoryMessage.Sections, "小节"), _heading);
		foreach (FlowBox box in boxes.Take(40))
		{
			bool main = trunk.Contains(box);
			// 岔路写怎么走进来：连进来的那几行去处的短说明（完整条件在剧情图里悬停看）
			string entry = main ? "" : Cached((box, "entry"), () => string.Join("；", graph.Boxes.SelectMany(b => b.Ways).Where(w => w.To == box && w.Short != "接着" && (!NativeKorean.Enabled || w.Short != "이어서")).Select(w => w.Short).Distinct().Take(4).ToArray()));
			string head = (main ? "<b>" + StoryUi.Text(KoreanStoryMessage.Trunk, "主干") + "</b>　" : "<color=#73644F>" + StoryUi.Text(KoreanStoryMessage.Branch, "岔路") + "</color>　") + Short(box.Title, 18) + (box.Read != null ? "　<color=#73644F>" + StoryUi.Text(KoreanStoryMessage.ReadPercent, "读过 {0}%", box.Read) + "</color>" : "");
			if (GUILayout.Button(head + (entry.Length > 0 ? "\n<size=" + Mathf.RoundToInt(14 * _scale) + "><color=#73644F>" + entry + "</color></size>" : ""),
				new GUIStyle(_button) { alignment = TextAnchor.MiddleLeft, wordWrap = true, richText = true }))
			{
				OpenFlow(m, box.Last != null ? box.Last.Name : null);
			}
		}
		if (boxes.Count > 40)
		{
			GUILayout.Label(StoryUi.Text(KoreanStoryMessage.SectionCount, "……共 {0} 段", boxes.Count), _muted);
		}
	}

	// ---------------- 旗标搜索 ----------------

	private const int FlagHitsShown = 5;
	private List<(string Name, string Note)> _allFlags;
	private string _flagSearchedFor;
	private List<(string Name, string Note, List<StoryUse> Uses)> _flagHits = new List<(string, string, List<StoryUse>)>();

	/// <summary>搜索词对得上旗标编号或说明时，列出这面旗标在哪些剧情里改动、判定（和数值页点旗标名字看到的一样），每处能跳到剧情图。</summary>
	private void DrawFlagSearch(string query)
	{
		if (_flagSearchedFor != query)
		{
			_flagSearchedFor = query;
			_allFlags ??= MissionManagerData.Instance?.FlagsCollection?.Where(c => c != null && c.List != null).SelectMany(c => c.List).Where(f => f != null)
				.Select(f => f.name).Distinct().OrderBy(n => n, StringComparer.Ordinal)
				.Select(n => (n, TextConverter.ToSimplified(MissionManagerData.Instance.GetFlag(n)?.DevNote ?? "").Replace("\n", " ").Trim())).ToList();
			_flagHits = new List<(string, string, List<StoryUse>)>();
			foreach (var (name, note) in _allFlags ?? new List<(string, string)>())
			{
				if (note.Contains("暂存") || !Matches(query, name, note))
				{
					continue;
				}
				List<StoryUse> uses = StoryUsage.Flag(name);
				if (uses == null)
				{
					// 反查还没整理好：下一帧再搜
					_flagSearchedFor = null;
					return;
				}
				if (uses.Count > 0)
				{
					_flagHits.Add((name, note, uses));
				}
			}
		}
		if (_flagHits.Count == 0)
		{
			return;
		}
		GUILayout.Label(StoryUi.Text(KoreanStoryMessage.FlagHits, "旗标里找到 {0} 面", _flagHits.Count) + (_flagHits.Count > FlagHitsShown ? StoryUi.Text(KoreanStoryMessage.FirstFlagsOnly, "（只列前 {0} 面，搜得更具体些能缩小范围）", FlagHitsShown) : ""), _heading);
		foreach (var (name, note, uses) in _flagHits.Take(FlagHitsShown))
		{
			GUILayout.Space(S(6));
			GUILayout.Label($"<b>{name}</b>" + (NativeKorean.Enabled ? "　" + KoreanLabels.Flag(name) : note.Length > 0 ? "　" + Short(note, 40) : ""), _label);
			foreach (StoryUse use in uses.OrderBy(u => !u.Changes).ThenBy(u => u.Mission == null).ThenBy(u => u.Mission?.From ?? int.MaxValue).Take(20))
			{
				DrawUse(use);
			}
			if (uses.Count > 20)
			{
				GUILayout.Label(StoryUi.Text(KoreanStoryMessage.MoreFlagUses, "还有 {0} 处，到数值页点这面旗标的名字看全部", uses.Count - 20), _muted);
			}
		}
		GUILayout.Space(S(12));
	}

	// ---------------- 台词搜索 ----------------

	private string _searchedFor;
	private List<(string Script, string Snippet)> _searchHits = new List<(string, string)>();

	private void DrawSearchResults(string query)
	{
		DrawFlagSearch(query);
		if (!StoryAtlas.DialogueReady)
		{
			GUILayout.Label(StoryUi.Text(KoreanStoryMessage.PreparingDialogue, "正在整理台词……"), _muted);
			return;
		}
		if (_searchedFor != query)
		{
			_searchedFor = query;
			_searchHits = StoryAtlas.SearchDialogue(query, 60);
		}
		GUILayout.Label(_searchHits.Count == 0 ? StoryUi.Text(KoreanStoryMessage.NoDialogueHits, "台词里没找到") : StoryUi.Text(KoreanStoryMessage.DialogueHits, "台词里找到 {0} 处", _searchHits.Count) + (_searchHits.Count >= 60 ? StoryUi.Text(KoreanStoryMessage.FirstDialogueOnly, "（只列前 {0} 处）", 60) : ""), _heading);
		foreach (var (script, snippet) in _searchHits)
		{
			StoryAtlas.Owner.TryGetValue(script, out StoryMission m);
			string where = m != null ? StoryUi.Series(m) + "「" + Short(m.Title, 14) + "」" : script;
			if (GUILayout.Button($"<size={Mathf.RoundToInt(14 * _scale)}><color=#73644F>{where}</color></size>\n{snippet}", new GUIStyle(_button) { alignment = TextAnchor.MiddleLeft, wordWrap = true, richText = true }) && m != null)
			{
				OpenFlow(m, script);
			}
		}
	}
}
