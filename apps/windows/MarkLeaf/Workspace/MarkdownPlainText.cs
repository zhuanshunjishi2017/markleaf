using System.Net;
using System.Text.RegularExpressions;

namespace MarkLeaf.Workspace;

internal static partial class MarkdownPlainText
{
    public static string FromDocument(string source, bool isMarkdown)
    {
        if (string.IsNullOrEmpty(source))
        {
            return string.Empty;
        }

        var text = source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (isMarkdown)
        {
            text = StripMarkdown(text);
        }

        text = WebUtility.HtmlDecode(text);
        return Whitespace().Replace(text, " ").Trim();
    }

    private static string StripMarkdown(string text)
    {
        text = FrontMatter().Replace(text, string.Empty, 1);
        text = FencedCodeBoundary().Replace(text, string.Empty);
        text = HtmlTag().Replace(text, " ");
        text = Image().Replace(text, "$1");
        text = Link().Replace(text, "$1");
        text = Autolink().Replace(text, "$1");
        text = FootnoteDefinition().Replace(text, "$1");
        text = FootnoteReference().Replace(text, "$1");
        text = BlockPrefix().Replace(text, string.Empty);
        text = SetextUnderline().Replace(text, string.Empty);
        text = TableSeparator().Replace(text, string.Empty);
        text = InlineCode().Replace(text, "$1");
        text = MathBoundary().Replace(text, string.Empty);
        text = EmphasisMarker().Replace(text, string.Empty);
        text = TablePipe().Replace(text, " ");
        return EscapedPunctuation().Replace(text, "$1");
    }

    [GeneratedRegex(@"\A---[ \t]*\n.*?\n---[ \t]*(?:\n|\z)", RegexOptions.Singleline)]
    private static partial Regex FrontMatter();

    [GeneratedRegex(@"(?m)^[ \t]{0,3}(?:`{3,}|~{3,})[^\n]*$")]
    private static partial Regex FencedCodeBoundary();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"!\[([^\]]*)\]\([^\)]*\)")]
    private static partial Regex Image();

    [GeneratedRegex(@"\[([^\]]+)\]\([^\)]*\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"<((?:https?://|mailto:)[^>]+)>", RegexOptions.IgnoreCase)]
    private static partial Regex Autolink();

    [GeneratedRegex(@"(?m)^[ \t]*\[\^[^\]]+\]:[ \t]*(.*)$")]
    private static partial Regex FootnoteDefinition();

    [GeneratedRegex(@"\[\^([^\]]+)\]")]
    private static partial Regex FootnoteReference();

    [GeneratedRegex(@"(?m)^[ \t]{0,3}(?:(?:#{1,6}|>)[ \t]+|[-+*][ \t]+\[[ xX]\][ \t]+|(?:[-+*]|\d+[.)])[ \t]+)")]
    private static partial Regex BlockPrefix();

    [GeneratedRegex(@"(?m)^[ \t]*(?:=+|-+)[ \t]*$")]
    private static partial Regex SetextUnderline();

    [GeneratedRegex(@"(?m)^[ \t]*\|?[ \t]*:?-{3,}:?[ \t]*(?:\|[ \t]*:?-{3,}:?[ \t]*)+\|?[ \t]*$")]
    private static partial Regex TableSeparator();

    [GeneratedRegex(@"`+([^`]*?)`+")]
    private static partial Regex InlineCode();

    [GeneratedRegex(@"\$\$?|\\[()[\]]")]
    private static partial Regex MathBoundary();

    [GeneratedRegex(@"(?<!\\)(?:\*{1,3}|_{1,3}|~{2}|={2})")]
    private static partial Regex EmphasisMarker();

    [GeneratedRegex(@"\|")]
    private static partial Regex TablePipe();

    [GeneratedRegex(@"\\([\\`*{}\[\]()#+\-.!_>~|])")]
    private static partial Regex EscapedPunctuation();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
