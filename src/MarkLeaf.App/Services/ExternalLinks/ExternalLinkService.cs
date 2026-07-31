using System.Diagnostics;

namespace MarkLeaf.Services.ExternalLinks;

internal static class ExternalLinkService
{
    public static bool IsAllowed(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https" or "mailto";
    }

    public static void Open(string value)
    {
        if (!IsAllowed(value))
        {
            throw new ArgumentException("The link protocol is not allowed.", nameof(value));
        }

        Process.Start(new ProcessStartInfo(value) { UseShellExecute = true });
    }
}
