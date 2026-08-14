struct StartupActionState {
    enum IncomingFileDisposition: Equatable {
        case pendingInitialIntent(String)
        case openImmediately(String)
    }

    private(set) var isConsumed = false

    func disposition(forIncomingFile path: String) -> IncomingFileDisposition {
        isConsumed ? .openImmediately(path) : .pendingInitialIntent(path)
    }

    mutating func consume() -> Bool {
        guard !isConsumed else { return false }
        isConsumed = true
        return true
    }
}
