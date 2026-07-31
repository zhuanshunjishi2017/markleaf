namespace MarkLeaf.Editor;

public enum EditorLifecycleState
{
    NotStarted,
    Initializing,
    LoadingPage,
    WaitingForEditorReady,
    Ready,
    Failed,
}
