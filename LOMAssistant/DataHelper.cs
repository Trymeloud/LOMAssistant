using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx;
using Mortal.Core;
using Mortal.Story;
using OBB.Framework.Data;
using OBB.Framework.Utils;
using UnityEngine;

namespace LOMAssistant;

public static class DataHelper
{
	public static string ExtractConditions(ConditionResultItem condition, out bool isInPeriod)
	{
		try
		{
			List<string> list = new List<string>();
			isInPeriod = true;
			foreach (StatCompareItem item in condition.GetFieldValue<List<StatCompareItem>>("_items"))
			{
				StatGroupVariable fieldValue = item.GetFieldValue<StatGroupVariable>("_value1");
				StatGroupVariable fieldValue2 = item.GetFieldValue<StatGroupVariable>("_value2");
				string compareOperator = GetCompareOperator(item.GetFieldValue<StatCompareType>("_compareOp"));
				if (fieldValue.name.Contains("时间_") && !item.Check())
				{
					isInPeriod = false;
				}
				string text = FormatConditionValName(fieldValue);
				FormatConditionValName(fieldValue2);
				string text2 = string.Format("{0} 需{2}{3} → 当前{1}", text, fieldValue.GetResult(), compareOperator, fieldValue2.GetResult());
				text2 = (item.Check() ? RichText.Green(text2) : RichText.Red(text2));
				list.Add(text2);
			}
			LogicOperatorType fieldValue3 = condition.GetFieldValue<LogicOperatorType>("_logicOp");
			return string.Join(" " + GetLogicOperator(fieldValue3) + " ", list);
		}
		catch (Exception ex)
		{
			DicePlugin.LogError("Error extracting conditions: " + ex.Message);
			isInPeriod = false;
			return string.Empty;
		}
	}

	private static string FormatConditionValName(StatGroupVariable variable)
	{
		string name = variable.name;
		if (name.StartsWith("标记_") || name.StartsWith("旗标_") || name.StartsWith("標記_") || name.StartsWith("旗標_") || IsEnglishAlphanumeric(name))
		{
			string text = name;
			if (text.StartsWith("标记_") || text.StartsWith("旗标_") || text.StartsWith("標記_") || text.StartsWith("旗標_"))
			{
				text = text.Substring(3);
			}
			try
			{
				List<StatValueReference> fieldValue = variable.GetFieldValue<List<StatValueReference>>("_value");
				if (fieldValue != null && fieldValue.Count > 0)
				{
					FlagData fieldValue2 = fieldValue[0].GetFieldValue<FlagData>("_flag");
					if (fieldValue2 != null)
					{
						string devNote = fieldValue2.DevNote;
						if (!string.IsNullOrEmpty(devNote))
						{
							// 第一行末尾的 \r 也去掉，见 GetFlagName
							return TextConverter.ToSimplified(devNote.Split('\n')[0].TrimEnd('\r')) + " (" + text + ")";
						}
					}
				}
			}
			catch
			{
			}
			try
			{
				string flagName = GetFlagName(text);
				if (!string.IsNullOrEmpty(flagName))
				{
					return flagName + " (" + text + ")";
				}
			}
			catch
			{
			}
		}
		return TextConverter.ToSimplified(name.Replace("数值_", "").Replace("时间_", "").Replace("常数_", "")
			.Replace("旗标_", "")
			.Replace("标记_", "")
			.Replace("數值_", "")
			.Replace("時間_", "")
			.Replace("常數_", "")
			.Replace("旗標_", "")
			.Replace("標記_", ""));
	}

	private static bool IsEnglishAlphanumeric(string s)
	{
		if (string.IsNullOrEmpty(s))
		{
			return false;
		}
		foreach (char c in s)
		{
			if ((c < 'a' || c > 'z') && (c < 'A' || c > 'Z') && (c < '0' || c > '9') && c != '_' && c != '-')
			{
				return false;
			}
		}
		return true;
	}

	private static SwitchResultConfig[] _switchConfigs;
	private static ConditionResultConfig _conditionConfig;

