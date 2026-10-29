using System;
using System.Collections.Generic;
using System.Linq;
using LOMAssistant.Config;
using LOMAssistant.Features.AnytimeSave;
using Mortal.Core;
using Mortal.Story;
using OBB.Framework.Utils;
using UnityEngine;

namespace LOMAssistant.Features.Story;

/// <summary>
/// 剧情跳转：从剧情图、路线图直接跳到某一段剧情的开头（包括这一局还没走到的），用来补剧情、收集分支和结局。
/// 中间的剧情没有真的演过：按剧本把一路上会设的旗标、剧情记录、任务进度、物品、好感、数值补上，日期改到那一段的时间，
/// 再从那一段开演。跳之前先存一份随时存档当「跳转前」，跳过去以后可以一键回去，也可以就这样玩下去。
/// 补的规则：
/// - 目标所在的任务：从任务开头一路到目标这一段，走哪条路由玩家选（只有一条时不问），这条路上的变化都补。
/// - 中间的主线任务（主线从现在这一步按剧本里写明的「下一个主线」一路接到目标）：只补不经选择、不看条件就会有的变化。
/// - 最后把主线（或目标所在的支线）的进度设到目标任务。
/// </summary>
public static class StoryJump
{
	/// <summary>目标任务里从开头到目标这一段的一条路：经过的剧本和每个剧本走的去处。</summary>
	public sealed class Route
	{
		public readonly List<(StoryScript Script, StoryExit Exit)> Steps = new List<(StoryScript, StoryExit)>();
		public string Label = "";
	}

	public sealed class Plan
	{
		public StoryMission Mission;
		public string Script = "";
		public string Title = "";
		public readonly List<Route> Routes = new List<Route>();
		public int Picked;
		/// <summary>中间的主线任务（按先后）。</summary>
		public readonly List<StoryMission> Between = new List<StoryMission>();
		/// <summary>主线从现在接不到目标：中间的主线任务没法补。</summary>
		public bool Unlinked;
		public int? FromRounds, ToRounds;
		public string MissionFrom = "", MissionTo = "";

		// 按选的路算出来的
		public readonly List<(string Method, string[] Args)> Commands = new List<(string, string[])>();
		public readonly List<string> Effects = new List<string>();
		public int Records, Skipped;
		public readonly List<string> Notes = new List<string>();
	}

	public static string ReturnSave { get; private set; }
	public static string ReturnTitle { get; private set; } = "";
	/// <summary>跳过去以后那张小提示还开着。</summary>
	public static bool NoteOpen;

	/// <summary>现在能不能跳：读档进入游戏、在养成地图或剧情里、没在战斗和切场景。</summary>
	public static bool CanJump(out string reason)
	{
		reason = null;
		string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
		if (PlayerStatManagerData.Instance == null || MissionManagerData.Instance == null || !StoryNodes.HasCurrentSave)
		{
			reason = "读档进入游戏后才能跳转";
		}
		else if (scene != "Free" && scene != "Story")
		{
			reason = "在养成地图或剧情里才能跳转";
		}
		else if (SceneController.Instance == null || SceneController.Instance.IsPrepare || SceneController.Instance.IsLoading)
		{
			reason = "正在切换场景，稍后再试";
		}
		else if (LOMAssistant.Features.AnytimeSave.AnytimeSave.Instance == null)
		{
			reason = "随时存档没有启用，存不了跳转前的进度";
		}
		return reason == null;
	}

