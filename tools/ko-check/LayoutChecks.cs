using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class LayoutChecks
{
    // The user approved upstream 3.2.1's new graphs; the surrounding window keeps ko.1 geometry.
    internal static void Run(string root, Action<bool, string> check)
    {
        foreach (string name in new[] { "StoryReviewPanel.cs", "StoryReviewPanel.Route.cs", "StoryReviewPanel.Flow.cs", "StoryReviewPanel.Jump.cs", "StoryReviewPanel.Credits.cs", "StoryReviewPanel.Ai.cs" })
        {
            string file = "LOMAssistant.Features.Story/" + name;
            var start = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            string revision = name == "StoryReviewPanel.Route.cs" || name == "StoryReviewPanel.Flow.cs" || name == "StoryReviewPanel.Jump.cs" ? "v3.2.1" : "2a7fe65cf132b735de3c1703ab4b5439f1ecaae8";
            start.ArgumentList.Add("show"); start.ArgumentList.Add(revision + ":" + file);
            using var process = Process.Start(start);
            string baseline = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            check(process.ExitCode == 0, "Cannot read approved layout baseline: " + error);
            var before = Geometry(baseline);
            var after = Geometry(File.ReadAllText(Path.Combine(root, file)));
            check(before.SequenceEqual(after), "Layout/style changed in " + name + ": " + string.Join("; ", before.Except(after).Take(2)));
        }
        Console.WriteLine("Layout: graphs match approved upstream 3.2.1; surrounding window and AI retain ko.1 geometry/styles");
    }

    private static string[] Geometry(string source) => CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes()
        .Where(n => n is ObjectCreationExpressionSyntax creation && (creation.Type.ToString() == "Rect" || creation.Type.ToString() == "GUIStyle")
            || n is InvocationExpressionSyntax call && (call.Expression.ToString() == "S" || call.Expression.ToString() == "Z"
                || new[] { "GUILayout.Width", "GUILayout.Height", "GUILayout.Space", "GUILayout.MinWidth", "GUILayout.MaxWidth", "GUILayout.MinHeight", "GUILayout.MaxHeight" }.Contains(call.Expression.ToString())))
        .Select(n => new RemoveTextWrapper().Visit(n).NormalizeWhitespace().ToFullString()).ToArray();

    private sealed class RemoveTextWrapper : CSharpSyntaxRewriter
    {
        public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node) => node.Expression.ToString() == "StoryUi.Text"
            ? Visit(node.ArgumentList.Arguments[1].Expression) : node.Expression.ToString() == "AiText.T"
            ? Visit(node.ArgumentList.Arguments[0].Expression) : base.VisitInvocationExpression(node);
    }
}
