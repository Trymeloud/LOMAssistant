using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using LOMAssistant.Features.Story;
using Mortal.Core;
using Newtonsoft.Json.Linq;

namespace LOMAssistant.Features.Ai;

/// <summary>
/// AI 查资料用的工具：都从 Mod 已经读好的剧情数据（StoryAtlas、StoryScripts）现场整理成文字，不另外打包知识库。
/// 剧情知识里不带玩家当前的数值（条件里的「→ 当前…」都去掉），玩家现在的进度、数值只从 player_state 拿。
/// 每个工具的结果有长度上限，太长的分页，免得一次塞给模型太多。
/// </summary>
public static class AiKnowledge
{
	private const int PageChars = 6000;

	/// <summary>给模型看的工具说明（名字、用途、参数），OpenAI 和 Claude 的格式由 LlmClient 各自包装。</summary>
	private static (string Name, string Description, JObject Parameters)[] _koreanTools;
	public static (string Name, string Description, JObject Parameters)[] Tools => NativeKorean.Enabled
		? (_koreanTools ??= KoreanAiTools.Localize(CanonicalTools)) : CanonicalTools;
	internal static readonly (string Name, string Description, JObject Parameters)[] CanonicalTools =
	{
		("search", "按关键词搜游戏剧情：任务名、任务说明和摘要、对白原文、结局和成就、可结缘的女主角。不知道编号时先用它。返回匹配的任务编号、剧本编号、结局编号和片段。",
			Schema(("query", "string", "关键词，如人名、任务名、地点、一句对白里的词。用简体中文"))),
		("quest", "看一个任务的详情：所属任务组、主线还是支线、发生时间、地点、触发条件、从哪些剧本接进来、之后接到哪些任务、包含的剧本和每个剧本的剧情摘要，以及玩家读过多少（全部周目）、这一局走过没有。",
			Schema(("key", "string", "任务编号，如 S2700_01（从 search 结果里拿）"))),
		("script", "看一个剧本的详情：游戏的剧情记录、选项、每条分岔路径上的数值和旗标变化、接到哪个剧本、结局和死亡、决斗；以及按分岔分开的对白原文。每个选项、去处标出玩家读过多少（全部周目）和这一局走过没有。对白多时分页。",
			Schema(("name", "string", "剧本编号，如 S2700_02_01"), ("page", "integer", "对白第几页，从 1 开始，默认 1"))),
		("endings", "按关键词查结局（汗青书）、死亡（生死簿）、成就、结局 CG：名称、说明，以及在哪个剧本、走哪条分岔能达成。也能按图鉴编号查，如「生死簿3」「结局4」。关键词带「奖励」「二周目」「命运」时，先列出图鉴奖励（收集多少条生死簿、汗青书、哪些成就，开二周目时加什么）。关键词留空则列出全部名称。",
			Schema(("query", "string", "关键词；传空字符串列出全部名称"))),
		("events", "查场所事件和随机事件：在某个场所修炼、工作、闲逛时随机抽到的事件（如求仙问道、跑步邀请、赤脚大仙），列出在哪个场所、事件名、抽中的权重和加减权重的条件、可能的结果、对应剧本（再用 script 看分岔）。任务以外的事件用它找，比搜对白快。",
			Schema(("query", "string", "事件名、场所名或事件里的一句对白，如 求仙问道、飞天殿、赤脚大仙"))),
		("item", "查物品和开发项目：物品（书、杂物、剧情物品、消耗品）的说明、这一局有几个、使用效果、哪些剧情给它判定它收走它、哪家店卖、哪个开发项目练满送它；"
			+ "铁铺、炼丹、制毒的开发项目（武器、暗器、陷阱、防具、丹药、毒）的门派规模要求、花费、各级点数、这一局开发到哪、从哪个项目练到几级进化出来、能进化成什么。也能查武学秘笈（类型、门派、每级要几点、修练条件、练成加减什么、每次修练消耗什么、这一局练到几级）和天赋（说明、最高几级、哪本秘笈或哪个开发项目练到几级给）。问某个物品做什么用、哪里拿、某件装备怎么开发出来、某门武功怎么练、某个天赋有什么用时先用它。",
			Schema(("name", "string", "物品、开发项目、秘笈或天赋的名字，如 睡丹、逆鳞宝衣、唐门小剑、南宫护心诀"))),
		("shop", "看四师兄的商店：现在架上有什么（价格、剩几个、杂物和消耗品吃了加什么），以及之后哪个月会进什么新货。问商店卖什么、买什么划算、某样东西什么时候有卖时用它。",
			new JObject { ["type"] = "object", ["properties"] = new JObject() }),
		("sect", "看门派经营：门派规模、门派资产、名声、人数、向心力，以及各设施（锻冶场、炼丹房等）现在几级、每旬收入、每级每旬加减哪些数值、追加项要什么旗标。问门派资产怎么来、为什么是负的、门派规模怎么升、设施有什么用时用它。",
			new JObject { ["type"] = "object", ["properties"] = new JObject() }),
		("person", "看一个人物的游戏人物志：称号、出身、门派、亲属、性格（游戏「人物」页的介绍）。问某人是谁、什么来历、哪一门、父母师父是谁时先用它，一次就够。",
			Schema(("name", "string", "人名或称呼，如 唐默铃、小梅、二师兄、火龙仙君"))),
		("heroine", "看一位可结缘女主角的结缘路线：要经过哪些任务、每一站的好感门槛和要设的结缘旗标、玩家现在做到哪一步。",
			Schema(("name", "string", "女主角的名字或称呼，如 小师妹、唐默铃、小竹、郁竹"))),
		("usage", "反查：一面旗标（名字或编号）、某人的好感、一项数值或技能（心相、性情、变心、门派规模、南宫护心诀……），在哪些剧情里被改、被判定（剧本、所属任务、走哪条分岔、改成多少或要多少），旗标还给出这一局现在的值。问「某个旗标在哪设」「某人好感在哪被判定」「心相要多少」「变心怎么攒」「要满足什么才能触发」时用。",
			Schema(("name", "string", "旗标的名字（说明里的词，如 小竹告白）或编号（如 G11006）；或人名（查她的好感在哪被判定）；或数值、技能名（如 心相、变心、南宫护心诀）"))),
		("routes", "整条路线算账：从一个任务的开头剧本走到底，列出每一条完整的路（要选什么、掷骰掷多少、满足什么条件），以及这一条路上加减的好感、数值合计和设的旗标、结局。分岔互斥，一次只走一条，所以「这个任务最多能加多少好感」「哪条路最好」一定要用它，不要把各剧本的变化自己加起来。",
			Schema(("key", "string", "任务编号（如 S2700_01）或剧本编号"), ("focus", "string", "关心的那一项，如 郁竹好感、心相；按它从多到少排，并给出最多、最少。不关心就传空字符串"))),
		("settings", "看咖啡伴侣（本 Mod）的设置项：名字、现在的值、可选值或范围、说明、改了要不要重启。玩家想改 Mod 的设置时先用它，再用 propose_change 提出修改。",
			Schema(("query", "string", "关键词，如 倍速、骰子、随时存档；传空字符串列出全部"))),
		("player_state", "看玩家现在的进度：游戏日期、主线和可触发的支线、正在进行的剧情、主要数值、所有人的好感、结缘对象。不含旗标：进的哪一门、任务进度计数、某段剧情触发过没有，用 usage 查旗标的当前值。回答和玩家当前进度有关的问题前先看一次。",
			new JObject { ["type"] = "object", ["properties"] = new JObject() }),
		("propose_change", "向玩家提议修改数值：会在对话里显示一张卡片，写着每项的当前值和新值，玩家点「确认」才会改，点「不改」就不改。"
			+ "能改的是 Mod 数值页里有的：各项数值（心相、银两、体力……）、好感（写成「好感·人名」）、心上人（新值可以写女主角的名字）、天赋、旗标（写编号或名字，如「旗标·小竹告白」，新值 1 是做到、0 是没做到）、书籍和物品数量等；"
			+ "也能改咖啡伴侣（本 Mod）的设置项（写成「设置·设置项名字」，名字和可选值先用 settings 查，开关写 开 / 关，几选一写选项原文）。"
			+ "调用后告诉玩家去确认，不要说已经改好了。"
			+ "数值的正常范围：体力、心相、性格、学识等大多是 0 到 100，好感 0 到 100，心上人是 0 到 8（女主角的编号）；玩家没明说要超过上限时，「拉满」就是改到 100，不要写 999 这类数。",
			new JObject
			{
				["type"] = "object",
				["properties"] = new JObject
				{
					["changes"] = new JObject
					{
						["type"] = "array",
						["description"] = "要改的项",
						["items"] = new JObject
						{
							["type"] = "object",
							["properties"] = new JObject
							{
								["name"] = new JObject { ["type"] = "string", ["description"] = "数值页里的名字，如 心相、银两、好感·虞小梅、心上人" },
								["value"] = new JObject { ["type"] = "string", ["description"] = "改成多少（整数）；心上人可以写女主角的名字" },
							},
							["required"] = new JArray("name", "value"),
						},
					},
					["reason"] = new JObject { ["type"] = "string", ["description"] = "一句话说明为什么改，会显示在卡片上" },
				},
				["required"] = new JArray("changes"),
			}),
	};