	/// <summary>
	/// 分支判断表。挂在剧情场景的 CheckPointManager 上，离开剧情场景（养成地图）就拿不到；
	/// 拿到过就记住，没拿到过就在已载入的资源里找。
	/// </summary>
	internal static SwitchResultConfig[] SwitchConfigs()
	{
		CheckPointManager instance = CheckPointManager.Instance;
		if (instance != null)
		{
			_switchConfigs = instance.GetFieldValue<SwitchResultConfig[]>("_switchList");
		}
		else if (_switchConfigs == null || _switchConfigs.Length == 0)
		{
			_switchConfigs = Resources.FindObjectsOfTypeAll<SwitchResultConfig>();
		}
		return _switchConfigs ?? new SwitchResultConfig[0];
	}

	/// <summary>剧情场景一建好就把两张表记住，之后在养成地图上也能写出条件。</summary>
	public static void RememberCheckpoints(CheckPointManager instance)
	{
		if (instance == null)
		{
			return;
		}
		_switchConfigs = instance.GetFieldValue<SwitchResultConfig[]>("_switchList");
		_conditionConfig = instance.GetFieldValue<ConditionResultConfig>("_condition");
		ConditionBook.Write(_switchConfigs, _conditionConfig);
	}

	/// <summary>条件写成文字，不带当前值（「小梅好感 需>=35」），给离开剧情场景后用。</summary>
	public static string ConditionTemplate(ConditionResultItem condition)
	{
		var list = new List<string>();
		foreach (StatCompareItem item in condition.GetFieldValue<List<StatCompareItem>>("_items"))
		{
			StatGroupVariable left = item.GetFieldValue<StatGroupVariable>("_value1");
			StatGroupVariable right = item.GetFieldValue<StatGroupVariable>("_value2");
			string op = GetCompareOperator(item.GetFieldValue<StatCompareType>("_compareOp"));
			string rhs = right != null && right.name.Contains("常") ? right.GetResult().ToString() : FormatConditionValName(right);
			list.Add(FormatConditionValName(left) + " 需" + op + rhs);
		}
		return string.Join(" " + GetLogicOperator(condition.GetFieldValue<LogicOperatorType>("_logicOp")) + " ", list.ToArray());
	}

	/// <summary>条件检查表，取法同 SwitchConfigs。</summary>
	private static ConditionResultConfig ConditionConfig()
	{
		CheckPointManager instance = CheckPointManager.Instance;
		if (instance != null)
		{
			_conditionConfig = instance.GetFieldValue<ConditionResultConfig>("_condition");
		}
		else if (_conditionConfig == null)
		{
			ConditionResultConfig[] found = Resources.FindObjectsOfTypeAll<ConditionResultConfig>();
			_conditionConfig = found.Length > 0 ? found[0] : null;
		}
		return _conditionConfig;
	}

	/// <summary>分支判断第 branch 支的条件文字（0 = 以上都不满足），找不到时返回空。</summary>
	public static string SwitchBranchCondition(string checkpoint, int branch)
	{
		foreach (SwitchResultConfig config in SwitchConfigs())
		{
			SwitchResultData data = config.GetData(checkpoint);
			if (data == null)
			{
				continue;
			}
			List<ConditionResultItem> items = data.GetFieldValue<List<ConditionResultItem>>("_items");
			if (branch == 0 || branch > items.Count)
			{
				return "以上条件都不满足";
			}
			return ExtractConditions(items[branch - 1], out var _);
		}
		// 不在剧情场景、表拿不到：用进剧情时记下的条件文字（不带当前值）
		if (ConditionBook.Switches.TryGetValue(checkpoint ?? "", out List<string> arms))
		{
			return branch == 0 || branch > arms.Count ? "以上条件都不满足" : arms[branch - 1];
		}
		return string.Empty;
	}

