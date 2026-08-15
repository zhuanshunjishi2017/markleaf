using MarkLeaf.Services;

namespace MarkLeaf.Tests;

internal static class LocTestHelper
{
    private static bool _initialized;

    public static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;

        var mainAssemblyDir = Path.GetDirectoryName(typeof(MarkLeaf.Program).Assembly.Location)!;
        var localesDir = Path.Combine(mainAssemblyDir, "Resources", "Locales");

        if (!Directory.Exists(localesDir))
        {
            var testAssemblyDir = Path.GetDirectoryName(typeof(LocTestHelper).Assembly.Location)!;
            var repoRoot = Path.GetFullPath(Path.Combine(testAssemblyDir, "..", "..", "..", ".."));
            localesDir = Path.Combine(repoRoot, "src", "MarkLeaf", "Resources", "Locales");
        }

        Loc.Initialize(localesDir, "zh-CN");
    }
}