	private static JObject Schema(params (string Name, string Type, string Description)[] props)
	{
		var properties = new JObject();
		foreach (var (name, type, description) in props)
		{
			properties[name] = new JObject { ["type"] = type, ["description"] = description };
		}
		// 字符串参数都必填，页码可省
		return new JObject { ["type"] = "object", ["properties"] = properties, ["required"] = new JArray(props.Where(p => p.Type == "string").Select(p => p.Name)) };
	}

	/// <summary>执行一个工具调用，返回给模型的文字（出错也返回文字，让模型自己换个办法）。</summary>
	public static string Run(string name, JObject args)
	{
		try
		{
			if (!StoryAtlas.Ready)
			{
				return AiText.T("剧情数据还在读取（打开一次咖啡伴侣窗口的「路线图」会开始读），请稍后再问。", "스토리 자료를 읽는 중입니다. 모드 창의 진행 경로를 한 번 열어 읽기를 시작한 뒤 다시 질문해 주세요.");
			}
			string Arg(string key) => args?[key]?.ToString().Trim() ?? "";
			switch (name)
			{
				case "search": return Search(Arg("query"));
				case "quest": return Quest(Arg("key"));
				case "script": return Script(Arg("name"), int.TryParse(Arg("page"), out int p) ? p : 1);
				case "endings":
				{
					string q = Arg("query");
					// 「图鉴奖励」「二周目有什么奖励」「收集多少条加命运」
					bool awards = System.Text.RegularExpressions.Regex.IsMatch(TextConverter.ToSimplified(q), "奖励|周目|命运|新游戏|보상|회차|운명|새 게임");
					if (!awards)
					{
						return EndingList(q);
					}
					string rest = System.Text.RegularExpressions.Regex.Replace(TextConverter.ToSimplified(q), "奖励|二周目|周目|命运|新游戏\\+?|图鉴|收集|有什么|的|보상|[0-9]+회차|회차|운명|새 게임\\+?|도감|수집|무엇|어떤", "").Trim();
					return AiItems.LibraryAwards() + (rest.Length > 0 ? "\n" + EndingList(rest) : "");
				}
				case "heroine": return Heroine(Arg("name"));
				case "person": return Person(Arg("name"));
				case "item": return AiItems.Lookup(Arg("name"));
				case "shop": return AiItems.Shop();
				case "sect": return AiItems.Sect();
				case "events": return Events(Arg("query"));
				case "player_state": return StoryReviewPanel.AiPlayerState();
				case "settings": return StoryReviewPanel.AiSettingsText(Arg("query"));
				case "usage": return AiAnalysis.Usage(Arg("name"));
				case "routes": return AiAnalysis.Routes(Arg("key"), Arg("focus"));
				default: return AiText.T("没有这个工具：", "지원하지 않는 도구: ") + name;
			}
		}
		catch (Exception ex)
		{
			FileLogger.LogWarning("[AI] 工具 " + name + " 出错: " + ex);
			return AiText.T("查询出错：", "조회 오류: ") + ex.Message;
		}
	}

