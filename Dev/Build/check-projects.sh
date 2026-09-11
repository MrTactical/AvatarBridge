#!/usr/bin/env bash
# Compile the repo together with each deployed project's own scripts: the
# probes, tests and helpers in its Assets that call into AvatarBridge or
# YAPS. compile-check.sh proves the repo builds; this proves a rename did
# not break something that only lives in a project.
#
#   Dev/Build/check-projects.sh                every project deploy.sh finds
#   Dev/Build/check-projects.sh "/d/path/A"    just these
#
# The VRChat SDK define goes on only where the project has the SDK, so a
# ChilloutVR-only project is checked as one. Errors are shown for the
# project's files; the repo's own are compile-check.sh's job.
set -u
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
UNITY="${UNITY_DATA:-/c/Program Files/Unity/Hub/Editor/2022.3.22f1/Editor/Data}"
MONO="$UNITY/MonoBleedingEdge/bin/mono.exe"
CSC="$UNITY/MonoBleedingEdge/lib/mono/4.5/csc.exe"
NETSTANDARD="$UNITY/MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# shellcheck source=/dev/null
[ -f "$REPO/Dev/local.cfg" ] && . "$REPO/Dev/local.cfg"

projects=("$@")
if [ ${#projects[@]} -eq 0 ]; then
    [ -n "${AVATARBRIDGE_PROJECT_ROOTS:-}" ] || {
        echo "set AVATARBRIDGE_PROJECT_ROOTS in Dev/local.cfg, or name projects on the command line" >&2
        exit 1
    }
    IFS=: read -ra roots <<< "$AVATARBRIDGE_PROJECT_ROOTS"
    for root in "${roots[@]}"; do
        for p in "$root"/*/; do
            [ -d "$p/Assets/AvatarBridge" ] && projects+=("${p%/}")
        done
    done
fi
[ ${#projects[@]} -gt 0 ] || { echo "no projects with AvatarBridge installed" >&2; exit 1; }

fail=0
for proj in "${projects[@]}"; do
    rsp="$WORK/build.rsp"
    dll="$WORK/out.dll"
    rm -f "$dll"
    sdk=$(find "$proj/Packages" "$proj/Library/PackageCache" -path "*VRCSDK/Plugins*" -name "VRC*.dll" 2>/dev/null)
    {
        echo "-target:library"
        echo "-out:\"$(cygpath -w "$dll")\""
        echo "-define:CVR_CCK_EXISTS;${sdk:+VRC_SDK_VRCSDK3;}AVATARBRIDGE_YAPS;UNITY_EDITOR;UNITY_2022_3_OR_NEWER"
        # 0436: the deployed copy in ScriptAssemblies defines the same types
        # as the repo sources, and the sources win.
        echo "-nowarn:0169,0414,0649,0067,0436"
        echo "-r:\"$(cygpath -w "$NETSTANDARD")\""
        for d in "$UNITY/Managed/UnityEngine"/*.dll "$proj/Library/ScriptAssemblies"/*.dll; do
            [ -f "$d" ] && echo "-r:\"$(cygpath -w "$d")\""
        done
        [ -n "$sdk" ] && while read -r f; do echo "-r:\"$(cygpath -w "$f")\""; done <<< "$sdk"
        find "$REPO/Editor" "$REPO/Runtime" -name '*.cs' | while read -r f; do echo "\"$(cygpath -w "$f")\""; done
        grep -rl --include=*.cs "AvatarBridge\|Yaps" "$proj/Assets" 2>/dev/null | grep -v "/Assets/AvatarBridge/" |
            while read -r f; do echo "\"$(cygpath -w "$f")\""; done
    } > "$rsp"

    if [ -n "$sdk" ]; then name="$(basename "$proj") (VRChat SDK)"; else name="$(basename "$proj") (no VRChat SDK)"; fi
    errors=$("$MONO" "$CSC" "@$(cygpath -w "$rsp")" 2>&1 | grep "error CS" | sort -u)
    if [ -f "$dll" ]; then
        echo "  ok      $name"
    else
        fail=1
        echo "  FAILED  $name"
        mine=$(grep -v -F "$(cygpath -w "$REPO")\\" <<< "$errors" | head -15)
        if [ -n "$mine" ]; then sed 's/^/          /' <<< "$mine"
        else echo "          the repo itself failed; run compile-check.sh"; fi
    fi
done

exit $fail
