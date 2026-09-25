#!/usr/bin/env bash
set -euo pipefail

# Build environment
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

# Use portable JDK if available
if [ -d "$HOME/tools/jdk-17.0.20.1+1" ]; then
    export JAVA_HOME="$HOME/tools/jdk-17.0.20.1+1"
    export PATH="$JAVA_HOME/bin:$PATH"
fi

# Android SDK
if [ -d "$HOME/tools/android-sdk" ]; then
    export ANDROID_HOME="$HOME/tools/android-sdk"
fi

echo "==> Building GramChat Android APK..."
./gradlew assembleDebug --no-daemon

APK="app/build/outputs/apk/debug/app-debug.apk"
if [ -f "$APK" ]; then
    VERSION=$(grep -oP 'versionName\s*=\s*"\K[^"]+' app/build.gradle.kts 2>/dev/null || echo "1.0.0")
    OUTPUT="GramChat-Android-v${VERSION}-debug.apk"
    cp "$APK" "$SCRIPT_DIR/../$OUTPUT"
    echo "==> APK built: $SCRIPT_DIR/../$OUTPUT"
else
    echo "==> ERROR: APK not found at $APK"
    exit 1
fi