	/// <summary>给玩家看的一行说明：AI 正在查什么。</summary>
	public static string Activity(string name, JObject args)
	{
		string Arg(string key) => args?[key]?.ToString().Trim() ?? "";
		switch (name)
		{
			case "search": return AiText.T("搜索「", "검색 「") + Arg("query") + "」";
			case "quest": return AiText.T("查看任务 ", "임무 확인: ") + Arg("key") + (StoryAtlas.Find(Arg("key")) is StoryMission m ? "「" + Clean(m.Title) + "」" : "");
			case "script": return AiText.T("查看剧本 ", "스크립트 확인: ") + Arg("name");
			case "endings": return AiText.T("查结局「", "결말 검색 「") + Arg("query") + "」";
			case "heroine": return AiText.T("查看「", "「") + Arg("name") + AiText.T("」的结缘路线", "」 인연 경로 확인");
			case "person": return AiText.T("查看「", "「") + Arg("name") + AiText.T("」的人物志", "」 인물지 확인");
			case "item": return AiText.T("查物品「", "물품 검색 「") + Arg("name") + "」";
			case "shop": return AiText.T("查看商店", "상점 확인");
			case "sect": return AiText.T("查看门派经营", "문파 운영 현황 확인");
			case "events": return AiText.T("查场所事件「", "장소 이벤트 검색 「") + Arg("query") + "」";
			case "player_state": return AiText.T("查看当前进度", "현재 진행도 확인");
			case "settings": return AiText.T("查看设置项", "설정 항목 확인") + (Arg("query").Length > 0 ? "「" + Arg("query") + "」" : "");
			case "usage": return AiText.T("反查「", "사용·변경 지점 검색 「") + Arg("name") + "」";
			case "routes": return AiText.T("算任务 ", "임무 ") + Arg("key") + AiText.T(" 每条路线", " 경로별 결과 계산");
			case "propose_change": return AiText.T("提议修改数值", "수치 변경 제안");
			default: return name;
		}
	}

	private static readonly Regex Current = new Regex(@" ?→ ?(?:当前[^ ，；、。)）\n]*|현재\s*[+-]?\d+)");

	/// <summary>去掉富文本标记、条件里的当前值，转成简体。</summary>
	internal static string CanonicalClean(string text) => AiAnalysis.ExplainTime(Current.Replace(TextConverter.ToSimplified(RichText.Strip(text ?? "")), "").Trim());
	internal static string Clean(string text) => AiText.Label(CanonicalClean(text));

	private static string PathText(List<StoryStep> path) => path.Count == 0 ? AiText.T("（开头，不经分岔）", "(시작 부분, 분기 없음)") : string.Join(" → ", path.Select(s => Clean(StoryScripts.Describe(s))).ToArray());

	private static string Speaker(string id)
	{
		if (id.Length == 0)
		{
			return AiText.T("旁白", "해설");
		}
		try
		{
			string text = LocalizationManager.Instance.LocaleResolver.GetString("Character/" + id);
			return string.IsNullOrWhiteSpace(text) || text.StartsWith("Character/") ? id : TextConverter.ToSimplified(text);
		}
		catch
		{
			return id;
		}
	}

	private static List<string> _people;

	/// <summary>剧本里说过话的人物编号（girl5_3 这类变体并到 girl5）。</summary>
	private static List<string> People()
	{
		if (_people == null || _people.Count == 0)
		{
			_people = StoryScripts.All.SelectMany(s => s.Talk.Select(t => t.Speaker)).Where(id => id.Length > 0)
				.Select(id => Regex.Replace(id, @"_\d+$", "")).Distinct().ToList();
		}
		return _people;
	}

	private static string Loc(string key)
	{
		try
		{
			string text = LocalizationManager.Instance.LocaleResolver.GetString(key);
			return string.IsNullOrWhiteSpace(text) || text == key ? "" : Clean(text);
		}
		catch
		{
			return "";
		}
	}

	// 人物志：剧透选全知时给全部段落；另外两档只给第 0、1 段（外貌和公开身份），身世和真相不给
	private static string Person(string name)
	{
		string q = TextConverter.ToSimplified(name ?? "").Trim();
		if (q.Length == 0)
		{
			return AiText.T("请给人名。", "인물 이름을 입력해 주세요.");
		}
		var heroine = FindHeroine(q);
		var ids = People().Where(id => Speaker(id) == q).ToList();
		if (ids.Count == 0 && heroine != null)
		{
			ids = People().Where(id => Speaker(id) == StoryRomance.Name(heroine)).ToList();
		}
		if (ids.Count == 0)
		{
			ids = People().Where(id => Speaker(id).Contains(q) || (Loc("CharacterTitle/" + id)).Contains(q)).Take(3).ToList();
		}
		ids = ids.Where(id => Loc("CharacterIntro0/" + id).Length > 0 || Loc("CharacterIntro1/" + id).Length > 0).ToList();
		if (ids.Count == 0)
		{
			return AiText.T("没有「", "「") + name + AiText.T("」的人物志。可以用 search 搜这个名字。", "」 인물지가 없습니다. search로 이름을 검색해 보세요.");
		}
		bool all = LOMAssistant.Config.ConfigManager.AiSpoiler.Value == 0;
		var sb = new StringBuilder();
		foreach (string id in ids.Take(2))
		{
			string title = Loc("CharacterTitle/" + id);
			sb.Append("【").Append(Speaker(id)).Append(title.Length > 0 && title != "{title}" ? "（" + title + "）" : "").Append("】\n");
			for (int i = 0; i <= (all ? 4 : 1); i++)
			{
				string intro = Loc("CharacterIntro" + i + "/" + id);
				if (intro.Length > 0)
				{
					sb.Append(intro).Append('\n');
				}
			}
			if (!all)
			{
				sb.Append(AiText.T("（身世和真相的段落按剧透设置不给。）\n", "(스포일러 설정에 따라 숨겨진 과거와 진상은 제공하지 않습니다.)\n"));
			}
		}
		return sb.ToString().Trim();
	}

