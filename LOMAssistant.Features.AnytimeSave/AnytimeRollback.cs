using System.Collections.Generic;
using System.Linq;
using Mortal.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LOMAssistant.Features.AnytimeSave;

/// <summary>
/// 回退到判定前（默认 F4）：剧情里每次分支判断、条件检查、掷骰判定时，记下它前一句台词或选项作为回退点，
/// 只在内存里留最近几个。回退和读随时存档一样：经标题界面从剧情入口快进到那一句停下，
/// 这时调参数再往下点，判定按新数值重新算。
/// 快照只在需要时取：两句话之间游戏数据只会被剧本的数值修改改变，所以在「上一句之后的第一次修改」之前、
/// 或判定时（中间没改过）取一次，就是那一句时的数据；大多数台词之间没有修改，不用取。
/// </summary>
public partial class AnytimeSave
{
	private const int MaxRollbackPoints = 3;

	private static readonly HashSet<string> Judgments = new HashSet<string> { "checkpointmanager.Switch", "checkpointmanager.Condition", "checkpointmanager.Dice" };

	private readonly List<SaveFile> rollbackPoints = new List<SaveFile>();

	// 最近一个台词/选项/掷骰在 trace 里的位置，以及为它取过的快照
	private int lastInteraction = -1;
	private SaveFile lastSnapshot;

	private string restoreLabel = "";

	public int RollbackCount => rollbackPoints.Count;

	private void ClearRollback()
	{
		rollbackPoints.Clear();
		lastInteraction = -1;
		lastSnapshot = null;
	}

	/// <summary>记录模式下开始一个新的台词/选项/掷骰。</summary>
	private void OnInteraction(int index, string kind)
	{
		lastInteraction = index;
		lastSnapshot = null;
		// 掷骰会在游戏代码里直接改天命点等数据，不经过剧本，所以掷骰前就取
		if (kind == "roll")
		{
			EnsureSnapshot();
		}
	}

	/// <summary>剧本要修改数值了：上一句的快照还没取就现在取，这时数据还是那一句时的。</summary>
	private void OnMutate()
	{
		if (!Replaying && !Restoring && lastInteraction >= 0 && lastSnapshot == null)
		{
			EnsureSnapshot();
		}
	}

	private void OnJudgment(string name)
	{
		if (Replaying || Restoring || lastInteraction < 0 || !Judgments.Contains(name))
		{
			return;
		}
		EnsureSnapshot();
		if (lastSnapshot == null || (rollbackPoints.Count > 0 && rollbackPoints[rollbackPoints.Count - 1].target == lastInteraction))
		{
			return;
		}
		var point = lastSnapshot;
		point.rootScript = rootScript;
		point.target = lastInteraction;
		point.trace = trace.Take(lastInteraction + 1).Select(e => new Entry { kind = e.kind, label = e.label, type = e.type, value = e.value }).ToList();
		point.scripts = new List<ScriptStamp>(stamps);
		rollbackPoints.Add(point);
		if (rollbackPoints.Count > MaxRollbackPoints)
		{
			rollbackPoints.RemoveAt(0);
		}
	}

	private void EnsureSnapshot()
	{
		if (lastSnapshot != null)
		{
			return;
		}
		try
		{
			lastSnapshot = new SaveFile
			{
				version = Application.version, compatibilityId = compatibilityId, scene = "Story",
				currentScript = PlayerStatManagerData.Instance.CurrentStoryScript,
				nativeSlot = SaveSystem.Instance.CurrentSlot,
				gameJson = SaveSystem.Instance.GetCurrentSaveData(),
				randomJson = JsonUtility.ToJson(Random.state),
				music = CurrentSound("_currentMusic"), envSound = CurrentSound("_currentEnvSound"), musicVolume = CurrentVolume("BackgroundMusic"), envVolume = CurrentVolume("EnvSound"),
				visibleCharacters = VisibleCharacters(),
				dice = new DiceState()
			};
		}
		catch (System.Exception e)
		{
			FileLogger.LogWarning("[随时存档] 回退点快照失败: " + e.Message);
		}
	}

	/// <summary>回退到最近一个判定前；连按继续往前退。</summary>
	public void Rollback()
	{
		if (!installed || Replaying || Restoring || pendingLoad) return;
		if (SceneManager.GetActiveScene().name != "Story" || SceneController.Instance == null || SceneController.Instance.IsPrepare || SceneController.Instance.IsLoading) return;
		if (rollbackPoints.Count == 0)
		{
			Notice("没有可以回退的判定");
			return;
		}
		var point = rollbackPoints[rollbackPoints.Count - 1];
		rollbackPoints.RemoveAt(rollbackPoints.Count - 1);
		// 留下更早的回退点：它们的执行记录是这次的前缀，回退后照样能用
		var older = new List<SaveFile>(rollbackPoints);
		restoreLabel = "已回退到判定前";
		LoadFrom(point);
		rollbackPoints.AddRange(older);
	}
}
