using System;
using System.Collections.Generic;
using System.Linq;
using LOMAssistant.LuaParse;
using Mortal.Core;
using Mortal.Story;
using UnityEngine;

namespace LOMAssistant.Features.Story;

/// <summary>
/// 「此刻」页：只讲当下。这一段剧情的选项（完整结果）和分支判定、当前地点的随机事件（概率、条件、结果，
/// 满足条件的可以选定下次触发）、现在能触发和差一点的任务。打开面板时整理一次，换了剧本再整理。
/// </summary>
public partial class StoryReviewPanel
{
	private sealed class NowEvent
	{
		public PositionEventData Data;
		public string Name = "", Desc = "";
		public int Rate, Percent;
		/// <summary>这个事件的剧情一句都没读过（和随机事件小窗的「没看过」一样）。</summary>
		public bool Unread;
	}

	private sealed class NowMission
	{
		public string Title = "", Desc = "", Place = "";
		public bool Ready, Main;
	}

	private sealed class NowSnapshot
	{
		public string Script = "";
		/// <summary>正在演的剧本（PlayerStatManagerData.CurrentStoryScript），跳剧情图、标未读用。</summary>
		public string Story = "";
		public StoryScript StoryScript;
		public bool Failed;
		public string Language = TextConverter.DisplayLanguage;
		public string MenuSignature = string.Join("\t", OptionInfoPatch.MenuKeys);
		public readonly List<(string Key, string Option, string Result)> Options = new List<(string, string, string)>();
		/// <summary>这一段剧情的去处：走过哪些选项、掷骰、条件，接到哪里（和剧情图里「接下来」一样）。</summary>
		public readonly List<(string Way, string Effects)> Ways = new List<(string, string)>();
		public readonly List<NowEvent> Events = new List<NowEvent>();
		public readonly List<NowMission> Missions = new List<NowMission>();
	}

	private NowSnapshot _now;
	private Vector2 _nowScroll;

