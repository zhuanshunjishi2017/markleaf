#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BUILD_DIR="$(mktemp -d "${TMPDIR:-/tmp}/markleaf-changelog-launch-test.XXXXXX")"
trap 'rm -rf "$BUILD_DIR"' EXIT
SDK_PATH="${SDKROOT:-$(xcrun --sdk macosx --show-sdk-path)}"
cp "$ROOT_DIR/script/tests/ChangelogLaunchPolicyTest.swift" "$BUILD_DIR/main.swift"
swiftc -sdk "$SDK_PATH" -module-cache-path "$BUILD_DIR/module-cache" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/ChangelogLaunchPolicy.swift" \
  "$BUILD_DIR/main.swift" \
  -o "$BUILD_DIR/test"
"$BUILD_DIR/test"

settings_source="$ROOT_DIR/Sources/MarkLeaf/Services/AppSettings.swift"
app_delegate_source="$ROOT_DIR/Sources/MarkLeaf/App/AppDelegate.swift"
grep -q 'var lastChangelogShownVersion: String?' "$settings_source" || {
  echo "FAIL: AppSettings must persist the changelog marker" >&2
  exit 1
}
grep -q 'loadedFromExistingSettingsFile' "$settings_source" || {
  echo "FAIL: SettingsService must expose whether startup loaded an existing settings file" >&2
  exit 1
}
grep -q 'presentChangelogAfterUpdateIfNeeded' "$app_delegate_source" || {
  echo "FAIL: application startup must evaluate the changelog launch policy" >&2
  exit 1
}