	/// <summary>决斗关卡的说明和回合条件（谁在第几回合加入、第几回合结束、输了会不会死）。</summary>
	internal static string CombatText(string level, string indent)
	{
		if (!ConditionBook.Combats.TryGetValue(level ?? "", out List<string> lines) || lines.Count == 0)
		{
			return "";
		}
		var sb = new StringBuilder();
		sb.Append(indent).Append(AiText.T("关卡说明：", "전투 설명: ")).Append(Clean(lines[0])).Append('\n');
		if (lines.Count > 1)
		{
			sb.Append(indent).Append(AiText.T("战斗中的条件（「计数」是这场战斗自己记的数，如嘴攻唤醒的次数；没写判赢判输的「战斗结束」按当时的结果走剧情）：\n", "전투 중 조건('횟수'는 해당 전투의 자체 기록으로 구공으로 깨운 횟수 등이 해당합니다. 승패 판정이 없는 '전투 종료'는 그때의 결과에 따라 이어집니다):\n"));
			foreach (string line in lines.Skip(1))
			{
				sb.Append(indent).Append("- ").Append(Clean(line)).Append('\n');
			}
		}
		return sb.ToString();
	}

	private const int MaxEvents = 8;

	// 场所事件：按事件名、场所名、事件剧本里的对白找
	private static string Events(string query)
	{
		string q = TextConverter.ToSimplified(query ?? "").Trim();
		if (q.Length == 0)
		{
			return AiText.T("请给关键词。", "검색어를 입력해 주세요.");
		}
		var all = LOMAssistant.PositionEventCache.Instance.AllEvents();
		if (all.Count == 0)
		{
			return AiText.T("场所事件表还没载入（游戏读档后进过一次剧情才拿得到）。可以先用 search 搜对白。", "장소 이벤트 목록을 아직 읽지 못했습니다. 저장을 불러온 뒤 이야기에 한 번 진입해야 합니다. 먼저 search로 대사를 검색할 수 있습니다.");
		}
		ReadStorySystem read = ReadStorySystem.Instance;
		var hits = new List<(string Pool, Mortal.Story.PositionEventData Data, string Script, int Score)>();
		foreach (var (pool, data) in all)
		{
			string name = Clean(DataHelper.GetEventName(data));
			string place = LOMAssistant.PositionEventCache.PlaceName(pool);
			string script = LOMAssistant.PositionEventCache.Instance.ResultScript(data) ?? "";
			int score = name.Contains(q) ? 3 : place.Contains(q) ? 2 : 0;
			if (score == 0 && script.Length > 0)
			{
				StoryScript s = StoryScripts.Get(script);
				if (s != null && s.Talk.Any(t => StoryAtlas.LineText(t.Key).Contains(q)))
				{
					score = 1;
				}
			}
			if (score > 0)
			{
				hits.Add((pool, data, script, score));
			}
		}
		if (hits.Count == 0)
		{
			return AiText.T("没有找到「", "「") + query + AiText.T("」相关的场所事件。", "」 관련 장소 이벤트를 찾지 못했습니다.");
		}
		var sb = new StringBuilder();
		foreach (var h in hits.OrderByDescending(x => x.Score).Take(MaxEvents))
		{
			sb.Append("【").Append(LOMAssistant.PositionEventCache.PlaceName(h.Pool)).Append(" · ").Append(Clean(DataHelper.GetEventName(h.Data))).Append("】");
			if (h.Script.Length > 0)
			{
				sb.Append(AiText.T("剧本 ", "스크립트 ")).Append(h.Script).Append(LOMAssistant.MapEventHints.Unread(h.Script) ? AiText.T("（玩家没看过）", "(사용자가 아직 보지 않음)") : "");
			}
			sb.Append('\n');
			string desc;
			try
			{
				desc = Clean(LOMAssistant.PositionEventCache.Instance.GetPositionEventDesc(h.Data));
			}
			catch
			{
				desc = "";
			}
			sb.Append(desc.Length > 900 ? desc.Substring(0, 900) + "…" : desc).Append('\n');
		}
		if (hits.Count > MaxEvents)
		{
			sb.Append(AiText.T("……还有 ", "…추가 ")).Append(hits.Count - MaxEvents).Append(AiText.T(" 个，换个更具体的关键词。\n", "개가 있습니다. 더 구체적인 검색어를 입력해 주세요.\n"));
		}
		return sb.ToString().Trim();
	}

	// 读过多少（全部周目，按对白算）：没有对白时不写
	private static string ReadNote(IEnumerable<string> lines)
	{
		int? read = StoryScripts.ReadPercent(lines);
		return read == null ? "" : read == 0 ? AiText.T("〔没读过〕", "〔안 읽음〕") : read == 100 ? AiText.T("〔读过〕", "〔읽음〕") : AiText.T("〔读过 ", "〔읽은 비율 ") + read + "%〕";
	}

	// 这一局走过：剧本写下的剧情记录出现在这一局的剧情记录里
	private static bool VisitedThisRun(IEnumerable<StoryScript> scripts)
	{
		List<string> keys = PlayerStatManagerData.Instance?.StoryKeyList;
		if (keys == null || keys.Count == 0)
		{
			return false;
		}
		var visited = new HashSet<string>(keys, StringComparer.Ordinal);
		return scripts.Any(x => x != null && x.Records.Any(visited.Contains));
	}

	// 一个去处：这条分岔上的对白加接下去那个剧本的对白读过多少，接下去的剧本这一局走过没有
	private static string ExitReadNote(StoryScript s, StoryExit exit)
	{
		var lines = s.Talk.Where(t => t.Path.Count > 0 && StoryScripts.IsPrefix(t.Path, exit.Path)).Select(t => t.Key).ToList();
		StoryScript next = exit.Target.Length > 0 ? StoryScripts.Get(exit.Target) : null;
		if (next != null)
		{
			lines.AddRange(next.Lines);
		}
		string note = ReadNote(lines);
		if (next != null && VisitedThisRun(new[] { next }))
		{
			note += AiText.T("（这一局走过）", "(이번 회차에 지나옴)");
		}
		return note;
	}

