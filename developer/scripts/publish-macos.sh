#!/usr/bin/env bash
set -euo pipefail

developer_root="$(cd "$(dirname "$0")/.." && pwd)"
install_root="$(dirname "$developer_root")"
rid="${1:-osx-universal}"
case "$rid" in
  osx-arm64|osx-x64|osx-universal) ;;
  *) echo "Usage: $0 [osx-universal|osx-arm64|osx-x64]" >&2; exit 2 ;;
esac

publish_root="$(mktemp -d "$developer_root/.publish.XXXXXX")"
publish_dir="$publish_root/$rid"
bundle_dir="$install_root/SAMERIDER.app"
zip_path="$install_root/SAMERIDER-$rid.zip"
project="$developer_root/src/SAMERIDER.App/SAMERIDER.App.csproj"

publish_one() {
  local target_rid="$1"
  local target_dir="$publish_root/$target_rid"
  dotnet publish "$project" -c Release -r "$target_rid" --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:DebugType=None -p:DebugSymbols=false -o "$target_dir"
}

trap 'rm -rf "$publish_root"' EXIT
mkdir -p "$publish_root"
if [[ "$rid" == "osx-universal" ]]; then
  command -v lipo >/dev/null || { echo "Universal macOS output requires Apple's lipo tool (run this script on macOS with Xcode Command Line Tools installed)." >&2; exit 1; }
  publish_one osx-arm64
  publish_one osx-x64
  mkdir -p "$publish_dir"
  cp -R "$publish_root/osx-arm64/." "$publish_dir/"
  rm -f "$publish_dir/SAMERIDER"
  lipo -create -output "$publish_dir/SAMERIDER" "$publish_root/osx-arm64/SAMERIDER" "$publish_root/osx-x64/SAMERIDER"
else
  publish_one "$rid"
fi

rm -rf "$bundle_dir" "$zip_path"
mkdir -p "$bundle_dir/Contents/MacOS" "$bundle_dir/Contents/Resources"
cp -R "$publish_dir/." "$bundle_dir/Contents/MacOS/"
cp "$developer_root/packaging/macos/Info.plist" "$bundle_dir/Contents/Info.plist"
cp "$developer_root/packaging/macos/SAMERIDER.icns" "$bundle_dir/Contents/Resources/SAMERIDER.icns"
chmod +x "$bundle_dir/Contents/MacOS/SAMERIDER"

if [[ -n "${CODESIGN_IDENTITY:-}" ]]; then
  entitlements="$developer_root/packaging/macos/Entitlements.plist"
  codesign --force --options runtime --timestamp --entitlements "$entitlements" \
    --sign "$CODESIGN_IDENTITY" "$bundle_dir/Contents/MacOS/SAMERIDER"
  codesign --force --options runtime --timestamp --sign "$CODESIGN_IDENTITY" "$bundle_dir"
  codesign --verify --deep --strict --verbose=2 "$bundle_dir"
else
  echo "Warning: app is unsigned. macOS Gatekeeper blocks unsigned apps downloaded from the internet; a downloaded ZIP can therefore show an app-open error." >&2
fi

ditto -c -k --sequesterRsrc --keepParent "$bundle_dir" "$zip_path"
if [[ -n "${CODESIGN_IDENTITY:-}" && -n "${APPLE_ID:-}" && -n "${APPLE_TEAM_ID:-}" && -n "${APP_SPECIFIC_PASSWORD:-}" ]]; then
  xcrun notarytool submit "$zip_path" --apple-id "$APPLE_ID" --team-id "$APPLE_TEAM_ID" \
    --password "$APP_SPECIFIC_PASSWORD" --wait
  xcrun stapler staple "$bundle_dir"
  rm -f "$zip_path"
  ditto -c -k --sequesterRsrc --keepParent "$bundle_dir" "$zip_path"
else
  echo "Not notarized. Gatekeeper can still block this downloaded app. Distribution requires a Developer ID signature and notarization; set CODESIGN_IDENTITY, APPLE_ID, APPLE_TEAM_ID, and APP_SPECIFIC_PASSWORD." >&2
fi

rm -rf "$publish_root"
echo "Created $bundle_dir and $zip_path for $rid"
