#!/usr/bin/env bash
set -euo pipefail
MARKLEAF_BEHAVIOR_TEST_SOURCE=ImageImportRoutingBehaviorTest.swift bash "$(dirname "${BASH_SOURCE[0]}")/closed-tab-restore-behavior-test.sh"
