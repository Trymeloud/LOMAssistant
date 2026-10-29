using System.Collections;
using System.Collections.Generic;
using System.Linq;
using LOMAssistant.Config;

namespace LOMAssistant.Features.Ai;

/// <summary>
/// AI 攻略对话：玩家问一句，AI 自己决定查哪些资料（AiKnowledge 的工具），查完再回答。
/// 一个问题最多查 MaxRounds 轮（深度模式 30 轮），最后一轮不带工具、拿着查到的资料作答。
/// 答完以后只留问答本身，查资料的来回丢掉，下一个问题不用再为它们付 token。
/// </summary>
public static class AiChat
{
	public enum Kind { User, Assistant, Activity, Error, Proposal }

	public sealed class Entry
	{
		public Kind Kind;
		public string Text = "";
		/// <summary>女主角回答时的表情（立绘文件名）。</summary>
		public string Mood = "";
		/// <summary>修改提议：要改的项、玩家点了什么。</summary>
		public List<Change> Changes;
		public ProposalState State;
	}

	public enum ProposalState { Pending, Applied, Declined, Expired }

	/// <summary>提议里的一项：数值页的一行，当前值、新值，确认后执行 Apply。</summary>
	public sealed class Change
	{
		public string Name = "", From = "", To = "";
		[Newtonsoft.Json.JsonIgnore]
		public System.Action Apply;
	}

	/// <summary>现在用的人设：空 = 攻略助手，否则是女主角的角色编号（AiPersona.Id）。</summary>
	public static string PersonaId = AiPersona.AssistantId;

	/// <summary>这位现在用的对话（存在磁盘上，见 AiSessions）。</summary>
	public static AiSession Session => AiSessions.Current(PersonaId);

	private static AiSession Current => Session;

	// 查资料的轮数上限（每轮可以同时查好几处）：只防死循环，正常问题远用不到；深度模式放宽
	private static int MaxRounds => Deep ? 30 : 20;
	// 发给模型的问答历史按字数算：超过 HistoryChars 时，较早的部分压缩成一段摘要，最近 HistoryKeepChars 字原样留着；
	// 两次压缩之间前缀不变，缓存一直命中。一问里查资料最多要 8 万 token 左右，历史再加 3 万字，128K 窗口的模型也放得下
	private const int HistoryChars = 30000;
	private const int HistoryKeepChars = 12000;
	private const string SummaryHead = "（前面聊过的内容摘要）";
	private const int MaxToolChars = 12000;

	/// <summary>给玩家看的对话（含「正在查…」和出错提示）。</summary>
	public static List<Entry> Shown => Current.Shown;

	/// <summary>最近一次女主角回答时的表情，立绘用。</summary>
	public static string Mood => Current.Shown.LastOrDefault(e => e.Kind == Kind.Assistant)?.Mood ?? "";

	public static bool Busy { get; private set; }
	public static int TokensUsed { get; private set; }
	/// <summary>其中命中服务商缓存的输入 token（按缓存价计费，便宜得多）。</summary>
	public static int CachedUsed { get; private set; }
	/// <summary>正在写的回答（流式收到的部分），写完清空。</summary>
	public static string Partial { get; private set; } = "";
	private static bool _stop;

	public static LlmProvider Provider => LlmProvider.At(ConfigManager.AiProvider.Value);

	public static bool Deep => ConfigManager.AiDeepMode.Value;

	/// <summary>这次用的模型：深度模式且填了深度模型就用它。</summary>
	public static string Model => Deep && ConfigManager.AiDeepModel.Value.Trim().Length > 0 ? ConfigManager.AiDeepModel.Value.Trim() : ConfigManager.AiModel.Value;

	private static string DeepRule => Deep
		? "\n\n【深度模式】回答前把要说的每个结论对照查到的资料核对一遍：数字、条件、选项通向哪里、日期都要有资料依据；拿不准的再查一次，宁可多查。好感、数值的「最多」「合计」一定用 routes 算。"
		: "";

	public static bool Configured => ConfigManager.AiBaseUrl.Value.Trim().Length > 0 && ConfigManager.AiModel.Value.Trim().Length > 0
		&& (Provider.NoKey || ConfigManager.AiApiKey.Value.Trim().Length > 0);

