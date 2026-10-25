using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class Audit
{
    public static void Run(string root)
    {
        var missing = new SortedSet<string>();
        int inspected = 0;
        foreach (string path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(path);
            if (!(name.Contains("Panel") || name.Contains("Browser") || name.Contains("Picker"))) continue;
            if (path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)) continue;
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path));
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                string text = null;
                if (node is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
                    text = literal.Token.ValueText;
                else if (node is InterpolatedStringExpressionSyntax interpolated)
                {
                    int i = 0;
                    text = string.Concat(interpolated.Contents.Select(c => c is InterpolatedStringTextSyntax t ? t.TextToken.ValueText : "{" + i++ + "}"));
                }
                if (text == null || !Regex.IsMatch(text, "[一-鿿]")) continue;
                inspected++;
                string translated = LOMAssistant.UiText.Translate(text, LOMAssistant.UiText.Korean);
                if (Regex.IsMatch(translated, "[一-鿿]"))
                    missing.Add(name + ":" + (tree.GetLineSpan(node.Span).StartLinePosition.Line + 1) + "\t" + text.Replace("\n", "\\n") + "\t" + translated.Replace("\n", "\\n"));
            }
        }
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "untranslated-ui.tsv"), missing);
        Console.WriteLine("UI source candidates inspected: " + inspected + "; remaining Chinese candidates (including internal keys/logs): " + missing.Count);
    }
}
