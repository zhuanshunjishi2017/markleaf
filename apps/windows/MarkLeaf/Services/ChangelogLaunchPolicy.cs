namespace MarkLeaf.Services;

public static class ChangelogLaunchPolicy
{
    public static bool ShouldOpen(
        string currentVersion,
        string? lastShownVersion,
        bool settingsFileExisted)
    {
        if (!TryNormalizeVersion(currentVersion, out var current))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(lastShownVersion))
        {
            return settingsFileExisted;
        }

        return TryNormalizeVersion(lastShownVersion, out var lastShown)
            && CompareVersions(current, lastShown) > 0;
    }

    private static bool TryNormalizeVersion(string text, out int[] version)
    {
        var numericPart = new string(text
            .TakeWhile(character => char.IsDigit(character) || character == '.')
            .ToArray());
        var segments = numericPart.Split('.');
        version = [];
        if (segments.Length == 0)
        {
            return false;
        }

        foreach (var segment in segments)
        {
            if (segment.Length == 0)
            {
                return false;
            }

            if (!int.TryParse(segment, out var number))
            {
                return false;
            }

            version = [.. version, number];
        }

        return true;
    }

    private static int CompareVersions(int[] left, int[] right)
    {
        var count = Math.Max(left.Length, right.Length);
        for (var index = 0; index < count; index++)
        {
            var leftValue = index < left.Length ? left[index] : 0;
            var rightValue = index < right.Length ? right[index] : 0;
            if (leftValue != rightValue)
            {
                return leftValue.CompareTo(rightValue);
            }
        }

        return 0;
    }
}
