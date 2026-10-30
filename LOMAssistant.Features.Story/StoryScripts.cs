using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Mortal.Core;

namespace LOMAssistant.Features.Story;

/// <summary>剧本里一次分岔：选了哪个选项、掷出哪一档、满足分支判断的第几支。</summary>
public sealed class StoryStep
{
	public const string Option = "选项", Dice = "掷骰", Branch = "条件";

	public string Kind = "";
	/// <summary>选项、掷骰：选项 key；条件：分支判断名。</summary>
	public string Key = "";
	/// <summary>条件的第几支，0 = 以上都不满足。</summary>
	public int Arm;
	/// <summary>选项要满足的条件（Condition 检查点名），没有为空。</summary>
	public string Requirement = "";
	/// <summary>掷骰这一档的门槛，如「&lt;=20」。</summary>
	public string Range = "";
}

/// <summary>剧本的一个去处：走过哪些分岔，最后接到哪个剧本、回地图、推进主线或回标题。</summary>
public sealed class StoryExit
{
	public List<StoryStep> Path = new List<StoryStep>();
	public string Target = "";
	/// <summary>Target 是战斗（或换场景）结束后接着的剧本。</summary>
	public bool AfterScene;
	public bool ToMap, ToTitle;
	public string MainMission = "";
}

/// <summary>一个剧本读出来的大概：有没有选项、掷骰、战斗，有哪些去处，用了哪些对白。只读剧本文字，不执行。</summary>
public sealed class StoryScript
{
	public string Name = "";
	public bool Menu, Dice, Fight;
	public List<StoryExit> Exits = new List<StoryExit>();
	/// <summary>对白 key（GetStoryText），已读标记按它算。</summary>
	public List<string> Lines = new List<string>();
	/// <summary>说出来的对白（say），按剧本顺序：在哪条分岔路上、谁说的（角色编号，旁白为空）、对白 key。给 AI 按分岔读剧情用。</summary>
	public List<(List<StoryStep> Path, string Speaker, string Key)> Talk = new List<(List<StoryStep>, string, string)>();
	/// <summary>剧情记录 key（AddStory），这一局走没走过按它算。</summary>
	public List<string> Records = new List<string>();
	/// <summary>菜单里的选项（每个 choose 一组），收录节点时列出来。</summary>
	public List<List<StoryStep>> Menus = new List<List<StoryStep>>();
	/// <summary>每个菜单出现时所在的分岔（和 Menus 一一对应）：整组菜单放在某个条件分支里的（和她结缘才有的那组），不在那条路上就不会出现。</summary>
	public List<List<StoryStep>> MenuPaths = new List<List<StoryStep>>();
	/// <summary>结局（汗青书）、死亡（生死簿）的触发位置：在哪条分岔路上、哪个条目。</summary>
	public List<(List<StoryStep> Path, bool Death, string Id)> Endings = new List<(List<StoryStep>, bool, string)>();
	/// <summary>剧本里直接解锁的成就（风云史，AddAchievementLib）：在哪条分岔路上、哪个条目。</summary>
	public List<(List<StoryStep> Path, string Id)> Achievements = new List<(List<StoryStep>, string)>();

