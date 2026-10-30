using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using LOMAssistant.Features.Story;
using LOMAssistant.LuaParse;
using Mortal.Core;
using Mortal.Story;
using UnityEngine;

namespace LOMAssistant;

/// <summary>
/// 游戏内自检：拿真实的游戏数据测剧情跳转、选项后果、剧本解析这些要游戏数据的功能。
/// 只有环境变量 LOMASSISTANT_SELFTEST 设了报告路径时才跑（tests/run-game-tests.sh 带着它启动游戏），平时不动。
/// 等剧本读完，逐项检查，结果写进报告（每项一行「通过 / 失败  名字：原因」），然后退出游戏。
/// </summary>
internal static class SelfTest
{
	private static string ReportPath => Environment.GetEnvironmentVariable("LOMASSISTANT_SELFTEST");

	public static bool Requested => !string.IsNullOrEmpty(ReportPath);

	private static readonly StringBuilder Report = new StringBuilder();
	private static int _passed, _failed;

	public static IEnumerator Run()
	{
		float start = Time.realtimeSinceStartup;
		// 等进到标题画面、游戏数据都载入了
		while (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Title" && Time.realtimeSinceStartup - start < 180f)
		{
			yield return null;
		}
		yield return new WaitForSecondsRealtime(2f);
		StoryAtlas.Start();
		while (!StoryAtlas.Ready && Time.realtimeSinceStartup - start < 300f)
		{
			StoryAtlas.Tick(true);
			yield return null;
		}
		Line("剧本读完用了 " + (Time.realtimeSinceStartup - start).ToString("0") + " 秒，" + StoryScripts.All.Count() + " 个剧本，" + StoryAtlas.Missions.Count + " 个任务");
		if (!StoryAtlas.Ready)
		{
			Fail("读取剧本", "5 分钟内没读完：" + StoryAtlas.Status);
		}
		else
		{
			foreach (var (name, test) in Tests)
			{
				Check(name, test);
				yield return null;
			}
			yield return LoadedSave();
		}
		Line("");
		Line($"{_passed} 项通过，{_failed} 项失败");
		try
		{
			File.WriteAllText(ReportPath, Report.ToString(), new UTF8Encoding(false));
		}
		catch (Exception e)
		{
			FileLogger.LogWarning("[自检] 报告写不进去：" + e.Message);
		}
		Application.Quit();
	}

	private static void Line(string text)
	{
		Report.AppendLine(text);
		FileLogger.Log("[自检] " + text);
	}

	private static void Fail(string name, string problem)
	{
		_failed++;
		Line("失败  " + name + "：" + problem.Replace("\n", "\n      "));
	}

	// test 返回 null 是通过，否则是问题
	private static void Check(string name, Func<string> test)
	{
		try
		{
			string problem = test();
			if (problem == null)
			{
				_passed++;
				Line("通过  " + name);
			}
			else
			{
				Fail(name, problem);
			}
		}
		catch (Exception e)
		{
			Fail(name, e.GetType().Name + "：" + e.Message + "\n" + e.StackTrace);
		}
	}

	// 列出问题：最多 15 条，后面写还有几条
	private static string Problems(List<string> list) => list.Count == 0 ? null
		: list.Count + " 处：\n" + string.Join("\n", list.Take(15).ToArray()) + (list.Count > 15 ? "\n……" : "");

	private static readonly (string, Func<string>)[] Tests =
	{
		("剧本：选项后果全部解析得了", OptionResultsParse),
		("剧情跳转：每个主线任务都能规划", JumpEveryMainMission),
		("剧情跳转：跳到最后一站要补的主线", JumpToLastMain),
		("剧情跳转：留学在会议里可选", StudyAbroadFork),
		("剧情跳转：太早跳过去不能顺便结缘", BondTooEarly),
		("分支判断：每个分支的条件都写得出来", SwitchConditions),
		("AI 查资料：每个工具都查得出东西", AiTools),
	};

	// 写给玩家看的文字里不该出现的：没填的占位符、空值、报错
	private static string Bad(string text) =>
		text.Contains("{0}") ? "{0}" : text.Contains("{1}") ? "{1}" : System.Text.RegularExpressions.Regex.IsMatch(text, @"(?<![A-Za-z])null(?![A-Za-z])") ? "null"
		: text.Contains("Exception") ? "Exception" : text.Contains("查询出错") ? "查询出错" : null;

	/// <summary>剧本里的分支判断（骰子大师、分支小窗、剧情图用）：每个分支的条件都能转成文字。</summary>
	private static string SwitchConditions()
	{
		var problems = new List<string>();
		int count = 0;
		var checks = StoryScripts.All.SelectMany(s => s.Checks.Where(c => c.Switch).Select(c => c.Checkpoint)).Distinct().ToList();
		foreach (string checkpoint in checks)
		{
			try
			{
				List<string> arms = StoryScripts.ArmConditions(checkpoint);
				for (int i = 1; i <= Math.Max(1, arms?.Count ?? 0); i++)
				{
					string text = DataHelper.SwitchBranchCondition(checkpoint, i) ?? "";
					count++;
					string bad = Bad(text);
					if (bad != null)
					{
						problems.Add(checkpoint + " 第 " + i + " 支：有「" + bad + "」：" + text);
					}
				}
			}
			catch (Exception e)
			{
				problems.Add(checkpoint + "：" + e.GetType().Name + " " + e.Message);
			}
		}
		Line("      " + checks.Count + " 个分支判断，" + count + " 条分支");
		return checks.Count < 100 ? "只找到 " + checks.Count + " 个分支判断" : Problems(problems);
	}

	/// <summary>AI 陪玩查资料的每个工具（只读）：拿常见的问法查一次，查得出东西，不报错。</summary>
	private static string AiTools()
	{
		StoryMission main = MainMissions[MainMissions.Count / 2];
		string menuScript = StoryScripts.All.FirstOrDefault(s => s.Menu && StoryAtlas.Owner.ContainsKey(s.Name))?.Name ?? main.StartScript;
		string routesKey = StoryAtlas.Owner.TryGetValue(menuScript, out StoryMission owner) ? owner.Key : main.Key;
		var queries = new (string Tool, Newtonsoft.Json.Linq.JObject Args)[]
		{
			("search", new Newtonsoft.Json.Linq.JObject { ["query"] = "丹霞子" }),
			("quest", new Newtonsoft.Json.Linq.JObject { ["key"] = main.Key }),
			("script", new Newtonsoft.Json.Linq.JObject { ["name"] = menuScript }),
			("endings", new Newtonsoft.Json.Linq.JObject { ["query"] = "" }),
			("endings", new Newtonsoft.Json.Linq.JObject { ["query"] = "二周目奖励" }),
			("heroine", new Newtonsoft.Json.Linq.JObject { ["name"] = "郁竹" }),
			("person", new Newtonsoft.Json.Linq.JObject { ["name"] = "郁竹" }),
			("item", new Newtonsoft.Json.Linq.JObject { ["name"] = "金创药" }),
			("shop", new Newtonsoft.Json.Linq.JObject()),
			("sect", new Newtonsoft.Json.Linq.JObject()),
			("events", new Newtonsoft.Json.Linq.JObject { ["query"] = "求仙问道" }),
			("usage", new Newtonsoft.Json.Linq.JObject { ["name"] = "心相" }),
			("routes", new Newtonsoft.Json.Linq.JObject { ["key"] = routesKey }),
			("settings", new Newtonsoft.Json.Linq.JObject { ["query"] = "极速" }),
			("player_state", new Newtonsoft.Json.Linq.JObject()),
		};
		var problems = new List<string>();
		foreach (var (tool, args) in queries)
		{
			string result = LOMAssistant.Features.Ai.AiKnowledge.Run(tool, args) ?? "";
			string bad = Bad(result);
			if (result.Trim().Length < 10)
			{
				problems.Add(tool + " " + args.ToString(Newtonsoft.Json.Formatting.None, new Newtonsoft.Json.JsonConverter[0]) + "：结果几乎是空的「" + result + "」");
			}
			else if (bad != null)
			{
				int at = Math.Max(0, result.IndexOf(bad, StringComparison.Ordinal) - 60);
				problems.Add(tool + " " + args.ToString(Newtonsoft.Json.Formatting.None, new Newtonsoft.Json.JsonConverter[0]) + "：有「" + bad + "」：…" + result.Substring(at, Math.Min(160, result.Length - at)).Replace("\n", " "));
			}
		}
		return Problems(problems);
	}

	/// <summary>每个剧本都让选项后果的解析跑一遍（打开选项时信息窗口用的就是它），不能抛异常。</summary>
	private static string OptionResultsParse()
	{
		var problems = new List<string>();
		int count = 0;
		foreach (StoryScript script in StoryScripts.All.ToList())
		{
			string text = StoryScripts.Text(script.Name);
			if (string.IsNullOrEmpty(text))
			{
				continue;
			}
			try
			{
				LuaParser.Instance.ExtractOptionActionsFromLua(script.Name, text);
				LuaParser.Instance.ExtractLockedOptions(script.Name, text);
				LuaParser.Instance.ExtractSwitchCheckFromLua(script.Name, text);
				count++;
			}
			catch (Exception e)
			{
				problems.Add(script.Name + "：" + e.GetType().Name + " " + e.Message);
			}
		}
		LuaParser.Instance.ClearCache();
		Line("      解析了 " + count + " 个剧本");
		return count < 1000 ? "只解析到 " + count + " 个剧本" : Problems(problems);
	}

	private static List<StoryMission> MainMissions => StoryAtlas.Missions.Where(m => m.Main && m.StartScript.Length > 0 && m.From != null).OrderBy(m => m.From).ToList();

	/// <summary>每个主线任务都能规划跳转：不抛异常，目标那一段从开头走得到，规划出日期。</summary>
	private static string JumpEveryMainMission()
	{
		var problems = new List<string>();
		foreach (StoryMission m in MainMissions)
		{
			try
			{
				StoryJump.Plan plan = StoryJump.Build(m, m.StartScript);
				if (plan.Unreachable)
				{
					problems.Add(m.Title + "：从任务开头走不到开始剧本");
				}
				if (plan.ToRounds == null)
				{
					problems.Add(m.Title + "：算不出日期");
				}
			}
			catch (Exception e)
			{
				problems.Add(m.Title + "：" + e.GetType().Name + " " + e.Message);
			}
		}
		return Problems(problems);
	}

	/// <summary>跳到最后一站：主线接得上，中间要补很多站，会改的东西里有剧情记录和旗标。</summary>
	private static string JumpToLastMain()
	{
		StoryMission last = MainMissions.Last();
		StoryJump.Plan plan = StoryJump.Build(last, last.StartScript);
		Line($"      跳到「{last.Title}」：中间 {plan.Between.Count} 站主线，{plan.Forks.Count(f => f.Matters)} 个要选的岔口，{plan.Commands.Count} 条改动，{plan.Records} 条剧情记录");		if (plan.Unlinked)
		{
			return "主线接不到最后一站";
		}
		if (plan.Between.Count < 5)
		{
			return "中间只补 " + plan.Between.Count + " 站主线";
		}
		if (plan.Records == 0 || !plan.Commands.Any(c => c.Method.Contains("Flag")))
		{
			return "一路上没有剧情记录或旗标改动";
		}
		return null;
	}

	/// <summary>跳到会议之后的主线：一路上的岔口里能选去崆峒还是青城留学。</summary>
	private static string StudyAbroadFork()
	{
		foreach (StoryMission target in MainMissions.AsEnumerable().Reverse())
		{
			StoryJump.Plan plan = StoryJump.Build(target, target.StartScript);
			// 会议上的岔口（键以「|会议」结尾）：开完会接到哪一站，其中要有去崆峒或青城留学的那一站
			var forks = plan.Forks.Where(f => f.Key.EndsWith("|会议") && f.Options.Any(o => o.Contains("崆峒") || o.Contains("青城") || o.Contains("留学"))).ToList();
			if (forks.Count == 0 && target == MainMissions.Last())
			{
				Line("      跳到最后一站时的会议岔口：" + string.Join("；", plan.Forks.Where(f => f.Key.EndsWith("|会议")).Select(f => string.Join(" / ", f.Options.ToArray())).ToArray()));
			}
			if (forks.Count > 0)
			{
				Line("      在「" + forks[0].Mission?.Title + "」可选：" + string.Join(" / ", forks[0].Options.ToArray()));
				return null;
			}
		}
		return "没有哪一站的岔口能选去崆峒还是青城";
	}

	/// <summary>跳到很早的主线时，结缘剧情还没到，每位女主都不能顺便结缘；跳到最后一站时至少有一位可以。</summary>
	private static string BondTooEarly()
	{
		StoryMission early = MainMissions[1];
		StoryJump.Plan early1 = StoryJump.Build(early, early.StartScript);
		var tooEarly = StoryRomance.All.Where(h => StoryJump.CanBond(early1, h, out _)).Select(StoryRomance.Name).ToList();
		StoryMission last = MainMissions.Last();
		StoryJump.Plan late = StoryJump.Build(last, last.StartScript);
		int lateOk = StoryRomance.All.Count(h => StoryJump.CanBond(late, h, out _));
		if (tooEarly.Count > 0)
		{
			// 写出她最后一步在哪些任务里设、那些任务的日期，方便查为什么没被时间挡住
			var detail = StoryRomance.All.Where(h => tooEarly.Contains(StoryRomance.Name(h))).Select(h =>
			{
				string last1 = h.Steps.Length > 0 ? h.Steps[h.Steps.Length - 1] : "（没有结缘旗标）";
				var stops = StoryRomance.StopsOf(h) ?? new List<StoryRomance.Stop>();
				var at = stops.Where(s => s.Steps.Contains(last1)).Select(s => s.Mission.Title + "（" + s.Mission.Key + "，" + (s.Mission.From?.ToString() ?? "无日期") + "）").ToList();
				return StoryRomance.Name(h) + " 最后一步 " + last1 + " 在：" + (at.Count > 0 ? string.Join("、", at.ToArray()) : "哪个任务都没找到");
			});
			return "跳到「" + early.Title + "」时竟然能结缘：" + string.Join("、", tooEarly.ToArray()) + "\n" + string.Join("\n", detail.ToArray());
		}
		// 瑞杏：游戏里没做她的结缘剧情（「攻略瑞杏」没有剧本设它），什么时候都不能选
		StoryRomance.Heroine ruixing = StoryRomance.All.FirstOrDefault(h => h.Relation == Mortal.Core.RelationshipStatType.瑞杏);
		if (ruixing != null && StoryJump.CanBond(late, ruixing, out _))
		{
			return "瑞杏没有结缘剧情，跳到最后一站时却能选她";
		}
		return lateOk == 0 ? "跳到最后一站时没有一位能结缘" : null;
	}

	// ---------------- 读档以后 ----------------
	// 拿一份第 2 年 1 月的手动存档（进度靠前，后面还有很多主线可跳）复制到空栏位（从 100 往下找第一个空的），读进去测；玩家原来的存档只读不写。
	// 跑完删掉复制出来的栏位，tests/run-game-tests.sh 再把整个存档文件夹还原成测之前的样子（读档、跳转会写随时存档、剧情记录这些）。

	private const int TestYear = 2, TestMonth = 1;

	private static string SlotName(int n) => n.ToString().PadLeft(3, '0');

	private static string SlotFile(string slot) => Traverse.Create(SaveSystem.Instance).Method("GetSaveDataFile", new[] { typeof(string) }).GetValue<string>(slot);

	private static string Scene => UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

	private static string DateNow => StoryAtlas.DateText(PlayerStatManagerData.Instance?.GameTime);

	// 等进到 scene、场景切换完，再多等几秒让剧本和界面就位；超时返回 false
	private static IEnumerator WaitScene(string scene, float seconds, bool[] arrived)
	{
		float start = Time.realtimeSinceStartup;
		while ((Scene != scene || SceneController.Instance == null || SceneController.Instance.IsLoading || SceneController.Instance.IsPrepare)
			&& Time.realtimeSinceStartup - start < seconds)
		{
			yield return null;
		}
		arrived[0] = Scene == scene;
		yield return new WaitForSecondsRealtime(5f);
	}

	private static bool Better(GameSave a, GameSave b) =>
		(a.CurrentScene == "Free") != (b.CurrentScene == "Free") ? a.CurrentScene == "Free" : a.TimeTick > b.TimeTick;

	private static IEnumerator LoadedSave()
	{
		// 挑存档：第 4 年 2 月的手动存档里，优先在养成地图上的，再挑最近存的
		string source = null;
		GameSave data = null;
		for (int i = 1; i <= 100; i++)
		{
			GameSave d = SaveSystem.Instance.GetSaveData(SlotName(i));
			if (d != null && d.CurrentYear == TestYear && d.CurrentMonth == TestMonth && (data == null || Better(d, data)))
			{
				source = SlotName(i);
				data = d;
			}
		}
		if (source == null)
		{
			Line($"跳过读档以后的测试：没有第 {TestYear} 年 {TestMonth} 月的手动存档");
			yield break;
		}
		string slot = null;
		for (int n = 100; n >= 1 && slot == null; n--)
		{
			string file = SlotFile(SlotName(n));
			if (!Directory.GetFiles(Path.GetDirectoryName(file), Path.GetFileName(file) + "*").Any())
			{
				slot = SlotName(n);
			}
		}
		if (slot == null)
		{
			Fail("读档", "100 个栏位都有存档，没地方放测试用的存档");
			yield break;
		}
		string copy = SlotFile(slot);
		File.Copy(SlotFile(source), copy, false);
		Line($"读档以后：把第 {source} 栏（{data.CurrentScene}）复制到第 {slot} 栏读进去测，测完删掉");
		var arrived = new bool[1];
		bool loaded = false;
		try
		{
			SaveSystem.Instance.SetSlot(slot);
			SoundManager.Instance?.StopMusic();
			SaveSystem.Instance.LoadGameData();
			MissionManagerData.Instance.UpdateCheckMissions();
			SceneController.Instance.LoadCurrentScene();
			loaded = true;
		}
		catch (Exception e)
		{
			Fail("读档", e.GetType().Name + "：" + e.Message + "\n" + e.StackTrace);
		}
		if (loaded)
		{
			yield return WaitScene(data.CurrentScene, 120f, arrived);
			string at = DateNow;
			Check("读档：进到存档里的场景和日期", () => !arrived[0] ? "2 分钟内没进到 " + data.CurrentScene + "，现在在 " + Scene
				: PlayerStatManagerData.Instance.GameTime.Year != TestYear || PlayerStatManagerData.Instance.GameTime.Month != TestMonth ? "日期是 " + at
				: !StoryNodes.HasCurrentSave ? "剧情回顾没认出这份存档" : null);
			if (arrived[0])
			{
				Check("AI 查资料：玩家进度是这份存档的", PlayerState);
				Check("剧情跳转：从存档出发只补现在以后的主线", JumpFromSave);
				yield return JumpAndReturn(at, data.CurrentScene);
			}
		}
		// 删掉复制出来的栏位（只删这次建的这一个）
		try
		{
			foreach (string f in Directory.GetFiles(Path.GetDirectoryName(copy), Path.GetFileName(copy) + "*"))
			{
				File.Delete(f);
			}
			Line("      删掉了第 " + slot + " 栏");
		}
		catch (Exception e)
		{
			Fail("删掉测试用的栏位", e.Message);
		}
	}

	/// <summary>AI 的 player_state：写的是读进来的日期、在哪、数值和好感。</summary>
	private static string PlayerState()
	{
		string text = LOMAssistant.Features.Ai.AiKnowledge.Run("player_state", new Newtonsoft.Json.Linq.JObject()) ?? "";
		string bad = Bad(text);
		return !text.Contains("游戏日期：" + DateNow) ? "日期不对：" + text.Split('\n')[0]
			: !text.Contains(Scene == "Free" ? "现在在：养成地图上" : "现在在：剧情里") ? "在哪写得不对：" + text.Split('\n')[1]
			: !text.Contains("角色数值：") || !text.Contains("好感：") ? "没写数值或好感"
			: bad != null ? "有「" + bad + "」" : null;
	}

	/// <summary>从这份存档规划跳到最后一站：从现在的主线这一站接过去，中间补的站都不早于这一站。</summary>
	private static string JumpFromSave()
	{
		StoryMission last = MainMissions.Last();
		StoryJump.Plan plan = StoryJump.Build(last, last.StartScript);
		Line($"      从存档跳到「{last.Title}」：从「{plan.MissionFrom}」接过去，中间 {plan.Between.Count} 站主线");
		if (plan.Current == null || plan.Unlinked)
		{
			return plan.Current == null ? "没认出现在的主线" : "主线从现在接不到最后一站";
		}
		var earlier = plan.Between.Where(m => m.From != null && plan.Current.From != null && m.From < plan.Current.From).Select(m => m.Title + "（" + m.When + "）").ToList();
		return plan.Between.Count == 0 || plan.Between[0] != plan.Current ? "中间的主线不是从现在这一站开始的"
			: earlier.Count > 0 ? "补了比现在早的主线：" + string.Join("、", earlier.ToArray()) : plan.FromRounds == null ? "没读到现在的日期" : null;
	}

	/// <summary>跳到后面的一站主线（隔几站，中间要补）：日期、主线进度改过去、进到剧情；再回到跳转前，日期和主线回到原样。</summary>
	private static IEnumerator JumpAndReturn(string before, string scene)
	{
		const string name = "剧情跳转：跳到后面的主线再回到跳转前";
		int now = PlayerStatManagerData.Instance.GameTime.ConvertToRounds();
		string mainBefore = MissionManagerData.Instance.Main?.CurrentKey ?? "";
		List<StoryMission> later = MainMissions.Where(m => m.From > now && m.Key != mainBefore).ToList();
		if (later.Count == 0)
		{
			Fail(name, "找不到比现在晚的主线（换一份进度靠前的存档）");
			yield break;
		}
		StoryMission next = later[Math.Min(3, later.Count - 1)];
		StoryJump.Plan plan = StoryJump.Build(next, next.StartScript);
		if (plan.Unlinked || plan.Between.Count == 0)
		{
			Fail(name, "主线从现在接不到「" + next.Title + "」，或中间一站都没补");
			yield break;
		}
		string message = null;
		bool jumped;
		try
		{
			jumped = StoryJump.Execute(plan, out message);
		}
		catch (Exception e)
		{
			Fail(name, "跳的时候 " + e.GetType().Name + "：" + e.Message + "\n" + e.StackTrace);
			yield break;
		}
		if (!jumped)
		{
			Fail(name, "跳不了：" + message);
			yield break;
		}
		var arrived = new bool[1];
		yield return WaitScene("Story", 120f, arrived);
		string after = DateNow, mainAfter = MissionManagerData.Instance.Main?.CurrentKey ?? "";
		Line($"      跳到「{next.Title}」（{next.Key}）：{before} → {after}，主线 {mainBefore} → {mainAfter}");
		if (!arrived[0])
		{
			Fail(name, "跳过去 2 分钟内没进到剧情，现在在 " + Scene);
			yield break;
		}
		int rounds = PlayerStatManagerData.Instance.GameTime.ConvertToRounds();
		if (rounds != Math.Max(1, next.From ?? 1) || mainAfter != next.Key)
		{
			Fail(name, $"跳过去以后日期或主线不对：应是第 {next.From} 旬、{next.Key}，实际第 {rounds} 旬、{mainAfter}");
			yield break;
		}
		StoryJump.Return();
		yield return WaitScene(scene, 120f, arrived);
		string back = DateNow, mainBack = MissionManagerData.Instance.Main?.CurrentKey ?? "";
		Line($"      回到跳转前：{back}，主线 {mainBack}");
		if (!arrived[0])
		{
			Fail(name, "回去 2 分钟内没回到 " + scene + "，现在在 " + Scene);
		}
		else if (back != before || mainBack != mainBefore)
		{
			Fail(name, "回去以后日期或主线没回到原样");
		}
		else
		{
			_passed++;
			Line("通过  " + name);
		}
	}
}
