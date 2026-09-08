#!/usr/bin/env bash
#
# STALE LIBRARY WARNING. This reuses the project's compiled Assembly-CSharp,
# so straight after a deploy it compiles the new editor code against the OLD
# runtime code and reports missing fields that exist. Seen 2026-08-27: six
# errors for bakedSlots, previewAsChannel and previewInPlayMode, every one of
# them present in the source, against a DLL a day older than the deploy.
# Compare Library/ScriptAssemblies/Assembly-CSharp.dll against the files you
# deployed before believing a failure, or let Unity open the project once.
# Compiles the editor scripts four times: with the VRChat SDK and without,
# each with and without the YAPS add-on.
#
# Everything but the first combination matters. A ChilloutVR-only user
# installs AvatarBridge for the toolkit and never has the VRChat SDK, and
# YAPS ships as a separate add-on, so a converter without it compiles with
# Editor/Yaps absent from the project entirely. Unity in the local projects
# only ever compiles the one where everything is installed.
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
for defines in "CVR_CCK_EXISTS" \
               "CVR_CCK_EXISTS;AVATARBRIDGE_YAPS" \
               "CVR_CCK_EXISTS;VRC_SDK_VRCSDK3" \
               "CVR_CCK_EXISTS;VRC_SDK_VRCSDK3;AVATARBRIDGE_YAPS"; do
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
    # Runtime as SOURCE, not as the deployed assembly the references pull
    # in. Linking the deployed one meant a runtime change was never checked
    # here at all: a new type read as missing and a broken attribute read as
    # fine, both until somebody opened Unity.
    sources="$REPO/Editor $REPO/Runtime"
    # Dev tooling assumes a machine with everything installed, so it only
    # joins the combination that has everything.
    case "$defines" in
        *VRC_SDK_VRCSDK3*AVATARBRIDGE_YAPS*) sources="$REPO/Editor $REPO/Runtime $REPO/Dev" ;;
    esac
    # Without the add-on those files are not in the project at all, so
    # compiling them would prove the wrong thing: the point of the run is
    # that the converter stands up when they are missing.
    # Runtime goes with Editor/Yaps: the public package prunes BOTH, so a
    # combination that keeps Runtime is not a shape any user has. Compiling
    # it was the same blind spot the YAPS closure check exists for, in the
    # other direction: a shipped file reaching a type that stayed behind.
    case "$defines" in
        *AVATARBRIDGE_YAPS*) find $sources -name '*.cs' ;;
        *)                   find $sources -name '*.cs'                                   -not -path '*/Editor/Yaps/*' -not -path '*/Runtime/*' ;;
    esac |
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