	/// <summary>打开结局 CG（讲经堂的 CG 图鉴）的地方。</summary>
	public List<(List<StoryStep> Path, string Id)> Cgs = new List<(List<StoryStep>, string)>();
	/// <summary>决斗（ChangeScene("Combat", 关卡)）：在哪条分岔路上、哪一关。输了会死的关卡见 ConditionBook.CombatDeaths。</summary>
	public List<(List<StoryStep> Path, string Level)> Fights = new List<(List<StoryStep>, string)>();
	/// <summary>数值变化（属性、好感、旗标、物品），记在发生时所在的分岔下。</summary>
	public List<(List<StoryStep> Path, string Text)> Effects = new List<(List<StoryStep>, string)>();
	// Analysis and reverse lookup continue to use canonical Effects above.
	// Display effects retain the action data so changing UI language cannot change analysis.
	[Newtonsoft.Json.JsonIgnore]
	public List<(List<StoryStep> Path, LOMAssistant.LuaParse.Action Action)> DisplayEffects = new List<(List<StoryStep>, LOMAssistant.LuaParse.Action)>();
	// Cache raw action inputs only. Action construction/localization must run on Unity's main thread.
	private List<(List<StoryStep> Path, LOMAssistant.LuaParse.ActionType Type, string Content)> _cachedDisplayEffects;
	[Newtonsoft.Json.JsonProperty(ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace)]
	public List<(List<StoryStep> Path, LOMAssistant.LuaParse.ActionType Type, string Content)> CachedDisplayEffects
	{
		get => _cachedDisplayEffects ?? DisplayEffects.Select(e => (e.Path, e.Action.Type, e.Action.Content)).ToList();
		set => _cachedDisplayEffects = value;
	}
	internal void RestoreDisplayEffects()
	{
		if (_cachedDisplayEffects == null) return;
		DisplayEffects = _cachedDisplayEffects.Select(e => (e.Path, new LOMAssistant.LuaParse.Action(e.Type, e.Content))).ToList();
		_cachedDisplayEffects = null;
	}
	/// <summary>改旗标、给收物品的原始调用（SetFlag、AddMisc……）和编号、数值，旗标、物品反查用。</summary>
	public List<(List<StoryStep> Path, string Method, string Id, string Value)> Modifies = new List<(List<StoryStep>, string, string, string)>();
	/// <summary>剧本里用到的条件检查点（分支判断 Switch、选项条件 Condition），旗标、物品反查用。</summary>
	public List<(List<StoryStep> Path, string Checkpoint, bool Switch)> Checks = new List<(List<StoryStep>, string, bool)>();

	/// <summary>
	/// 跳转补进度用：改状态的调用（旗标、剧情记录、主线支线任务、物品、好感、数值），记在发生时所在的分岔下，按剧本顺序。
	/// Args 是括号里的参数（字符串去掉引号）。
	/// </summary>
	public List<(List<StoryStep> Path, string Method, string[] Args)> Commands = new List<(List<StoryStep>, string, string[])>();

	/// <summary>走这个去处一路上会有的变化。</summary>
	public List<string> EffectsOn(StoryExit exit)
	{
		// 「旗标暂存」是游戏内部记数用的临时旗标，对玩家没意义，不显示
		if (NativeKorean.Enabled)
			return DisplayEffects.Where(e => StoryScripts.IsPrefix(e.Path, exit.Path) && !e.Action.CanonicalDescription.Contains("暂存"))
				.Select(e => RichText.Strip(e.Action.Description)).Where(t => t.Length > 0).Distinct().ToList();
		return CanonicalEffectsOn(exit);
	}

	// Numeric analysis must not parse language-dependent presentation text.
	internal List<string> CanonicalEffectsOn(StoryExit exit) => Effects
		.Where(e => StoryScripts.IsPrefix(e.Path, exit.Path) && !e.Text.Contains("暂存"))
		.Select(e => e.Text).Distinct().ToList();

	public bool Branches => Menu || Dice || Fight;
}

/// <summary>按名字读剧本（游戏 Resources 里的繁中原文），读过的结果缓存起来。</summary>
public static class StoryScripts
{
	private static readonly Dictionary<string, StoryScript> Outlines = new Dictionary<string, StoryScript>(StringComparer.OrdinalIgnoreCase);

