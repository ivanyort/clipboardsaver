#!/usr/bin/env bash
set -euo pipefail
if command -v dotnet >/dev/null 2>&1; then
  exec dotnet "$@"
fi
clipboard_dotnet="$HOME/.local/share/clipboard-dotnet/dotnet"
if [[ -x "$clipboard_dotnet" ]]; then
  exec "$clipboard_dotnet" "$@"
fi
echo "SDK .NET 10 não encontrado. Consulte a instalação no README.md." >&2
exit 1