	/// <summary>开一段新对话（旧的还在，可以切回去）。</summary>
	public static void NewSession()
	{
		if (!Busy)
		{
			AiSessions.New(PersonaId);
		}
	}

	/// <summary>删掉现在这段对话。</summary>
	public static void DeleteSession()
	{
		if (!Busy)
		{
			AiSessions.Delete(Current);
		}
	}

	public static void Stop()
	{
		_stop = true;
		LlmClient.Abort();
	}

	public static void Ask(string question)
	{
		question = (question ?? "").Trim();
		if (Busy || question.Length == 0)
		{
			return;
		}
		// 中途换了人设，这一问还是记在原来那位的对话里
		LOMAssistantPlugin.Instance.StartCoroutine(Run(question, Current, AiPersona.Find(PersonaId), null, true));
	}

	/// <summary>问一句：答完（或出错、停止）后 done 拿到回答和查了几处资料；save 为假时不存盘（测试题用）。</summary>
	internal static IEnumerator Run(string question, AiSession session, AiPersona persona, System.Action<string, int> done, bool save)
	{
		Busy = true;
		_stop = false;
		List<Entry> Shown = session.Shown;
		List<ChatMessage> History = session.History;
		Shown.Add(new Entry { Kind = Kind.User, Text = question });
		// 玩家上次点了确认或不改：接在这次问题前面告诉 AI
		string asked = session.Note.Length > 0 ? "（" + session.Note + "）\n" + question : question;
		session.Note = "";
		var turn = new List<ChatMessage> { new ChatMessage { Role = ChatMessage.User, Text = asked } };
		var proposals = new List<Entry>();
		LlmProvider provider = Provider;
		string answer = null;
		// 系统提示词这一问只算一次，每轮都一样（缓存靠它）；会变的现状接在这次问题前面
		string system = (persona != null ? persona.Prompt(LanguageRule, Spoiler) : SystemPrompt()) + DeepRule;
		string context = persona != null ? persona.Context() : "";
		var asking = new ChatMessage { Role = ChatMessage.User, Text = context.Length > 0 ? context + "\n\n" + asked : asked };
		// 同一个问题里一模一样的查询不再重查、不再把结果再发一遍
		var looked = new HashSet<string>();
		AiDebug.Turn(persona?.Name ?? "攻略助手", system, asking.Text);
		bool rewrite = false;
		for (int round = 0; round < MaxRounds + (rewrite ? 1 : 0) && !_stop; round++)
		{
			LlmReply reply = null;
			// 最后一轮（查满了，或者上一条回答被工具调用格式截断）：不带工具，把这一问查到的资料整理成一段文字交给它直接写。
			// 带着工具调用的历史再禁止调用，DeepSeek 常会把调用格式写进正文，回答只剩半截
			bool last = round >= MaxRounds - 1 || rewrite;
			// Claude 查满轮数时不换成摘要：原样带着这一问的查询记录和工具定义，只是不许再调用，前面的缓存全部接得上
			bool keep = last && !rewrite && provider.Claude;
			var send = keep
				? History.Concat(new[] { asking }).Concat(turn.Skip(1)).Concat(new[] { new ChatMessage { Role = ChatMessage.User, Text = "查资料的轮数用完了，不能再查。直接用上面查到的资料回答；没查到的就说没查到。" } }).ToList()
				: last
				? History.Concat(new[] { asking, new ChatMessage { Role = ChatMessage.User, Text = Digest(turn) } }).ToList()
				: History.Concat(new[] { asking }).Concat(turn.Skip(1)).ToList();
			Partial = "";
			yield return LlmClient.Complete(provider, ConfigManager.AiBaseUrl.Value, ConfigManager.AiApiKey.Value, Model,
				system, send, !last || keep, last ? (Deep ? 8000 : 6000) : Deep ? 4000 : 2000, r => reply = r, keep, text => Partial = Showable(text));
			Partial = "";
			if (_stop)
			{
				break;
			}
			if (reply == null || reply.Error != null)
			{
				Shown.Add(new Entry { Kind = Kind.Error, Text = reply?.Error ?? "没有回复" });
				FileLogger.LogWarning("[AI] " + (reply?.Error ?? "没有回复"));
				break;
			}
			TokensUsed += reply.InputTokens + reply.OutputTokens;
			CachedUsed += reply.CachedTokens;
			AiDebug.Reply(reply);
			if (reply.Calls.Count == 0)
			{
				// 回复是空的（Claude 查完资料有时直接结束这一轮，一个字不写）：不带工具、把查到的资料交给它再写一次
				if ((reply.Text ?? "").Trim().Length == 0)
				{
					FileLogger.LogWarning("[AI] 回复是空的（停止原因 " + (reply.StopReason.Length > 0 ? reply.StopReason : "未知") + "，第 " + (round + 1) + " 轮）"
						+ (rewrite ? "，重写也是空的" : "，让它根据查到的资料直接写"));
					if (!rewrite)
					{
						rewrite = true;
						continue;
					}
					answer = "（查了很多资料还没整理出答案，换个具体点的问法再问一次吧。）";
					break;
				}
				string cut = StripToolMarkup(reply.Text);
				// 模型把调用工具的格式写进了正文，从那里往后都被剪掉，回答只剩半截：让它不调工具、从头写一遍
				if (cut.Length < (reply.Text ?? "").Length && !rewrite)
				{
					FileLogger.Log("[AI] 回答里夹着工具调用格式，后半截被剪掉，让它根据查到的资料重写一遍");
					rewrite = true;
					continue;
				}
				answer = StripIds(cut, persona != null).Trim();
				if (answer.Length == 0 && reply.Text.Trim().Length > 0)
				{
					answer = "（查了很多资料还没整理出答案，换个具体点的问法再问一次吧。）";
				}
				break;
			}
			turn.Add(new ChatMessage { Role = ChatMessage.Assistant, Text = reply.Text, Calls = reply.Calls, Raw = reply.Raw, Reasoning = reply.Reasoning ?? "" });
			foreach (ToolCall call in reply.Calls)
			{
				Shown.Add(new Entry { Kind = Kind.Activity, Text = AiKnowledge.Activity(call.Name, call.Args) });
				string result;
				if (call.Name != "propose_change" && !looked.Add(call.Name + call.Args.ToString(Newtonsoft.Json.Formatting.None, new Newtonsoft.Json.JsonConverter[0])))
				{
					result = "（这个查询前面已经查过，结果就在上面，直接用）";
				}
				else
				{
					result = call.Name == "propose_change" ? Propose(call.Args, proposals) : AiKnowledge.Run(call.Name, call.Args);
				}
				if (result.Length > MaxToolChars)
				{
					result = result.Substring(0, MaxToolChars) + "\n……（太长，后面省略）";
				}
				AiDebug.Tool(call.Name, call.Args.ToString(Newtonsoft.Json.Formatting.None, new Newtonsoft.Json.JsonConverter[0]), result);
				turn.Add(new ChatMessage { Role = ChatMessage.Tool, CallId = call.Id, Text = result });
			}
		}
		if (_stop)
		{
			Shown.Add(new Entry { Kind = Kind.Error, Text = "已停止" });
		}
		else if (answer != null)
		{
			string mood = "";
			if (persona != null)
			{
				answer = TakeMood(answer, persona, out mood);
			}
			Shown.Add(new Entry { Kind = Kind.Assistant, Text = answer.Length > 0 ? answer : "（AI 没有说话）", Mood = mood });
			Shown.AddRange(proposals);
			History.Add(turn[0]);
			History.Add(new ChatMessage { Role = ChatMessage.Assistant, Text = answer });
			if (History.Sum(m => m.Text.Length) > HistoryChars)
			{
				yield return Compress(provider, History);
			}
		}
		if (save)
		{
			AiSessions.Save(session);
		}
		Busy = false;
		done?.Invoke(answer ?? Shown.LastOrDefault(e => e.Kind == Kind.Error)?.Text ?? "", Shown.Count(e => e.Kind == Kind.Activity));
	}