	private static string MissionLine(StoryMission m) => m.Key + "「" + Clean(m.Title) + "」" + (m.Main ? AiText.T("（主线）", "(주요 임무)") : AiText.T("（支线）", "(곁가지 임무)")) + (m.When.Length > 0 ? " " + Clean(m.When) : "");

	private static string Search(string query)
	{
		if (query.Length == 0)
		{
			return AiText.T("请给关键词。", "검색어를 입력해 주세요.");
		}
		string q = TextConverter.ToSimplified(query);
		var sb = new StringBuilder();
		var missions = StoryAtlas.Missions.Where(m => (m.Key + m.Title + m.Desc + m.Summary + m.SeriesTitle).Contains(q) || TextConverter.ToSimplified(m.Summary).Contains(q)).Take(15).ToList();
		if (missions.Count > 0)
		{
			sb.Append(AiText.T("【任务】\n", "[임무]\n"));
			foreach (StoryMission m in missions)
			{
				sb.Append("- ").Append(MissionLine(m));
				string summary = Clean(m.Summary).Replace("\n", " ");
				if (summary.Length > 0)
				{
					sb.Append("：").Append(summary.Length > 60 ? summary.Substring(0, 60) + "…" : summary);
				}
				sb.Append('\n');
			}
		}
		if (FindHeroine(q) is StoryRomance.Heroine heroine)
		{
			sb.Append(AiText.T("【可结缘女主角】", "[인연을 맺을 수 있는 인물]")).Append(StoryRomance.Name(heroine)).Append(AiText.T("（用 heroine 看她的结缘路线）\n", "(heroine으로 인연 경로 확인)\n"));
		}
		var endings = StoryAtlas.Endings.Concat(StoryAtlas.Achievements).Concat(StoryAtlas.Cgs).Where(e => (e.Title + e.Desc).Contains(q)).Take(10).ToList();
		if (endings.Count > 0)
		{
			sb.Append(AiText.T("【结局、死亡、成就、CG】（用 endings 看达成方法）\n", "[결말·사망·업적·CG](endings로 달성 방법 확인)\n"));
			foreach (StoryEnding e in endings)
			{
				sb.Append("- ").Append(Clean(e.Title)).Append(e.Death ? AiText.T("（死亡）", "(사망)") : "").Append('\n');
			}
		}
		var duels = ConditionBook.Combats.Where(c => c.Value.Count > 0 && Clean(c.Value[0]).Contains(q)).Take(3).ToList();
		if (duels.Count > 0)
		{
			sb.Append(AiText.T("【决斗关卡】\n", "[결투 전투]\n"));
			foreach (var d in duels)
			{
				sb.Append(AiText.T("- 关卡 ", "- 전투 ")).Append(d.Key).Append('\n').Append(CombatText(d.Key, "  "));
			}
		}
		try
		{
			var events = LOMAssistant.PositionEventCache.Instance.AllEvents()
				.Select(e => (Place: LOMAssistant.PositionEventCache.PlaceName(e.Pool), Name: Clean(DataHelper.GetEventName(e.Data))))
				.Where(e => e.Name.Contains(q) || e.Place.Contains(q)).Distinct().Take(8).ToList();
			if (events.Count > 0)
			{
				sb.Append(AiText.T("【场所事件】（用 events 看权重、条件和结果）\n", "[장소 이벤트](events로 추첨 가중치·조건·결과 확인)\n"));
				foreach (var e in events)
				{
					sb.Append("- ").Append(e.Place).Append(" · ").Append(e.Name).Append('\n');
				}
			}
		}
		catch
		{
		}
		var flags = AiAnalysis.FlagsNamed(q, 15);
		if (flags.Count > 0)
		{
			sb.Append(AiText.T("【旗标】（用 usage 看在哪改、在哪判定）\n", "[플래그](usage로 변경·판정 지점 확인)\n"));
			foreach (string f in flags)
			{
				sb.Append("- ").Append(f).Append('\n');
			}
		}
		var lines = StoryAtlas.SearchDialogue(q, 15);
		// 任务以外的剧本（地点事件、随机事件）也搜
		lines.AddRange(AiAnalysis.SearchOtherDialogue(q, 10));
		if (lines.Count > 0)
		{
			sb.Append(AiText.T("【对白】（剧本编号：片段）\n", "[대사](스크립트 ID: 발췌)\n"));
			foreach (var (script, snippet) in lines)
			{
				sb.Append("- ").Append(script).Append(StoryAtlas.Owner.TryGetValue(script, out StoryMission owner) ? AiText.T("（任务 ", "(임무 ") + owner.Key + "「" + Clean(owner.Title) + "」）" : "").Append("：").Append(Clean(snippet)).Append('\n');
			}
		}
		return sb.Length > 0 ? sb.ToString() : AiText.T("没有找到「", "「") + query + AiText.T("」。换个说法试试（人名、地名、任务名里的词）。", "」 검색 결과가 없습니다. 인물명·장소명·임무명에 들어가는 다른 말로 검색해 보세요.");
	}