	/// <summary>跳到任务 mission 里从 script 开始的那一段。</summary>
	public static Plan Build(StoryMission mission, string script)
	{
		var plan = new Plan { Mission = mission, Script = script, Title = StoryScripts.SegmentName(script) };
		PlayerStatManagerData player = PlayerStatManagerData.Instance;
		GameTime now = player?.GameTime;
		if (!ReferenceEquals(now, null))
		{
			plan.FromRounds = now.ConvertToRounds();
		}
		plan.ToRounds = mission.From;

		// 目标任务里，从开头到目标这一段的各条路
		foreach (var steps in Paths(mission, script).Take(12))
		{
			var route = new Route();
			route.Steps.AddRange(steps);
			route.Label = steps.Count == 0 ? "直接从任务开头开始" : string.Join(" → ", steps.Where(s => s.Exit.Path.Count > 0).Select(s => StoryScripts.Describe(s.Exit.Path.Last())).ToArray());
			if (route.Label.Length == 0)
			{
				route.Label = "一路没有分岔";
			}
			plan.Routes.Add(route);
		}

		// 主线进度：现在这一步 → 目标
		MissionManagerData missions = MissionManagerData.Instance;
		StoryMission current = missions?.Main != null ? StoryAtlas.Find(missions.Main.CurrentKey) : null;
		plan.MissionFrom = current?.Title ?? "";
		plan.MissionTo = mission.Title;
		if (mission.Main && current != null && current != mission)
		{
			List<StoryMission> chain = MainChain(current, mission);
			if (chain == null)
			{
				plan.Unlinked = true;
			}
			else
			{
				plan.Between.AddRange(chain);
			}
		}
		Recompute(plan);
		return plan;
	}

	/// <summary>按选的路重算要补的变化。</summary>
	public static void Recompute(Plan plan)
	{
		plan.Commands.Clear();
		plan.Effects.Clear();
		plan.Notes.Clear();
		plan.Records = 0;
		plan.Skipped = 0;
		// 中间的主线任务：沿着第一条去处走完，只补不在分岔里的
		foreach (StoryMission m in plan.Between)
		{
			foreach (StoryScript s in DefaultScripts(m))
			{
				Collect(plan, s, null);
			}
		}
		if (plan.Routes.Count > 0)
		{
			Route route = plan.Routes[Mathf.Clamp(plan.Picked, 0, plan.Routes.Count - 1)];
			foreach (var (script, exit) in route.Steps)
			{
				Collect(plan, script, exit);
				if (exit.Path.Any(s => s.Kind == StoryStep.Dice))
				{
					plan.Notes.Add("「" + StoryScripts.SegmentName(script.Name) + "」这一步要靠掷骰，按选的这条路补");
				}
				if (script.Fight)
				{
					plan.Notes.Add("「" + StoryScripts.SegmentName(script.Name) + "」里有战斗，没有真的打，按选的这条路补");
				}
			}
		}
		if (plan.Unlinked)
		{
			plan.Notes.Add("主线从现在这一步接不到目标任务（不在同一条路线上，或要靠别的支线接进来），中间的主线剧情没有补");
		}
		if (plan.Skipped > 0)
		{
			plan.Notes.Add("中间的主线剧情里有 " + plan.Skipped + " 处变化要看选项或条件，没有补");
		}
		if (plan.FromRounds != null && plan.ToRounds != null && plan.ToRounds < plan.FromRounds)
		{
			plan.Notes.Add("目标在现在之前，日期会往回调");
		}
	}

	// 一个剧本里要补的变化：exit 为 null 时只补不在分岔里的；否则补这条去处一路上的
	private static void Collect(Plan plan, StoryScript script, StoryExit exit)
	{
		foreach (var (path, method, args) in script.Commands)
		{
			bool taken = exit == null ? path.Count == 0 : StoryScripts.IsPrefix(path, exit.Path);
			if (!taken)
			{
				if (exit == null)
				{
					plan.Skipped++;
				}
				continue;
			}
			plan.Commands.Add((method, args));
			if (method == "AddStory")
			{
				plan.Records++;
			}
		}
		foreach (var (path, text) in script.Effects)
		{
			if ((exit == null ? path.Count == 0 : StoryScripts.IsPrefix(path, exit.Path)) && !text.Contains("暂存"))
			{
				plan.Effects.Add(text);
			}
		}
	}

