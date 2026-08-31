#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
APP_DIR="${1:-$REPO_ROOT/artifacts/macos/QuickTranslate.app}"
CONTENTS_DIR="$APP_DIR/Contents"
MACOS_DIR="$CONTENTS_DIR/MacOS"

rm -rf "$APP_DIR"
mkdir -p "$MACOS_DIR"

xcrun swiftc \
  -O \
  -target "$(uname -m)-apple-macosx13.0" \
  -framework AppKit \
  -framework ApplicationServices \
  -framework ServiceManagement \
  "$SCRIPT_DIR/Sources/QuickTranslateCore/OfficialCodexTranslator.swift" \
  "$SCRIPT_DIR/Sources/QuickTranslateCore/PasteTargetPolicy.swift" \
  "$SCRIPT_DIR/Sources/QuickTranslateCore/TripleSpaceSequence.swift" \
  "$SCRIPT_DIR/Sources/QuickTranslate/main.swift" \
  -o "$MACOS_DIR/QuickTranslate"

cp "$SCRIPT_DIR/Info.plist" "$CONTENTS_DIR/Info.plist"
codesign --force --deep --sign - "$APP_DIR"

echo "Built $APP_DIR"