	/// <summary>这一局旗标的当前值（玩家在标题画面、没读档时为空）。</summary>
	internal static int? FlagNow(string id)
	{
		if (PlayerStatManagerData.Instance == null || UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Title")
		{
			return null;
		}
		try
		{
			return MissionManagerData.Instance?.GetFlag(id)?.State;
		}
		catch
		{
			return null;
		}
	}

	private static string FlagsNow(IEnumerable<string> ids)
	{
		var parts = new List<string>();
		foreach (string id in ids.Distinct())
		{
			if (FlagNow(id) is int v)
			{
				parts.Add(Clean(DataHelper.GetFlagName(id)) + " (" + id + ") = " + v);
			}
		}
		return string.Join("，", parts.ToArray());
	}

	private static string Quest(string key)
	{
		StoryMission m = StoryAtlas.Find(key) ?? StoryAtlas.Missions.FirstOrDefault(x => Clean(x.Title).Contains(key));
		if (m == null)
		{
			return AiText.T("没有任务「", "임무 「") + key + AiText.T("」。先用 search 找编号。", "」를 찾지 못했습니다. search로 ID를 먼저 확인하세요.");
		}
		var sb = new StringBuilder(AiText.T("任务 ", "임무 ")).Append(MissionLine(m)).Append('\n');
		bool walked = m.Visited || VisitedThisRun(m.Scripts.Select(StoryScripts.Get));
		sb.Append(AiText.T("玩家进度：对白读过 ", "사용자 진행도: 대사 읽은 비율 ")).Append(m.Read == null ? AiText.T("（没有对白）", "(대사 없음)") : m.Read + "%").Append(AiText.T("（全部周目合计）", "(모든 회차 합산)"))
			.Append(PlayerStatManagerData.Instance != null ? (walked ? AiText.T("；这一局走过", "; 이번 회차에 지나옴") : AiText.T("；这一局还没走过", "; 이번 회차에 지나오지 않음")) : "").Append('\n');
		sb.Append(AiText.T("任务组：", "임무 그룹: ")).Append(Clean(m.SeriesTitle)).Append(m.Inactive ? AiText.T("；不会在地图上自己触发，只能由别的剧本接进来", "; 지도에서 직접 발생하지 않고 다른 스크립트에서 연결되어야 함") : "").Append('\n');
		if (m.Desc.Length > 0)
		{
			sb.Append(AiText.T("说明：", "설명: ")).Append(Clean(m.Desc)).Append('\n');
		}
		sb.Append(AiText.T("时间：", "시기: ")).Append(Clean(m.When)).Append(AiText.T("；地点：", "; 장소: ")).Append(Clean(m.Place)).Append('\n');
		string cond = Clean(StoryAtlas.ConditionText(m)).Replace("\n", " ");
		if (cond.Length > 0)
		{
			sb.Append(AiText.T("触发条件：", "발생 조건: ")).Append(cond).Append('\n');
			string now = FlagsNow(Regex.Matches(cond, @"\(([A-Z]\d{5})\)").Cast<Match>().Select(x => x.Groups[1].Value));
			if (now.Length > 0)
			{
				sb.Append(AiText.T("条件里的旗标这一局现在：", "조건에 쓰인 플래그의 이번 회차 현재 값: ")).Append(now).Append(AiText.T("（拿它对照触发条件，判断玩家还能不能触发）\n", "(발생 조건과 대조하여 아직 발생 가능한지 판단하세요.)\n"));
			}
		}
		foreach (StoryEntry e in m.Entries)
		{
			sb.Append(AiText.T("从", "연결 출처: ")).Append(e.From != null ? AiText.T("任务 ", "임무 ") + e.From.Key + "「" + Clean(e.From.Title) + AiText.T("」的", "」 / ") : "").Append(AiText.T("剧本 ", "스크립트 ")).Append(e.Script).Append(AiText.T(" 接进来：", " / 진입 경로: ")).Append(PathText(e.Path)).Append('\n');
		}
		if (m.Next.Count > 0)
		{
			sb.Append(AiText.T("之后接到：", "이후 연결: ")).Append(string.Join("、", m.Next.Select(k => StoryAtlas.Find(k) is StoryMission n ? n.Key + "「" + Clean(n.Title) + "」" : k).ToArray())).Append('\n');
		}
		sb.Append(AiText.T("\n剧本（编号：剧情记录）：\n", "\n스크립트(ID: 스토리 기록):\n"));
		foreach (string name in m.Scripts.Where(s => StoryAtlas.Owner.TryGetValue(s, out StoryMission o) && o == m))
		{
			StoryScript s = StoryScripts.Get(name);
			string first = s == null ? "" : s.Records.Select(StoryScripts.Legend).FirstOrDefault(t => t.Length > 0) ?? "";
			sb.Append("- ").Append(name).Append(s != null && s.Branches ? AiText.T("（有分岔）", "(분기 있음)") : "").Append(first.Length > 0 ? "：" + (first.Length > 80 ? first.Substring(0, 80) + "…" : first) : "").Append('\n');
		}
		return sb.ToString();
	}

	private static string Script(string name, int page)
	{
		StoryScript s = StoryScripts.Get(name);
		if (s == null)
		{
			return AiText.T("没有剧本「", "스크립트 「") + name + AiText.T("」。先用 search 或 quest 找编号。", "」를 찾지 못했습니다. search 또는 quest로 ID를 확인하세요.");
		}
		var sb = new StringBuilder(AiText.T("剧本 ", "스크립트 ")).Append(name);
		if (StoryAtlas.Owner.TryGetValue(name, out StoryMission owner))
		{
			sb.Append(AiText.T("，属于任务 ", ", 소속 임무 ")).Append(MissionLine(owner));
		}
		sb.Append('\n');
		if (page <= 1)
		{
			var legends = s.Records.Select(StoryScripts.Legend).Where(t => t.Length > 0).Distinct().ToList();
			if (legends.Count > 0)
			{
				sb.Append(AiText.T("\n【剧情记录】（游戏回想里的文字，按剧本顺序，分岔各自的都在）\n", "\n[스토리 기록](회상에 나오는 기록입니다. 스크립트 순서이며 서로 다른 분기의 기록을 모두 포함합니다.)\n"));
				foreach (string t in legends)
				{
					sb.Append("- ").Append(t).Append('\n');
				}
			}
			if (s.Menus.Count > 0)
			{
				sb.Append(AiText.T("\n【选项】（〔〕里是这个选项后面的对白玩家读过多少，全部周目合计）\n", "\n[선택지](〔〕는 선택지 이후 대사의 읽은 비율이며 모든 회차 합산입니다.)\n"));
				foreach (List<StoryStep> menu in s.Menus)
				{
					sb.Append("- ").Append(string.Join(" / ", menu.Select(x => Clean(StoryScripts.Describe(x)) + ReadNote(s.Talk.Where(t => t.Path.Any(p => p.Kind == x.Kind && p.Key == x.Key)).Select(t => t.Key))).ToArray())).Append('\n');
				}
			}
			if (s.Exits.Count > 0)
			{
				sb.Append(AiText.T("\n【去处】（分岔路径 ⇒ 接到哪；变化是这条路上的数值、好感、旗标、物品变化；〔〕里是这条路和接下去的剧本玩家读过多少（全部周目），以及这一局走过没有）\n", "\n[연결](분기 경로 ⇒ 연결 대상; 변화는 해당 경로의 수치·호감도·플래그·물품 변화입니다. 〔〕는 이 경로와 다음 스크립트의 모든 회차 읽은 비율이며 이번 회차 방문 여부는 별도 표시합니다.)\n"));
				foreach (StoryExit exit in s.Exits)
				{
					string target = exit.ToTitle ? AiText.T("回到标题（结局）", "제목 화면으로(결말)") : exit.ToMap ? AiText.T("回地图", "지도로") : exit.Target.Length > 0 ? AiText.T("剧本 ", "스크립트 ") + exit.Target + (StoryAtlas.Owner.TryGetValue(exit.Target, out StoryMission t) && t != owner ? AiText.T("（任务 ", "(임무 ") + t.Key + "「" + Clean(t.Title) + "」）" : "") : AiText.T("（剧本结束）", "(스크립트 종료)");
					sb.Append("- ").Append(PathText(exit.Path)).Append(exit.AfterScene ? AiText.T("，战斗后", ", 전투 후") : "").Append(" ⇒ ").Append(target).Append(ExitReadNote(s, exit));
					if (exit.MainMission.Length > 0)
					{
						sb.Append(AiText.T("，主线推进到 ", ", 주요 임무 진행: ")).Append(exit.MainMission);
					}
					var effects = s.EffectsOn(exit).Select(Clean).ToList();
					if (effects.Count > 0)
					{
						sb.Append(AiText.T("\n  变化：", "\n  변화: ")).Append(string.Join("；", effects.ToArray()));
					}
					sb.Append('\n');
				}
			}
			foreach (var (path, death, id) in s.Endings)
			{
				StoryEnding e = StoryAtlas.Endings.FirstOrDefault(x => x.Id == id);
				sb.Append(death ? AiText.T("【死亡】", "[사망]") : AiText.T("【结局】", "[결말]")).Append(e != null ? Clean(e.Title) : id).Append("：").Append(PathText(path)).Append('\n');
			}
			foreach (var (path, level) in s.Fights)
			{
				sb.Append(AiText.T("【决斗】关卡 ", "[결투] 전투 ")).Append(level).Append("：").Append(PathText(path)).Append('\n');
				sb.Append(CombatText(level, "  "));
			}
		}
		// 对白按分岔分组：同一条路上的连在一起
		var dialogue = new StringBuilder();
		string lastPath = null;
		foreach (var (path, speaker, key) in s.Talk)
		{
			string text = StoryAtlas.LineText(key);
			if (text.Length == 0)
			{
				continue;
			}
			string p = PathText(path);
			if (p != lastPath)
			{
				dialogue.Append("\n〔").Append(p).Append("〕\n");
				lastPath = p;
			}
			dialogue.Append(Speaker(speaker)).Append("：").Append(text).Append('\n');
		}
		if (dialogue.Length > 0)
		{
			string all = dialogue.ToString();
			int pages = (all.Length + PageChars - 1) / PageChars;
			page = Math.Max(1, Math.Min(page, pages));
			string part = all.Substring((page - 1) * PageChars, Math.Min(PageChars, all.Length - (page - 1) * PageChars));
			sb.Append(AiText.T("\n【对白】第 ", "\n[대사] 페이지 ")).Append(page).Append('/').Append(pages).Append(AiText.T(" 页（〔〕里是这段对白所在的分岔）\n", " (〔〕는 해당 대사가 속한 분기입니다.)\n")).Append(part);
			if (page < pages)
			{
				sb.Append(AiText.T("\n……还有下一页：script 加 page=", "\n…다음 페이지: script의 page=")).Append(page + 1);
			}
		}
		return sb.ToString();
	}

	/// <summary>问题里的编号：阿拉伯数字或一到九十九的中文数字；没有为 0。</summary>
	private static int ChineseNumber(string q)
	{
		// 只认「第三」「生死簿3」「结局四」「3条」这种位置的数字，别把「一如人间」当成 1
		var m = System.Text.RegularExpressions.Regex.Match(q, @"(?:第|簿|局|亡)\s*([一二三四五六七八九十\d]+)|([一二三四五六七八九十\d]+)\s*[条号]");
		if (!m.Success)
		{
			return 0;
		}
		string s = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
		if (char.IsDigit(s[0]))
		{
			return int.TryParse(s, out int n) ? n : 0;
		}
		const string digits = "一二三四五六七八九";
		int ten = s.IndexOf('十');
		if (ten < 0)
		{
			return s.Length == 1 ? digits.IndexOf(s[0]) + 1 : 0;
		}
		int tens = ten == 0 ? 1 : digits.IndexOf(s[ten - 1]) + 1;
		int ones = ten + 1 < s.Length ? digits.IndexOf(s[ten + 1]) + 1 : 0;
		return tens * 10 + ones;
	}

	private static string EndingList(string query)
	{
		string q = TextConverter.ToSimplified(query);
		if (NativeKorean.Enabled) q = KoreanAiQuery.EndingQuery(q);
		var sb = new StringBuilder();
		// 「生死簿第三条」「结局4」：按图鉴编号找
		int number = ChineseNumber(q);
		bool deaths = q.Contains("生死簿") || q.Contains("死亡") || q.Contains("死法");
		bool endings = q.Contains("汗青") || q.Contains("结局");
		bool byNumber = number > 0 && deaths != endings;
		void List(string title, List<StoryEnding> items)
		{
			var hits = items.Where(e => byNumber ? e.Number == number && e.Death == deaths : q.Length == 0 || (e.Title + e.Desc).Contains(q)).ToList();
			if (hits.Count == 0)
			{
				return;
			}
			sb.Append("【").Append(title).Append("】\n");
			foreach (StoryEnding e in hits.Take(q.Length == 0 ? 200 : 12))
			{
				sb.Append("- ").Append(e.Number > 0 ? e.Number + ". " : "").Append(Clean(e.Title)).Append(e.Death ? AiText.T("（死亡）", "(사망)") : "").Append(e.Achieved ? AiText.T("（玩家达成过）", "(달성 기록 있음)") : "");
				if (q.Length > 0)
				{
					if (e.Desc.Length > 0)
					{
						sb.Append("：").Append(Clean(e.Desc).Replace("\n", " "));
					}
					if (e.Ways.Count == 0)
					{
						// 游戏只有两条路记生死簿、汗青书：剧本跳到死亡或结局画面（ChangeScene），决斗输了（关卡上写着死法）；两样都没有就是哪里都触发不到
						sb.Append(AiText.T("\n  · 剧本和决斗里都没有走到它的地方。游戏代码里也只有这两条路会记下死法和结局，所以当前版本大概触发不到（没用上的条目）。照实告诉玩家，不要拿别的剧情去猜。", "\n  · 스크립트와 결투 어디에도 이 항목으로 이어지는 경로가 없습니다. 게임 코드에서 사망과 결말을 기록하는 경로도 이 둘뿐이므로 현재 버전에서는 발생하지 않는 미사용 항목으로 보입니다. 그대로 설명하고 다른 이야기로 추측하지 마세요."));
					}
					foreach (StoryEndingWay w in e.Ways.Take(6))
					{
						sb.Append(AiText.T("\n  · 剧本 ", "\n  · 스크립트 ")).Append(w.Script).Append(w.Mission != null ? AiText.T("（任务 ", "(임무 ") + w.Mission.Key + "「" + Clean(w.Mission.Title) + "」）" : "").Append("：").Append(PathText(w.Path)).Append(w.LostDuel ? AiText.T("，决斗输了", ", 결투 패배 시") : "");
					}
				}
				sb.Append('\n');
			}
		}
		List(AiText.T("结局与死亡", "결말·사망"), StoryAtlas.Endings);
		List(AiText.T("成就", "업적"), StoryAtlas.Achievements);
		List(AiText.T("结局 CG", "결말 CG"), StoryAtlas.Cgs);
		return sb.Length > 0 ? sb.ToString() : AiText.T("没有找到「", "「") + query + AiText.T("」相关的结局。", "」 관련 결말을 찾지 못했습니다.");
	}

	// 玩家常用的叫法（好感里的人名、昵称），按 StoryRomance.All 的顺序
	/// <summary>按 StoryRomance.All 的顺序（即 Lover 编号）。</summary>
	internal static readonly string[][] Aliases =
	{
		new[] { "小师妹", "唐默铃", "默铃" },
		new[] { "瑞杏", "杏花仙", "咖啡" },
		new[] { "叶云裳", "云裳" },
		new[] { "虞小梅", "小梅" },
		new[] { "上官萤", "上官" },
		new[] { "夏侯兰", "小兰" },
		new[] { "郁竹", "小竹" },
		new[] { "魏菊", "小菊", "魏招弟" },
		new[] { "龙湘", "湘姊" },
	};

	internal static StoryRomance.Heroine FindHeroine(string name)
	{
		string q = TextConverter.ToSimplified(name ?? "");
		if (q.Length == 0)
		{
			return null;
		}
		for (int i = 0; i < StoryRomance.All.Length && i < Aliases.Length; i++)
		{
			StoryRomance.Heroine h = StoryRomance.All[i];
			if (Aliases[i].Any(a => q.Contains(a)) || q.Contains(StoryRomance.Name(h)))
			{
				return h;
			}
		}
		return null;
	}

	private static string Heroine(string name)
	{
		var h = FindHeroine(name);
		if (h == null)
		{
			return "「" + name + AiText.T("」不是可结缘的女主角。可结缘的有：", "」 인물과는 인연을 맺을 수 없습니다. 가능한 인물: ") + string.Join("、", StoryRomance.All.Select(StoryRomance.Name).ToArray());
		}
		var stops = StoryRomance.StopsOf(h);
		if (stops == null)
		{
			return AiText.T("结缘路线还在整理，请稍后再问。", "인연 경로를 정리하는 중입니다. 잠시 후 다시 질문해 주세요.");
		}
		var sb = new StringBuilder(StoryRomance.Name(h)).Append(AiText.T(" 的结缘路线", " 인연 경로"));
		sb.Append(AiText.T("（玩家现在对她的好感 ", "(현재 호감도 ")).Append(StoryRomance.Affection(h)).Append(StoryRomance.IsLover(h) ? AiText.T("，是现在的心上人", ", 현재 마음에 둔 사람") : "").Append(AiPersona.Bonded(h) ? AiText.T("，已经结缘", ", 인연을 맺음") : AiText.T("，还没结缘", ", 아직 인연을 맺지 않음")).Append("）\n");
		foreach (var stop in stops)
		{
			sb.Append("- ").Append(MissionLine(stop.Mission));
			if (stop.Affection != 0)
			{
				sb.Append(AiText.T("，剧本里有加减她好感的地方（各分岔合计 ", ", 스크립트에 호감도 변화가 있음(모든 분기 합산 ")).Append(stop.Affection > 0 ? "+" : "").Append(stop.Affection).Append(AiText.T("；分岔互斥，实际走一条路拿到的少得多，具体哪条路加多少用 routes 查，不要把合计说成「最多能加」）", "; 분기들이 서로 배타적이므로 실제 한 경로의 획득량은 이보다 훨씬 적습니다. routes로 경로별 합계를 확인하고 이 합산치를 최댓값이라고 설명하지 마세요.)"));
			}
			if (stop.Steps.Count > 0)
			{
				sb.Append(AiText.T("，结缘旗标：", ", 인연 플래그: ")).Append(string.Join("、", stop.Steps.Select(f => StoryRomance.StepName(f) + (StoryRomance.StepDone(f) ? AiText.T("（已做到）", "(완료)") : AiText.T("（未做到）", "(미완료)"))).ToArray()));
			}
			sb.Append('\n');
		}
		return sb.ToString();
	}
}