	// 问答历史太长：较早的部分（连同上次的摘要）请模型压成一段摘要放在最前面，最近的原样留着。
	// 摘要只记玩家说过的情况和打算、聊过什么、确认过哪些修改；回答里的数字和结论不当事实记，免得一错再错。压缩失败就直接丢掉较早的部分
	private static IEnumerator Compress(LlmProvider provider, List<ChatMessage> history)
	{
		int keep = history.Count, chars = 0;
		while (keep >= 2 && chars + history[keep - 1].Text.Length + history[keep - 2].Text.Length <= HistoryKeepChars)
		{
			chars += history[keep - 1].Text.Length + history[keep - 2].Text.Length;
			keep -= 2;
		}
		keep = System.Math.Min(keep, history.Count - 2);
		if (keep <= 0)
		{
			yield break;
		}
		var old = new System.Text.StringBuilder();
		for (int i = 0; i < keep; i++)
		{
			old.Append(history[i].Role == ChatMessage.User ? "玩家：" : "回答：").Append(history[i].Text).Append("\n\n");
		}
		string system = "下面是《活侠传》玩家和游戏 Mod 里 AI 的一段对话。把它压缩成摘要，给接着聊的 AI 当记忆用。\n"
			+ "- 只记：玩家说过的自己的情况、打算和偏好（走哪条线、想和谁结缘、不想知道什么）；聊过哪些话题；玩家确认或拒绝过哪些修改；说好的约定；还没解决的问题。\n"
			+ "- 回答里说过的数字、条件、剧情结论不要当事实记，最多写「聊过某某的条件」，之后要用时会重新查。\n"
			+ "- 开头已经有摘要的，把它和后面的内容合在一起重写成一份。\n"
			+ "- 分条写，不超过 800 字，用" + ReplyLanguage + "。只输出摘要。";
		LlmReply reply = null;
		yield return LlmClient.Complete(provider, ConfigManager.AiBaseUrl.Value, ConfigManager.AiApiKey.Value, ConfigManager.AiModel.Value,
			system, new List<ChatMessage> { new ChatMessage { Role = ChatMessage.User, Text = old.ToString() } }, false, 1500, r => reply = r);
		history.RemoveRange(0, keep);
		string summary = reply != null && reply.Error == null ? StripToolMarkup(reply.Text ?? "").Trim() : "";
		if (summary.Length == 0)
		{
			FileLogger.LogWarning("[AI] 压缩对话失败，较早的 " + keep + " 条直接丢掉：" + (reply?.Error ?? "没有回复"));
			yield break;
		}
		TokensUsed += reply.InputTokens + reply.OutputTokens;
		CachedUsed += reply.CachedTokens;
		AiDebug.Reply(reply);
		history.InsertRange(0, new[]
		{
			new ChatMessage { Role = ChatMessage.User, Text = SummaryHead + "\n" + summary },
			new ChatMessage { Role = ChatMessage.Assistant, Text = "（记下了）" },
		});
		FileLogger.Log("[AI] 对话太长，较早的 " + keep + " 条压缩成摘要（" + summary.Length + " 字）");
	}

