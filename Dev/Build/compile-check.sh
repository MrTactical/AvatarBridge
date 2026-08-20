#!/usr/bin/env bash
# Compiles the editor scripts twice: with the VRChat SDK and without it.
#
# The second one matters. A ChilloutVR-only user installs AvatarBridge for
# the toolkit and never has the VRChat SDK, so every #else branch has to
# stand on its own. Unity in Joe's projects only ever compiles the first.
#
# Mono's csc, not the Roslyn one under lib/mono/msbuild: that one fails to
# start and prints no error CS lines at all, which reads as a clean pass.
# The assembly having been produced is the only trustworthy signal.
set -u

UNITY="${UNITY_DATA:-/c/Program Files/Unity/Hub/Editor/2022.3.22f1/Editor/Data}"
PROJECT="${1:-/d/UnityVRCCrap/Non Corpus Zone}"
REPO="$(cd "$(dirname "$0")/../.." && pwd)"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

MONO="$UNITY/MonoBleedingEdge/bin/mono.exe"
CSC="$UNITY/MonoBleedingEdge/lib/mono/4.5/csc.exe"
NETSTANDARD="$UNITY/MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll"

for tool in "$MONO" "$CSC" "$NETSTANDARD"; do
    if [ ! -f "$tool" ]; then
        echo "ABORT: missing $tool" >&2
        exit 1
    fi
done

fail=0
for defines in "CVR_CCK_EXISTS" "CVR_CCK_EXISTS;VRC_SDK_VRCSDK3"; do
    rsp="$WORK/build.rsp"
    dll="$WORK/out.dll"
    rm -f "$dll"
    {
        echo "-target:library"
        echo "-out:\"$(cygpath -w "$dll")\""
        echo "-define:$defines;UNITY_EDITOR;UNITY_2022_3_OR_NEWER"
        echo "-nowarn:0169,0414,0649,0067"
        echo "-r:\"$(cygpath -w "$NETSTANDARD")\""
    } > "$rsp"

    # The split modules only. Managed/*.dll holds the old monolithic
    # UnityEngine and UnityEditor too, and having both makes every type
    # ambiguous.
    for dll_path in "$UNITY/Managed/UnityEngine"/*.dll "$PROJECT/Library/ScriptAssemblies"/*.dll; do
        [ -f "$dll_path" ] && echo "-r:\"$(cygpath -w "$dll_path")\"" >> "$rsp"
    done

    # The SDK ships precompiled, and only these are managed. Sweeping the
    # whole package tree drags in native plugins that csc cannot read.
    case "$defines" in
        *VRC_SDK_VRCSDK3*)
            find "$PROJECT/Packages" -path "*VRCSDK/Plugins*" -name "VRC*.dll" 2>/dev/null |
                while read -r f; do echo "-r:\"$(cygpath -w "$f")\"" >> "$rsp"; done
            ;;
    esac

    # Dev tooling only in the SDK build. It never ships, and it runs in
    # projects that always have the VRChat SDK; only Editor has to stand up
    # without it.
    sources="$REPO/Editor"
    case "$defines" in *VRC_SDK_VRCSDK3*) sources="$REPO/Editor $REPO/Dev" ;; esac
    find $sources -name '*.cs' |
        while read -r f; do echo "\"$(cygpath -w "$f")\"" >> "$rsp"; done

    echo "--- $defines"
    "$MONO" "$CSC" "@$(cygpath -w "$rsp")" 2>&1 | grep -E "error CS" | head -20

    if [ -f "$dll" ]; then
        echo "    ok"
    else
        echo "    FAILED: no assembly produced"
        fail=1
    fi
done

exit $fail