	/// <summary>从任务开头到 script 的各条路（每条是经过的剧本和走的去处，不含目标那一段本身），短的在前。</summary>
	private static IEnumerable<List<(StoryScript Script, StoryExit Exit)>> Paths(StoryMission mission, string target)
	{
		if (string.Equals(mission.StartScript, target, StringComparison.OrdinalIgnoreCase))
		{
			yield return new List<(StoryScript, StoryExit)>();
			yield break;
		}
		var inMission = new HashSet<string>(mission.Scripts, StringComparer.OrdinalIgnoreCase);
		var found = new List<List<(StoryScript, StoryExit)>>();
		var path = new List<(StoryScript, StoryExit)>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		void Walk(string name, int depth)
		{
			if (found.Count >= 40 || depth > 40)
			{
				return;
			}
			StoryScript s = StoryScripts.Get(name);
			if (s == null || !seen.Add(name))
			{
				return;
			}
			// 同一个下一段只算一条：不同的选项接到同一个剧本时，取第一条（分岔上的变化不同，但去处一样）
			foreach (StoryExit exit in s.Exits.Where(e => e.Target.Length > 0).GroupBy(e => e.Target, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
			{
				path.Add((s, exit));
				if (string.Equals(exit.Target, target, StringComparison.OrdinalIgnoreCase))
				{
					found.Add(new List<(StoryScript, StoryExit)>(path));
				}
				else if (inMission.Contains(exit.Target))
				{
					Walk(exit.Target, depth + 1);
				}
				path.RemoveAt(path.Count - 1);
			}
			seen.Remove(name);
		}
		Walk(mission.StartScript, 0);
		foreach (var p in found.OrderBy(p => p.Count))
		{
			yield return p;
		}
	}

	/// <summary>中间主线任务走的剧本：从开头起每次走第一条接到本任务剧本的去处。</summary>
	private static IEnumerable<StoryScript> DefaultScripts(StoryMission m)
	{
		var inMission = new HashSet<string>(m.Scripts, StringComparer.OrdinalIgnoreCase);
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string name = m.StartScript;
		while (name != null && seen.Add(name))
		{
			StoryScript s = StoryScripts.Get(name);
			if (s == null)
			{
				yield break;
			}
			yield return s;
			name = s.Exits.Select(e => e.Target).FirstOrDefault(t => t.Length > 0 && inMission.Contains(t));
		}
	}

	/// <summary>主线从 from 按剧本里写明的「下一个主线」接到 to，中间经过的任务（含 from，不含 to）；接不到返回 null。</summary>
	private static List<StoryMission> MainChain(StoryMission from, StoryMission to)
	{
		var prev = new Dictionary<StoryMission, StoryMission> { [from] = null };
		var queue = new Queue<StoryMission>();
		queue.Enqueue(from);
		while (queue.Count > 0)
		{
			StoryMission m = queue.Dequeue();
			if (m == to)
			{
				var chain = new List<StoryMission>();
				for (StoryMission x = prev[m]; x != null; x = prev[x])
				{
					chain.Add(x);
				}
				chain.Reverse();
				return chain;
			}
			foreach (string key in m.Next)
			{
				StoryMission n = StoryAtlas.Find(key);
				if (n != null && !prev.ContainsKey(n))
				{
					prev[n] = m;
					queue.Enqueue(n);
				}
			}
		}
		return null;
	}

	/// <summary>跳：先存「跳转前」，再补变化、改日期和任务进度，从目标那一段开演。</summary>
	public static bool Execute(Plan plan, out string message)
	{
		if (!CanJump(out message))
		{
			return false;
		}
		string back = LOMAssistant.Features.AnytimeSave.AnytimeSave.Instance.SaveSlot("跳转前（去「" + plan.Title + "」）");
		if (back == null)
		{
			message = "存不了跳转前的随时存档，没有跳";
			return false;
		}
		PlayerStatManagerData player = PlayerStatManagerData.Instance;
		MissionManagerData missions = MissionManagerData.Instance;
		int failed = 0;
		foreach (var (method, args) in plan.Commands)
		{
			try
			{
				Apply(method, args, player, missions);
			}
			catch (Exception ex)
			{
				failed++;
				FileLogger.LogWarning("[剧情跳转] " + method + "(" + string.Join(",", args) + ") 没补上: " + ex.Message);
			}
		}
		if (plan.ToRounds != null)
		{
			int r = Math.Max(1, plan.ToRounds.Value) - 1;
			player.SetGameTime(r / 36 + 1, r % 36 / 3 + 1, (MonthStageType)(r % 3 + 1));
		}
		missions.SetKey(plan.Mission.Main ? "Main" : plan.Mission.Series, plan.Mission.Key);
		ReturnSave = back;
		ReturnTitle = plan.Title;
		NoteOpen = true;
		FileLogger.Log($"[剧情跳转] 跳到 {plan.Script}（{plan.Mission.Key}），补了 {plan.Commands.Count - failed} 项变化" + (failed > 0 ? $"，{failed} 项没补上" : "") + "，跳转前存在随时存档 " + back);
		player.SetStoryScript(plan.Script);
		SceneController.Instance.LoadStory();
		message = null;
		return true;
	}

	private static void Apply(string method, string[] args, PlayerStatManagerData player, MissionManagerData missions)
	{
		string a0 = args.Length > 0 ? args[0] : "";
		int Int(int i) => args.Length > i && int.TryParse(args[i], out int v) ? v : 0;
		switch (method)
		{
			case "SetFlag": missions.SetFlag(a0, Int(1)); break;
			case "AddFlag": missions.AddFlag(a0, Int(1)); break;
			case "AddStory":
				if (!player.StoryKeyList.Contains(a0))
				{
					player.StoryKeyList.Add(a0);
				}
				break;
			case "Mission": missions.SetKey(a0, args.Length > 1 ? args[1] : ""); break;
			case "IsTriggerSubMission": missions.SetIsTriggerSubMission(a0, Int(1) == 1); break;
			case "AddBook": ItemDatabase.Instance.AddItem(GameItemType.書籍, a0, Int(1)); break;
			case "AddMisc": ItemDatabase.Instance.AddItem(GameItemType.雜物, a0, Int(1)); break;
			case "AddSpecial": ItemDatabase.Instance.AddItem(GameItemType.貴重品, a0, Int(1)); break;
			case "RemoveBook": ItemDatabase.Instance.RemoveItem(GameItemType.書籍, a0); break;
			case "RemoveMisc": ItemDatabase.Instance.RemoveItem(GameItemType.雜物, a0); break;
			case "RemoveSpecial": ItemDatabase.Instance.RemoveItem(GameItemType.貴重品, a0); break;
			case "Character":
				if (EnumUtils.TryParseByStringValue<RelationshipStatType>(a0, out var who)) player.Relationships.Get(who).AddValue(Int(1));
				break;
			case "SetCharacter":
				if (EnumUtils.TryParseByStringValue<RelationshipStatType>(a0, out var whom)) player.Relationships.Set(whom, Int(1));
				break;
			case "Player":
				if (EnumUtils.TryParseByStringValue<GameStatType>(a0, out var stat)) player.Stats.Add(stat, Int(1));
				break;
			case "SetPlayer":
				if (EnumUtils.TryParseByStringValue<GameStatType>(a0, out var set)) player.Stats.Set(set, Int(1));
				break;
		}
	}

	/// <summary>回到跳转前：读那份随时存档。</summary>
	public static void Return()
	{
		if (ReturnSave == null || LOMAssistant.Features.AnytimeSave.AnytimeSave.Instance == null)
		{
			return;
		}
		LOMAssistant.Features.AnytimeSave.AnytimeSave.Instance.LoadFile(ReturnSave);
		NoteOpen = false;
	}

	public static bool Enabled => ConfigManager.EnableStoryJump.Value;
}

/// <summary>
/// 跳过去以后屏幕上方的一张小卡片：和咖啡伴侣窗口一样的宣纸底、墨色字，左边一道朱砂。
/// 「回到跳转前」读回那份随时存档；「就这样玩」关掉卡片，当正常的进度玩下去（随时存档里那份「跳转前」还在）。
/// </summary>
public sealed class StoryJumpNote : MonoBehaviour
{
	private static StoryJumpNote _instance;
	private GUIStyle _card, _small, _title, _back, _keep;
	private float _scale = -1f;
	private int _fontMode = -1;
	private bool _dark;

	public static void Ensure(GameObject host)
	{
		if (_instance == null)
		{
			_instance = host.GetComponent<StoryJumpNote>() ?? host.AddComponent<StoryJumpNote>();
		}
	}

	private void OnGUI()
	{
		if (!StoryJump.NoteOpen || StoryJump.ReturnSave == null || UiHider.Active || Event.current == null)
		{
			return;
		}
		float scale = PanelTheme.UiScale;
		if (scale <= 0f)
		{
			return;
		}
		if (_card == null || ModFont.NeedsRebuild(scale, _scale, _fontMode) || _dark != PanelTheme.Dark)
		{
			_scale = scale;
			_fontMode = ModFont.Mode;
			_dark = PanelTheme.Dark;
			_card = new GUIStyle { border = new RectOffset(2, 2, 2, 2) };
			_card.normal.background = PanelTheme.Framed(new Color(PanelTheme.Paper.r, PanelTheme.Paper.g, PanelTheme.Paper.b, PanelTheme.DialogOpacity), PanelTheme.Edge);
			_small = PanelTheme.Label(scale, 14, PanelTheme.InkMuted, false);
			_title = PanelTheme.Label(scale, 18, PanelTheme.Ink, false);
			_title.clipping = TextClipping.Clip;
			_back = PanelTheme.Primary(scale, 16);
			_keep = PanelTheme.Button(scale, 16);
		}
		float s = scale;
		string where = StoryJump.ReturnTitle;
		float back = Mathf.Max(132f * s, _back.CalcSize(new GUIContent("回到跳转前")).x + 16f * s);
		float keep = Mathf.Max(116f * s, _keep.CalcSize(new GUIContent("就这样玩")).x + 16f * s);
		float text = Mathf.Min(560f * s, _title.CalcSize(new GUIContent("已跳到「" + where + "」")).x);
		float width = Mathf.Min(Screen.width - 24f * s, text + back + keep + 70f * s), height = 64f * s;
		var rect = new Rect((Screen.width - width) / 2f, 14f * s, width, height);
		GUI.Box(rect, GUIContent.none, _card);
		// 左边一道朱砂
		Color old = GUI.color;
		GUI.color = PanelTheme.Cinnabar;
		GUI.DrawTexture(new Rect(rect.x + 2f * s, rect.y + 2f * s, 4f * s, rect.height - 4f * s), Texture2D.whiteTexture);
		GUI.color = old;
		float x = rect.x + 20f * s, textWidth = rect.width - back - keep - 56f * s;
		GUI.Label(new Rect(x, rect.y + 9f * s, textWidth, 20f * s), "剧情跳转", _small);
		GUI.Label(new Rect(x, rect.y + 29f * s, textWidth, 26f * s), "已跳到「" + where + "」", _title);
		float bh = 38f * s, by = rect.y + (height - bh) / 2f;
		if (GUI.Button(new Rect(rect.xMax - keep - back - 24f * s, by, back, bh), "回到跳转前", _back))
		{
			StoryJump.Return();
		}
		if (GUI.Button(new Rect(rect.xMax - keep - 14f * s, by, keep, bh), "就这样玩", _keep))
		{
			StoryJump.NoteOpen = false;
		}
	}
}
