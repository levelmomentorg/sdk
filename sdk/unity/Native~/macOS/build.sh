#!/bin/sh
# Build the macOS WebView host bundle the SDK ships prebuilt.
#
# Unity does not compile Objective-C source for macOS standalone players, so
# the bundle is committed under Runtime/Plugins/macOS. Rebuild it with this
# script whenever LevelMomentWebView.m changes, and commit both together.
set -eu

here="$(cd "$(dirname "$0")" && pwd)"
out="$here/../../Runtime/Plugins/macOS/LevelMomentWebView.bundle"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# The navigation fence is the security boundary; refuse to build a bundle
# whose fence fails its cases.
xcrun clang -fobjc-arc -Wall -Werror -framework Cocoa -framework WebKit \
  -o "$work/OriginFenceTests" "$here/OriginFenceTests.m"
"$work/OriginFenceTests"

for arch in arm64 x86_64; do
  xcrun clang -fobjc-arc -fvisibility=hidden -O2 -Wall -Werror \
    -arch "$arch" -mmacosx-version-min=10.13 \
    -bundle -framework Cocoa -framework WebKit \
    -o "$work/LevelMomentWebView.$arch" "$here/LevelMomentWebView.m"
done

# Assemble and sign in a scratch copy. The committed bundle also holds the
# .meta files Unity needs for an immutable package; signing in place would seal
# them into the signature, and the player build (which leaves them out) would
# then carry a broken signature.
stage="$work/LevelMomentWebView.bundle"
mkdir -p "$stage/Contents/MacOS"
xcrun lipo -create -output "$stage/Contents/MacOS/LevelMomentWebView" \
  "$work/LevelMomentWebView.arm64" "$work/LevelMomentWebView.x86_64"

cat > "$stage/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key>
  <string>en</string>
  <key>CFBundleExecutable</key>
  <string>LevelMomentWebView</string>
  <key>CFBundleIdentifier</key>
  <string>com.levelmoment.sdk.webview</string>
  <key>CFBundleInfoDictionaryVersion</key>
  <string>6.0</string>
  <key>CFBundleName</key>
  <string>LevelMomentWebView</string>
  <key>CFBundlePackageType</key>
  <string>BNDL</string>
  <key>CFBundleShortVersionString</key>
  <string>1</string>
  <key>CFBundleVersion</key>
  <string>1</string>
</dict>
</plist>
PLIST

# Ad-hoc sign so a hardened-runtime player can load it; the publisher's own
# signing step re-signs the bundle inside their app.
xcrun codesign --force --sign - "$stage"

mkdir -p "$out/Contents/MacOS" "$out/Contents/_CodeSignature"
cp "$stage/Contents/Info.plist" "$out/Contents/Info.plist"
cp "$stage/Contents/MacOS/LevelMomentWebView" "$out/Contents/MacOS/LevelMomentWebView"
cp "$stage/Contents/_CodeSignature/CodeResources" "$out/Contents/_CodeSignature/CodeResources"
echo "Built $out"
