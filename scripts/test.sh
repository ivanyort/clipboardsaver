#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
scripts/dotnet.sh run --project tests/ClipboardSaver.Core.Tests -c Release
scripts/dotnet.sh build src/ClipboardSaver.Windows/ClipboardSaver.Windows.csproj -c Release
