#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if ! command -v wslpath >/dev/null 2>&1; then
  echo "Este script requer WSL com interoperabilidade Windows habilitada. Consulte README.md." >&2
  exit 1
fi
scripts/dotnet.sh publish tests/ClipboardSaver.Windows.Tests \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=embedded -o artifacts/windows-tests
artifacts/windows-tests/ClipboardSaver.Windows.Tests.exe \
  --results "$(wslpath -w "$PWD/artifacts/windows-test-results.txt")"
