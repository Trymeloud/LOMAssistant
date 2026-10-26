using HarmonyLib;
using Mortal.Core;
using Mortal.Story;
using OBB.Framework.Extensions;
using UnityEngine.SceneManagement;

namespace LOMAssistant.Features.Extras;

/// <summary>
/// 剧情里手动存档，读档时游戏从上一个存档点（StartStoryScript，多是剧本开头的 AutoSave）重新演，
/// 存档里却是存档那一刻的时间。存档点之后剧本里跳过时间（NextMonth / NextRound / SetGameTime）的，
/// 读档重演时会再跳一次：抵达崆峒的 ch2_5_1 开头先 AutoSave 再 NextMonth，在这段里存的档读回来就从 3 月上旬跳到 4 月上旬，
/// 只在 3 月中旬触发的主线「小梅邀請」错过，后面的主线全部卡住。
/// 这里记下存档点那一刻的时间和行动点；同一个剧情场景里（没换过场景）剧本跳过时间后再手动存档，
/// 就把存档里的时间和行动点改回存档点那一刻，读档重演时剧本自己会再跳到正确的时间。
/// </summary>
public static class StorySaveTimePatch
{
	private static bool _valid;
	private static bool _timeMoved;
	private static bool _keepOnSceneChange;
	private static string _start;
	private static int _year;
	private static int _month;
	private static MonthStageType _stage;
	private static int _actions;

	internal static void Install()
	{
		SceneManager.activeSceneChanged += (_, next) =>
		{
			// 读档后紧接着的那次换场景不算：读档时记下的就是重演开始的时间
			if (_keepOnSceneChange && next.name == "Story")
			{
				_keepOnSceneChange = false;
				return;
			}
			_keepOnSceneChange = false;
			_valid = false;
		};
	}

	private static readonly AccessTools.FieldRef<PlayerStatManagerData, string> StartScript = AccessTools.FieldRefAccess<PlayerStatManagerData, string>("_startStoryScript");

	private static void Snapshot()
	{
		PlayerStatManagerData stats = PlayerStatManagerData.Instance;
		if (stats == null || AnytimeSave.AnytimeSave.Replaying || AnytimeSave.AnytimeSave.Restoring)
		{
			// 随时存档恢复时剧本里的时间跳转不执行，记下的时间对不上，干脆不管
			_valid = false;
			return;
		}
		GameTime t = stats.GameTime;
		_start = StartScript(stats);
		_year = t.Year;
		_month = t.Month;
		_stage = t.Stage;
		_actions = stats.CurrentActionCount;
		_timeMoved = false;
		_valid = true;
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(PlayerStatManagerData), nameof(PlayerStatManagerData.SetStartScript))]
	public static void SetStartScript_Postfix() => Snapshot();

	[HarmonyPostfix]
	[HarmonyPatch(typeof(SaveSystem), nameof(SaveSystem.LoadGameData))]
	public static void LoadGameData_Postfix()
	{
		Snapshot();
		_keepOnSceneChange = _valid;
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(LuaManager), nameof(LuaManager.NextMonth))]
	public static void NextMonth_Prefix() => _timeMoved = true;

	[HarmonyPrefix]
	[HarmonyPatch(typeof(LuaManager), nameof(LuaManager.NextRound))]
	public static void NextRound_Prefix() => _timeMoved = true;

	[HarmonyPrefix]
	[HarmonyPatch(typeof(LuaManager), nameof(LuaManager.SetGameTime))]
	public static void SetGameTime_Prefix() => _timeMoved = true;

	private static bool _manual;

	[HarmonyPrefix]
	[HarmonyPatch(typeof(SaveSystem), nameof(SaveSystem.SaveGameData), new[] { typeof(string) })]
	public static void SaveGameData_Prefix(string slot) => _manual = slot != LOMAssistant.Features.Story.StoryNodes.ReplaySlot;

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(SaveSystem), nameof(SaveSystem.SaveGameData), new[] { typeof(string) })]
	public static void SaveGameData_Finalizer() => _manual = false;

	[HarmonyPostfix]
	[HarmonyPatch(typeof(SaveSystem), "CreateSaveData")]
	public static void CreateSaveData_Postfix(GameSave __result)
	{
		if (!_manual || !_valid || !_timeMoved || __result == null || __result.CurrentScene != "Story" || __result.StartStoryScript != _start)
		{
			return;
		}
		try
		{
			FileLogger.Log($"[剧情存档时间] 存档点 {_start} 之后剧本跳过时间，存档时间 {__result.CurrentYear}年{__result.CurrentMonth}月{(int)__result.CurrentStage} 改回存档点的 {_year}年{_month}月{(int)_stage}，读档重演时不会再多跳一次。");
			__result.CurrentYear = _year;
			__result.CurrentMonth = _month;
			__result.CurrentStage = _stage;
			string key = GameStatType.行動次數.GetStringValue();
			if (__result.Stats != null)
			{
				foreach (StatSave stat in __result.Stats)
				{
					if (stat.Key == key)
					{
						stat.Value = _actions;
					}
				}
			}
		}
		catch (System.Exception ex)
		{
			FileLogger.LogWarning("[剧情存档时间] 出错（已忽略）：" + ex.Message);
		}
	}
}
