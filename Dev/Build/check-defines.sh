#!/usr/bin/env bash
# Compiles the project's editor assembly once per optional-dependency
# combination, so a file guarded by one define can never again reference a
# symbol that lives behind another.
#
# 4.3.1 shipped exactly that bug: DynamicBoneWriter called two helpers on
# MagicaClothWriter, and a project with DynamicBone and no MagicaCloth2
# could not compile at all. Every gate we own — corpus, test project, the
# editor sitting open — runs the both-installed combination, so nothing
# caught it. A user did.
#
# No Unity launch and no domain reload: Unity leaves the exact compile
# arguments for Assembly-CSharp-Editor in a response file, so this reuses
# them and only swaps the -define: lines. Seconds per combination.
#
# BLIND SPOT, and it will mislead you: this compiles the EDITOR assembly
# against the prebuilt Assembly-CSharp.dll in Library/ScriptAssemblies. A
# change to anything under Runtime/ is invisible until Unity rebuilds that
# dll, so editor code using a new runtime member fails here with "does not
# contain a definition for" while Unity itself compiles it happily. When
# that happens, let Unity recompile once:
#
#   Unity.exe -batchmode -quit -projectPath "<project>" -logFile <log>
#
# then re-run. The failure is real only if it survives that.
#
# Usage:
#   check-defines.sh [project]     default: the corpus project
set -uo pipefail

PROJ="${1:-D:/UnityVRCCrap/Attempt Conversion}"
UNITY_ROOT="${UNITY_ROOT:-C:/Program Files/Unity/Hub/Editor/2022.3.22f1}"

RSP="$(ls -t "$PROJ"/Library/Bee/artifacts/*.dag/Assembly-CSharp-Editor.rsp 2>/dev/null | head -1)"
if [ -z "$RSP" ]; then
  echo "no response file: open the project in Unity once so it compiles, then re-run" >&2
  exit 2
fi

CSC="$UNITY_ROOT/Editor/Data/DotNetSdkRoslyn/csc.dll"
DOTNET="$UNITY_ROOT/Editor/Data/NetCoreRuntime/dotnet.exe"
if [ ! -f "$CSC" ] || [ ! -f "$DOTNET" ]; then
  echo "no Roslyn at $UNITY_ROOT — set UNITY_ROOT to the editor this project uses" >&2
  exit 2
fi

# Inside the project: csc runs with the project as its working directory
# and resolves -out against it, and a Git Bash /tmp path is a drive root
# Windows does not have.
WORK="$PROJ/Temp/define-gate"
rm -rf "$WORK"; mkdir -p "$WORK"
trap 'rm -rf "$WORK"' EXIT
fail=0

for magica in 1 0; do
  for dynbone in 1 0; do
    name="MAGICA=$magica DYNBONE=$dynbone"
    out="$WORK/out-$magica$dynbone.dll"
    # The stock arguments, minus our two defines and its output, plus the
    # combination under test. Everything else — 300-odd references, the
    # source list, langversion — is exactly what Unity itself used.
    grep -v "^-define:AVATARBRIDGE_MAGICA$\|^-define:AVATARBRIDGE_DYNBONE$\|^-out:" "$RSP" > "$WORK/args.rsp"
    echo "-out:\"$out\"" >> "$WORK/args.rsp"
    [ "$magica" = 1 ] && echo "-define:AVATARBRIDGE_MAGICA" >> "$WORK/args.rsp"
    [ "$dynbone" = 1 ] && echo "-define:AVATARBRIDGE_DYNBONE" >> "$WORK/args.rsp"

    log="$WORK/log-$magica$dynbone.txt"
    ( cd "$PROJ" && "$DOTNET" "$CSC" "@$WORK/args.rsp" ) > "$log" 2>&1
    # csc's exit code is unreliable through the wrapper; the errors are the
    # answer. Ours only — a project full of other assets' scripts is not
    # this gate's business.
    ours="$(grep "error CS" "$log" | grep -i "AvatarBridge" | sort -u)"
    any="$(grep -c "error CS" "$log")"

    if [ -n "$ours" ]; then
      echo "FAIL  $name"
      echo "$ours" | sed 's/^/        /' | head -20
      fail=1
    elif [ "$any" -gt 0 ]; then
      echo "ok    $name  ($any error(s), none in AvatarBridge)"
    else
      echo "ok    $name"
    fi
  done
done

echo
if [ "$fail" = 0 ]; then echo "all four combinations compile"; else echo "a combination is broken — see above"; fi
exit "$fail"
