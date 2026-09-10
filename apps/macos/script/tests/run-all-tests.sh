#!/usr/bin/env bash
set -euo pipefail

TESTS_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TESTS=("$TESTS_DIR"/*-test.sh)
TOTAL=${#TESTS[@]}
failures=0
INDEX=0
for script in "${TESTS[@]}"; do
  name="$(basename "$script")"
  INDEX=$((INDEX + 1))
  echo "RUN [$INDEX/$TOTAL] $name"
  if bash "$script" > /dev/null 2>&1; then
    echo "PASS $name"
  else
    echo "FAIL $name"
    failures=$((failures + 1))
  fi
done
if [ "$failures" -gt 0 ]; then
  echo "FAILED: $failures of $TOTAL test script(s)"
  exit 1
fi
echo "ALL PASS: $TOTAL test script(s)"
