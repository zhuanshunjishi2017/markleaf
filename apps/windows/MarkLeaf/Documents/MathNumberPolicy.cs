using System.Text.RegularExpressions;

namespace MarkLeaf.Documents;

internal static partial class MathNumberPolicy
{
    public static bool IsValid(string value) => value.Trim() is { Length: 0 } || Hierarchy().IsMatch(value.Trim());

    [GeneratedRegex(@"\A[0-9]+(?:\.[0-9]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex Hierarchy();
}
