using System.Linq;
using System.Text.RegularExpressions;

namespace LOMAssistant.Features.Ai;

// Output-language instructions are separate from the Chinese source material.
internal static class AiReplyLanguage
{
    internal static readonly Regex MoodTag = new Regex(
        @"\[\s*(?:表情|표정|mood)\s*[:：]\s*([A-Za-z_0-9]+)\s*\]", RegexOptions.IgnoreCase);

    internal static string KoreanRules(string personaId)
    {
        string rules = "[응답 언어 규칙: 한국어]\n"
            + "답변 본문, 행동 묘사, 인용 대사, 공략 힌트는 모두 자연스러운 한국어로 작성하세요. "
            + "자료와 인물 설정의 중국어는 참고 원문이며, 답변에 그대로 따라 쓸 표현이 아닙니다. "
            + "과거 답변에 중국어가 섞여 있어도 반복하지 마세요. "
            + "인물의 성격과 말투는 살리되 이름·호칭·자칭·문장 어미까지 한국어로 옮기세요. "
            + "중국어 이름이나 어미를 한글 문장에 붙이지 마세요.\n"
            + "이름 표기: 赵活=조활, 唐默铃=당묵령, 瑞杏=서행, 叶云裳=엽운상, 叶云舟=엽운주, "
            + "龙湘=용상, 虞小梅=우소매, 夏侯兰=하후란, 郁竹=욱죽, 魏菊=위국, 上官萤=상관형, 唐布衣=당포의.\n";
        if (!string.IsNullOrEmpty(personaId))
            rules += "표정 메타데이터만 예외입니다. 필요한 경우 끝에 [mood: laugh1]처럼 허용된 표정 코드를 쓰고, "
                + "mood라는 키와 코드명은 번역하지 마세요.\n";
        if (AiKoreanVoice.Find(personaId) is AiKoreanVoice voice) rules += voice.Rules;
        return rules;
    }

    internal static string Apply(string system, string language, string personaId)
    {
        if (language != UiText.Korean) return system;
        string rules = KoreanRules(personaId);
        return rules + "\n" + system + "\n\n[최종 응답 확인] 중국어 원문이나 이전 혼합 답변을 따라 쓰지 말고, "
            + "호칭·자칭·말끝과 행동 묘사·공략까지 모두 한국어로 작성하세요.";
    }

    internal static string TakeMood(string answer, string[] allowed, out string mood)
    {
        mood = allowed[0];
        Match match = MoodTag.Match(answer);
        if (match.Success)
        {
            string raw = match.Groups[1].Value.ToLowerInvariant();
            mood = allowed.Contains(raw) ? raw
                : allowed.FirstOrDefault(x => x.TrimEnd('1', '2', '3') == raw.TrimEnd('1', '2', '3')) ?? allowed[0];
        }
        return MoodTag.Replace(answer, "").Trim();
    }
}