	private static readonly Regex TableStart = new Regex(@"^local (\w+) = \{\}$");
	private static readonly Regex TableRow = new Regex(@"^(\w+)\[(\d+)\] = (?:(\w+) and )?""([^""]*)""");
	private static readonly Regex Checkpoint = new Regex(@"^local (\w+) = checkpointmanager\.(Switch|Condition)\(""([^""]+)""\)");
	private static readonly Regex Choose = new Regex(@"^local (\w+) = choose\((\w+)\)");
	private static readonly Regex Roll = new Regex(@"dicemenudialog\.ExecuteRoll\((\w+)");
	private static readonly Regex RollResult = new Regex(@"^local (\w+) = dicemenudialog\.ResultSelection");
	private static readonly Regex ArmTest = new Regex(@"^(if|elseif) (\w+) == (\d+) then$");
	private static readonly Regex Next = new Regex(@"luamanager\.(SetNextScript|SetCurrentStoryScript)\(""([^""]+)""\)");
	private static readonly Regex Scene = new Regex(@"luamanager\.ChangeScene\(""(\w+)""(?:, *""(\w*)"")?");
	private static readonly Regex EndPanel = new Regex(@"endgamepanel\.Open\(""(\w+)""\)");
	private static readonly Regex CgPanel = new Regex(@"cgpanel\.Open\(""(\w+)""\)");
	private static readonly Regex Achievement = new Regex(@"statmodifymanager\.AddAchievementLib\(""(\w+)""\)");
	private static readonly Regex MainSet = new Regex(@"statmodifymanager\.Mission\(""Main"", *""([^""]+)""");
	private static readonly Regex StoryText = new Regex(@"GetStoryText\(""([^""]+)""\)");
	private static readonly Regex SetCharacter = new Regex(@"^setcharacter\(characters\.Get\(""(\w+)""\)");
	private static readonly Regex AddStory = new Regex(@"AddStory\(""([^""]+)""\)");
	private static readonly Regex Modify = new Regex(@"statmodifymanager\.(?!GetDisplayTime|AddStory|Mission|IsTrigger|ResetTalkFlag)\w+\(.*?\)");
	private static readonly Regex Command = new Regex(@"statmodifymanager\.(SetFlag|AddFlag|AddStory|Mission|IsTriggerSubMission|AddBook|RemoveBook|AddMisc|RemoveMisc|AddSpecial|RemoveSpecial|Character|SetCharacter|Player|SetPlayer)\(([^()]*)\)");
	private static readonly Regex CommandArg = new Regex(@"""([^""]*)""|(-?\d+)");
	private static readonly Regex Tracked = new Regex(@"^statmodifymanager\.(SetFlag|AddFlag|AddBook|RemoveBook|AddMisc|RemoveMisc|AddSpecial|RemoveSpecial|Character)\(""(\w+)""(?:, *(-?\d+))?");

	/// <summary>读过的全部剧本（生成时间线时一口气读进来的）。</summary>
	public static IEnumerable<StoryScript> All => Outlines.Values.Where(s => s != null);

	/// <summary>剧本原文，没有这个剧本返回 null。</summary>
	public static string Text(string name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return null;
		}
		string text = DataHelper.LoadLuaScript(name);
		return text.Length > 0 ? text : null;
	}

	public static StoryScript Get(string name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return null;
		}
		if (Outlines.TryGetValue(name, out StoryScript cached))
		{
			return cached;
		}
		string text = Text(name);
		StoryScript script = text == null ? null : Parse(name, text);
		Outlines[name] = script;
		return script;
	}

	/// <summary>生成时间线时一口气读了全部剧本，读出来的大概先存着。</summary>
	public static void Remember(StoryScript script)
	{
		if (script != null)
		{
			script.RestoreDisplayEffects();
			Outlines[script.Name] = script;
		}
	}

	/// <summary>「exit+O_KEY+0+0」「O_KEY|>=50」「~O_KEY」都是同一个选项 key。</summary>
	public static string OptionKey(string raw, out string range)
	{
		range = "";
		string text = (raw ?? "").TrimStart('~');
		string[] plus = text.Split('+');
		if (plus.Length == 4)
		{
			text = plus[1];
		}
		int bar = text.IndexOf('|');
		if (bar >= 0)
		{
			range = text.Substring(bar + 1).Trim();
			text = text.Substring(0, bar);
		}
		return text == "x" ? "" : text.Trim();
	}

	private sealed class Block
	{
		public string Var = "";
		public int Arm;
		public bool Else;
	}

	public static StoryScript Parse(string name, string text)
	{
		var script = new StoryScript { Name = name };
		var tables = new Dictionary<string, Dictionary<int, StoryStep>>(StringComparer.Ordinal);
		var conditions = new Dictionary<string, string>(StringComparer.Ordinal);
		var switches = new Dictionary<string, string>(StringComparer.Ordinal);
		var choices = new Dictionary<string, (string Kind, Dictionary<int, StoryStep> Rows)>(StringComparer.Ordinal);
		var blocks = new List<Block>();
		string lastRoll = null;
		// 下一句 say 的说话人：前面最近一次 setcharacter，说完就清掉（没设的是旁白）
		string speaker = "";

		foreach (string raw in text.Split('\n'))
		{
			string line = raw.Trim();
			if (line.Length == 0 || line.StartsWith("--", StringComparison.Ordinal))
			{
				continue;
			}
			Match m;
			if ((m = TableStart.Match(line)).Success)
			{
				tables[m.Groups[1].Value] = new Dictionary<int, StoryStep>();
				continue;
			}
			if ((m = TableRow.Match(line)).Success && tables.TryGetValue(m.Groups[1].Value, out var rows))
			{
				string key = OptionKey(m.Groups[4].Value, out string range);
				string requirement = m.Groups[3].Success && conditions.TryGetValue(m.Groups[3].Value, out string c) ? c : "";
				rows[int.Parse(m.Groups[2].Value)] = new StoryStep { Key = key, Requirement = requirement, Range = range };
				continue;
			}
			if ((m = Checkpoint.Match(line)).Success)
			{
				(m.Groups[2].Value == "Switch" ? switches : conditions)[m.Groups[1].Value] = m.Groups[3].Value;
				script.Checks.Add((CurrentPath(blocks, choices, switches), m.Groups[3].Value, m.Groups[2].Value == "Switch"));
				continue;
			}
			if ((m = Choose.Match(line)).Success)
			{
				var options = tables.TryGetValue(m.Groups[2].Value, out var t) ? t : new Dictionary<int, StoryStep>();
				choices[m.Groups[1].Value] = (StoryStep.Option, options);
				script.Menu = true;
				script.MenuPaths.Add(CurrentPath(blocks, choices, switches));
				script.Menus.Add(options.OrderBy(p => p.Key).Select(p => p.Value).Where(s => s.Key.Length > 0)
					.Select(s => new StoryStep { Kind = StoryStep.Option, Key = s.Key, Requirement = s.Requirement, Range = s.Range }).ToList());
				continue;
			}
			if ((m = Roll.Match(line)).Success)
			{
				lastRoll = m.Groups[1].Value;
				script.Dice = true;
			}
			if ((m = RollResult.Match(line)).Success)
			{
				choices[m.Groups[1].Value] = (StoryStep.Dice, lastRoll != null && tables.TryGetValue(lastRoll, out var t) ? t : new Dictionary<int, StoryStep>());
				continue;
			}
			// 分支结构
			if ((m = ArmTest.Match(line)).Success)
			{
				if (m.Groups[1].Value == "if")
				{
					blocks.Add(new Block { Var = m.Groups[2].Value, Arm = int.Parse(m.Groups[3].Value) });
				}
				else if (blocks.Count > 0)
				{
					Block top = blocks[blocks.Count - 1];
					top.Arm = top.Var == m.Groups[2].Value ? int.Parse(m.Groups[3].Value) : 0;
				}
				continue;
			}
			if ((line.StartsWith("if ", StringComparison.Ordinal) && line.EndsWith(" then", StringComparison.Ordinal))
				|| ((line.StartsWith("for ", StringComparison.Ordinal) || line.StartsWith("while ", StringComparison.Ordinal)) && line.EndsWith(" do", StringComparison.Ordinal))
				|| line.StartsWith("function", StringComparison.Ordinal) || line.Contains("= function("))
			{
				blocks.Add(new Block());
				continue;
			}
			if (line.StartsWith("elseif ", StringComparison.Ordinal) && blocks.Count > 0)
			{
				blocks[blocks.Count - 1].Var = "";
				continue;
			}
			if (line == "else" && blocks.Count > 0)
			{
				blocks[blocks.Count - 1].Else = true;
				continue;
			}
			if (line == "end")
			{
				if (blocks.Count > 0)
				{
					blocks.RemoveAt(blocks.Count - 1);
				}
				continue;
			}

			if ((m = SetCharacter.Match(line)).Success)
			{
				speaker = m.Groups[1].Value;
			}
			foreach (Match t in StoryText.Matches(line))
			{
				script.Lines.Add(t.Groups[1].Value);
				if (line.StartsWith("say(", StringComparison.Ordinal))
				{
					script.Talk.Add((CurrentPath(blocks, choices, switches), speaker, t.Groups[1].Value));
					speaker = "";
				}
			}
			foreach (Match t in AddStory.Matches(line))
			{
				script.Records.Add(t.Groups[1].Value);
			}
			foreach (Match c in Command.Matches(line))
			{
				string[] args = CommandArg.Matches(c.Groups[2].Value).Cast<Match>().Select(x => x.Groups[1].Success ? x.Groups[1].Value : x.Groups[2].Value).ToArray();
				script.Commands.Add((CurrentPath(blocks, choices, switches), c.Groups[1].Value, args));
			}
			foreach (Match t in Modify.Matches(line))
			{
				Match tracked = Tracked.Match(t.Value);
				if (tracked.Success)
				{
					script.Modifies.Add((CurrentPath(blocks, choices, switches), tracked.Groups[1].Value, tracked.Groups[2].Value, tracked.Groups[3].Value));
				}
				var action = new LOMAssistant.LuaParse.Action(LOMAssistant.LuaParse.ActionType.ModifyAction, t.Value);
				string effect = action.CanonicalDescription;
				if (!string.IsNullOrEmpty(effect))
				{
					var path = CurrentPath(blocks, choices, switches);
					script.Effects.Add((path, TextConverter.ToSimplified(RichText.Strip(effect))));
					script.DisplayEffects.Add((path, action));
				}
			}
			if ((m = Next.Match(line)).Success)
			{
				AddExit(script, blocks, choices, switches, e => { e.Target = m.Groups[2].Value; e.AfterScene = m.Groups[1].Value == "SetCurrentStoryScript"; });
			}
			if ((m = Scene.Match(line)).Success)
			{
				string scene = m.Groups[1].Value;
				if (scene == "Combat" || scene == "Battle")
				{
					script.Fight = true;
					if (scene == "Combat" && m.Groups[2].Value.Length > 0)
					{
						script.Fights.Add((CurrentPath(blocks, choices, switches), m.Groups[2].Value));
					}
				}
				else if (scene == "Free")
				{
					AddExit(script, blocks, choices, switches, e => e.ToMap = true);
				}
				else if (scene == "Title")
				{
					AddExit(script, blocks, choices, switches, e => e.ToTitle = true);
				}
				else if ((scene == "GameOver" || scene == "End") && m.Groups[2].Value.Length > 0)
				{
					script.Endings.Add((CurrentPath(blocks, choices, switches), scene == "GameOver", m.Groups[2].Value));
				}
			}
			if ((m = EndPanel.Match(line)).Success)
			{
				script.Endings.Add((CurrentPath(blocks, choices, switches), false, m.Groups[1].Value));
			}
			if ((m = CgPanel.Match(line)).Success)
			{
				script.Cgs.Add((CurrentPath(blocks, choices, switches), m.Groups[1].Value));
			}
			if ((m = Achievement.Match(line)).Success)
			{
				script.Achievements.Add((CurrentPath(blocks, choices, switches), m.Groups[1].Value));
			}
			if ((m = MainSet.Match(line)).Success)
			{
				string mission = m.Groups[1].Value;
				AddExit(script, blocks, choices, switches, e => e.MainMission = mission);
			}
		}
		script.Lines = script.Lines.Distinct().ToList();
		return script;
	}

	/// <summary>按当前所在的分岔记一个去处；同一条路上的「接到剧本」和「推进主线」合成一条。</summary>
	private static void AddExit(StoryScript script, List<Block> blocks, Dictionary<string, (string Kind, Dictionary<int, StoryStep> Rows)> choices,
		Dictionary<string, string> switches, Action<StoryExit> fill)
	{
		List<StoryStep> path = CurrentPath(blocks, choices, switches);
		StoryExit same = script.Exits.Find(e => SamePath(e.Path, path));
		if (same == null)
		{
			same = new StoryExit { Path = path };
			script.Exits.Add(same);
		}
		else if (same.Target.Length > 0)
		{
			// 同一条路上再跳一次：前一个是战斗后接着的剧本，这次是马上要去的
			var copy = new StoryExit { Path = path };
			fill(copy);
			if (copy.Target.Length > 0 && copy.Target != same.Target)
			{
				script.Exits.Add(copy);
				return;
			}
		}
		fill(same);
	}

	/// <summary>当前所在的分岔（从外到里）。</summary>
	private static List<StoryStep> CurrentPath(List<Block> blocks, Dictionary<string, (string Kind, Dictionary<int, StoryStep> Rows)> choices, Dictionary<string, string> switches)
	{
		var path = new List<StoryStep>();
		foreach (Block block in blocks)
		{
			if (block.Var.Length == 0)
			{
				continue;
			}
			if (choices.TryGetValue(block.Var, out var choice))
			{
				if (!block.Else && choice.Rows.TryGetValue(block.Arm, out StoryStep row) && row.Key.Length > 0)
				{
					path.Add(new StoryStep { Kind = choice.Kind, Key = row.Key, Requirement = row.Requirement, Range = row.Range });
				}
			}
			else if (switches.TryGetValue(block.Var, out string name))
			{
				path.Add(new StoryStep { Kind = StoryStep.Branch, Key = name, Arm = block.Else ? 0 : block.Arm });
			}
		}
		return path;
	}

	internal static bool IsPrefix(List<StoryStep> prefix, List<StoryStep> path)
	{
		return prefix.Count <= path.Count && SamePath(prefix, path.Take(prefix.Count).ToList());
	}

	private static bool SamePath(List<StoryStep> a, List<StoryStep> b)
	{
		if (a.Count != b.Count)
		{
			return false;
		}
		for (int i = 0; i < a.Count; i++)
		{
			if (a[i].Kind != b[i].Kind || a[i].Key != b[i].Key || a[i].Arm != b[i].Arm)
			{
				return false;
			}
		}
		return true;
	}

	/// <summary>这些剧本的对白读过多少（全部周目）；没有对白返回 null。</summary>
	public static int? ReadPercent(IEnumerable<string> lines)
	{
		var keys = new HashSet<string>(lines, StringComparer.Ordinal);
		if (keys.Count == 0 || ReadStorySystem.Instance == null)
		{
			return null;
		}
		int read = keys.Count(k => ReadStorySystem.Instance.ExistKey(k));
		return (int)Math.Round(100.0 * read / keys.Count);
	}

	/// <summary>剧情记录（回想里那一行）的文字。</summary>
	public static string Legend(string key)
	{
		try
		{
			string text = LocalizationManager.Instance.LocaleResolver.GetString("LegendInfo/" + key);
			if (!string.IsNullOrWhiteSpace(text) && text != "LegendInfo/" + key)
			{
				return TextConverter.ToSimplified(Regex.Replace(text, "<[^>]+>", "").Replace("\n", " ").Trim());
			}
		}
		catch
		{
		}
		return "";
	}

	/// <summary>选项、掷骰结果的文字（游戏里显示的那句）。</summary>
	public static string OptionText(string key)
	{
		// 只要选项原文：不要选项提示接在后面的结果说明
		OptionInfoPatch.Raw = true;
		try
		{
			string text = LocalizationManager.Instance.LocaleResolver.GetStoryText(key);
			if (!string.IsNullOrWhiteSpace(text) && text != key)
			{
				return TextConverter.ToSimplified(Regex.Replace(text, "<[^>]+>", "").Replace("\n", " ").Trim());
			}
		}
		catch
		{
		}
		finally
		{
			OptionInfoPatch.Raw = false;
		}
		return key;
	}

	/// <summary>一步分岔写成一句话，条件带当前值。</summary>
	public static string Describe(StoryStep step)
	{
		bool korean = TextConverter.DisplayLanguage == UiText.Korean;
		switch (step.Kind)
		{
			case StoryStep.Option:
				string need = step.Requirement.Length > 0 ? DataHelper.ParseConditionCheckpoint(step.Requirement) : "";
				if (korean)
					return KoreanStoryText.Get(KoreanStoryMessage.SelectOption, OptionText(step.Key)) + (need.Length > 0 ? " (" + KoreanStoryText.Get(KoreanStoryMessage.Requires, TextConverter.ForDisplay(TextConverter.ToSimplified(need))) + ")" : "");
				return "选「" + OptionText(step.Key) + "」" + (need.Length > 0 ? "（需 " + TextConverter.ToSimplified(need) + "）" : "");
			case StoryStep.Dice:
				if (korean)
					return KoreanStoryText.Get(KoreanStoryMessage.DiceResult, OptionText(step.Key)) + (step.Range.Length > 0 ? " (" + step.Range + ")" : "");
				return "掷骰「" + OptionText(step.Key) + "」" + (step.Range.Length > 0 ? "（" + step.Range + "）" : "");
			default:
				if (step.Arm == 0)
					return Otherwise(step.Key, true);
				string arm = DataHelper.SwitchBranchCondition(step.Key, step.Arm);
				if (korean)
					return arm.Length > 0 ? KoreanStoryText.Get(KoreanStoryMessage.IfCondition, TextConverter.ForDisplay(TextConverter.ToSimplified(arm))) : KoreanStoryText.Get(KoreanStoryMessage.UnknownCondition);
				return arm.Length > 0 ? "若 " + TextConverter.ToSimplified(arm) : "条件不明";
		}
	}

	/// <summary>分支判断前面几支的条件（不带当前值），给「否则」用。</summary>
	public static List<string> ArmConditions(string checkpoint)
	{
		var arms = new List<string>();
		for (int i = 1; i <= 12; i++)
		{
			string text = RichText.Strip(DataHelper.SwitchBranchCondition(checkpoint, i)).Trim();
			if (text.Length == 0 || text == "以上条件都不满足")
			{
				break;
			}
			arms.Add(TextConverter.ToSimplified(KoreanText.CompactCondition(text)));
		}
		return arms;
	}

	/// <summary>分支判断的「以上都不满足」那一支：写出不满足的是哪些条件。</summary>
	public static string Otherwise(string checkpoint, bool full)
	{
		var arms = ArmConditions(checkpoint);
		bool korean = TextConverter.DisplayLanguage == UiText.Korean;
		if (arms.Count == 0)
			return korean ? KoreanStoryText.Get(KoreanStoryMessage.Otherwise) : "否则";
		if (korean)
		{
			arms = arms.Select(a => UiTextKorean.ConditionSummary(a, full)).ToList();
			return KoreanStoryText.Get(arms.Count == 1 ? KoreanStoryMessage.OtherwiseOne : KoreanStoryMessage.OtherwiseAll, string.Join("; ", arms));
		}
		if (!full)
			arms = arms.Select(a => a.Length > 18 ? a.Substring(0, 18) + "…" : a).ToList();
		return arms.Count == 1 ? "否则（不满足：" + arms[0] + "）" : "否则（以下都不满足：" + string.Join("；", arms.ToArray()) + "）";
	}

	/// <summary>一段剧情的名字：它第一条剧情记录（回想里那一行），没有就用剧本名。</summary>
	public static string SegmentName(string script)
	{
		StoryScript s = Get(script);
		string legend = s == null ? "" : s.Records.Select(Legend).FirstOrDefault(t => t.Length > 0) ?? "";
		if (legend.Length == 0) return script;
		string title = TextConverter.ForDisplay(legend);
		return title.Length > 16 ? title.Substring(0, 16) + "…" : title;
	}

	/// <summary>去处写成一句话：走过哪些分岔，接到哪段剧情、推进到哪个主线任务。</summary>
	public static string Describe(StoryExit exit)
	{
		var parts = exit.Path.Select(Describe).ToList();
		bool korean = TextConverter.DisplayLanguage == UiText.Korean;

		// The target and mission IDs remain untouched; only UI phrases change.
		// Do not compose a Chinese sentence first and expect the whole string
		// to match the Korean translation dictionary.
		string where;
		if (korean)
		{
			where = exit.ToTitle ? KoreanStoryText.Get(KoreanStoryMessage.ReturnTitle)
				: exit.ToMap ? KoreanStoryText.Get(KoreanStoryMessage.BackToMap)
				: exit.Target.Length > 0 ? KoreanStoryText.Get(exit.AfterScene ? KoreanStoryMessage.ContinueAfterBattle : KoreanStoryMessage.ContinueScript, TextConverter.ForDisplay(SegmentName(exit.Target)))
				: "";
		}
		else
		{
			where = exit.ToTitle ? "回到标题（结局）" : exit.ToMap ? "回到地图" : exit.Target.Length > 0 ? (exit.AfterScene ? "战斗之后接到「" : "接到「") + SegmentName(exit.Target) + "」" : "";
		}
		if (exit.MainMission.Length > 0)
		{
			StoryMission mission = StoryAtlas.Find(exit.MainMission);
			string title = mission != null ? mission.Title : exit.MainMission;
			if (korean)
			{
				where = (where.Length > 0 ? where + ", " : "") + KoreanStoryText.Get(KoreanStoryMessage.AdvanceMain, TextConverter.ForDisplay(title));
			}
			else
			{
				where = (where.Length > 0 ? where + "，" : "") + "主线推进到「" + title + "」";
			}
		}
		parts.Add(where.Length > 0 ? where : (korean ? KoreanStoryText.Get(KoreanStoryMessage.ContinueScene) : "（这一段里接着往下）"));
		return string.Join(" → ", parts.ToArray());
	}
}
