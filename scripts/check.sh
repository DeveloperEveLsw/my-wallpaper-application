#!/usr/bin/env bash
set -euo pipefail

WALLPAPER_REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WALLPAPER_DOTNET="${WALLPAPER_DOTNET:-dotnet}"
WALLPAPER_NODE="${WALLPAPER_NODE:-node}"

if ! command -v "$WALLPAPER_DOTNET" >/dev/null 2>&1; then
  echo "dotnet을 찾을 수 없습니다. WALLPAPER_DOTNET에 .NET 10 dotnet 경로를 지정하세요." >&2
  exit 1
fi

if ! command -v "$WALLPAPER_NODE" >/dev/null 2>&1; then
  echo "node를 찾을 수 없습니다. WALLPAPER_NODE에 Node.js 22 이상 경로를 지정하세요." >&2
  exit 1
fi

cd "$WALLPAPER_REPO_ROOT"
"$WALLPAPER_DOTNET" restore Wallpaper.slnx
"$WALLPAPER_DOTNET" build Wallpaper.slnx --configuration Release --no-restore
"$WALLPAPER_DOTNET" test Wallpaper.slnx --configuration Release --no-build
while IFS= read -r visualizer_script; do
  "$WALLPAPER_NODE" --check "$visualizer_script"
done < <(find src/Wallpaper.Rendering.WebView/WebAssets -type f -name '*.js' -not -path '*/vendor/*' | sort)
