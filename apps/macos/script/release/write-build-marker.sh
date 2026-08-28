#!/usr/bin/env bash

set -euo pipefail

MARKER_PATH="${1:?marker output path is required}"
APP_VERSION="${2:?app version is required}"
APP_BUILD="${3:?app build is required}"
COMMIT_SHA="${4:?commit SHA is required}"

[[ "$APP_BUILD" =~ ^[0-9]+$ ]] || {
    echo "build number must be numeric" >&2
    exit 1
}

mkdir -p "$(dirname "$MARKER_PATH")"
cat > "$MARKER_PATH" <<MARKER
version=$APP_VERSION
build=$APP_BUILD
commit=$COMMIT_SHA
MARKER
