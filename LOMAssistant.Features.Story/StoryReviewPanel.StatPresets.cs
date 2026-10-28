using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using Mortal.Core;
using Newtonsoft.Json;
using UnityEngine;

namespace LOMAssistant.Features.Story;

/// <summary>
/// 数值页的「数值预设」：在数值页手动改过的项（加减、设定）一项项记下来，存成一个有名字的预设；
/// 换存档、开新档以后点「套用」，把预设里的每一项一次设成存的值。只记改过的项，没改的不动。
/// 预设存在配置目录的 LOMAssistant_StatPresets.json，和存档无关，哪一局都能用。
/// </summary>
public partial class StoryReviewPanel
{
	private sealed class StatPreset
	{
		public string Name = "";
		public List<StatPresetItem> Items = new List<StatPresetItem>();
	}

	private sealed class StatPresetItem
	{
		public string Id = "", Name = "";
		public int Value;
	}

	// 这次在数值页改过的项（按行的 Id），改同一项只留最后的值；存成预设或清除前一直留着，换存档也不清
	private static readonly List<StatPresetItem> StatTouched = new List<StatPresetItem>();
	private static List<StatPreset> _statPresets;

	private bool _statPresetsOpen;
	private Vector2 _statPresetsScroll;
	private string _statPresetName = "";
	private int _statPresetDelete = -1;
	private float _statPresetDeleteUntil;
	private string _statPresetResult;

	private static string StatPresetsPath => Path.Combine(Paths.ConfigPath, "LOMAssistant_StatPresets.json");

	private static List<StatPreset> StatPresets
	{
		get
		{
			if (_statPresets != null)
			{
				return _statPresets;
			}
			_statPresets = new List<StatPreset>();
			try
			{
				if (File.Exists(StatPresetsPath))
				{
					_statPresets = JsonConvert.DeserializeObject<List<StatPreset>>(File.ReadAllText(StatPresetsPath, Encoding.UTF8)) ?? new List<StatPreset>();
				}
			}
			catch (Exception e)
			{
				FileLogger.LogWarning("[数值预设] 读取失败: " + e.Message);
			}
			return _statPresets;
		}
	}

	private static void SaveStatPresets()
	{
		try
		{
			string tmp = StatPresetsPath + ".tmp";
			File.WriteAllText(tmp, JsonConvert.SerializeObject(StatPresets, Formatting.Indented), Encoding.UTF8);
			if (File.Exists(StatPresetsPath))
			{
				File.Delete(StatPresetsPath);
			}
			File.Move(tmp, StatPresetsPath);
		}
		catch (Exception e)
		{
			FileLogger.LogWarning("[数值预设] 保存失败: " + e.Message);
		}
	}

	/// <summary>数值页手动改了一项：记下改完以后的值。</summary>
	private static void TouchStat(StatRow row)
	{
		int value;
		try
		{
			value = row.Get();
		}
		catch
		{
			return;
		}
		StatTouched.RemoveAll(x => x.Id == row.Id);
		StatTouched.Add(new StatPresetItem { Id = row.Id, Name = row.Name, Value = value });
	}

	private static string ItemsText(IEnumerable<StatPresetItem> items) => string.Join("\n", items.Select(x => x.Name + "　" + x.Value));

