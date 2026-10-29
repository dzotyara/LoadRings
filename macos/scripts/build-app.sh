#!/usr/bin/env bash
# Builds LoadRings.app into ./build (ad-hoc signed, runs locally without a developer account).
set -euo pipefail

cd "$(dirname "$0")/.."
VERSION="${VERSION:-0.1.0}"
APP="build/LoadRings.app"

# Build each architecture separately and merge: `--arch a --arch b` needs a full Xcode install.
BINS=()
for ARCH in arm64 x86_64; do
    FLAGS=(-c release --triple "$ARCH-apple-macosx14.0" --scratch-path ".build/$ARCH")
    swift build "${FLAGS[@]}"
    BINS+=("$(swift build "${FLAGS[@]}" --show-bin-path)/LoadRings")
done

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
lipo -create "${BINS[@]}" -output "$APP/Contents/MacOS/LoadRings"

cat > "$APP/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key><string>LoadRings</string>
    <key>CFBundleDisplayName</key><string>LoadRings</string>
    <key>CFBundleIdentifier</key><string>io.github.loadrings</string>
    <key>CFBundleExecutable</key><string>LoadRings</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>CFBundleShortVersionString</key><string>${VERSION}</string>
    <key>CFBundleVersion</key><string>${VERSION}</string>
    <key>LSMinimumSystemVersion</key><string>14.0</string>
    <key>LSUIElement</key><true/>
    <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
EOF

codesign --force --sign - "$APP"
echo "Built $APP"
