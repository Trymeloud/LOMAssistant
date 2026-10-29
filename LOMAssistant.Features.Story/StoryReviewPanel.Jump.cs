using System.Linq;
using UnityEngine;

namespace LOMAssistant.Features.Story;

/// <summary>
/// 剧情跳转的确认页：选中一段剧情点「跳到这里」后整页换成它，写明会改什么、补不全什么，有几条路能走到时让玩家选一条。
/// 确认后先存「跳转前」的随时存档，再跳。
/// </summary>
public partial class StoryReviewPanel
{
	private StoryJump.Plan _jumpPlan;
	private Vector2 _jumpScroll;
	private string _jumpError;

	/// <summary>剧情图、路线图详情里的「跳到这里」。</summary>
	private void JumpButton(StoryMission mission, string script)
	{
		if (!StoryJump.Enabled || mission == null || string.IsNullOrEmpty(script))
		{
			return;
		}
		if (GUILayout.Button("跳到这里", _button, GUILayout.Height(S(38))))
		{
			_jumpPlan = StoryJump.Build(mission, script);
			_jumpScroll = Vector2.zero;
			_jumpError = null;
		}
	}

	private void DrawJump(Rect r)
	{
		StoryJump.Plan plan = _jumpPlan;
		if (GUI.Button(new Rect(r.x, r.y, S(110), S(36)), "返回", _button))
		{
			_jumpPlan = null;
			return;
		}
		GUI.Label(new Rect(r.x + S(126), r.y, r.width - S(126), S(36)), "跳到「" + plan.Title + "」", Middle(_heading));
		var area = new Rect(r.x, r.y + S(46), r.width, r.height - S(46) - S(56));
		DragScroll("jump", area, ref _jumpScroll);
		GUILayout.BeginArea(area);
		_jumpScroll = GUILayout.BeginScrollView(_jumpScroll);
		GUILayout.Label("<color=#A3302A><b>正常游玩不建议这样做。</b></color>中间的剧情没有真的演过，只按剧本把一路上会设的旗标、剧情记录、任务进度、物品、好感和数值补上，"
			+ "有些状态补不全，之后可能出现对话对不上、分支判断出错，甚至卡住。跳之前会先存一份随时存档，跳过去以后可以回到跳转前。", _rich);
		GUILayout.Space(S(10));

		GUILayout.Label("会改的", _heading);
		GUILayout.Label("任务：" + plan.Mission.Title + (plan.Mission.Main ? "（主线）" : "（" + plan.Mission.SeriesTitle + "）"), _label);
		if (plan.Mission.Main && plan.MissionFrom.Length > 0 && plan.MissionFrom != plan.MissionTo)
		{
			GUILayout.Label("主线进度：" + plan.MissionFrom + " → " + plan.MissionTo, _label);
		}
		if (plan.ToRounds != null)
		{
			GUILayout.Label("日期：" + (plan.FromRounds != null ? RoundsDate(plan.FromRounds.Value) + " → " : "") + RoundsDate(plan.ToRounds.Value), _label);
		}
		if (plan.Between.Count > 0)
		{
			GUILayout.Label("补上中间的主线：" + string.Join("、", plan.Between.Select(m => m.Title).ToArray()), _label);
		}
		GUILayout.Label(string.Format("补 {0} 项变化（其中剧情记录 {1} 条）", plan.Commands.Count, plan.Records), _label);
		if (plan.Effects.Count > 0)
		{
			string effects = Cached(("jump", plan.Picked, plan.Effects.Count, PanelTheme.Dark, LOMAssistant.Config.ConfigManager.ColorOptionDeltas.Value),
				() => PanelDeltas(string.Join("　", plan.Effects.Distinct().ToArray())));
			GUILayout.Label(effects, _muted);
		}

		if (plan.Routes.Count > 1)
		{
			GUILayout.Space(S(10));
			GUILayout.Label("走哪条路到这里", _heading);
			GUILayout.Label("任务里有几条路都能走到这一段，选一条，就补那条路上的变化。", _muted);
			for (int i = 0; i < plan.Routes.Count; i++)
			{
				bool picked = i == plan.Picked;
				if (GUILayout.Button((picked ? "● " : "○ ") + plan.Routes[i].Label, picked ? _primary : _button))
				{
					plan.Picked = i;
					StoryJump.Recompute(plan);
				}
			}
		}
		else if (plan.Routes.Count == 0)
		{
			GUILayout.Space(S(10));
			GUILayout.Label("从任务开头找不到走到这一段的路（可能要从别的任务接进来），任务里的变化没有补。", _muted);
		}

		if (plan.Notes.Count > 0)
		{
			GUILayout.Space(S(10));
			GUILayout.Label("补不全的", _heading);
			foreach (string note in plan.Notes)
			{
				GUILayout.Label("· " + note, _label);
			}
		}
		GUILayout.EndScrollView();
		GUILayout.EndArea();

		// 底下一行：跳
		var bar = new Rect(r.x, r.yMax - S(46), r.width, S(46));
		bool can = StoryJump.CanJump(out string reason);
		string hint = _jumpError ?? (can ? "跳之前会先存一份随时存档，备注「跳转前」" : reason);
		GUI.Label(new Rect(bar.x, bar.y, bar.width - S(200), bar.height), hint, _jumpError != null ? _redNotice : Middle(_muted));
		bool wasEnabled = GUI.enabled;
		GUI.enabled = wasEnabled && can;
		if (GUI.Button(new Rect(bar.xMax - S(190), bar.y + S(4), S(190), S(40)), "仍然跳过去", _primary))
		{
			if (StoryJump.Execute(plan, out string message))
			{
				_jumpPlan = null;
				_visible = false;
			}
			else
			{
				_jumpError = message;
			}
		}
		GUI.enabled = wasEnabled;
	}

	private static string RoundsDate(int rounds)
	{
		int r = Mathf.Max(1, rounds) - 1;
		return $"第{r / 36 + 1}年{r % 36 / 3 + 1}月{StoryAtlas.StageText(r % 3 + 1)}";
	}
}
