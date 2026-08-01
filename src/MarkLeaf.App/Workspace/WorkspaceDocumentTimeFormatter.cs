namespace MarkLeaf.Workspace;

internal static class WorkspaceDocumentTimeFormatter
{
    public static string Format(DateTime modifiedTime, DateTime now)
    {
        var dayDifference = (now.Date - modifiedTime.Date).Days;
        return dayDifference switch
        {
            <= 0 => modifiedTime.ToString("HH:mm"),
            1 => $"昨天{modifiedTime:HH:mm}",
            2 => $"前天{modifiedTime:HH:mm}",
            <= 7 => $"{dayDifference}天前",
            _ when modifiedTime.Year == now.Year => $"{modifiedTime.Month}月{modifiedTime.Day}日",
            _ => $"{modifiedTime.Year}/{modifiedTime.Month}/{modifiedTime.Day}",
        };
    }
}
