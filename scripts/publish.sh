#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
scripts/dotnet.sh publish src/ClipboardSaver.Windows/ClipboardSaver.Windows.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=embedded -p:PublishTrimmed=false \
  -o artifacts/win-x64
