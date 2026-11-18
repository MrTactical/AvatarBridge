#!/usr/bin/env bash
# Copy the working tree into every Unity project that already has the
# toolkit installed.
#
#   Dev/Build/deploy.sh                 every project that has it
#   Dev/Build/deploy.sh "/d/path/A"     just these
#
# No arguments: scans AVATARBRIDGE_PROJECT_ROOTS (colon-separated, Dev/local.cfg).
#
# The corpus runs against the DEPLOYED copy, so skipping a deploy tests
# nothing (2026-08-27: a 4.3.4 deploy measured while the repo was at 4.4.0).
# Copies, never mirrors: deleting would remove the harness the package build
# put under Editor/DevTools. Files gone from the repo are listed as ORPHAN.
set -euo pipefail
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SHIPPED=(AvatarScaler Editor FaceTracking Presets Runtime)
FILES=(LICENSE.md README.md)

# shellcheck source=/dev/null
[ -f "$REPO/Dev/local.cfg" ] && . "$REPO/Dev/local.cfg"

projects=("$@")
if [ ${#projects[@]} -eq 0 ]; then
    [ -n "${AVATARBRIDGE_PROJECT_ROOTS:-}" ] || {
        echo "set AVATARBRIDGE_PROJECT_ROOTS in Dev/local.cfg, or name projects on the command line" >&2
        exit 1
    }
    while IFS= read -r d; do projects+=("$d"); done < <(
        IFS=: read -ra roots <<< "$AVATARBRIDGE_PROJECT_ROOTS"
        for root in "${roots[@]}"; do
            for p in "$root"/*/; do
                [ -d "$p/Assets/AvatarBridge" ] && echo "${p%/}"
            done
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
    # A stale harness copy that stops compiling stops the project (a corpus run
    # died on one). Refresh by name what is present; add and delete nothing.
    if [ -d "$dest/Editor/DevTools" ]; then
        stale=0
        while IFS= read -r have; do
            # Harness folders only, never all of Dev: a gitignored spike sharing
            # a name replaced the harness copy. Last match wins, as in the
            # package build. "|| true": a missing folder fails find under pipefail.
            src=$(find "$REPO"/Dev/{Corpus,Tests,Probes,Scenes,Tester} -name "$(basename "$have")" -type f 2>/dev/null | tail -n 1) || true
            [ -n "$src" ] || continue
            cmp -s "$src" "$have" || { cp "$src" "$have"; stale=$((stale + 1)); }
        done < <(find "$dest/Editor/DevTools" -maxdepth 1 -name '*.cs')
        if [ "$stale" -gt 0 ]; then echo "  refreshed $stale dev harness file(s)"; fi
    fi
    for f in "${FILES[@]}"; do
        [ -f "$REPO/$f" ] && cp "$REPO/$f" "$dest/"
        [ -f "$REPO/$f.meta" ] && cp "$REPO/$f.meta" "$dest/"
    done
    # A file the repo removed keeps compiling here. Name it; delete by hand.
    for d in "${SHIPPED[@]}"; do
        [ -d "$dest/$d" ] || continue
        while IFS= read -r f; do
            [ -e "$REPO/${f#"$dest"/}" ] || echo "  ORPHAN (not in repo): ${f#"$dest"/}"
        done < <(find "$dest/$d" -path "$dest/Editor/DevTools" -prune -o -type f ! -name '*.meta' -print)
    done
    now=$(sed -n 's/.*const string Version = "\(.*\)".*/\1/p' "$dest/Editor/BridgeDefines.cs")
    echo "  ${was:-none} -> $now   $proj"
done
