using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using LOMAssistant.Config;
using LOMAssistant.Features.Story;
using Mortal.Core;
using Mortal.Free;
using Mortal.Story;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LOMAssistant;

/// <summary>
/// 养成地图上的建筑：鼠标停上去，列出这里的事件和触发条件（主线、支线这一步在不在这里、时间到没到、条件满足没有，
/// 以及点进去会抽的随机事件和概率），满足的绿字、不满足的红字，写法参考赵活系统。
/// 有现在就能触发、这一局还没走过的主线或支线的建筑，一直亮着游戏自己的悬停光效。
/// </summary>
public class MapEventHints : MonoBehaviour
{
	private static MapEventHints _instance;
	private static PositionController _hovered;
	private static string _text;
	// 我们点亮光效的建筑：鼠标移开时游戏会把光效关掉，再点亮
	private static readonly HashSet<PositionController> Glowing = new HashSet<PositionController>();
	private static readonly Regex PoolCall = new Regex(@"checkpointmanager\.Position\(\s*""([A-Za-z0-9_]+)""\s*\)");

	private float _nextGlowCheck;
	private float _scale = -1f;
	private int _fontMode = -1;
	private GUIStyle _box;

	public static void Ensure(GameObject host)
	{
		if (_instance == null)
		{
			_instance = host.GetComponent<MapEventHints>() ?? host.AddComponent<MapEventHints>();
			SceneManager.activeSceneChanged += (a, b) =>
			{
				_hovered = null;
				Glowing.Clear();
			};
		}
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(PositionController), nameof(PositionController.OnPositionEnter))]
	public static void Enter_Postfix(PositionController __instance)
	{
		_hovered = __instance;
		_text = null;
		FreePositionData data = Traverse.Create(__instance).Field("_positionData").GetValue<FreePositionData>();
		LastScript = data != null ? data.GetFieldValue<string>("_defaultScript") : LastScript;
	}

	/// <summary>最后停过的建筑的剧本（点进去以后选项小窗找选项用）。</summary>
	internal static string LastScript;

	[HarmonyPostfix]
	[HarmonyPatch(typeof(PositionController), nameof(PositionController.OnPositionExit))]
	public static void Exit_Postfix(PositionController __instance)
	{
		if (_hovered == __instance)
		{
			_hovered = null;
		}
	}

	// 鼠标移开时游戏会停掉悬停光效：一直亮着的建筑只收起名牌、光效不停。
	// 以前是停了再重新播放，每次重播都从头喷一阵，鼠标扫过几座建筑就一闪一闪
	[HarmonyPrefix]
	[HarmonyPatch(typeof(PositionController), "ShowHover")]
	public static bool ShowHover_Prefix(PositionController __instance, bool show)
	{
		if (show || !Glowing.Contains(__instance))
		{
			return true;
		}
		GameObject tooltip = Traverse.Create(__instance).Field("_tooltip").GetValue<GameObject>();
		if (tooltip != null)
		{
			tooltip.SetActive(false);
		}
		return false;
	}

	private void Update()
	{
		if (Time.unscaledTime < _nextGlowCheck)
		{
			return;
		}
		_nextGlowCheck = Time.unscaledTime + 2f;
		try
		{
			RefreshGlow();
		}
		catch (Exception ex)
		{
			FileLogger.LogWarning("[地图事件] 更新高亮失败: " + ex.Message);
		}
	}

	private static void RefreshGlow()
	{
		bool on = ConfigManager.EnableMapHints.Value;
		foreach (PositionController position in FindObjectsOfType<PositionController>())
		{
			// 不看「现在能不能点」：点了建筑、正在演出时游戏把所有建筑设成不能点，以前会把光效全关掉又重开
			bool unread = on && position.isActiveAndEnabled && HasUnread(position);
			ParticleSystem particle = Particle(position);
			if (particle == null)
			{
				continue;
			}
			if (unread && !Glowing.Contains(position))
			{
				Glowing.Add(position);
				if (!particle.isPlaying)
				{
					particle.Play();
				}
			}
			else if (!unread && Glowing.Remove(position) && position != _hovered)
			{
				particle.Stop();
			}
		}
	}

	private void OnGUI()
	{
		if (_hovered == null || !ConfigManager.EnableMapHints.Value || InputGuard.ModWindowOpen || EventPicker.IsWaiting || !Active(_hovered))
		{
			return;
		}
		float scale = PanelTheme.UiScale;
		if (scale <= 0f)
		{
			return;
		}
		if (_box == null || ModFont.NeedsRebuild(scale, _scale, _fontMode))
		{
			_scale = scale;
			_fontMode = ModFont.Mode;
			// 和其他提示框同一种样子（墨色底、宣纸色字、褐色细边），不透明度用设置里的「提示框不透明度」
			_box = PanelTheme.Toast(scale, 19);
			_box.alignment = TextAnchor.UpperLeft;
			_box.padding = new RectOffset((int)(14f * scale), (int)(14f * scale), (int)(10f * scale), (int)(10f * scale));
		}
		if (_text == null)
		{
			try
			{
				_text = Describe(_hovered);
			}
			catch (Exception ex)
			{
				_text = "";
				FileLogger.LogWarning("[地图事件] 读取事件失败: " + ex.Message);
			}
		}
		if (_text.Length == 0)
		{
			return;
		}
		var content = new GUIContent(_text);
		float width = 600f * scale;
		float height = _box.CalcHeight(content, width);
		// 比屏幕还高（事件特别多的建筑）就把字缩小、框放宽，整段都放得下
		int fontSize = _box.fontSize;
		while (height > Screen.height - 16f * scale && _box.fontSize > 10)
		{
			_box.fontSize--;
			width = Mathf.Min(Screen.width * 0.6f, width * 1.04f);
			height = _box.CalcHeight(content, width);
		}
		Vector2 mouse = Event.current.mousePosition;
		// 放在鼠标右下，超出屏幕就挪到左边、上边
		float x = mouse.x + 24f * scale, y = mouse.y + 16f * scale;
		if (x + width > Screen.width)
		{
			x = mouse.x - width - 16f * scale;
		}
		y = Mathf.Clamp(y, 0f, Mathf.Max(0f, Screen.height - height));
		GUI.Box(new Rect(Mathf.Max(0f, x), y, width, height), content, PanelTheme.Sync(_box));
		_box.fontSize = fontSize;
	}

	// ---------------- 这里有什么事件 ----------------

	private static PositionType Position(PositionController position) => Traverse.Create(position).Field("_position").GetValue<PositionType>();

	private static ParticleSystem Particle(PositionController position) => Traverse.Create(position).Field("_hoverParticle").GetValue<ParticleSystem>();

	// 游戏自己能不能点（正在演出、这个场所没开放时不显示）
	private static bool Active(PositionController position) => position != null && position.isActiveAndEnabled && Traverse.Create(position).Method("IsActiveStatus").GetValue<bool>();

	/// <summary>主线和每条支线现在这一步，在这个场所的。</summary>
	private static IEnumerable<(MissionCheckData Check, bool Main, string Series)> Missions(PositionType place)
	{
		MissionManagerData data = MissionManagerData.Instance;
		if (data == null)
		{
			yield break;
		}
		var series = new List<(MissionData, bool)> { (data.Main, true) };
		SubMissionsData subs = data.GetFieldValue<SubMissionsData>("_subMissions");
		if (subs != null)
		{
			series.AddRange(subs.List.Select(s => (s, false)));
		}
		foreach (var (mission, main) in series)
		{
			if (mission == null || mission.CurrentKey == "clear")
			{
				continue;
			}
			MissionCheckData check = mission.GetByKey(mission.CurrentKey);
			if (check != null && check.Position == place && !string.IsNullOrEmpty(check.StartScript))
			{
				yield return (check, main, mission.Name ?? mission.name);
			}
		}
	}

	// 现在点进去就会演这一步：主线开始了且在这里，或支线已经满足条件
	private static bool Triggerable(MissionCheckData check, bool main)
	{
		MissionManagerData data = MissionManagerData.Instance;
		// 支线：游戏的「已满足的支线」按任务组名记（SubMissionsData.GetActiveMissions），不是这一步的编号，按值找；
		// 主线开始了、在别的地方时，点这里只会叫你先去办主线，支线也触发不了
		return main
			? data.MainMissionStart && data.MainMissionPosition == check.Position
			: data.ActiveSubMissions != null && data.ActiveSubMissions.ContainsValue(check) && !LockedByMain(check.Position);
	}

	// 场所默认剧本里抽随机事件的池子（checkpointmanager.Position("…")）
	private static List<PositionEventData> Events(PositionController position)
	{
		FreePositionData data = Traverse.Create(position).Field("_positionData").GetValue<FreePositionData>();
		string script = data != null ? data.GetFieldValue<string>("_defaultScript") : null;
		string text = StoryScripts.Text(script);
		if (text == null)
		{
			return new List<PositionEventData>();
		}
		return PoolCall.Matches(text).Cast<Match>().Select(m => m.Groups[1].Value).Distinct()
			.SelectMany(pool => PositionEventCache.Instance.GetPositionEvents(pool)).Distinct().ToList();
	}

	internal static bool Unread(string script)
	{
		// 一句都没读过才算没看过：触发过的事件只走了其中一条分岔，读过的比例到不了 100%
		StoryScript s = StoryScripts.Get(script);
		ReadStorySystem read = ReadStorySystem.Instance;
		return s != null && read != null && s.Lines.Count > 0 && !s.Lines.Any(read.ExistKey);
	}

	// 主线开始了、在别的地方：这里点了只会说「先去办主线」，什么事件都不触发
	private static bool LockedByMain(PositionType place)
	{
		MissionManagerData data = MissionManagerData.Instance;
		return data != null && data.MainMissionStart && data.MainMissionPosition != PositionType.無 && data.MainMissionPosition != place;
	}

	private static bool HasUnread(PositionController position)
	{
		// 主线把地点锁住时只亮主线那一处；这个场所本身没开放（游戏的开放条件）也不亮
		if (LockedByMain(Position(position)) || !Traverse.Create(position).Field("_active").GetValue<bool>())
		{
			return false;
		}
		foreach (var (check, main, _) in Missions(Position(position)))
		{
			if (Triggerable(check, main))
			{
				StoryMission mission = StoryAtlas.Ready ? StoryAtlas.Find(check.Key) : null;
				if (mission == null ? Unread(check.StartScript) : !mission.Visited)
				{
					return true;
				}
			}
		}
		// 随机事件没看过的不算：每个建筑都有一堆，又不能指定抽哪个，亮着也没意义（悬停提示里照样标「没看过」）
		return false;
	}

	/// <summary>
	/// 条件里比「目前时间」的小条件（游戏时间换成旬数：第 1 年 1 月上旬 = 1，GameTime.ConvertToRounds）：
	/// 写成日期（=10 → 第1年4月上旬，>=79 → 第3年3月上旬～，<=15 → ～第1年5月下旬），并带上现在满不满足。
	/// </summary>
	private static List<(string Text, bool Ok)> TimeClauses(MissionCheckData check)
	{
		var list = new List<(string, bool)>();
		foreach (ConditionResultItem condition in check.GetFieldValue<ConditionResultItem[]>("_conditions") ?? new ConditionResultItem[0])
		{
			foreach (StatCompareItem item in condition?.GetFieldValue<List<StatCompareItem>>("_items") ?? new List<StatCompareItem>())
			{
				StatGroupVariable left = item.GetFieldValue<StatGroupVariable>("_value1");
				StatGroupVariable right = item.GetFieldValue<StatGroupVariable>("_value2");
				if (left == null || right == null || !(left.name.Contains("目前時間") || left.name.Contains("目前时间")))
				{
					continue;
				}
				int at = right.GetResult();
				string text = DataHelper.GetCompareOperator(item.GetFieldValue<StatCompareType>("_compareOp")) switch
				{
					"=" => RoundsText(at),
					">=" => RoundsText(at) + "～",
					">" => RoundsText(at + 1) + "～",
					"<=" => "～" + RoundsText(at),
					"<" => "～" + RoundsText(at - 1),
					string op => op + RoundsText(at),
				};
				list.Add((text, item.Check()));
			}
		}
		return list;
	}

	private static string RoundsText(int rounds)
	{
		int r = Math.Max(1, rounds) - 1;
		return $"第{r / 36 + 1}年{r % 36 / 3 + 1}月{StoryAtlas.StageText(r % 3 + 1)}";
	}

	private static bool IsJealousy(MissionCheckData check, string series)
	{
		StoryMission mission = StoryAtlas.Ready ? StoryAtlas.Find(check.Key) : null;
		string title = mission?.Title ?? TextConverter.ToSimplified(series ?? check.Key);
		if (title.Contains("修罗场") || title.Contains("修羅場"))
		{
			return true;
		}
		// 条件里要「启动修罗场事件」「发生修罗场事件」这类旗标的（各女角的「陪×」等）
		foreach (ConditionResultItem condition in check.GetFieldValue<ConditionResultItem[]>("_conditions") ?? new ConditionResultItem[0])
		{
			string line = condition != null ? RichText.Strip(DataHelper.ExtractConditions(condition, out _)) : "";
			if (line.Contains("修罗场") || line.Contains("修羅場"))
			{
				return true;
			}
		}
		return false;
	}

	private static string Describe(PositionController position)
	{
		PositionType place = Position(position);
		// 修罗场（任务名或条件里提到修罗场）：大部分玩家走不到，不满足条件时就不列出来，免得每次都看到一条「还不能触发」
		var missions = Missions(place).Where(m => !(IsJealousy(m.Check, m.Series) && !Triggerable(m.Check, m.Main))).ToList();
		List<PositionEventData> events = Events(position);
		var text = new StringBuilder();
		text.Append($"<b>{TextConverter.ToSimplified(place.ToString())}：{missions.Count + events.Count} 个事件</b>");
		if (LockedByMain(place))
		{
			text.Append("\n" + RichText.Red("主线进行中，现在只能去主线所在的地方，点这里不会触发事件"));
		}
		// 能触发、或在时间内的写全（状态、时间、条件）；不在时间内的只在最后写一行名字和时间，提示不至于太长
		var later = new List<string>();
		int hidden = 0;
		foreach (var (check, main, series) in missions)
		{
			StoryMission mission = StoryAtlas.Ready ? StoryAtlas.Find(check.Key) : null;
			string title = mission?.Title ?? TextConverter.ToSimplified(series ?? check.Key);
			// 有的任务说明本身写成「支线「同门关怀」」，去掉外面这一层，免得套两层
			System.Text.RegularExpressions.Match wrapped = System.Text.RegularExpressions.Regex.Match(title, "^(?:支线|主线|支線|主線)「(.+)」$");
			if (wrapped.Success)
			{
				title = wrapped.Groups[1].Value;
			}
			string head = (main ? "主线" : "支线") + "「" + title + "」";
			bool now = Triggerable(check, main);
			bool inPeriod = DataHelper.GetMissionPeriod(check, out string period);
			// 有的任务不设时间窗，把时间写在条件里（「目前时间 需<=15」）：换成日期一起算，过了或还没到就折进「不在时间内」
			var clauses = TimeClauses(check);
			if (clauses.Count > 0)
			{
				string dates = string.Join("、", clauses.Select(c => c.Text).ToArray());
				period = period == "不限" ? dates : period + "、" + dates;
				inPeriod = inPeriod && clauses.All(c => c.Ok);
			}
			string unseen = mission != null && !mission.Visited ? "　" + RichText.Golden("没看过") : "";
			// 设置页「地图提示只列能触发的」：现在不能触发的一概不列，只在最后写个数
			if (!now && ConfigManager.MapHintsNowOnly.Value)
			{
				hidden++;
				continue;
			}
			if (!now && !inPeriod)
			{
				later.Add(head + "　" + period);
				continue;
			}
			text.Append("\n\n" + RichText.Golden(head) + unseen);
			text.Append("\n" + (now ? RichText.Green("现在点进去就会触发") : RichText.Red("还不能触发")) + "　" + RichText.Green("时间 " + period));
			foreach (ConditionResultItem condition in check.GetFieldValue<ConditionResultItem[]>("_conditions") ?? new ConditionResultItem[0])
			{
				string line = condition != null ? DataHelper.ExtractConditions(condition, out _) : "";
				if (line.Length > 0)
				{
					text.Append("\n" + line);
				}
			}
		}
		if (later.Count > 0)
		{
			text.Append("\n\n" + RichText.Golden("不在时间内"));
			foreach (string line in later)
			{
				text.Append("\n<color=#9A9286>" + line + "</color>");
			}
		}
		if (hidden > 0)
		{
			text.Append($"\n\n<color=#9A9286>另有 {hidden} 个主线、支线现在还不能触发</color>");
		}
		if (events.Count == 0 && !PositionEventCache.Available)
		{
			text.Append("\n\n" + RichText.Golden("随机事件") + "\n<color=#9A9286>进过一次剧情后才能列出（事件表只在剧情场景里读得到）</color>");
		}
		if (events.Count > 0)
		{
			text.Append("\n\n" + RichText.Golden("随机事件"));
			AppendEvents(text, events);
		}
		return text.ToString();
	}

	/// <summary>一组随机事件：只列现在能抽到的（按概率从高到低），条件不满足的只写个数（后山这类事件多的建筑，全列出来提示太长）。</summary>
	internal static void AppendEvents(StringBuilder text, List<PositionEventData> events)
	{
		var rates = events.Select(e => (Data: e, Rate: e.GetRate())).ToList();
		var possible = rates.Where(r => r.Rate > 0).OrderByDescending(r => r.Rate).ToList();
		int total = possible.Sum(r => r.Rate);
		foreach (var (data, rate) in possible)
		{
			string name = RichText.Strip(DataHelper.GetEventName(data));
			string unread = Unread(PositionEventCache.Instance.ResultScript(data)) ? "　" + RichText.Golden("没看过") : "";
			text.Append($"\n{name}　{RichText.Green($"{rate * 100f / total:0.#}%")}{unread}");
		}
		int blocked = rates.Count - possible.Count;
		if (blocked > 0)
		{
			text.Append($"\n<color=#9A9286>{(possible.Count > 0 ? "另有 " + blocked + " 个条件不满足" : "全部 " + blocked + " 个都条件不满足")}</color>");
		}
	}

	/// <summary>
	/// 建筑里的一个选项（后山的挑柴、修炼……）会抽哪些事件池：在剧本里找到选项表里写着这个选项的那一项，
	/// 再取「选了第几项」那一支里调用的事件池（checkpointmanager.Position("…")），按调用顺序，每个池子各抽一次。
	/// </summary>
	internal static List<string> OptionPools(string script, string option)
	{
		var pools = new List<string>();
		string text = StoryScripts.Text(script);
		if (text == null || option.Length == 0)
		{
			return pools;
		}
		string[] lines = text.Split('\n');
		string quoted = "\"" + option + "\"";
		Match item = lines.Where(l => l.Contains(quoted)).Select(l => Regex.Match(l, @"^\s*([A-Za-z0-9_]+)\[(\d+)\]\s*=")).FirstOrDefault(m => m.Success);
		if (item == null)
		{
			return pools;
		}
		Match choose = Regex.Match(text, @"local\s+([A-Za-z0-9_]+)\s*=\s*choose\(\s*" + item.Groups[1].Value + @"\s*\)");
		if (!choose.Success)
		{
			return pools;
		}
		var head = new Regex(@"^(?:if|elseif)\s+" + choose.Groups[1].Value + @"\s*==\s*" + item.Groups[2].Value + @"\s+then$");
		// 按 if / elseif / else / end 数层数：这一支从它的 if、elseif 开始，到同一层的下一个 elseif、else 或 end 结束
		int depth = 0, at = -1;
		var body = new StringBuilder();
		foreach (string raw in lines)
		{
			string line = raw.Trim();
			bool opens = (line.StartsWith("if ") && line.EndsWith("then")) || line.EndsWith(" do") || line == "do" || Regex.IsMatch(line, @"\bfunction\b.*\)$");
			bool sibling = line.StartsWith("elseif ") || line == "else";
			bool closes = line == "end" || line.StartsWith("end ") || line.StartsWith("end)");
			if (at >= 0 && ((sibling && depth - 1 == at) || (closes && depth - 1 == at)))
			{
				break;
			}
			if (at < 0 && head.IsMatch(line))
			{
				at = line.StartsWith("if ") ? depth : depth - 1;
			}
			else if (at >= 0)
			{
				body.Append(raw).Append('\n');
			}
			if (opens)
			{
				depth++;
			}
			else if (closes)
			{
				depth--;
			}
		}
		var calls = PoolCall.Matches(body.ToString()).Cast<Match>().ToList();
		// 外出的「移动」这类选项接着演另一个场所的剧本（door_01_01 闹市……），那个剧本里抽的池子也算上
		foreach (Match next in Regex.Matches(body.ToString(), @"SetNextScript\(\s*""([A-Za-z0-9_]+)""\s*\)"))
		{
			string nextText = StoryScripts.Text(next.Groups[1].Value);
			if (nextText != null)
			{
				calls.AddRange(PoolCall.Matches(nextText).Cast<Match>());
			}
		}
		foreach (Match m in calls)
		{
			if (!pools.Contains(m.Groups[1].Value))
			{
				pools.Add(m.Groups[1].Value);
			}
		}
		return pools;
	}
}

/// <summary>
/// 点进养成建筑后的选项（后山的挑柴、修练……）和外出的列表（移动去闹市、行商……）：鼠标停在选项上（或用方向键、手柄选中时），旁边列出选这一项会抽的随机事件、
/// 概率和没看过的，和地图建筑的悬停提示同一种写法。开关跟着「养成地图事件提示」。
/// </summary>
internal sealed class PositionOptionHints : MonoBehaviour
{
	private static PositionOptionHints _instance;

	// 建筑里的选项（PositionOptionButton）和外出这类列表选项（BreakOptionButton）：都有 _source（选项编号）和 _titleText
	private MonoBehaviour[] _options = new MonoBehaviour[0];
	private float _nextScan;
	private bool _useSelection, _touched;
	private Vector2 _lastMouse;
	private MonoBehaviour _pointed;
	private string _key, _text;
	private GUIStyle _box;
	private float _scale = -1f;
	private int _fontMode = -1;

	public static void Ensure(GameObject host)
	{
		if (_instance == null)
		{
			_instance = host.GetComponent<PositionOptionHints>() ?? host.AddComponent<PositionOptionHints>();
		}
	}

	private void Update()
	{
		if (!ConfigManager.EnableMapHints.Value)
		{
			_pointed = null;
			return;
		}
		// 游戏的选项卡（MenuToggleButton）鼠标一进整张卡就把按钮设成选中，方向键、手柄也是换选中：看选中的是哪一项，
		// 和游戏自己的高亮范围一样。菜单打开时游戏会默认选中第一项，动过鼠标或按过方向键以后才显示
		var mouse = UnityEngine.InputSystem.Mouse.current;
		if (mouse != null)
		{
			Vector2 position = mouse.position.ReadValue();
			if ((position - _lastMouse).sqrMagnitude > 4f)
			{
				_useSelection = false;
				_touched = true;
			}
			_lastMouse = position;
		}
		var keyboard = UnityEngine.InputSystem.Keyboard.current;
		var pad = UnityEngine.InputSystem.Gamepad.current;
		if ((keyboard != null && (keyboard.upArrowKey.wasPressedThisFrame || keyboard.downArrowKey.wasPressedThisFrame || keyboard.leftArrowKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame))
			|| (pad != null && (pad.dpad.ReadValue().sqrMagnitude > 0.25f || pad.leftStick.ReadValue().sqrMagnitude > 0.25f)))
		{
			_useSelection = true;
			_touched = true;
		}
		if (Time.unscaledTime >= _nextScan)
		{
			_nextScan = Time.unscaledTime + 0.3f;
			int before = _options.Count(o => o != null && o.isActiveAndEnabled);
			_options = FindObjectsOfType<PositionOptionButton>().Cast<MonoBehaviour>().Concat(FindObjectsOfType<BreakOptionButton>()).ToArray();
			int now = _options.Count(o => o.isActiveAndEnabled);
			if (now != before)
			{
				// 换了一个菜单：等动过鼠标、按过方向键再显示
				_touched = false;
			}
		}
		GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
		MonoBehaviour pointed = _touched && selected != null ? _options.FirstOrDefault(o => o != null && o.isActiveAndEnabled && o.gameObject == selected) : null;
		_pointed = pointed;
	}

	private static Camera CanvasCamera(Component option)
	{
		Canvas canvas = option.GetComponentInParent<Canvas>();
		return canvas == null || canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.rootCanvas.worldCamera;
	}

	private static IEnumerable<Graphic> Targets(Component option) => option.GetComponentsInChildren<Graphic>().Where(g => g.isActiveAndEnabled && g.raycastTarget);

	/// <summary>整张卡在屏幕上的范围（屏幕坐标，原点左下）。</summary>
	private static Rect Bounds(Component option)
	{
		Camera cam = CanvasCamera(option);
		var corners = new Vector3[4];
		float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
		foreach (RectTransform rect in Targets(option).Select(g => g.rectTransform).Concat(new[] { (RectTransform)option.transform }))
		{
			rect.GetWorldCorners(corners);
			foreach (Vector3 corner in corners)
			{
				Vector2 point = RectTransformUtility.WorldToScreenPoint(cam, corner);
				minX = Mathf.Min(minX, point.x);
				minY = Mathf.Min(minY, point.y);
				maxX = Mathf.Max(maxX, point.x);
				maxY = Mathf.Max(maxY, point.y);
			}
		}
		return Rect.MinMaxRect(minX, minY, maxX, maxY);
	}

	private string Describe(MonoBehaviour option)
	{
		Text source = Traverse.Create(option).Field("_source").GetValue<Text>();
		string key = source != null && source.text != null ? source.text.Trim() : "";
		if (key != _key)
		{
			_key = key;
			_text = null;
		}
		if (_text != null)
		{
			return _text;
		}
		_text = "";
		// 正在演的就是建筑的剧本；找不到这个选项时再看最后点的建筑
		List<string> pools = MapEventHints.OptionPools(PlayerStatManagerData.Instance?.CurrentStoryScript, key);
		if (pools.Count == 0 && MapEventHints.LastScript != null)
		{
			pools = MapEventHints.OptionPools(MapEventHints.LastScript, key);
		}
		if (pools.Count == 0)
		{
			return _text;
		}
		Text[] titles = Traverse.Create(option).Field("_titleText").GetValue<Text[]>();
		string title = titles != null && titles.Length > 0 && titles[0] != null ? TextConverter.ToSimplified(titles[0].text) : "";
		var text = new StringBuilder("<b>" + (title.Length > 0 ? title + "：" : "") + "随机事件</b>");
		if (!PositionEventCache.Available)
		{
			text.Append("\n<color=#9A9286>进过一次剧情后才能列出（事件表只在剧情场景里读得到）</color>");
			return _text = text.ToString();
		}
		// 一个选项可能接连抽好几个池子（先抽干活的结果，再抽之后的事件），每个池子各算概率，空一行分开
		bool first = true;
		foreach (string pool in pools)
		{
			List<PositionEventData> events = PositionEventCache.Instance.GetPositionEvents(pool);
			if (events.Count == 0)
			{
				continue;
			}
			if (!first)
			{
				text.Append('\n');
			}
			first = false;
			MapEventHints.AppendEvents(text, events);
		}
		return _text = first ? "" : text.ToString();
	}

	private void OnGUI()
	{
		if (_pointed == null || !ConfigManager.EnableMapHints.Value || UiHider.Active || InputGuard.ModWindowOpen || EventPicker.IsWaiting || Event.current.type != EventType.Repaint)
		{
			return;
		}
		string note;
		try
		{
			note = Describe(_pointed);
		}
		catch (Exception ex)
		{
			_text = note = "";
			FileLogger.LogWarning("[地图事件] 读取选项事件失败: " + ex.Message);
		}
		if (note.Length == 0)
		{
			return;
		}
		float scale = PanelTheme.UiScale;
		if (scale <= 0f)
		{
			return;
		}
		if (_box == null || ModFont.NeedsRebuild(scale, _scale, _fontMode))
		{
			_scale = scale;
			_fontMode = ModFont.Mode;
			_box = PanelTheme.Toast(scale, 19);
			_box.alignment = TextAnchor.UpperLeft;
			_box.padding = new RectOffset((int)(14f * scale), (int)(14f * scale), (int)(10f * scale), (int)(10f * scale));
		}
		var content = new GUIContent(note);
		float width = 460f * scale;
		float height = _box.CalcHeight(content, width);
		float x, y;
		if (!_useSelection)
		{
			// 鼠标：和地图建筑的提示一样放在鼠标右下，超出屏幕就挪到左边、上边
			Vector2 at = Event.current.mousePosition;
			x = at.x + 24f * scale;
			if (x + width > Screen.width)
			{
				x = at.x - width - 16f * scale;
			}
			y = Mathf.Clamp(at.y + 16f * scale, 0f, Mathf.Max(0f, Screen.height - height));
		}
		else
		{
			// 方向键、手柄：放在选中的按钮右边，放不下放左边
			Rect card = Bounds(_pointed);
			x = card.xMax + 12f * scale;
			if (x + width > Screen.width)
			{
				x = card.xMin - width - 12f * scale;
			}
			y = Mathf.Clamp(Screen.height - card.yMax, 0f, Mathf.Max(0f, Screen.height - height));
		}
		x = Mathf.Clamp(x, 0f, Mathf.Max(0f, Screen.width - width));
		GUI.Box(new Rect(x, y, width, height), content, PanelTheme.Sync(_box));
	}
}