	/// <summary>Condition 检查点的条件文字（多组之间是「或」），找不到时返回空。</summary>
	public static string ParseConditionCheckpoint(string checkpoint)
	{
		ConditionResultConfig config = ConditionConfig();
		ConditionResultData data = config != null && config.List != null ? config.List.Find(x => x != null && x.name == checkpoint) : null;
		if (data == null)
		{
			return ConditionBook.Conditions.TryGetValue(checkpoint ?? "", out string known) ? known : string.Empty;
		}
		var parts = new List<string>();
		foreach (ConditionResultItem item in data.GetFieldValue<List<ConditionResultItem>>("_items"))
		{
			string text = ExtractConditions(item, out var _);
			if (!string.IsNullOrEmpty(text))
			{
				parts.Add(text);
			}
		}
		return string.Join(" 或 ", parts.ToArray());
	}

	public static string ParseSwitchCheckpoint(string checkpoint)
	{
		StringBuilder stringBuilder = new StringBuilder();
		SwitchResultConfig[] fieldValue = SwitchConfigs();
		SwitchResultData switchResultData = null;
		for (int i = 0; i < fieldValue.Length; i++)
		{
			switchResultData = fieldValue[i].GetData(checkpoint);
			if (switchResultData != null)
			{
				break;
			}
		}
		if (switchResultData == null)
		{
			return string.Empty;
		}
		foreach (ConditionResultItem item in switchResultData.GetFieldValue<List<ConditionResultItem>>("_items"))
		{
			stringBuilder.AppendLine("    " + ExtractConditions(item, out var _));
		}
		return stringBuilder.ToString();
	}

	public static Dictionary<MissionCheckData, (string, string)> GetMissions()
	{
		Dictionary<MissionCheckData, (string, string)> dictionary = new Dictionary<MissionCheckData, (string, string)>();
		MissionManagerData instance = MissionManagerData.Instance;
		SubMissionsData subMissionsData = ((instance != null) ? instance.GetFieldValue<SubMissionsData>("_subMissions") : null);
		if (subMissionsData == null)
		{
			return dictionary;
		}
		foreach (MissionData item2 in subMissionsData.List)
		{
			if (DescribeMission(item2, out MissionCheckData byKey, out string item, out string desc))
			{
				dictionary[byKey] = (item, desc);
			}
		}
		return dictionary;
	}

	/// <summary>主线当前这一步：标题、说明（位置、时间、条件），和支线同样的写法。没有可触发的（已完结、不在时间内等）返回 false。</summary>
	public static bool GetMainMission(out MissionCheckData check, out string title, out string desc)
	{
		check = null;
		title = desc = "";
		MissionData main = MissionManagerData.Instance?.Main;
		return main != null && DescribeMission(main, out check, out title, out desc);
	}

	private static bool DescribeMission(MissionData mission, out MissionCheckData byKey, out string item, out string desc)
	{
		byKey = null;
		item = desc = "";
		string fieldValue = mission.GetFieldValue<string>("_desc");
		fieldValue = (fieldValue.IsNullOrWhiteSpace() ? mission.Name : fieldValue);
		string currentKey = mission.CurrentKey;
		if (currentKey == "clear")
		{
			return false;
		}
		byKey = mission.GetByKey(currentKey);
		if (byKey == null || !IsValidMission(byKey))
		{
			return false;
		}
		string fieldValue2 = byKey.GetFieldValue<string>("_desc");
		fieldValue2 = (fieldValue2.IsNullOrWhiteSpace() ? byKey.name : fieldValue2);
		item = fieldValue + " - " + fieldValue2;
		string text = byKey.Position.ToString();
		string periodText;
		bool isInPeriod = GetMissionPeriod(byKey, out periodText);
		if (!isInPeriod)
		{
			return false;
		}
		List<string> list = ExtractConditions(byKey, out isInPeriod);
		if (!isInPeriod)
		{
			return false;
		}
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("位置：" + text);
		stringBuilder.AppendLine("时间: " + periodText);
		stringBuilder.AppendLine("条件:");
		foreach (string item3 in list)
		{
			stringBuilder.AppendLine("    " + item3);
		}
		desc = stringBuilder.ToString();
		return true;
	}

