using MarkLeaf.App;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class WorkspaceMutationChannelTests
{
    [TestMethod]
    public async Task SuccessfulMutationReachesOtherWindowOnly()
    {
        var identity = "test-" + Guid.NewGuid().ToString("N");
        var received = new TaskCompletionSource<WorkspaceMutationNotice>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var receiver = new WorkspaceMutationChannel(identity, notice => received.TrySetResult(notice));
        var notice = new WorkspaceMutationNotice(@"C:\work\中文 空格", @"C:\work\renamed", true);
        Assert.IsTrue(await WorkspaceMutationChannel.SendAsync(identity, notice));
        Assert.AreEqual(notice, await received.Task.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.IsFalse(await WorkspaceMutationChannel.SendAsync("missing-" + identity, notice));
    }
}
