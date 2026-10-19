enum ChangelogOpenPolicy {
    enum Destination: Equatable {
        case currentTab
        case newWindow
    }

    /// Windows opens the changelog in the active window. Match that behavior on
    /// macOS when tabs are available; single-document mode must not replace the
    /// user's current document, so it intentionally uses a separate window.
    static func destination(multiTabEnabled: Bool) -> Destination {
        multiTabEnabled ? .currentTab : .newWindow
    }
}