	// AI 的修改提议：找到的项做成一张卡片（等回答说完再显示），找不到的告诉 AI
	private static string Propose(Newtonsoft.Json.Linq.JObject args, List<Entry> proposals)
	{
		var missing = new List<string>();
		List<Change> changes = LOMAssistant.Features.Story.StoryReviewPanel.AiResolveChanges(args?["changes"] as Newtonsoft.Json.Linq.JArray, missing);
		string reason = args?["reason"]?.ToString().Trim() ?? "";
		if (changes.Count > 0)
		{
			proposals.Add(new Entry { Kind = Kind.Proposal, Text = reason, Changes = changes });
		}
		var sb = new System.Text.StringBuilder();
		if (changes.Count > 0)
		{
			sb.Append("已经把修改卡片给玩家看了（").Append(string.Join("；", changes.Select(c => c.Name + " " + c.From + " → " + c.To).ToArray())).Append("），玩家点确认才会改。告诉玩家去确认，不要说已经改好了。");
		}
		if (missing.Count > 0)
		{
			sb.Append("这些没找到，没放进卡片：").Append(string.Join("、", missing.ToArray())).Append("。可以用数值页里的名字再试一次。");
		}
		return sb.Length > 0 ? sb.ToString() : "没有要改的项。";
	}

	// 有的模型不能调用工具时会把自己的调用格式当正文写出来（DeepSeek 的「<｜DSML｜function_calls>」、<invoke …>、<tool_call>），剪掉
	private static readonly System.Text.RegularExpressions.Regex ToolMarkup = new System.Text.RegularExpressions.Regex(
		@"<[｜|]?\s*DSML[\s\S]*$|<\s*/?\s*(?:function_calls|invoke|parameter|tool_call|tool_use)\b[\s\S]*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

	private static string StripToolMarkup(string text) => ToolMarkup.Replace(text ?? "", "");

	private const int MaxDigestChars = 40000;

	/// <summary>
	/// 这一问查到的资料拼成一段文字，给最后一轮不带工具直接作答用。按查的先后全放进去，太长时每条按比例截短：
	/// 只留最近查的会把一开始就查到的关键条件丢掉（最早查的往往最对题）。
	/// </summary>
	private static string Digest(List<ChatMessage> turn)
	{
		var results = turn.Where(m => m.Role == ChatMessage.Tool && m.Text.Length > 0).Select(m => m.Text).ToList();
		int total = results.Sum(r => r.Length);
		int each = total <= MaxDigestChars || results.Count == 0 ? int.MaxValue : System.Math.Max(600, MaxDigestChars / results.Count);
		var parts = results.Select(r => r.Length > each ? r.Substring(0, each) + "…" : r).ToList();
		if (parts.Count == 0)
		{
			return "（直接回答上面的问题，不用再查资料。）";
		}
		return "（下面是你为这个问题查到的资料。不能再查了，根据这些资料把完整的回答写出来；资料里没有的就说没查到，不要编。）\n\n"
			+ string.Join("\n\n———\n\n", parts.ToArray());
	}

	// 流式显示的半截回答：去掉工具调用格式和末尾的表情标记（可能只写了一半）
	private static string Showable(string text)
	{
		string t = StripToolMarkup(text);
		int tag = t.LastIndexOf('[');
		if (tag >= 0 && t.IndexOf(']', tag) < 0)
		{
			t = t.Substring(0, tag);
		}
		return MoodTag.Replace(t, "").Trim();
	}

	// 剧本编号（ch4_9_3_8、S2700_01_03）；女主角口中连任务编号、旗标编号（S2700_01、G11006）也不要
	private const string ScriptId = @"ch\d+(?:_[A-Za-z0-9]+)+|[A-Z]\d{4}(?:_[A-Za-z0-9]+){2,}";
	private const string AnyId = ScriptId + @"|[A-Z]{1,2}\d{4,}(?:_[A-Za-z0-9]+)*";
	private static readonly System.Text.RegularExpressions.Regex ScriptIds = IdPattern(ScriptId);
	private static readonly System.Text.RegularExpressions.Regex AnyIds = IdPattern(AnyId);

	private static System.Text.RegularExpressions.Regex IdPattern(string id) => new System.Text.RegularExpressions.Regex(
		@"[（(「]\s*(?:" + id + @")\s*[）)」]|[ \t]?(?<![A-Za-z0-9_])(?:" + id + @")(?![A-Za-z0-9_])[ \t]?");

	/// <summary>模型不听话写出来的编号，显示前去掉。</summary>
	private static string StripIds(string text, bool all) => (all ? AnyIds : ScriptIds).Replace(text, "");

	/// <summary>玩家点了卡片上的确认或不改。</summary>
	public static void Resolve(AiSession session, Entry proposal, bool apply)
	{
		if (proposal.State != ProposalState.Pending || proposal.Changes.Any(c => c.Apply == null))
		{
			return;
		}
		string items = string.Join("；", proposal.Changes.Select(c => c.Name + " " + c.From + " → " + c.To).ToArray());
		if (apply)
		{
			foreach (Change c in proposal.Changes)
			{
				try
				{
					c.Apply();
				}
				catch (System.Exception ex)
				{
					FileLogger.LogWarning("[AI] 修改 " + c.Name + " 失败: " + ex.Message);
				}
			}
			FileLogger.Log("[AI] 玩家确认修改：" + items);
		}
		proposal.State = apply ? ProposalState.Applied : ProposalState.Declined;
		session.Note = (apply ? "玩家确认了修改：" : "玩家没有同意修改：") + items;
		AiSessions.Save(session);
	}

	private static readonly System.Text.RegularExpressions.Regex MoodTag = new System.Text.RegularExpressions.Regex(@"\[\s*(?:表情|mood)\s*[:：]\s*([A-Za-z_0-9]+)\s*\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

	// 回复末尾的「[表情: shy]」拿出来，不显示；不在她能用的表情里就用默认
	private static string TakeMood(string answer, AiPersona persona, out string mood)
	{
		mood = persona.Moods[0];
		System.Text.RegularExpressions.Match m = MoodTag.Match(answer);
		if (m.Success)
		{
			string raw = m.Groups[1].Value.ToLowerInvariant();
			mood = persona.Moods.Contains(raw) ? raw : persona.Moods.FirstOrDefault(x => x.TrimEnd('1', '2', '3') == raw.TrimEnd('1', '2', '3')) ?? persona.Moods[0];
		}
		return MoodTag.Replace(answer, "").Trim();
	}

	/// <summary>试一下能不能连上：不带工具，问一句很短的话。</summary>
	public static IEnumerator Test(System.Action<string> done)
	{
		LlmReply reply = null;
		yield return LlmClient.Complete(Provider, ConfigManager.AiBaseUrl.Value, ConfigManager.AiApiKey.Value, ConfigManager.AiModel.Value,
			"用一句话回答。", new List<ChatMessage> { new ChatMessage { Text = "你好，请回复「连上了」。" } }, false, 30, r => reply = r);
		done(reply == null ? "没有回复" : reply.Error ?? "");
	}

	internal static string ReplyLanguage => TextConverter.DisplayLanguage switch
	{
		"ChineseTraditional" => "繁體中文（正體字）",
		UiText.Korean => "韩语（한국어）",
		UiText.Japanese => "日语（日本語）",
		UiText.English => "英语（English）",
		_ => "简体中文",
	};

	/// <summary>回答用的语言：跟玩家这一条消息走（界面是繁体、玩家用繁体问，也常被满篇简体的资料带成简体），看不出来时用界面语言。</summary>
	internal static string LanguageRule => "用玩家最近一条消息所用的语言和文字回答：玩家写繁體中文就全用繁體中文，写简体中文就用简体中文，写日语、韩语、英语就用那种语言；看不出来时用"
		+ ReplyLanguage + "。资料里的中文都是简体，引用对白、人名时也换成你回答用的文字。";

	internal static string Spoiler => ConfigManager.AiSpoiler.Value switch
	{
		1 => "只说玩家当前进度和已经经历过的剧情；问到后面的剧情时，只说「后面会有相关剧情」和要做的准备（数值、好感、选项），不讲具体发生了什么、谁会死、结局是什么，人物的真实身份和身世秘密也不说。",
		2 => "只给提示，不直接说结果：告诉玩家该往哪个方向努力（提高什么数值、关注哪个人、在哪类选项上谨慎），让玩家自己去发现剧情。人物的真实身份、身世秘密、幕后黑手、谁会死、结局内容一律不说，哪怕工具查到了；被问到就说这是要自己在剧情里发现的，只给怎么去发现的方向。",
		_ => "玩家想知道什么就说什么，可以剧透。",
	};

	private static string SystemPrompt()
	{
		return "你是武侠游戏《活侠传》的攻略助手，在游戏 Mod「咖啡伴侣」里回答玩家的问题。玩家操控的主角是赵活。\n\n"
			+ "怎么查资料\n"
			+ "- 剧情、条件、数值、结局一律以工具查到的游戏数据为准，不要凭印象编。查不到就说查不到，不要猜。\n"
			+ "- 前面的回答可能说错过：涉及数字、条件、剧情走向时，以这次查到的资料为准，不要照抄前面说过的。\n"
			+ "- 要资料就直接用工具查，不要先问玩家「要我查吗」。\n"
			+ "- 问某个人物是谁、什么来历、哪一门、父母师父是谁，先用 person 看人物志，一次就够，不要一处处去搜对白。\n"
			+ "- 问某个物品做什么用、哪里拿，某件装备、暗器、丹药怎么开发出来、为什么开发列表里没有，先用 item 查；开发项目要从别的项目练到几级才进化出来，看它的「怎么出现在开发列表里」。问四师兄（商店）卖什么、吃了加什么、什么时候有货，用 shop。问某门武功怎么练、修练条件、某个天赋有什么用，也用 item。问门派资产、门派规模、设施，用 sect。问某项数值（心相、性情、变心、门派规模等）或技能要多少、在哪加，用 usage。\n"
			+ "- 在某个场所修炼、工作、闲逛时随机抽到的事件（求仙问道、跑步邀请这类）用 events 找；决斗要撑几回合、第几回合谁来帮、输了会不会死，看 script 或 search 结果里的「战斗中的条件」。\n"
			+ "- 不知道编号时先 search，再用 quest 看任务、script 看剧本的分岔和对白、endings 看结局、heroine 看结缘路线。\n"
			+ "- 问「这个任务最多能加多少」「哪条路最好」「怎么走好感最高」时用 routes：它把每条完整的路和这条路上的合计算好了。分岔互斥，不要自己把几个剧本的变化加在一起。\n"
			+ "- 问「某个旗标在哪设」「要满足什么才触发」「某人好感在哪被判定」时用 usage。\n"
			+ "- 「目前时间」是从开局数的第几旬，资料里已经在括号里换成了日期。\n"
			+ "- 问题和玩家现在的进度有关（「我现在」「接下来」「来得及吗」）时，先用 player_state 看进度、数值和好感。\n"
			+ "- 玩家问推荐走哪条、选哪个时，看 script 里选项和去处后面的〔读过多少〕、「这一局走过」，quest 里的「玩家进度」：在玩家想要的结果都能达到的前提下，优先推荐没读过、这一局没走过的那条，并说明另一条已经读过；玩家明说要稳妥、要某个结果时以结果为准。\n"
			+ "- 说玩家还能不能碰到某段剧情之前，看相关旗标这一局的值（quest 列出触发条件里旗标的当前值，usage 也给当前值）：进的哪一门、任务进度计数、触发过没有。不要只按时间推断，也不要说读不到进度。\n"
			+ "- 玩家要你帮他改数值、好感、心上人、旗标时，用 propose_change 提出修改，玩家在卡片上点确认才会生效；改之前先用 player_state 看当前值。要改咖啡伴侣的设置时，先用 settings 查准名字和可选值。\n"
			+ "- 玩家问「这里怎么选」「这个选项」「这次掷骰」时，player_state 的【眼前】是屏幕上正在显示的选项或掷骰，再用 script 查那个剧本，按选项文字找到对应的分岔，结合玩家现在的数值说每个选项的结果和推荐。\n"
			+ "- 资料里「若 …需>=40」是条件，「掷骰「…」（>=50）」是骰子要掷到的点数，「变化」是这条路上的数值和旗标变化。\n"
			+ "- 剧本的对白很长时会分页，需要时再翻页，不要一次全翻。\n\n"
			+ "怎么回答\n"
			+ "- 先给结论，再分条说清楚条件、每条路的结果和依据；可以引用一两句原文对白。\n"
			+ "- 查到几条不同的途径（不同任务、不同时期、不同条件）都要列出来，按时间先后排，不要只挑其中一条讲。\n"
			+ "- 说「一定会」「没有办法」「只能」之前，先用 usage 查相关的旗标（名字里带「幸存」「复活」「在唐门」这类的）确认没有别的路。\n"
			+ "- 问「怎么才能…」「怎么换成…」时，先讲游戏里怎么做到（剧情、条件、选项）；能用 propose_change 直接改的，放在最后当另一个办法提一句，不要一上来就提修改。\n"
			+ "- 好感、数值、旗标的变化只说资料「变化」里写了的；资料里没写某人的好感变化，就是这段剧情不改她的好感，不要说「好感容易涨」「好感涨得最多」这类话。\n"
			+ "- 「准备」只说具体能做的（提高哪项数值到多少、先存档、用 Mod 的骰子自选），不说空泛的话。\n"
			+ "- 用游戏里的叫法（人名、数值名、任务名）。任务编号可以括注在任务名后面，方便玩家在剧情图里找；剧本编号（ch4_9_3_8、S2700_01_03 这类）不要写给玩家，用「之后」「接下来的剧情」代替；不要提工具、文件、数据这些词。\n"
			+ "- 简洁：一般不超过 400 字，玩家要细节时再展开。\n"
			+ "- 剧透：" + Spoiler + "\n"
			+ "- " + LanguageRule;
	}
}
