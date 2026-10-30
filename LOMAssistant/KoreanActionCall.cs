using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace LOMAssistant;

// The preview reads a Lua call as data. It never evaluates or executes Lua.
internal readonly struct KoreanActionCall
{
    private static readonly Regex Pattern = new Regex(@"statmodifymanager\.(\w+)\(([^)]*)\)");
    internal readonly string Method, Id;
    internal readonly int Value;
    internal readonly bool HasValue;
    private KoreanActionCall(string method, string id, int value, bool hasValue)
    { Method = method; Id = id; Value = value; HasValue = hasValue; }

    internal static bool TryParse(string content, out KoreanActionCall call)
    {
        call = default;
        Match match = Pattern.Match(content ?? "");
        if (!match.Success) return false;
        string[] args = match.Groups[2].Value.Split(',').Select(s => s.Trim().Trim('"')).ToArray();
        string method = match.Groups[1].Value;
        if (args[0].Length == 0) return false;
        if (args.Length == 1 && method == "RemoveMisc")
        { call = new KoreanActionCall(method, args[0], 0, false); return true; }
        if (args.Length < 2 || !int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)) return false;
        call = new KoreanActionCall(method, args[0], value, true);
        return true;
    }
}
