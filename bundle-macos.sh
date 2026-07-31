#!/bin/bash
# Bundle the published macOS app into a proper .app for double-click launch
set -e

ARCH="${1:-osx-arm64}"
PUBLISH_DIR="$(cd "$(dirname "$0")/publish/${ARCH}" && pwd)"
BUNDLE_DIR="$(cd "$(dirname "$0")" && pwd)/PgOperator.app"

echo "📦 Creating macOS .app bundle from ${PUBLISH_DIR}..."

# Create .app structure
rm -rf "${BUNDLE_DIR}"
mkdir -p "${BUNDLE_DIR}/Contents/MacOS"
mkdir -p "${BUNDLE_DIR}/Contents/Resources"

# Copy ALL published files into the bundle (exclude the .app itself if re-running)
cp -R "${PUBLISH_DIR}/" "${BUNDLE_DIR}/Contents/MacOS/"

# Create Info.plist
cat > "${BUNDLE_DIR}/Contents/Info.plist" << 'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key><string>PgOperator</string>
    <key>CFBundleDisplayName</key><string>PgOperator</string>
    <key>CFBundleExecutable</key><string>PgOperator.App</string>
    <key>CFBundleIdentifier</key><string>dev.diego.pgoperator</string>
    <key>CFBundleVersion</key><string>1.0.0</string>
    <key>CFBundleShortVersionString</key><string>1.0</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST

# Ad-hoc sign so macOS allows it to run as a bundled app
codesign --force --deep --sign - "${BUNDLE_DIR}" 2>/dev/null

echo "✅ Done!"
echo "   Double-click: open ${BUNDLE_DIR}"
echo "   Or drag PgOperator.app to /Applications"
