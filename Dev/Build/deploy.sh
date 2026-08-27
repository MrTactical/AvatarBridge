#!/usr/bin/env bash
# Copy the working tree into every Unity project that already has the
# toolkit installed.
#
#   Dev/Build/deploy.sh                 every project that has it
#   Dev/Build/deploy.sh "D:/path/A"     just these
#
# The corpus runs against the DEPLOYED copy, never the repo, so a run
# started without deploying first measures whatever was installed last
# time and comes back clean having tested nothing. That happened on
# 2026-08-27: the corpus was launched against a 4.3.4 deploy while the
# repo sat at 4.4.0 with fifty-six commits of changes.
#
# Copies rather than mirrors. Deleting what is not in the repo would take
# the corpus harness out of the corpus project, which lives under
# Editor/DevTools and is put there by the package build, not by this.
set -euo pipefail
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SHIPPED=(AvatarScaler Editor FaceTracking Presets Runtime)
FILES=(LICENSE.md README.md)

projects=("$@")
if [ ${#projects[@]} -eq 0 ]; then
    while IFS= read -r d; do projects+=("$d"); done < <(
        for p in /e/Unity\ Projects/*/ /d/UnityVRCCrap/*/; do
            [ -d "$p/Assets/AvatarBridge" ] && echo "${p%/}"
        done)
fi
[ ${#projects[@]} -gt 0 ] || { echo "no projects with AvatarBridge installed" >&2; exit 1; }

version=$(sed -n 's/.*const string Version = "\(.*\)".*/\1/p' "$REPO/Editor/BridgeDefines.cs")
echo "deploying $version to ${#projects[@]} project(s)"

for proj in "${projects[@]}"; do
    dest="$proj/Assets/AvatarBridge"
    [ -d "$dest" ] || { echo "  SKIP (not installed): $proj"; continue; }
    was=$(sed -n 's/.*const string Version = "\(.*\)".*/\1/p' "$dest/Editor/BridgeDefines.cs" 2>/dev/null || true)
    for d in "${SHIPPED[@]}"; do
        [ -d "$REPO/$d" ] || continue
        cp -r "$REPO/$d" "$dest/"
        [ -f "$REPO/$d.meta" ] && cp "$REPO/$d.meta" "$dest/"
    done
    for f in "${FILES[@]}"; do
        [ -f "$REPO/$f" ] && cp "$REPO/$f" "$dest/"
        [ -f "$REPO/$f.meta" ] && cp "$REPO/$f.meta" "$dest/"
    done
    now=$(sed -n 's/.*const string Version = "\(.*\)".*/\1/p' "$dest/Editor/BridgeDefines.cs")
    echo "  ${was:-none} -> $now   $proj"
done
