using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using LOMAssistant.Config;
using Newtonsoft.Json.Linq;

namespace LOMAssistant.Features.Ai;

/// <summary>
/// 测试题：BepInEx\config\LOMAssistant_AI\eval.json 里一组答案已知的问题，一题题问（每题一段新对话，不存盘），
/// 按关键词判对错，结果写进同一个文件夹的 eval_result.md。改了提示词、工具或换了模型以后跑一遍，看对了几题。
/// 题目格式：[{ "persona": "" 或女主角编号（girl5…）, "spoiler": 0/1/2, "question": "…",
///   "expect": [["甲", "乙"], ["丙"]]（每组至少出现一个）, "forbid": ["丁", "re:正则"]（一个都不许出现）, "note": "…" }]
/// </summary>
public static class AiEval
{
	public static string File => Path.Combine(AiSessions.Folder, "eval.json");
	public static string ReportPath => Path.Combine(AiSessions.Folder, "eval_result.md");
	private static string Report => ReportPath;
	public static bool HasReport => System.IO.File.Exists(ReportPath);

	public static bool Running { get; private set; }
	public static string Status = "";

	private sealed class Item
	{
		public string Persona = "", Question = "", Note = "";
		public int Spoiler;
		public List<string[]> Expect = new List<string[]>();
		public List<string> Forbid = new List<string>();
	}

	/// <summary>题目数；没有题目文件返回 0。</summary>
	public static int Count
	{
		get
		{
			try
			{
				return System.IO.File.Exists(File) ? Load().Count : 0;
			}
			catch
			{
				return 0;
			}
		}
	}

	// 手动解析，不靠 Json.NET 的反射建对象
	private static List<Item> Load()
	{
		var items = new List<Item>();
		foreach (JObject o in JArray.Parse(System.IO.File.ReadAllText(File, Encoding.UTF8)).OfType<JObject>())
		{
			var item = new Item
			{
				Persona = o["persona"]?.ToString() ?? "",
				Question = o["question"]?.ToString() ?? "",
				Note = o["note"]?.ToString() ?? "",
				Spoiler = o["spoiler"]?.Value<int>() ?? 0
			};
			foreach (JToken group in o["expect"] as JArray ?? new JArray())
			{
				item.Expect.Add(group is JArray any ? any.Select(x => x.ToString()).ToArray() : new[] { group.ToString() });
			}
			item.Forbid = (o["forbid"] as JArray ?? new JArray()).Select(x => x.ToString()).ToList();
			if (item.Question.Length > 0)
			{
				items.Add(item);
			}
		}
		return items;
	}

	public static void Start()
	{
		if (!Running && !AiChat.Busy)
		{
			LOMAssistantPlugin.Instance.StartCoroutine(Run());
		}
	}

	private static bool Hit(string answer, string pattern) => pattern.StartsWith("re:", StringComparison.Ordinal)
		? Regex.IsMatch(answer, pattern.Substring(3))
		: answer.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;

	private static IEnumerator Run()
	{
		List<Item> items;
		try
		{
			items = Load();
		}
		catch (Exception ex)
		{
			Status = "题目文件读不了：" + ex.Message;
			yield break;
		}
		Running = true;
		int spoiler = ConfigManager.AiSpoiler.Value;
		int passed = 0, tokens = AiChat.TokensUsed, cached = AiChat.CachedUsed;
		var report = new StringBuilder();
		var started = DateTime.Now;
		for (int i = 0; i < items.Count; i++)
		{
			Item item = items[i];
			Status = string.Format("正在跑测试题 {0}/{1}", i + 1, items.Count);
			ConfigManager.AiSpoiler.Value = Math.Max(0, Math.Min(2, item.Spoiler));
			AiPersona persona = item.Persona.Length > 0 ? AiPersona.Find(item.Persona) : null;
			string answer = null;
			int lookups = 0;
			var t0 = DateTime.Now;
			yield return AiChat.Run(item.Question, new AiSession { Persona = item.Persona }, persona, (a, n) => { answer = a; lookups = n; }, false);
			answer = answer ?? "";
			var missing = item.Expect.Where(g => !g.Any(x => Hit(answer, x))).Select(g => string.Join(" / ", g)).ToList();
			var forbidden = item.Forbid.Where(x => Hit(answer, x)).ToList();
			bool ok = answer.Length > 0 && missing.Count == 0 && forbidden.Count == 0;
			passed += ok ? 1 : 0;
			report.Append("\n## ").Append(ok ? "✓" : "✗").Append(' ').Append(i + 1).Append(". ").Append(item.Question).Append('\n');
			report.Append("- ").Append(persona != null ? persona.Name : "攻略助手").Append("，剧透 ").Append(item.Spoiler).Append("，查了 ").Append(lookups).Append(" 处，")
				.Append((DateTime.Now - t0).TotalSeconds.ToString("0")).Append(" 秒").Append(item.Note.Length > 0 ? "；" + item.Note : "").Append('\n');
			if (missing.Count > 0)
			{
				report.Append("- 缺少：").Append(string.Join("；", missing.ToArray())).Append('\n');
			}
			if (forbidden.Count > 0)
			{
				report.Append("- 不该有：").Append(string.Join("；", forbidden.ToArray())).Append('\n');
			}
			report.Append("\n> ").Append(answer.Replace("\n", "\n> ")).Append('\n');
		}
		ConfigManager.AiSpoiler.Value = spoiler;
		string head = "# AI 测试题结果\n\n" + started.ToString("yyyy-MM-dd HH:mm") + " · " + AiChat.Provider.Name + " · " + AiChat.Model + (AiChat.Deep ? "（深度模式）" : "")
			+ "\n\n**对了 " + passed + " / " + items.Count + " 题**，用了 " + (AiChat.TokensUsed - tokens) + " token（缓存命中 " + (AiChat.CachedUsed - cached) + "），"
			+ (DateTime.Now - started).TotalMinutes.ToString("0.0") + " 分钟\n";
		try
		{
			System.IO.File.WriteAllText(Report, head + report, Encoding.UTF8);
		}
		catch (Exception ex)
		{
			FileLogger.LogWarning("[AI] 写测试结果失败: " + ex.Message);
		}
		FileLogger.Log("[AI] 测试题：对了 " + passed + "/" + items.Count);
		Status = string.Format("测试题对了 {0}/{1}，结果在 eval_result.md", passed, items.Count);
		Running = false;
	}
}
