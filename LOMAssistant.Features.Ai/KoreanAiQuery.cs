using System.Text.RegularExpressions;
namespace LOMAssistant.Features.Ai;

internal static class KoreanAiQuery
{
    internal static string AffectionPerson(string text) => Regex.Replace(text ?? "",
        @"^(?:好感|호감도?)[·:：]?\s*|(?:的?好感度?|의?\s*호감도?)$", "").Trim();
    private static string Comparable(string text) => Regex.Replace(
        Regex.Replace(text ?? "", @"\s+", "").Replace("호감도", "호감").Replace("의호감", "호감"), @"^호감[·:：]?(.+)$", "$1호감");
    internal static bool Equal(string text, string query) => Comparable(text) == Comparable(query);
    internal static bool Contains(string text, string query) => Comparable(query).Length > 0 && Comparable(text).Contains(Comparable(query));
    // Only normalize a numbered catalogue lookup, never a Korean title search.
    internal static string EndingQuery(string text) => Regex.Replace(text ?? "",
        @"^(생사부|사망|한청서|결말)\s*(\d+)(?:번|번째)?$", m =>
            (m.Groups[1].Value == "생사부" || m.Groups[1].Value == "사망" ? "生死簿" : "结局") + m.Groups[2].Value);
}
