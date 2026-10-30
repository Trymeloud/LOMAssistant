using System.Collections.Generic;
namespace LOMAssistant.Features.Ai;
internal static class KoreanAiMood
{
    private static readonly Dictionary<string, string> Meanings = new Dictionary<string, string>
    {
        ["normal"] = "평소", ["laugh1"] = "미소", ["laugh2"] = "활짝 웃음", ["close_eye_laugh"] = "눈을 감고 웃음",
        ["shy"] = "수줍음", ["proud"] = "자신만만", ["revel"] = "황홀함", ["serious"] = "진지함",
        ["nervous1"] = "긴장·당황", ["nervous2"] = "허둥댐", ["nervous3"] = "난처함", ["shock"] = "놀람",
        ["angry1"] = "못마땅함", ["angry2"] = "화남", ["angry3"] = "격노", ["gloomy1"] = "침울함", ["gloomy2"] = "어두운 표정",
        ["sad1"] = "슬픔", ["sad2"] = "깊은 슬픔", ["cry1"] = "울음", ["doubt"] = "의아함", ["love"] = "애정",
        ["discouraged"] = "풀이 죽음", ["annoyed"] = "짜증", ["hurt"] = "아픔",
    };
    internal static string Meaning(string id) => Meanings.TryGetValue(id, out string value) ? value : id;
}