	private void DrawNow(Rect r)
	{
		// 标题画面：和数值页一样，只在左上角写一句
		if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Title")
		{
			GUI.Label(new Rect(r.x, r.y, r.width, S(40)), NativeKorean.Text(KoreanMessage.LoadGame, "读档进入游戏后才有内容"), _muted);
			return;
		}
		if (_now == null || _now.Script != (OptionInfoPatch.LastScriptKey ?? "") || _now.Language != TextConverter.DisplayLanguage
			|| _now.MenuSignature != string.Join("\t", OptionInfoPatch.MenuKeys))
		{
			_now = BuildNow();
		}
		if (GUI.Button(new Rect(r.xMax - S(130), r.y, S(130), S(36)), NativeKorean.Text(KoreanMessage.Refresh, "刷新"), _button))
		{
			_now = BuildNow();
		}
		// 跳到这一段所在任务的剧情图（剧本还没读完时先切过去，读完再定位）
		if (_now.Story.Length > 0)
		{
			bool owned = !StoryAtlas.Ready || StoryAtlas.Owner.ContainsKey(_now.Story);
			GUI.enabled = owned;
			if (GUI.Button(new Rect(r.xMax - S(130) - S(10) - S(220), r.y, S(220), S(36)), owned
				? NativeKorean.Text(KoreanMessage.OpenFlow, "在剧情图里看")
				: NativeKorean.Text(KoreanMessage.NotAMission, "这一段不属于任务"), owned ? _primary : _button))
			{
				_flowJumpScript = _now.Story;
				_tab = 2;
			}
			GUI.enabled = true;
		}
		var area = new Rect(r.x, r.y + S(46), r.width, r.height - S(46));
		DragScroll("now", area, ref _nowScroll);
		GUILayout.BeginArea(area);
		_nowScroll = GUILayout.BeginScrollView(_nowScroll);
		bool any = false;
		if (_now.Options.Count > 0)
		{
			any = true;
			// 游戏里正在显示的那一组放最上面；其余按剧本里第几次选择分组（一段剧情里常有好几轮选择）
			var showing = new HashSet<string>(OptionInfoPatch.MenuKeys);
			var current = OptionInfoPatch.MenuKeys.Distinct().Join(_now.Options, key => key, option => option.Key, (key, option) => option).ToList();
			if (current.Count > 0)
			{
				GUILayout.Label(NativeKorean.Text(KoreanMessage.CurrentOptions, "现在可以选的"), _heading);
				foreach (var option in current)
				{
					DrawNowOption(option.Key, option.Option, option.Result);
				}
				GUILayout.Space(S(10));
			}
			var rest = _now.Options.Where(o => !showing.Contains(o.Key)).ToList();
			if (rest.Count > 0)
			{
				GUILayout.Label(current.Count > 0 ? NativeKorean.Text(KoreanMessage.OtherOptions, "这一段的其他选项")
					: NativeKorean.Text(KoreanMessage.SceneOptions, "这一段的选项"), _heading);
				var groups = rest.GroupBy(o => MenuOf(o.Key)).OrderBy(g => g.Key < 0 ? int.MaxValue + g.Key : g.Key).ToList();
				foreach (var group in groups)
				{
					if (groups.Count > 1)
					{
						GUILayout.Label(group.Key == -2 ? NativeKorean.Text(KoreanMessage.Dice, "掷骰")
							: group.Key < 0 ? NativeKorean.Text(KoreanMessage.Other, "其他")
							: NativeKorean.Text(KoreanMessage.ChoiceNumber, "第 {0} 次选择", group.Key + 1), _muted);
					}
					foreach (var option in group)
					{
						DrawNowOption(option.Key, option.Option, option.Result);
					}
				}
				GUILayout.Space(S(10));
			}
		}
		if (_now.Ways.Count > 0)
		{
			any = true;
			GUILayout.Label(NativeKorean.Text(KoreanMessage.NextRoutes, "这一段接下来会怎么走"), _heading);
			foreach (var (way, effects) in _now.Ways)
			{
				GUILayout.BeginVertical(_card);
				GUILayout.Label(way, _rich);
				if (effects.Length > 0)
				{
					GUILayout.Label(Cached((way, effects, PanelTheme.Dark), () => PanelDeltas(effects)), _muted);
				}
				GUILayout.EndVertical();
			}
			GUILayout.Space(S(10));
		}
		if (_now.Events.Count > 0)
		{
			any = true;
			GUILayout.Label(NativeKorean.Text(KoreanMessage.RandomEvents, "这里的随机事件"), _heading);
			foreach (NowEvent ev in _now.Events)
			{
				GUILayout.BeginVertical(_card);
				GUILayout.BeginHorizontal();
				GUILayout.Label(ev.Name + "　" + (ev.Rate > 0 ? $"<color=#2F6B3A>{ev.Percent}%</color>"
					: "<color=#A3302A>" + NativeKorean.Text(KoreanMessage.Unmet, "条件不满足") + "</color>") + (ev.Unread ? "　<color=#A3302A>" + StoryUi.Text(KoreanStoryMessage.UnreadMark, "没看过") + "</color>" : ""), _rich);
				GUILayout.FlexibleSpace();
				if (ev.Rate > 0)
				{
					bool chosen = EventPicker.Pending == ev.Data;
					if (GUILayout.Button(chosen ? NativeKorean.Text(KoreanMessage.Selected, "已选定")
						: NativeKorean.Text(KoreanMessage.SelectNext, "下次触发这个"), chosen ? _primary : _button, GUILayout.Width(S(150))))
					{
						EventPicker.Pending = chosen ? null : ev.Data;
					}
				}
				GUILayout.EndHorizontal();
				if (ev.Desc.Length > 0)
				{
					GUILayout.Label(ev.Desc, _rich);
				}
				GUILayout.EndVertical();
			}
			GUILayout.Space(S(10));
		}
		if (_now.Missions.Count > 0)
		{
			any = true;
			GUILayout.Label(NativeKorean.Text(KoreanMessage.Missions, "现在的任务"), _heading);
			foreach (NowMission m in _now.Missions)
			{
				GUILayout.BeginVertical(_card);
				string readiness = NativeKorean.Enabled
					? m.Ready ? (m.Place.Length > 0 ? KoreanText.Get(KoreanMessage.ReadyAt, m.Place) : KoreanText.Get(KoreanMessage.Ready)) : KoreanText.Get(KoreanMessage.Missing)
					: m.Ready ? "条件已满足" + (m.Place.Length > 0 ? "，去「" + m.Place + "」就会触发" : "") : "还差条件";
				GUILayout.Label((m.Main ? "<color=#A3302A><b>" + NativeKorean.Text(KoreanMessage.Main, "主线") + "</b></color>　" : "")
					+ m.Title + "　<color=" + (m.Ready ? "#2F6B3A" : "#73644F") + ">" + readiness + "</color>", _rich);
				if (m.Desc.Length > 0)
				{
					GUILayout.Label(m.Desc, _rich);
				}
				GUILayout.EndVertical();
			}
		}
		if (_now.Failed)
		{
			GUILayout.Label(NativeKorean.Text(KoreanMessage.SnapshotError, "读取此刻失败，请刷新或查看日志"), _muted);
		}
		else if (!any)
		{
			GUILayout.Label(NativeKorean.Text(KoreanMessage.EmptyScene, "这一段没有可显示的选项、判定或任务"), _muted);
		}
		GUILayout.EndScrollView();
		GUILayout.EndArea();
	}