	private static bool IsValidMission(MissionCheckData checkData)
	{
		bool fieldValue = checkData.GetFieldValue<bool>("_dayCheck");
		bool flag = checkData.Position > PositionType.無;
		bool flag2 = checkData.GetFieldValue<ConditionResultItem[]>("_conditions").Length != 0;
		if (LoadLuaScript(checkData.StartScript) != string.Empty)
		{
			return fieldValue || flag || flag2;
		}
		return false;
	}

	private static List<string> ExtractConditions(MissionCheckData checkData, out bool isInPeriod)
	{
		List<string> list = new List<string>();
		ConditionResultItem[] fieldValue = checkData.GetFieldValue<ConditionResultItem[]>("_conditions");
		isInPeriod = true;
		ConditionResultItem[] array = fieldValue;
		foreach (ConditionResultItem condition in array)
		{
			list.Add(ExtractConditions(condition, out var isInPeriod2));
			if (!isInPeriod2)
			{
				isInPeriod = false;
			}
		}
		return list;
	}

	public static bool GetMissionPeriod(MissionCheckData checkData, out string periodText)
	{
		if (!checkData.GetFieldValue<bool>("_dayCheck"))
		{
			periodText = "不限";
			return true;
		}
		PlayerStatManagerData instance = PlayerStatManagerData.Instance;
		GameTime gameTime = ((instance != null) ? instance.GameTime : null);
		switch (checkData.GetFieldValue<TimeCheckType>("_timeCheckType"))
		{
		case TimeCheckType.特定:
		{
			GameTime fieldValue3 = checkData.GetFieldValue<GameTime>("_specificTime");
			periodText = LOMAssistant.Features.Story.StoryAtlas.DateText(fieldValue3);
			return gameTime == fieldValue3;
		}
		default:
			periodText = "不限";
			return true;
		case TimeCheckType.區間:
		{
			GameTime fieldValue = checkData.GetFieldValue<GameTime>("_minTime");
			GameTime fieldValue2 = checkData.GetFieldValue<GameTime>("_maxTime");
			periodText = LOMAssistant.Features.Story.StoryAtlas.DateText(fieldValue) + " ~ " + LOMAssistant.Features.Story.StoryAtlas.DateText(fieldValue2);
			if (gameTime >= fieldValue)
			{
				return gameTime <= fieldValue2;
			}
			return false;
		}
		}
	}

	public static bool TryParseEnum<TEnum>(string value, out TEnum result) where TEnum : struct
	{
		return EnumUtils.TryParseByStringValue<TEnum>(value, out result);
	}

	public static string GetGameStatLocalizedName(string statStringValue)
	{
		if (string.IsNullOrEmpty(statStringValue))
		{
			return string.Empty;
		}
		string text = LocalizationManager.Instance.LocaleResolver.GetString("PlayerStat/" + statStringValue);
		if (!text.IsNullOrWhiteSpace())
		{
			return text;
		}
		if (TryParseEnum<GameStatType>(statStringValue, out var result))
		{
			return TextConverter.ToSimplified(result.ToString());
		}
		return statStringValue;
	}

	public static string GetCharacterLocalizedName(string characterStringValue)
	{
		if (string.IsNullOrEmpty(characterStringValue))
		{
			return string.Empty;
		}
		string text = LocalizationManager.Instance.LocaleResolver.GetString("Character/" + characterStringValue);
		if (!text.IsNullOrWhiteSpace())
		{
			return text;
		}
		if (TryParseEnum<RelationshipStatType>(characterStringValue, out var result))
		{
			return TextConverter.ToSimplified(result.ToString());
		}
		return characterStringValue;
	}

	public static string GetItemName(GameItemType itemType, string id)
	{
		IGameItem item = ItemDatabase.Instance.GetItem(itemType, id);
		if (item == null)
		{
			return string.Empty;
		}
		return LocalizationManager.Instance.LocaleResolver.GetString(item.GetNameKey());
	}

