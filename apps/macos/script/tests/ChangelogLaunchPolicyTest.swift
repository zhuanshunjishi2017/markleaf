import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

expect(ChangelogLaunchPolicy.shouldOpen(
    currentVersion: "1.7.8",
    lastShownVersion: nil,
    settingsFileExisted: true
), "an upgrade from a version without the marker opens the changelog once")
expect(ChangelogLaunchPolicy.shouldOpen(
    currentVersion: "1.7.8 (Build 700)",
    lastShownVersion: nil,
    settingsFileExisted: true
), "version strings with build metadata still resolve to the release version")
expect(!ChangelogLaunchPolicy.shouldOpen(
    currentVersion: "1.7.8",
    lastShownVersion: nil,
    settingsFileExisted: false
), "a fresh installation does not open the changelog automatically")
expect(ChangelogLaunchPolicy.shouldOpen(
    currentVersion: "1.8.0",
    lastShownVersion: "1.7.10",
    settingsFileExisted: true
), "a newer application version opens the changelog")
expect(!ChangelogLaunchPolicy.shouldOpen(
    currentVersion: "1.7.8",
    lastShownVersion: "1.7.8",
    settingsFileExisted: true
), "the same version opens the changelog only once")
expect(!ChangelogLaunchPolicy.shouldOpen(
    currentVersion: "1.7.8",
    lastShownVersion: "1.7.8 (Build 700)",
    settingsFileExisted: true
), "build metadata does not make the same release look newer")

print("PASS")