	/// <summary>这个选项是剧本里第几次选择（从 0 数）；掷骰的结果是 -2，找不到是 -1。</summary>
	private int MenuOf(string key)
	{
		StoryScript script = _now.StoryScript;
		if (script == null)
		{
			return -1;
		}
		int menu = script.Menus.FindIndex(m => m.Any(step => step.Key == key));
		if (menu >= 0)
		{
			return menu;
		}
		return script.Exits.Any(e => e.Path.Any(step => step.Kind == StoryStep.Dice && step.Key == key)) ? -2 : -1;
	}

	private void DrawNowOption(string key, string option, string result)
	{
		GUILayout.BeginVertical(_card);
		// 选了以后接下去的剧情读过多少（和剧情图里一样）
		string mark = _now.StoryScript == null ? "" : string.Concat(_now.StoryScript.Exits.Where(e => e.Path.Count > 0 && e.Path[0].Key == key).Select(ReadMark).Distinct().ToArray());
		GUILayout.Label(option + (mark.Length > 0 ? "　<color=#A3302A>" + mark + "</color>" : ""), _rich);
		if (result.Length > 0)
		{
			GUILayout.Label(Cached((key, result, PanelTheme.Dark, LOMAssistant.Config.ConfigManager.ColorOptionDeltas.Value), () => PanelDeltas(result)), _muted);
		}
		GUILayout.EndVertical();
	}

	// Translate each mission-title segment separately. IDs and internal keys stay unchanged.
	private static string LocalizeNowMissionTitle(string title)
	{
		if (TextConverter.DisplayLanguage != UiText.Korean)
		{
			return title;
		}
		return string.Join(" - ", TextConverter.ToSimplified(title ?? "")
			.Split(new[] { " - " }, StringSplitOptions.None)
			.Select(part => TextConverter.ForDisplay(part.Trim())).ToArray());
	}

	// Developer-only PositionType names are often embedded in a longer description.
	// Convert just the location value, preserving the original condition and flag IDs.
	private static string LocalizeNowMissionDescription(string description)
	{
		if (TextConverter.DisplayLanguage != UiText.Korean)
		{
			return description;
		}
		return System.Text.RegularExpressions.Regex.Replace(
			description ?? "", @"(?m)^(位置：)([^\r\n]*)",
			m => m.Groups[1].Value + TextConverter.ForDisplay(TextConverter.ToSimplified(m.Groups[2].Value.Trim())));
	}