	public static string GetTalentName(string id)
	{
		PlayerTalentData playerTalentData = PlayerStatManagerData.Instance.Talents.Get(id);
		if (playerTalentData == null)
		{
			return string.Empty;
		}
		return LocalizationManager.Instance.LocaleResolver.GetString(playerTalentData.GetIdKey());
	}

	public static string GetStoryName(string id)
	{
		string key = "LegendInfo/" + id;
		return LocalizationManager.Instance.LocaleResolver.GetString(key);
	}

	public static string GetTeamName(string id)
	{
		BattleTeamStat battleTeamStat = PlayerStatManagerData.Instance.EnemyTeam.Get(id);
		if (battleTeamStat == null)
		{
			return string.Empty;
		}
		return LocalizationManager.Instance.LocaleResolver.GetString(battleTeamStat.GetNameKey());
	}

	public static string GetEventName(PositionEventData eventData)
	{
		StoryMappingItem fieldValue = eventData.GetFieldValue<StoryMappingItem>("_config");
		string text = ((fieldValue != null) ? fieldValue.name : null);
		string fieldValue2 = eventData.GetFieldValue<string>("_devNote");
		if (!fieldValue2.IsNullOrWhiteSpace())
		{
			text = fieldValue2;
		}
		// In Korean, _devNote is a Chinese developer label rather than official localized text.
		// Use the stable event ID to look up a Korean display name when available.
		if (TextConverter.DisplayLanguage == UiText.Korean)
			text = EventNameKorean.Resolve(eventData, text);
		return RichText.Golden(TextConverter.ToSimplified(text));
	}

	public static string LoadLuaScript(string scriptName)
	{
		string text = scriptName.ToLower();
		TextAsset textAsset = Resources.Load<TextAsset>("story/chinesetraditional/" + text);
		return ((textAsset != null) ? textAsset.text : null) ?? string.Empty;
	}

	public static string GetCompareOperator(StatCompareType compareType)
	{
		return compareType switch
		{
			StatCompareType.Equal => "=", 
			StatCompareType.NotEqual => "!=", 
			StatCompareType.Less => "<", 
			StatCompareType.LessEqual => "<=", 
			StatCompareType.Greater => ">", 
			StatCompareType.GreaterEqual => ">=", 
			_ => "unknown", 
		};
	}

	public static string GetLogicOperator(LogicOperatorType logicOp)
	{
		return logicOp switch
		{
			LogicOperatorType.OR => "或", 
			LogicOperatorType.AND => "且", 
			_ => "unknown", 
		};
	}

	public static string GetFlagName(string id)
	{
		return GetFlagName(MissionManagerData.Instance.GetFlag(id));
	}

	public static string GetFlagName(FlagData flagData)
	{
		if (flagData == null)
		{
			return string.Empty;
		}
		// 备注有的是 Windows 换行（\r\n），第一行末尾会留着 \r：TextMeshPro 遇到 \r 回到行首，后面的字叠在前面的字上（骰子菜单的结果说明）
		string text = flagData.DevNote.Split('\n')[0].TrimEnd('\r');
		if (!string.IsNullOrEmpty(text))
		{
			return TextConverter.ToSimplified(text);
		}
		return string.Empty;
	}
}

