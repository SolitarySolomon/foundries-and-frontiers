#!/usr/bin/env bash
# Builds Foundries & Frontiers and packages it as an installable Vintage Story mod zip.
set -e
export PATH="/home/claude/.dotnet:$PATH"
cd "$(dirname "$0")"

VERSION=$(grep -oP '"version"\s*:\s*"\K[^"]+' modinfo.json)
OUT="dist"
STAGE="$OUT/stage"

rm -rf "$STAGE" && mkdir -p "$STAGE"
dotnet build -c Release -v quiet --nologo

cp bin/Release/FoundriesFrontiers.dll "$STAGE/"
cp modinfo.json "$STAGE/"
cp -r assets "$STAGE/"

ZIP="$OUT/foundriesfrontiers_${VERSION}.zip"
rm -f "$ZIP"
(cd "$STAGE" && zip -qr "../foundriesfrontiers_${VERSION}.zip" .)

echo "packaged: $ZIP  ($(du -h "$ZIP" | cut -f1))"
unzip -l "$ZIP"
