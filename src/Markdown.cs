using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace TinyTodo
{
    public sealed class MarkdownRun
    {
        public string Text;
        public string Link;
        public bool Strike;
        public bool Bold, Italic, Code;
        public MarkdownRun(string text, bool bold, bool italic, bool code)
        { Text = text; Bold = bold; Italic = italic; Code = code; }
    }
    public sealed class MarkdownLine
    {
        public int Heading;
        public bool Code, Quote;
        public string Prefix = "";
        public readonly List<MarkdownRun> Runs = new List<MarkdownRun>();
    }
    // A deliberately small, local text renderer: no HTML, scripts or remote resources.
    public static class SimpleMarkdown
    {
        public static List<MarkdownLine> Parse(string source)
        {
            var result = new List<MarkdownLine>(); bool fenced = false;
            foreach (string raw in (source ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                string text = raw; var line = new MarkdownLine();
                if (text.TrimStart().StartsWith("```", StringComparison.Ordinal)) { fenced = !fenced; continue; }
                if (fenced) { line.Code = true; line.Runs.Add(new MarkdownRun(text, false, false, true)); }
                else
                {
                    Match heading = Regex.Match(text, @"^(#{1,6})\s+(.*)$");
                    if (heading.Success) { line.Heading = heading.Groups[1].Length; text = heading.Groups[2].Value; }
                    else if (text.StartsWith("> ", StringComparison.Ordinal)) { line.Quote = true; text = text.Substring(2); line.Prefix = "│ "; }
                    else
                    {
                        Match item = Regex.Match(text, @"^\s*(?:[-+*]|(\d+)\.)\s+(.*)$");
                        if (item.Success) { line.Prefix = item.Groups[1].Success ? item.Groups[1].Value + ". " : "• "; text = item.Groups[2].Value; }
                    }
                    if (line.Prefix.Length > 0 && (text.StartsWith("[ ] ") || text.StartsWith("[x] ") || text.StartsWith("[X] ")))
                    { line.Prefix = text[1] == ' ' ? "☐ " : "☑ "; text = text.Substring(4); }
                    Inline(text, line.Runs, false, false, 0);
                }
                result.Add(line);
            }
            return result;
        }
        public static bool IsWebLink(string target)
        {
            Uri uri; return !String.IsNullOrWhiteSpace(target) && target.IndexOfAny(new char[] { '\r', '\n', '\0' }) < 0 &&
                Uri.TryCreate(target, UriKind.Absolute, out uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) && !String.IsNullOrEmpty(uri.Host);
        }
        private static int LinkEnd(string text, int start)
        {
            int nesting = 0;
            for (int i = start; i < text.Length; i++)
            {
                if (text[i] == '\\') { i++; continue; }
                if (text[i] == '(') nesting++;
                else if (text[i] == ')') { if (nesting == 0) return i; nesting--; }
            }
            return -1;
        }
        private static void Inline(string text, List<MarkdownRun> runs, bool bold, bool italic, int depth)
        {
            if (depth > 8) { runs.Add(new MarkdownRun(text, bold, italic, false)); return; }
            var plain = new StringBuilder(); int i = 0;
            Action flush = delegate { if (plain.Length > 0) { runs.Add(new MarkdownRun(plain.ToString(), bold, italic, false)); plain.Length = 0; } };
            while (i < text.Length)
            {
                if (text[i] == '\\' && i + 1 < text.Length && "\\`*_[]()!".IndexOf(text[i + 1]) >= 0)
                { plain.Append(text[i + 1]); i += 2; continue; }
                if (i + 1 < text.Length && text.Substring(i, 2) == "~~")
                {
                    int strikeEnd = text.IndexOf("~~", i + 2, StringComparison.Ordinal);
                    if (strikeEnd > i + 2) { flush(); var nested = new List<MarkdownRun>(); Inline(text.Substring(i + 2, strikeEnd - i - 2), nested, bold, italic, depth + 1); foreach (var run in nested) { run.Strike = true; runs.Add(run); } i = strikeEnd + 2; continue; }
                }
                string token = text[i] == '`' ? "`" : i + 1 < text.Length && text.Substring(i, 2) == "**" ? "**" : text[i] == '*' ? "*" : null;
                if (token != null)
                {
                    int end = text.IndexOf(token, i + token.Length, StringComparison.Ordinal);
                    if (end > i + token.Length)
                    {
                        flush(); string content = text.Substring(i + token.Length, end - i - token.Length);
                        if (token == "`") runs.Add(new MarkdownRun(content, bold, italic, true));
                        else Inline(content, runs, bold || token == "**", italic || token == "*", depth + 1);
                        i = end + token.Length; continue;
                    }
                }
                if (text[i] == '[' || (text[i] == '!' && i + 1 < text.Length && text[i + 1] == '['))
                {
                    bool image = text[i] == '!'; int start = i + (image ? 2 : 1);
                    int middle = text.IndexOf("](", start, StringComparison.Ordinal);
                    int end = middle < 0 ? -1 : LinkEnd(text, middle + 2);
                    if (end >= 0)
                    {
                        flush(); string label = text.Substring(start, middle - start);
                        string target = text.Substring(middle + 2, end - middle - 2).Trim();
                        if (target.StartsWith("<") && target.EndsWith(">")) target = target.Substring(1, target.Length - 2);
                        var labels = new List<MarkdownRun>(); Inline(label, labels, bold, italic, depth + 1);
                        foreach (var run in labels) { if (!image && IsWebLink(target)) run.Link = target; runs.Add(run); }
                        i = end + 1; continue;
                    }
                }
                bool angle = text[i] == '<'; int urlStart = i + (angle ? 1 : 0);
                if ((urlStart == 0 || angle || Char.IsWhiteSpace(text[urlStart - 1])) &&
                    (text.Substring(urlStart).StartsWith("https://", StringComparison.OrdinalIgnoreCase) || text.Substring(urlStart).StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
                {
                    int end = urlStart; while (end < text.Length && !Char.IsWhiteSpace(text[end]) && text[end] != '<' && text[end] != '>') end++;
                    string target = text.Substring(urlStart, end - urlStart).TrimEnd('.', ',', ';', '!', '?', '。', '，');
                    if (IsWebLink(target)) { flush(); runs.Add(new MarkdownRun(target, bold, italic, false) { Link = target }); i = urlStart + target.Length; if (angle && i < text.Length && text[i] == '>') i++; continue; }
                }
                plain.Append(text[i++]);
            }
            flush();
        }
    }
}
