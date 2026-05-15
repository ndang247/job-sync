#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repo_root"

chmod +x api-web/.githooks/run-checks.sh api-web/.githooks/pre-commit api-web/.githooks/pre-push

git config core.hooksPath api-web/.githooks

echo "Git hooks configured."
echo "hooksPath=$(git config --get core.hooksPath)"