	/// <summary>数值预设一整页：这次改过的项、存成预设，下面是存好的预设（套用、删除）。</summary>
	private void DrawStatPresets(Rect r)
	{
		if (GUI.Button(new Rect(r.x, r.y, S(110), S(36)), "返回", _button))
		{
			_statPresetsOpen = false;
			_statPresetResult = null;
			return;
		}
		GUI.Label(new Rect(r.x + S(126), r.y, r.width - S(126), S(36)), "数值预设", Middle(_heading));
		var area = new Rect(r.x, r.y + S(46), r.width, r.height - S(46));
		DragScroll("statpresets", area, ref _statPresetsScroll);
		GUILayout.BeginArea(area);
		_statPresetsScroll = GUILayout.BeginScrollView(_statPresetsScroll);
		GUILayout.Label("在数值页改过的项（加减、设定）会记在这里，存成预设以后，换存档或开新档时点「套用」，一次把这些项设成存的值。没改过的项不动。", _muted);
		if (_statPresetResult != null)
		{
			GUILayout.Label(_statPresetResult, _label);
		}

		GUILayout.Space(S(8));
		GUILayout.Label(StatTouched.Count == 0 ? "这次还没改过数值" : string.Format("这次改过的 {0} 项", StatTouched.Count), _heading);
		if (StatTouched.Count > 0)
		{
			GUILayout.Label(string.Join("　·　", StatTouched.Select(x => x.Name + " " + x.Value)), _muted);
			GUILayout.BeginHorizontal();
			GUILayout.Label("预设名字", Middle(_label), GUILayout.Width(S(100)), Ctl());
			_statPresetName = GUILayout.TextField(_statPresetName, 30, _field, GUILayout.Width(S(300)), Ctl());
			if (GUILayout.Button("存成预设", _primary, GUILayout.Width(S(120)), Ctl()))
			{
				string name = _statPresetName.Trim();
				if (name.Length == 0)
				{
					name = string.Format("预设 {0}", StatPresets.Count + 1);
				}
				// 同名的覆盖
				StatPresets.RemoveAll(p => p.Name == name);
				StatPresets.Add(new StatPreset { Name = name, Items = StatTouched.Select(x => new StatPresetItem { Id = x.Id, Name = x.Name, Value = x.Value }).ToList() });
				SaveStatPresets();
				FileLogger.Log("[数值预设] 存了「" + name + "」，" + StatTouched.Count + " 项");
				StatTouched.Clear();
				_statPresetName = "";
				_statPresetResult = string.Format("已存成预设「{0}」", name);
				GUIUtility.keyboardControl = 0;
			}
			if (GUILayout.Button("清除", _button, GUILayout.Width(S(80)), Ctl()))
			{
				StatTouched.Clear();
				_statPresetResult = null;
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
		}

		GUILayout.Space(S(12));
		GUILayout.Label(StatPresets.Count == 0 ? "还没有存好的预设" : "存好的预设", _heading);
		bool inGame = PlayerStatManagerData.Instance != null && StoryNodes.HasCurrentSave;
		for (int i = 0; i < StatPresets.Count; i++)
		{
			StatPreset preset = StatPresets[i];
			GUILayout.BeginHorizontal(_card);
			GUILayout.Label(Fit(preset.Name, Middle(_label), S(260)), Middle(_label), GUILayout.Width(S(260)), Ctl());
			GUILayout.Label(string.Format("{0} 项", preset.Items.Count), Middle(_muted), GUILayout.Width(S(80)), Ctl());
			Hover(GUILayoutUtility.GetLastRect(), ItemsText(preset.Items));
			GUILayout.Label(Fit(string.Join("　·　", preset.Items.Select(x => x.Name + " " + x.Value)), Middle(_muted), S(560)), Middle(_muted), GUILayout.Width(S(560)), Ctl());
			Hover(GUILayoutUtility.GetLastRect(), ItemsText(preset.Items));
			GUILayout.FlexibleSpace();
			bool wasEnabled = GUI.enabled;
			GUI.enabled = wasEnabled && inGame;
			if (GUILayout.Button("套用", _primary, GUILayout.Width(S(90)), Ctl()))
			{
				ApplyStatPreset(preset);
			}
			GUI.enabled = wasEnabled;
			// 删除点两下：第一下变成「确定删除？」，几秒内再点才删
			bool confirming = _statPresetDelete == i && Time.realtimeSinceStartup < _statPresetDeleteUntil;
			if (GUILayout.Button(confirming ? "确定删除？" : "删除", confirming ? _primary : _button, GUILayout.Width(S(130)), Ctl()))
			{
				if (confirming)
				{
					StatPresets.RemoveAt(i);
					SaveStatPresets();
					_statPresetDelete = -1;
					GUILayout.EndHorizontal();
					break;
				}
				_statPresetDelete = i;
				_statPresetDeleteUntil = Time.realtimeSinceStartup + 3f;
			}
			GUILayout.EndHorizontal();
		}
		if (!inGame && StatPresets.Count > 0)
		{
			GUILayout.Label("读档进入游戏后才能套用", _muted);
		}
		GUILayout.EndScrollView();
		GUILayout.EndArea();
	}

	/// <summary>把预设里的每一项设成存的值（和数值页手动设定一样，锁住的数值也照改）。找不到的项（这一局没有这个人、这本书）跳过。</summary>
	private void ApplyStatPreset(StatPreset preset)
	{
		PlayerStatManagerData player = PlayerStatManagerData.Instance;
		if (player == null)
		{
			return;
		}
		Dictionary<string, StatRow> rows = BuildStatSections(player).SelectMany(s => s.Item2).Where(x => x.Set != null)
			.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
		int done = 0;
		var missing = new List<string>();
		foreach (StatPresetItem item in preset.Items)
		{
			if (!rows.TryGetValue(item.Id, out StatRow row))
			{
				missing.Add(item.Name);
				continue;
			}
			try
			{
				SetManual(row, item.Value, record: false);
				done++;
			}
			catch (Exception e)
			{
				missing.Add(item.Name);
				FileLogger.LogWarning("[数值预设] 设 " + item.Id + " 失败: " + e.Message);
			}
		}
		// 物品、书籍改了以后功法那段要重新整理
		_statsDirty = true;
		_statInputs.Clear();
		FileLogger.Log("[数值预设] 套用「" + preset.Name + "」：改了 " + done + " 项" + (missing.Count > 0 ? "，跳过 " + string.Join("、", missing) : ""));
		_statPresetResult = string.Format("已套用「{0}」：改了 {1} 项", preset.Name, done) + (missing.Count > 0 ? "\n" + string.Format("这一局没有，跳过：{0}", string.Join("、", missing)) : "");
	}
}
