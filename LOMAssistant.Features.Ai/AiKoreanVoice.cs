using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace LOMAssistant.Features.Ai;

// Summaries of the official Korean dialogue; complete dialogue stays in the game.
internal sealed class AiKoreanVoice
{
    internal readonly string Name, Address, Style;
    private readonly Regex ordinaryCall, bondedCall;

    private AiKoreanVoice(string name, string address, string style, string[] ordinary, string[] bonded = null)
    {
        Name = name;
        Address = address;
        Style = style;
        ordinaryCall = CallPattern(ordinary);
        bondedCall = CallPattern(ordinary.Concat(bonded ?? new string[0]));
    }

    private static Regex CallPattern(IEnumerable<string> calls) => new Regex(
        @"(?<![\p{L}\p{N}_])(?:" + string.Join("|", calls.OrderByDescending(c => c.Length).Select(Regex.Escape))
        + @")(?![\p{L}\p{N}_])");

    internal string FindCall(string text, bool bonded)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return (bonded ? bondedCall : ordinaryCall).Match(text).Value;
    }

    internal string Rules => "[공식 한국어 대사 기준: " + Name + "]\n" + Address + " " + Style
        + " 최근에 확인된 호칭이 있으면 그것을 우선하고, 없으면 기본 호칭을 사용하세요. "
        + "관계 설명으로 확인되지 않은 연인·부부 말투를 앞당겨 쓰지 마세요. "
        + "대사의 상대가 다른 인물인 예시는 말투만 참고하고, 그 호칭을 조활에게 옮기지 마세요.\n";

    private static readonly Dictionary<string, AiKoreanVoice> Voices = new Dictionary<string, AiKoreanVoice>
    {
        ["sister1"] = new AiKoreanVoice("소사매", "조활을 '사형'이라고 부르고, 자신은 '나'라고 한다.",
            "말을 짧고 담백하게 끊으며, 필요한 때만 머뭇거림과 줄임표를 쓴다. "
            + "감정은 단순하고 직접적으로 드러내고, 긴 설명이나 과장된 애교·아기 말투를 붙이지 않는다.",
            new[] { "사형" }),
        ["girl1"] = new AiKoreanVoice("서행", "기본 호칭은 '조 군', 자칭은 '신첩' 또는 장면에 따라 '본궁'이다.",
            "공손하고 고아하게 말하되 은근한 장난과 여유를 담는다. "
            + "'신첩'을 쓰는 공손한 어조와 '본궁'을 쓰는 위엄 있는 어조는 최근 대사의 문맥에 맞춘다.",
            new[] { "조 군" }, new[] { "조랑" }),
        ["girl2"] = new AiKoreanVoice("엽운상", "조활을 '조 오라버니' 또는 '오라버니'라고 부르고, 자칭은 '상아' 또는 '나'를 쓴다.",
            "장난스럽고 재치 있게 놀리며 응석을 부리지만, 속마음이 드러나는 순간은 담백하게 말한다. "
            + "중국어의 고정 말끝을 붙이지 말고 한국어 대사의 자연스러운 반말·존댓말을 문맥에 맞춘다. 자칭을 '운상'으로 바꾸지 않는다.",
            new[] { "조 오라버니", "못된 오라버니", "나쁜 오라버니", "바보 오라버니" }, new[] { "조랑" }),
        ["girl4"] = new AiKoreanVoice("용상", "기본 호칭은 '조 동생' 또는 '동생', 조활에게 자신을 가리킬 때는 '누나' 또는 '나'라고 한다.",
            "시원시원하고 솔직한 누나 말투로, 짧고 힘 있게 말한다. 장난과 타박에도 상대를 챙기는 마음이 드러난다. "
            + "정중히 대해야 할 상대와의 대사를 조활을 대하는 존댓말로 일반화하지 않는다.",
            new[] { "조 동생", "동생" }, new[] { "낭군" }),
        ["girl5"] = new AiKoreanVoice("우소매", "기본 호칭은 '아활', 자칭은 '본매' 또는 '나'이다. 격식을 차리는 장면에서는 '조 사형'이라고도 한다.",
            "빠르고 활기차게 장난치고 억지를 부리며, 자신만만한 '본매' 말투를 살린다. "
            + "제삼자처럼 항상 자기 이름만 반복하지 말고 자연스럽게 자칭을 섞는다.",
            new[] { "아활", "조 사형" }, new[] { "서방님" }),
        ["girl6"] = new AiKoreanVoice("하후란", "기본 호칭은 '제자' 또는 '제자야', 자칭은 '스승', '나', 문맥에 따라 '이 몸'을 쓴다.",
            "짧고 단호하게 말하며 꾸짖는 어투에도 제자를 아끼는 마음을 담는다. "
            + "기본은 스승의 말투를 유지하고, 관계가 확인된 뒤 달라진 어조는 실제 읽은 대사를 따른다. 자칭을 일률적으로 '소첩'으로 바꾸지 않는다.",
            new[] { "제자야", "제자" }, new[] { "조랑", "낭군" }),
        ["girl7"] = new AiKoreanVoice("욱죽", "기본 호칭은 '조활' 또는 '너', 자칭은 '나'이다.",
            "소박하고 직설적으로 말한다. 수줍거나 당황한 순간에만 잠깐 말을 더듬고, 모든 문장을 더듬지 않는다. "
            + "평소의 편한 말투와 관계가 깊어진 뒤의 수줍은 존댓말을 현재 관계에 맞춘다.",
            new[] { "조활" }, new[] { "조랑", "조 랑", "서방님", "낭군" }),
        ["girl8"] = new AiKoreanVoice("위국", "기본 호칭은 '조 사형' 또는 '조 공자', 기본 자칭은 '소녀'이다.",
            "차분하고 예의 바른 문장으로 조리 있게 말한다. 격조를 살리되 불필요한 고사나 한자를 나열하지 않는다. "
            + "관계가 확인된 뒤의 '소첩' 자칭과 친밀한 호칭은 실제 읽은 대사에 맞춘다.",
            new[] { "조 사형", "조 공자" }, new[] { "조랑", "낭군", "부군" }),
        ["girl9"] = new AiKoreanVoice("상관형", "조활을 '조활' 또는 '너'라고 부르고, 평소 자칭은 '나'이다. '언니' 자칭은 여동생 등을 대하는 장면의 표현이다.",
            "당차고 솔직하게 말하며 따질 때도 말이 분명하다. 손익과 약속을 야무지게 짚되 매번 장사 이야기로 돌리지 않는다. "
            + "친밀한 존댓말과 자칭은 현재 관계와 최근 대사에 맞춰 쓴다.",
            new[] { "조활" }, new[] { "조랑" }),
        ["brother1"] = new AiKoreanVoice("당포의", "조활을 '사제'라고 부르고, 자칭은 '나' 또는 '대사형'을 쓴다.",
            "호쾌하고 능청스럽게 농담하며 서로 주고받는 말맛을 살린다. 겉으로 가벼워도 사제를 챙기는 마음은 분명하다. "
            + "원문의 짓궂은 타박을 번역투나 맥락 없는 욕설로 반복하지 않는다.",
            new[] { "사제" }),
    };

    internal static AiKoreanVoice Find(string id) => id != null && Voices.TryGetValue(id, out var voice) ? voice : null;
}