	private static NowSnapshot BuildNow()
	{
		var now = new NowSnapshot { Script = OptionInfoPatch.LastScriptKey ?? "" };
		// 标题画面上游戏还留着默认（或上一局）的任务数据，不算「此刻」
		if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Title")
		{
			return now;
		}
		try
		{
			if (now.Script.Length > 0 && StoryManager.Instance != null)
			{
				now.Story = PlayerStatManagerData.Instance?.CurrentStoryScript ?? "";
				StoryScript script = StoryScripts.Get(now.Story);
				now.StoryScript = script;
				var options = LuaParser.Instance.ExtractOptionActionsFromLua(now.Script);
				// Dice and special menus may have no --index marker. Include only the
				// keys the game actually presented, keeping the game's order intact.
				foreach (string key in options.Keys.Concat(OptionInfoPatch.MenuKeys).Distinct())
				{
					string text = RichText.Strip(StoryScripts.OptionText(key)).Trim();
					string result = options.TryGetValue(key, out var actions)
						? RichText.Strip(TextConverter.ToSimplifiedUnlessJapanese(LuaParser.Description(actions))).Trim()
						: NativeKorean.Enabled ? KoreanOptionEffects(script, key) : "";
					// 选项文字里已经被选项提示接上了「(结果)」，去掉，结果单独写在下面
					if (result.Length > 0 && text.EndsWith(result + ")"))
					{
						int cut = text.LastIndexOf('(', text.Length - result.Length - 1);
						if (cut > 0)
						{
							text = text.Substring(0, cut).Trim();
						}
					}
					now.Options.Add((key, text.Length > 0 ? text : key, result));
				}
				if (script != null && script.Exits.Count > 1)
				{
					foreach (StoryExit exit in script.Exits)
					{
						now.Ways.Add((OnPaper(TextConverter.ToSimplified(StoryScripts.Describe(exit))), string.Join("　", script.EffectsOn(exit).ToArray())));
					}
				}
				foreach (string pool in LuaParser.Instance.ExtractPositionWorkFromLua(now.Script))
				{
					var events = PositionEventCache.Instance.GetPositionEventsSortedByRate(pool);
					int total = Math.Max(1, events.Sum(e => Math.Max(0, e.GetRate())));
					foreach (PositionEventData data in events)
					{
						int rate = data.GetRate();
						now.Events.Add(new NowEvent
						{
							Data = data,
							Name = TextConverter.ToSimplified(PositionEventCache.Instance.GetPositionEventName(data)),
							Desc = OnPaper(TextConverter.ToSimplified(PositionEventCache.Instance.GetPositionEventDesc(data)).Trim()),
							Rate = rate,
							Percent = rate > 0 ? Mathf.RoundToInt(rate * 100f / total) : 0,
							Unread = MapEventHints.Unread(PositionEventCache.Instance.ResultScript(data))
						});
					}
				}
			}
			// 主线当前这一步：游戏判定条件满足时 MainMissionStart 为真，去 MainMissionPosition 就会触发
			MissionManagerData missions = MissionManagerData.Instance;
			if (DataHelper.GetMainMission(out MissionCheckData main, out string mainTitle, out string mainDesc))
			{
				string place = TextConverter.ToSimplified(main.Position.ToString());
				now.Missions.Add(new NowMission
				{
					Main = true,
					Title = LocalizeNowMissionTitle(TextConverter.ToSimplified(mainTitle.Replace("\n", " "))),
					Desc = OnPaper(LocalizeNowMissionDescription(TextConverter.ToSimplified(mainDesc).Trim())),
					Place = (place == "無" || place == "无") ? "" : (TextConverter.DisplayLanguage == UiText.Korean ? TextConverter.ForDisplay(place) : place),
					Ready = missions != null && missions.MainMissionStart && missions.MainCheckData == main && !string.IsNullOrEmpty(main.StartScript)
				});
			}
			var active = missions?.ActiveSubMissions;
			// 留学（崆峒、青城）等主角不在唐门的时候，唐门各处的支线去不了、触发不了，只列不限地点的
			bool away = (missions?.GetFlag("E11000")?.State ?? 0) == 1;
			foreach (var pair in DataHelper.GetMissions())
			{
				MissionCheckData check = pair.Key;
				if (away && check.Position != PositionType.無)
				{
					continue;
				}
				string place = TextConverter.ToSimplified(check.Position.ToString());
				now.Missions.Add(new NowMission
				{
					Title = LocalizeNowMissionTitle(TextConverter.ToSimplified(pair.Value.Item1.Replace("\n", " "))),
					Desc = OnPaper(LocalizeNowMissionDescription(TextConverter.ToSimplified(pair.Value.Item2).Trim())),
					Place = (place == "無" || place == "无") ? "" : (TextConverter.DisplayLanguage == UiText.Korean ? TextConverter.ForDisplay(place) : place),
					Ready = active != null && active.Values.Contains(check) && !string.IsNullOrEmpty(check.StartScript)
				});
			}
			// 主线在最前，其余条件满足的在前；OrderBy 是稳定排序，同组保持游戏里的顺序
			var sorted = now.Missions.OrderByDescending(m => m.Main).ThenByDescending(m => m.Ready).ToList();
			now.Missions.Clear();
			now.Missions.AddRange(sorted);
		}
		catch (Exception ex)
		{
			now.Failed = true;
			FileLogger.LogWarning("[剧情回顾] 整理「此刻」失败: " + ex.Message);
		}
		return now;
	}

	private static string KoreanOptionEffects(StoryScript script, string key)
	{
		if (script == null) return "";
		var certain = new List<string>();
		var conditional = new List<string[]>();
		foreach (var effect in script.DisplayEffects)
		{
			int selected = effect.Path.FindIndex(p => p.Key == key);
			if (selected < 0 || effect.Action.CanonicalDescription.Contains("暂存")) continue;
			string text = RichText.Strip(effect.Action.Description);
			if (text.Length == 0) continue;
			var remaining = effect.Path.Skip(selected + 1).Select(StoryScripts.Describe).ToArray();
			if (remaining.Length == 0) certain.Add(text);
			else conditional.Add(new[] { KoreanText.Get(KoreanMessage.EffectWhen, string.Join(" → ", remaining), text) });
		}
		return KoreanText.Effects(certain, conditional);
	}
}
