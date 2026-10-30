using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LOMAssistant;
using LOMAssistant.Features.Ai;

namespace LOMAssistant.Tests;

public static partial class AiTests
{
	public static void TestKoreanEmbeddedTables()
	{
		foreach (var spec in new[] { ("flag-names-ko.tsv", 1069, 2), ("event-names-ko.tsv", 406, 3) })
		{
			using (var stream = typeof(UiText).Assembly.GetManifestResourceStream("LOMAssistant." + spec.Item1))
			{
				Assert(stream != null, "Missing embedded Korean table: " + spec.Item1);
				using (var reader = new StreamReader(stream))
				{
					var keys = new HashSet<string>();
					string line;
					while ((line = reader.ReadLine()) != null)
					{
						if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
						string[] cells = line.Split('\t');
						Assert(cells.Length == spec.Item3, "Malformed Korean table row: " + spec.Item1);
						Assert(keys.Add(string.Join("\t", cells.Take(cells.Length - 1))), "Duplicate contextual translation key");
						Assert(!Regex.IsMatch(cells.Last(), "[一-鿿]"), "Chinese in Korean table value");
					}
					Assert(keys.Count == spec.Item2, "Embedded Korean table lost rows: " + spec.Item1);
				}
			}
		}
	}

	public static void TestKoreanFastModeAndCentralPrompts()
	{
		try
		{
			Language(3);
			Mode(false, false);
			Assert(AiChat.Tools.Count() == 15, "Korean localization must preserve the normal tool set");
			string rule = AiPrompts.LanguageRule;
			Assert(AiPrompts.Assistant(AiPrompts.Spoiler(1)).Contains(rule), "Normal Korean instructions bypassed AiPrompts");
			Assert(AiPrompts.Summary("legacy") == KoreanAiPrompt.Summary, "Korean summary instructions bypassed AiPrompts");
			Mode(true, false);
			Assert(AiChat.Tools.Select(t => t.Name).OrderBy(x => x).SequenceEqual(
				new[] { "search", "script", "heroine", "player_state", "propose_change" }.OrderBy(x => x)), "Fast mode tool filter changed");
			string fast = AiPrompts.FastAssistant(AiPrompts.Spoiler(1));
			string persona = KoreanAiPrompt.PersonaRules("엽운상", true, AiPrompts.Spoiler(1), "normal", true);
			Assert(fast.Contains(rule) && fast.Length < AiPrompts.Assistant(AiPrompts.Spoiler(1)).Length, "Fast mode must keep shorter Korean instructions");
			Assert(!fast.Contains("routes") && !persona.Contains("routes") && !persona.Contains("usage") && !persona.Contains("quest"), "Fast Korean instructions request unavailable tools");
			Assert(persona.Contains(KoreanAiPrompt.HintMarker), "Fast mode lost Korean hint formatting");
			Language(5);
			Assert(AiPrompts.Summary("unchanged") == "unchanged", "Korean summary changed another language");
		}
		finally { Mode(false, false); Language(1); }
	}

	public static void TestKoreanVoiceAndMetadata()
	{
		var voice = AiKoreanVoice.Find("girl2");
		Assert(voice.FindCall("조 오라버니, 놀랐지?", false) == "조 오라버니", "Official Korean address was lost");
		Assert(voice.FindCall("조랑", false) == "" && voice.FindCall("조랑", true) == "조랑", "Bonded address leaked into an earlier relationship");
		Assert(voice.FindCall("가짜조 오라버니님", false) == "", "Address matched inside another word");
		foreach (string tag in new[] { "[표정: laugh]", "[表情: laugh1]", "[mood: laugh1]" })
		{
			string body = AiReplyLanguage.TakeMood("안녕 " + tag, new[] { "normal", "laugh1" }, out string mood);
			Assert(body == "안녕" && mood == "laugh1", "Korean/legacy mood metadata must not appear in dialogue");
		}
		foreach (string marker in new[] { KoreanAiPrompt.HintMarker, KoreanAiPrompt.LegacyHintMarker })
		{
			string text = "대사\n" + marker + "첫 글자";
			int offset = KoreanAiPrompt.HintIndex(text);
			Assert(text.Substring(offset + KoreanAiPrompt.HintLength(text, offset)) == "첫 글자", "Hint marker consumed dialogue text");
		}
	}
}
