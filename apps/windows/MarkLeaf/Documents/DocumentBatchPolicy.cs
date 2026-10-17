namespace MarkLeaf.Documents;

internal static class DocumentBatchPolicy
{
    public static async Task<bool> RunAsync<T>(IEnumerable<T> items, Func<T, Task<bool>> operation)
    {
        foreach (var item in items.ToArray())
            if (!await operation(item)) return false;
        return true;
    }
}
