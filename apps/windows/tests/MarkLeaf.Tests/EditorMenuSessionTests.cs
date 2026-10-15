using MarkLeaf.Commands;
using MarkLeaf.Native.ModernMenus;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class EditorMenuSessionTests
{
    [TestMethod]
    public void ClosedBeforeClickExecutesBeforeEnd()
    {
        var events = new List<string>();
        var queue = new Queue<Action>();
        var session = Create(events);
        session.Closed(queue.Enqueue);
        session.Select(AppCommand.ToggleBold);
        queue.Dequeue()();
        CollectionAssert.AreEqual(new[] { "preview:", "execute:ToggleBold", "end" }, events);
        Assert.IsTrue(session.Completion.IsCompleted);
    }

    [TestMethod]
    public void ClickBeforeClosedExecutesOnlyOnceAndLateHoverIsIgnored()
    {
        var events = new List<string>();
        var queue = new Queue<Action>();
        var session = Create(events);
        session.Preview("toggleItalic");
        session.Select(AppCommand.ToggleBold);
        session.Select(AppCommand.ToggleItalic);
        session.Closed(queue.Enqueue);
        session.Closed(queue.Enqueue);
        while (queue.TryDequeue(out var callback)) callback();
        session.Preview("toggleItalic");
        CollectionAssert.AreEqual(new[] { "preview:toggleItalic", "preview:", "execute:ToggleBold", "end" }, events);
    }

    [TestMethod]
    public void CancelInvalidatesQueuedCloseAndLateClick()
    {
        var events = new List<string>();
        var queue = new Queue<Action>();
        var session = Create(events);
        session.Closed(queue.Enqueue);
        session.Cancel();
        session.Select(AppCommand.ToggleBold);
        session.Preview("toggleItalic");
        queue.Dequeue()();
        CollectionAssert.AreEqual(new[] { "preview:", "end" }, events);
    }

    [TestMethod]
    public async Task DisabledCommandDoesNotCommitAndCommandFailureStillEndsSession()
    {
        var events = new List<string>();
        var disabled = new EditorMenuSession(1, _ => new CommandState(false), _ => { events.Add("wrong"); return Task.CompletedTask; }, _ => {}, () => events.Add("end"));
        disabled.Select(AppCommand.ToggleBold);
        disabled.Cancel();
        CollectionAssert.AreEqual(new[] { "end" }, events);
        var throwing = new EditorMenuSession(2, _ => new CommandState(true), _ => throw new InvalidOperationException("test"), _ => {}, () => events.Add("end2"));
        throwing.Select(AppCommand.ToggleBold);
        await Assert.ThrowsAsync<InvalidOperationException>(() => throwing.Completion);
        Assert.IsTrue(throwing.Completion.IsCompleted);
        Assert.AreEqual("end2", events[^1]);
    }

    [TestMethod]
    public async Task ReentrantClosedDuringAsyncCommandDoesNotReleaseItsSelection()
    {
        var events = new List<string>();
        var queue = new Queue<Action>();
        var pending = new TaskCompletionSource();
        var session = new EditorMenuSession(1, _ => new CommandState(true), async _ =>
        {
            // A modal dialog can pump this queued Closed callback before returning.
            queue.Dequeue()();
            events.Add("started");
            await pending.Task;
            events.Add("finished");
        }, _ => {}, () => events.Add("end"));
        session.Closed(queue.Enqueue);
        session.Select(AppCommand.Cut);
        Assert.IsFalse(session.Completion.IsCompleted);
        CollectionAssert.AreEqual(new[] { "started" }, events);
        pending.SetResult();
        await session.Completion;
        CollectionAssert.AreEqual(new[] { "started", "finished", "end" }, events);
    }

    private static EditorMenuSession Create(List<string> events) => new(1, _ => new CommandState(true),
        command => { events.Add("execute:" + command); return Task.CompletedTask; }, preview => events.Add("preview:" + preview), () => events.Add("end"));
}
