using MarkLeaf.Services;

namespace MarkLeaf.Workspace;

internal static class WorkspaceDocumentTimeFormatter
{
    public static string Format(DateTime modifiedTime, DateTime now)
    {
        var dayDifference = (now.Date - modifiedTime.Date).Days;
        return dayDifference switch
        {
            <= 0 => Loc.Format("time.today", modifiedTime.ToString("HH:mm")),
            1 => Loc.Format("time.yesterday", modifiedTime.ToString("HH:mm")),
            <= 7 => Loc.Format("time.monthDay", modifiedTime.Month, modifiedTime.Day),
            _ when modifiedTime.Year == now.Year => Loc.Format("time.monthDay", modifiedTime.Month, modifiedTime.Day),
            _ => $"{modifiedTime.Year}/{modifiedTime.Month}/{modifiedTime.Day}",
        };
    }
}
