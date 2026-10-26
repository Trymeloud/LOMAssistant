using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using LOMAssistant;

internal static class Program
{
    private static int count;
    private static readonly Regex Han = new Regex("[一-鿿]");
    private static void Check(bool ok, string name)
    {
        count++;
        if (!ok) throw new Exception(name);
    }
    private static void Expect(string input, string expected, string language = UiText.Korean)
    {
        string actual = UiText.Translate(input, language);
        Check(actual == expected, input + " => " + actual + " (expected " + expected + ")");
    }
    public static void Main(string[] args)
    {
        Expect("嘴力 需>=40 且 性情 需<=60", "구력 필요 >=40 그리고 성정 필요 <=60");
        Expect("好感度_三师兄 需>好感度_二师兄", "삼사형 호감도 필요 >이사형 호감도");
        Expect("攻略小师妹 (攻略人數) 需>=2", "공략 인원 필요 >=2");
        Expect("共 <color=green>5</color> 项", "총 <color=green>5</color>개");
        Expect("<color=red>嘴力 需<=40</color>", "<color=red>구력 필요 <=40</color>");
        Expect("读不了：存档系统尚未初始化", "불러올 수 없음: 저장 시스템이 아직 준비되지 않았습니다");
        Expect("读取「测试」？\n当前没有存档的进度会丢失。", "「测试」을(를) 불러올까요?\n저장하지 않은 진행은 사라집니다.");
        Expect("【旗标】大师兄不在唐门  0 → 1", "【플래그】대사형이 당문에 없음  0 → 1");
        Expect("第三年一月上旬", "3년차 1월 상순");
        Expect("<b>설정</b> · F9 · -10 <= 40", "<b>설정</b> · F9 · -10 <= 40");
        Expect("未知文本", "未知文本");
        Expect("设置", "設定", UiText.Japanese);
        Expect("设置", "Settings", UiText.English);
        Expect("설정", "설정");
        Expect("", "");
        Expect(null, null);
        foreach (string[] row in UiTextKorean.Flags)
        {
            Check(UiText.Translate(row[0], UiText.Korean) == row[1], "Flag: " + row[0]);
            string text = UiText.Translate(row[0] + " (M12345) 需>=1", UiText.Korean);
            Check(!Han.IsMatch(text) && text.Contains("(M12345)") && text.Contains(">=1"), "Flag condition: " + text);
        }
        foreach (string[] row in UiTextKorean.Conditions)
        {
            Expect(row[0], row[1]);
            string text = UiText.Translate(row[0] + " 需<=40", UiText.Korean);
            Check(!Han.IsMatch(text) && text.Contains("<=40"), "Condition: " + text);
        }
        foreach (string[] row in UiQuestNames.Table)
            Check(!Han.IsMatch(UiText.Translate(row[0], UiText.Korean)), "Quest: " + row[0]);
        foreach (string[] row in (string[][])typeof(UiText).GetField("Table", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null))
        {
            if (row[1] == null) continue;
            string[] sourceArgs = Regex.Matches(row[0], @"\{\d\}").Select(m => m.Value).Distinct().OrderBy(x => x).ToArray();
            string[] targetArgs = Regex.Matches(row[1], @"\{\d\}").Select(m => m.Value).Distinct().OrderBy(x => x).ToArray();
            Check(sourceArgs.SequenceEqual(targetArgs), "Placeholders: " + row[0]);
        }
        if (args.Length > 0)
        {
            using var input = File.OpenRead(args[0]);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var json = JsonDocument.Parse(gzip);
            var missing = new SortedSet<string>();
            foreach (var section in new[] { "Conditions", "Switches" })
                foreach (var property in json.RootElement.GetProperty(section).EnumerateObject())
                    foreach (string text in property.Value.ValueKind == JsonValueKind.Array
                        ? property.Value.EnumerateArray().Select(x => x.GetString()) : new[] { property.Value.GetString() })
                    {
                        string translated = UiText.Translate(text, UiText.Korean);
                        // Chinese IDs in parentheses are technical identifiers, kept intentionally.
                        string visible = Regex.Replace(translated, @"\([^()]*\)", "");
                        if (Han.IsMatch(visible)) missing.Add(text + "\t" + translated);
                    }
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "untranslated-conditions.tsv"), missing);
            Console.WriteLine("Built-in condition strings with remaining Chinese: " + missing.Count);
        }
        Console.WriteLine("PASS: " + count + " translation checks");
        if (args.Length > 1) Audit.Run(args[1]);
        if (args.Length > 3) FlagAudit.Run(args[2], args[3]);
    }
}

namespace LOMAssistant
{
    // Game-independent checks exercise the real translation code and real tables.
    internal static class XUnityTranslations
    {
        public static Dictionary<string, string> Load() => new Dictionary<string, string>();
    }
    internal static class JapanesePatchCompat { public const string NoticeText = "Japanese patch notice"; }
}
