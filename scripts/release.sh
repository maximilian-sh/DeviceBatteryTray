#!/usr/bin/env bash
set -euo pipefail

# Usage:
#   ./scripts/release.sh 4.0.2   # bump to 4.0.2, commit, push, tag v4.0.2, push tag
#   ./scripts/release.sh          # auto-commit/push, then tag using current VersionPrefix

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
CSPROJ="$ROOT_DIR/LGSTrayUI/LGSTrayUI.csproj"

if [[ ! -f "$CSPROJ" ]]; then
  echo "Could not find LGSTrayUI.csproj at $CSPROJ" >&2
  exit 1
fi

VERSION_ARG="${1-}"

current_branch=$(git rev-parse --abbrev-ref HEAD)

# Always commit and push any local changes first
git add -A
git commit -m "chore: release prep" || true
git push -u origin "$current_branch" || git push origin "$current_branch"

if [[ -n "$VERSION_ARG" ]]; then
  # Bump VersionPrefix in csproj (simple in-place edit)
  # Note: requires GNU sed on macOS: brew install gnu-sed (or use ed)
  if sed --version >/dev/null 2>&1; then
    SED=sed
  else
    # macOS fallback to gsed if available
    if command -v gsed >/dev/null 2>&1; then SED=gsed; else SED=sed; fi
  fi

  $SED -i "s#<VersionPrefix>[^<]*</VersionPrefix>#<VersionPrefix>${VERSION_ARG}</VersionPrefix>#" "$CSPROJ"
  git add "$CSPROJ"
  git commit -m "chore: bump version to ${VERSION_ARG}" || true
  git push -u origin "$current_branch" || git push origin "$current_branch"
  TAG="v${VERSION_ARG}"
else
  # Extract current VersionPrefix
  CURRENT=$(grep -oE '<VersionPrefix>[^<]+' "$CSPROJ" | head -1 | cut -d '>' -f2)
  if [[ -z "$CURRENT" ]]; then
    echo "No VersionPrefix found; provide a version arg" >&2
    exit 1
  fi
  TAG="v${CURRENT}"
fi

# Recreate tag locally and push
git tag -d "$TAG" >/dev/null 2>&1 || true
git tag "$TAG"
git push origin "$TAG"

echo "Pushed tag $TAG. GitHub Actions will build and attach the zip to the release."


