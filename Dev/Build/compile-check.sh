#!/usr/bin/env bash
#
# STALE LIBRARY: references the project's compiled Assembly-CSharp, so right
# after a deploy it can report fields missing that exist (2026-08-27: six such
# errors against a day-old DLL). Check the DLL's age, or open Unity once.
# Compiles the editor scripts with and without the VRChat SDK, each with and
# without the YAPS add-on, and MagicaCloth2 without the SDK: the shapes users
# have that local Unity never compiles.
# The references hold the deployed copy, so a type a combination leaves out
# still resolves; each assembly is read back and any AvatarBridge type bound
# out of Assembly-CSharp* fails it.
# Mono's csc: the Roslyn one under lib/mono/msbuild fails to start and prints
# no errors. Only a produced assembly is a trustworthy pass.
set -u

UNITY="${UNITY_DATA:-/c/Program Files/Unity/Hub/Editor/2022.3.22f1/Editor/Data}"
REPO="$(cd "$(dirname "$0")/../.." && pwd)"
# The same project check-defines.sh uses, so the two gates agree.
# shellcheck source=/dev/null
[ -f "$REPO/Dev/local.cfg" ] && . "$REPO/Dev/local.cfg"
PROJECT="${1:-${AVATARBRIDGE_PROJECT:-}}"
[ -n "$PROJECT" ] || {
    echo "pass a project path, or set AVATARBRIDGE_PROJECT in Dev/local.cfg" >&2
    exit 1
}
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

MONO="$UNITY/MonoBleedingEdge/bin/mono.exe"
CSC="$UNITY/MonoBleedingEdge/lib/mono/4.5/csc.exe"
IKDASM="$UNITY/MonoBleedingEdge/lib/mono/4.5/ikdasm.exe"
NETSTANDARD="$UNITY/MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll"

for tool in "$MONO" "$CSC" "$IKDASM" "$NETSTANDARD"; do
    if [ ! -f "$tool" ]; then
        echo "ABORT: missing $tool" >&2
        exit 1
    fi
done

fail=0
# MagicaCloth2 without the SDK is the toolkit's audience. check-defines.sh
# varies the physics defines only in a project that has the SDK.
. "$REPO/Dev/Build/yaps-files.sh"
# "addon:" = the add-on installed alone (4.1.0 and 4.1.1 could not compile that way).
for defines in "CVR_CCK_EXISTS" \
               "CVR_CCK_EXISTS;AVATARBRIDGE_MAGICA" \
               "CVR_CCK_EXISTS;AVATARBRIDGE_YAPS" \
               "addon:CVR_CCK_EXISTS;AVATARBRIDGE_YAPS"                "addon:CVR_CCK_EXISTS;AVATARBRIDGE_YAPS;AVATARBRIDGE_MAGICA" \
               "CVR_CCK_EXISTS;VRC_SDK_VRCSDK3" \
               "CVR_CCK_EXISTS;VRC_SDK_VRCSDK3;AVATARBRIDGE_YAPS"; do
    shape="$defines"
    defines="${defines#addon:}"
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

    # Split modules only: the monolithic UnityEngine beside them makes every type ambiguous.
    for dll_path in "$UNITY/Managed/UnityEngine"/*.dll "$PROJECT/Library/ScriptAssemblies"/*.dll; do
        # Without the SDK, nothing built on it either.
        case "$defines" in
            *VRC_SDK_VRCSDK3*) ;;
            *) case "$(basename "$dll_path")" in VRC*|com.vrcfury*|*vrchat*) continue ;; esac ;;
        esac
        [ -f "$dll_path" ] && echo "-r:\"$(cygpath -w "$dll_path")\"" >> "$rsp"
    done

    # Only these SDK dlls are managed; the rest are native and csc cannot read them.
    case "$defines" in
        *VRC_SDK_VRCSDK3*)
            find "$PROJECT/Packages" -path "*VRCSDK/Plugins*" -name "VRC*.dll" 2>/dev/null |
                while read -r f; do echo "-r:\"$(cygpath -w "$f")\"" >> "$rsp"; done
            ;;
    esac

    # Runtime as SOURCE: linking the deployed assembly never checked runtime changes.
    sources=("$REPO/Editor" "$REPO/Runtime")
    # Dev tooling assumes everything installed, so only that combination.
    case "$defines" in
        *VRC_SDK_VRCSDK3*AVATARBRIDGE_YAPS*) sources+=("$REPO/Dev") ;;
    esac
    # Without the add-on, drop Editor/Yaps AND Runtime: the public package prunes both.
    case "$shape" in
        addon:*)             (cd "$REPO" && yaps_files | tr '\0' '\n' | grep '\.cs$' | sed "s|^|$REPO/|") ;;
        *AVATARBRIDGE_YAPS*) find "${sources[@]}" -name '*.cs' ;;
        *)                   find "${sources[@]}" -name '*.cs' -not -path '*/Editor/Yaps/*' -not -path '*/Runtime/*' ;;
    esac |
        while read -r f; do echo "\"$(cygpath -w "$f")\"" >> "$rsp"; done

    echo "--- $shape"
    "$MONO" "$CSC" "@$(cygpath -w "$rsp")" 2>&1 | grep -E "error CS" | head -20

    if [ -f "$dll" ]; then
        # A type bound out of the deployed Assembly-CSharp* is one the user lacks.
        # A failed read-back is a failure.
        if "$MONO" "$IKDASM" "$(cygpath -w "$dll")" > "$WORK/il.txt"; then
            leaked="$(grep -o "\['\?Assembly-CSharp[^]]*]AvatarBridge[A-Za-z0-9_.]*" "$WORK/il.txt" | sort -u)"
        else
            leaked="(could not read the assembly back)"
        fi
        if [ -n "$leaked" ]; then
            echo "    FAILED: resolved from the project's deployed copy, not these sources"
            echo "$leaked" | sed 's/^/      /' | head -20
            fail=1
        else
            echo "    ok"
        fi
    else
        echo "    FAILED: no assembly produced"
        fail=1
    fi
done

exit $fail
