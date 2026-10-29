using System.Collections;
using System.Linq;
using HarmonyLib;
using Mortal.Core;
using Mortal.Story;
using UnityEngine;

namespace LOMAssistant.Features.Story;

/// <summary>
/// 回看片尾动画：通关时最后一章（ch8_8_1）放的片尾字幕面板（creditpanel.Open()，配乐「傳奇_001」）。
/// 这个面板挂在剧情场景里，所以只在剧情场景能放；放的时候关掉咖啡伴侣窗口、停住对话的按键，
/// 按游戏的取消键（Esc / 手柄 B）结束，结束后换回原来的音乐。
/// </summary>
public partial class StoryReviewPanel
{
	private const string CreditMusic = "傳奇_001";
	private bool _creditsPlaying;

	/// <summary>当前场景里的片尾字幕面板；不在剧情场景时为空。</summary>
	private static CreditPanel FindCreditPanel()
	{
		return Resources.FindObjectsOfTypeAll<CreditPanel>().FirstOrDefault(p => p != null && p.gameObject.scene.IsValid());
	}

	private void DrawCreditsButton()
	{
		CreditPanel panel = _creditsPlaying ? null : FindCreditPanel();
		bool wasEnabled = GUI.enabled;
		GUI.enabled = wasEnabled && panel != null;
		bool clicked = GUILayout.Button(StoryUi.Text(KoreanStoryMessage.Credits, "看片尾动画"), _button, GUILayout.Width(TextWidth(StoryUi.Text(KoreanStoryMessage.Credits, "看片尾动画"), _button, S(160))));
		GUI.enabled = wasEnabled;
		Hover(GUILayoutUtility.GetLastRect(), panel != null ? StoryUi.Text(KoreanStoryMessage.CreditsHint, "放一遍通关时的片尾字幕动画，按 Esc 结束") : StoryUi.Text(KoreanStoryMessage.CreditsUnavailable, "在剧情场景里才能放（读档进入剧情后再来）"));
		if (clicked && panel != null)
		{
			_visible = false;
			StartCoroutine(PlayCredits(panel));
		}
	}

	private IEnumerator PlayCredits(CreditPanel panel)
	{
		_creditsPlaying = true;
		SoundManager sound = SoundManager.Instance;
		string music = sound != null ? Traverse.Create(sound).Field("_currentMusic").GetValue<string>() : "";
		try
		{
			LuaManager.Instance?.PlayMusic(CreditMusic);
			StoryManager.Instance?.SetEnableActions(false);
		}
		catch (System.Exception e)
		{
			FileLogger.LogWarning("[片尾动画] 准备失败: " + e.Message);
		}
		yield return panel.Open();
		try
		{
			StoryManager.Instance?.SetEnableActions(true);
			if (sound != null)
			{
				sound.ResetMusic();
				if (!string.IsNullOrEmpty(music))
				{
					sound.PlayMusic(music);
				}
			}
		}
		catch (System.Exception e)
		{
			FileLogger.LogWarning("[片尾动画] 收尾失败: " + e.Message);
		}
		_creditsPlaying = false;
	}
}
