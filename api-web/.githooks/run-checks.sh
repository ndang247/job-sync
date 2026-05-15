#!/usr/bin/env bash
set -euo pipefail

repo_root="$(git rev-parse --show-toplevel)"
cd "$repo_root/api-web"

echo "[git-hooks] Running required .NET quality checks (api-web)..."

if ! command -v dotnet >/dev/null 2>&1; then
  echo "[git-hooks] dotnet CLI not found. Install .NET SDK before committing/pushing."
  exit 1
fi

echo "[git-hooks] dotnet restore"
dotnet restore api-web.slnx --nologo

echo "[git-hooks] dotnet build (warnings treated as errors)"
dotnet build api-web.slnx --nologo --no-restore -warnaserror

echo "[git-hooks] dotnet test"
dotnet test api-web.slnx --nologo --no-build

echo "[git-hooks] All checks passed."
