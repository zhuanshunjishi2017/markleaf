namespace MarkLeaf.Prototype;

internal sealed record PrototypeOptions(string? SmokeTestOutputPath, string? InitialScrollTarget)
{
    public bool IsSmokeTest => !string.IsNullOrWhiteSpace(SmokeTestOutputPath);

    public static PrototypeOptions Parse(string[] args)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], "--smoke-test", StringComparison.OrdinalIgnoreCase))
            {
                return new PrototypeOptions(Path.GetFullPath(args[index + 1]), null);
            }

            if (string.Equals(args[index], "--scroll-to", StringComparison.OrdinalIgnoreCase))
            {
                return new PrototypeOptions(null, args[index + 1]);
            }
        }

        return new PrototypeOptions(null, null);
    }
}
