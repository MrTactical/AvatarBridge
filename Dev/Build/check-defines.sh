#!/usr/bin/env bash
# Compiles the project's editor assembly once per optional-dependency
# combination, so a file guarded by one define can never again reference a
# symbol that lives behind another.
#
# 4.3.1 shipped exactly that bug: DynamicBoneWriter called two helpers on
# MagicaClothWriter, and a project with DynamicBone and no MagicaCloth2
# could not compile at all. Every gate here, the corpus, the test project, the
# editor sitting open, runs the both-installed combination, so nothing
# caught it. A user did.
#
# No Unity launch and no domain reload: Unity leaves the exact compile
# arguments for Assembly-CSharp-Editor in a response file, so this reuses
# them and swaps only the -define: lines and its copy of this repo for the
# repo itself. Seconds per combination.
#
# Runtime goes in as source as well, beside the project's prebuilt runtime
# assembly (CS0436, the source wins). Linked alone, that dll is whatever
# Unity last compiled, so editor code using a runtime member added since
# failed here with "does not contain a definition for" while Unity built it.
#
# Usage:
#   check-defines.sh [project]     default: the corpus project
set -uo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
# shellcheck source=/dev/null
[ -f "$REPO/Dev/local.cfg" ] && . "$REPO/Dev/local.cfg"
PROJ="${1:-${AVATARBRIDGE_PROJECT:-}}"
[ -n "$PROJ" ] || {
  echo "pass a project path, or set AVATARBRIDGE_PROJECT in Dev/local.cfg" >&2
  exit 2
}
UNITY_ROOT="${UNITY_ROOT:-C:/Program Files/Unity/Hub/Editor/2022.3.22f1}"

RSP="$(ls -t "$PROJ"/Library/Bee/artifacts/*.dag/Assembly-CSharp-Editor.rsp 2>/dev/null | head -1)"
if [ -z "$RSP" ]; then
  echo "no response file: open the project in Unity once so it compiles, then re-run" >&2
  exit 2
fi

CSC="$UNITY_ROOT/Editor/Data/DotNetSdkRoslyn/csc.dll"
DOTNET="$UNITY_ROOT/Editor/Data/NetCoreRuntime/dotnet.exe"
if [ ! -f "$CSC" ] || [ ! -f "$DOTNET" ]; then
  echo "no Roslyn at $UNITY_ROOT: set UNITY_ROOT to the editor this project uses" >&2
  exit 2
fi

# Inside the project: csc runs with the project as its working directory
# and resolves -out against it, and a Git Bash /tmp path is a drive root
# Windows does not have.
WORK="$PROJ/Temp/define-gate"
rm -rf "$WORK"; mkdir -p "$WORK"
trap 'rm -rf "$WORK"' EXIT
fail=0
baseline=""

# Unity's source list names the project's deployed copy as of the last time
# Unity compiled it: the previous deploy, missing every file added since. In
# the release order this runs before deploying, so it checked the last
# release, not this one. The repo's own files go in instead, and the dev
# harness under Editor/DevTools maps back to Dev by name, as deploy.sh does.
sources="$WORK/sources.rsp"
{
  find "$REPO/Editor" "$REPO/Runtime" -name '*.cs' | while read -r f; do echo "\"$(cygpath -w "$f")\""; done
  grep '^"Assets/AvatarBridge/Editor/DevTools/' "$RSP" | tr -d '"\r' | while read -r have; do
    src="$(find "$REPO/Dev" -name "$(basename "$have")" -type f -print -quit)"
    echo "\"$(cygpath -w "${src:-$PROJ/$have}")\""
  done
} > "$sources"

for magica in 1 0; do
  for dynbone in 1 0; do
    name="MAGICA=$magica DYNBONE=$dynbone"
    out="$WORK/out-$magica$dynbone.dll"
    # The stock arguments, minus our two defines, its outputs and its copy of
    # this repo, plus the combination under test and the repo's sources.
    # Everything else, 300-odd references, langversion, is exactly what Unity
    # itself used. -refout goes too: left in, it overwrote Unity's own ref
    # assembly in Library.
    grep -v "^-define:AVATARBRIDGE_MAGICA$\|^-define:AVATARBRIDGE_DYNBONE$\|^-out:\|^-refout:\|^\"Assets/AvatarBridge/" "$RSP" > "$WORK/args.rsp"
    cat "$sources" >> "$WORK/args.rsp"
    echo "-out:\"$out\"" >> "$WORK/args.rsp"
    [ "$magica" = 1 ] && echo "-define:AVATARBRIDGE_MAGICA" >> "$WORK/args.rsp"
    [ "$dynbone" = 1 ] && echo "-define:AVATARBRIDGE_DYNBONE" >> "$WORK/args.rsp"

    log="$WORK/log-$magica$dynbone.txt"
    ( cd "$PROJ" && "$DOTNET" "$CSC" "@$WORK/args.rsp" ) > "$log" 2>&1
    # csc's exit code is unreliable through the wrapper; the errors are the
    # answer. This project only: a project full of other assets' scripts is not
    # this gate's business.
    ours="$(grep "error CS" "$log" | grep -iF -e "AvatarBridge" -e "$(cygpath -w "$REPO")" | sort -u)"
    any="$(grep -c "error CS" "$log")"
    # Warnings too, but only the ones a combination adds over the full build
    # (the first one run): code after a switch whose every case compiled out
    # (CS0162) is a define mistake the full build cannot show. CS0436 is the
    # runtime-as-source overlap described above.
    warn="$(grep "warning CS" "$log" | grep -v "warning CS0436" | grep -iF "$(cygpath -w "$REPO")" | sort -u)"
    [ "$magica$dynbone" = 11 ] && baseline="$warn"
    added="$(comm -13 <(printf '%s\n' "$baseline") <(printf '%s\n' "$warn") | sed '/^$/d')"

    if [ -n "$ours" ] || [ -n "$added" ]; then
      echo "FAIL  $name"
      [ -n "$ours" ] && echo "$ours" | sed 's/^/        /' | head -20
      [ -n "$added" ] && echo "$added" | sed 's/^/        /' | head -20
      fail=1
    elif [ "$any" -gt 0 ]; then
      echo "ok    $name  ($any error(s), none in AvatarBridge)"
    else
      echo "ok    $name"
    fi
  done
done

echo
if [ "$fail" = 0 ]; then echo "all four combinations compile"; else echo "a combination is broken: see above"; fi
exit "$fail"