/// <summary>
/// 条件文字本：游戏的条件表只挂在剧情场景上，养成地图上拿不到。进剧情场景时把所有分支判断、条件检查写成文字
/// （不带当前值）存到 Mod 文件夹，之后在地图上打开剧情回顾也能看懂条件。游戏版本变了就重写。
/// </summary>
public static class ConditionBook
{
	public static Dictionary<string, List<string>> Switches = new Dictionary<string, List<string>>();
	public static Dictionary<string, string> Conditions = new Dictionary<string, string>();
	/// <summary>决斗输了会死的关卡 → 生死簿条目（剧本里 ChangeScene("Combat", 关卡) 的关卡名）。只来自 Mod 自带的数据。</summary>
	public static Dictionary<string, string> CombatDeaths = new Dictionary<string, string>();
	/// <summary>决斗关卡 → 说明和回合条件（第一行是关卡说明，之后「第 8 回合 → 我方加入「瑞笙」」这类）。只来自 Mod 自带的数据。</summary>
	public static Dictionary<string, List<string>> Combats = new Dictionary<string, List<string>>();
	private static bool _written;
	private static string File => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(ConditionBook).Assembly.Location) ?? ".", "StoryReview", "conditions.json");

	private sealed class Book
	{
		public string Version = "";
		public Dictionary<string, List<string>> Switches = new Dictionary<string, List<string>>();
		public Dictionary<string, string> Conditions = new Dictionary<string, string>();
		public Dictionary<string, string> CombatDeaths = new Dictionary<string, string>();
		public Dictionary<string, List<string>> Combats;
	}

	static ConditionBook()
	{
		try
		{
			if (System.IO.File.Exists(File))
			{
				Book book = Newtonsoft.Json.JsonConvert.DeserializeObject<Book>(System.IO.File.ReadAllText(File, Encoding.UTF8));
				if (book != null && book.Version == Application.version)
				{
					Switches = book.Switches ?? Switches;
					Conditions = book.Conditions ?? Conditions;
					_written = true;
				}
			}
			// Mod 自带的一份（tools/make-conditions.py 从解包数据生成）：决斗死亡表总是用它；还没进过剧情场景时条件也先用它
			using (var stream = typeof(ConditionBook).Assembly.GetManifestResourceStream("LOMAssistant.conditions.json.gz"))
			using (var zip = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress))
			using (var reader = new System.IO.StreamReader(zip, Encoding.UTF8))
			{
				Book builtin = Newtonsoft.Json.JsonConvert.DeserializeObject<Book>(reader.ReadToEnd());
				CombatDeaths = builtin?.CombatDeaths ?? CombatDeaths;
				Combats = builtin?.Combats ?? Combats;
				if (Switches.Count == 0)
				{
					Switches = builtin?.Switches ?? Switches;
					Conditions = builtin?.Conditions ?? Conditions;
				}
			}
		}
		catch (Exception ex)
		{
			FileLogger.LogWarning("[条件文字] 读取失败: " + ex.Message);
		}
	}

	public static void Write(SwitchResultConfig[] switches, ConditionResultConfig conditions)
	{
		if (_written || switches == null)
		{
			return;
		}
		_written = true;
		try
		{
			var book = new Book { Version = Application.version };
			foreach (SwitchResultConfig config in switches)
			{
				foreach (SwitchResultData data in config?.GetFieldValue<List<SwitchResultData>>("_list") ?? new List<SwitchResultData>())
				{
					if (data == null || book.Switches.ContainsKey(data.name))
					{
						continue;
					}
					book.Switches[data.name] = (data.GetFieldValue<List<ConditionResultItem>>("_items") ?? new List<ConditionResultItem>())
						.Select(item => item == null ? "" : DataHelper.ConditionTemplate(item)).ToList();
				}
			}
			if (conditions != null && conditions.List != null)
			{
				foreach (ConditionResultData data in conditions.List)
				{
					if (data == null || book.Conditions.ContainsKey(data.name))
					{
						continue;
					}
					book.Conditions[data.name] = string.Join(" 或 ", (data.GetFieldValue<List<ConditionResultItem>>("_items") ?? new List<ConditionResultItem>())
						.Where(item => item != null).Select(DataHelper.ConditionTemplate).ToArray());
				}
			}
			Switches = book.Switches;
			Conditions = book.Conditions;
			System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(File));
			System.IO.File.WriteAllText(File, Newtonsoft.Json.JsonConvert.SerializeObject(book), new UTF8Encoding(false));
			FileLogger.Log($"[条件文字] 已记下 {Switches.Count} 个分支判断、{Conditions.Count} 个条件检查");
		}
		catch (Exception ex)
		{
			FileLogger.LogWarning("[条件文字] 生成失败: " + ex.Message);
		}
	}
}
