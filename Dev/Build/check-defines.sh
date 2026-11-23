#!/usr/bin/env bash
# Compiles the editor assembly once per optional-dependency combination, so a
# file behind one define cannot reference a symbol behind another. 4.3.1
# shipped that: DynamicBone without MagicaCloth2 could not compile, and every
# other gate runs with both installed.
# No Unity launch: reuses Unity's Assembly-CSharp-Editor response file, swapping
# the -define: lines and the deployed copy for the repo. Seconds per combination.
# Runtime goes in as source beside the prebuilt runtime dll (CS0436, source
# wins); that dll is whatever Unity last compiled, so new members read missing.
# With the add-on, then as the public package (no Editor/Yaps, Runtime, Dev).
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
# Mono's ikdasm reads the public builds back, as compile-check.sh does.
MONO="$UNITY_ROOT/Editor/Data/MonoBleedingEdge/bin/mono.exe"
IKDASM="$UNITY_ROOT/Editor/Data/MonoBleedingEdge/lib/mono/4.5/ikdasm.exe"
if [ ! -f "$MONO" ] || [ ! -f "$IKDASM" ]; then
  echo "no ikdasm at $UNITY_ROOT: set UNITY_ROOT to the editor this project uses" >&2
  exit 2
fi

# Inside the project: csc resolves -out against it, and Git Bash /tmp is not a Windows path.
WORK="$PROJ/Temp/define-gate"
rm -rf "$WORK"; mkdir -p "$WORK"
trap 'rm -rf "$WORK"' EXIT
fail=0
baseline=""

# Unity's source list is the last deploy, which this runs before, so use the
# repo's files. Editor/DevTools maps back to Dev by name, as deploy.sh does.
sources="$WORK/sources.rsp"
{
  find "$REPO/Editor" "$REPO/Runtime" -name '*.cs' | while read -r f; do echo "\"$(cygpath -w "$f")\""; done
  grep '^"Assets/AvatarBridge/Editor/DevTools/' "$RSP" | tr -d '"\r' | while read -r have; do
    # Harness folders only, last match wins, as deploy.sh: a gitignored spike sharing a name is not the harness.
    src="$(find "$REPO"/Dev/{Corpus,Tests,Probes,Scenes,Tester} -name "$(basename "$have")" -type f 2>/dev/null | tail -n 1)" || true
    src="${src:-$PROJ/$have}"
    # A scratch probe deleted since that compile is not a source any more.
    [ -f "$src" ] && echo "\"$(cygpath -w "$src")\""
  done
} > "$sources"
# The public package: Editor without Editor/Yaps, no Runtime, no Dev harness.
public="$WORK/sources-public.rsp"
find "$REPO/Editor" -name '*.cs' -not -path '*/Editor/Yaps/*' | while read -r f; do echo "\"$(cygpath -w "$f")\""; done > "$public"
# MagicaCloth 1's namespace is MagicaCloth, MC2's component name, and a global
# namespace beats a using'd type: both installed could not compile (5.0.0 rc2).
foreign="$WORK/foreign-namespaces.cs"
echo 'namespace MagicaCloth { internal class AvatarBridgeForeignNamespace { } }' > "$foreign"
echo "\"$(cygpath -w "$foreign")\"" | tee -a "$sources" >> "$public"
# Project scripts using this tool's types are dev spikes; without the add-on
# they fail and csc writes no assembly for the read-back.
spikes="$WORK/spikes.txt"
grep '^"Assets/' "$RSP" | tr -d '"\r' | grep -v '^Assets/AvatarBridge/' | while read -r f; do
  case "$f" in *.cs) grep -qs "AvatarBridge" "$PROJ/$f" && echo "$f" ;; esac
done > "$spikes"

for yaps in 1 0; do
 for magica in 1 0; do
  for dynbone in 1 0; do
    combo="$yaps$magica$dynbone"
    name="YAPS=$yaps MAGICA=$magica DYNBONE=$dynbone"
    out="$WORK/out-$combo.dll"
    # Unity's own arguments minus the three defines, outputs and deployed copy.
    # -refout too: left in, it overwrote Unity's ref assembly in Library.
    grep -v "^-define:AVATARBRIDGE_MAGICA$\|^-define:AVATARBRIDGE_DYNBONE$\|^-define:AVATARBRIDGE_YAPS$\|^-out:\|^-refout:\|^\"Assets/AvatarBridge/" "$RSP" > "$WORK/args.rsp"
    if [ "$yaps" = 1 ]; then cat "$sources"; else cat "$public"; fi >> "$WORK/args.rsp"
    echo "-out:\"$out\"" >> "$WORK/args.rsp"
    [ "$yaps" = 1 ] && echo "-define:AVATARBRIDGE_YAPS" >> "$WORK/args.rsp"
    if [ "$yaps" = 0 ]; then
      awk 'FILENAME == ARGV[1] { skip[$0]; next } { k = $0; gsub(/["\r]/, "", k) } !(k in skip)'         "$spikes" "$WORK/args.rsp" > "$WORK/args-public.rsp" && mv "$WORK/args-public.rsp" "$WORK/args.rsp"
    fi
    [ "$magica" = 1 ] && echo "-define:AVATARBRIDGE_MAGICA" >> "$WORK/args.rsp"
    [ "$dynbone" = 1 ] && echo "-define:AVATARBRIDGE_DYNBONE" >> "$WORK/args.rsp"

    log="$WORK/log-$combo.txt"
    ( cd "$PROJ" && "$DOTNET" "$CSC" "@$WORK/args.rsp" ) > "$log" 2>&1
    # csc's exit code is unreliable through the wrapper; count this repo's errors only,
    # not other assets' scripts.
    ours="$(grep "error CS" "$log" | grep -iF -e "AvatarBridge" -e "$(cygpath -w "$REPO")" | sort -u)"
    any="$(grep -c "error CS" "$log")"
    # Warnings a combination adds over the full build (e.g. CS0162 after a
    # switch whose cases all compiled out). CS0436 is the runtime-as-source overlap.
    warn="$(grep "warning CS" "$log" | grep -v "warning CS0436" | grep -iF "$(cygpath -w "$REPO")" | sort -u)"
    [ "$combo" = 111 ] && baseline="$warn"
    added="$(comm -13 <(printf '%s\n' "$baseline") <(printf '%s\n' "$warn") | sed '/^$/d')"

    # Assembly-CSharp.ref.dll holds the deployed Runtime, so a public build
    # reaching a Runtime type still compiles. A type bound out of it is one the
    # user lacks. Foreign errors write no assembly, so it goes unread.
    leaked=""
    if [ "$yaps" = 0 ] && [ -z "$ours" ]; then
      if [ ! -f "$out" ]; then
        [ "$any" = 0 ] && leaked="(no assembly produced)"
      elif "$MONO" "$IKDASM" "$(cygpath -w "$out")" > "$WORK/il.txt"; then
        leaked="$(grep -o "\['\?Assembly-CSharp[^]]*]AvatarBridge[A-Za-z0-9_.]*" "$WORK/il.txt" | sort -u | sed 's/^/not in the public package: /')"
      else
        leaked="(could not read the assembly back)"
      fi
    fi

    if [ -n "$ours" ] || [ -n "$added" ] || [ -n "$leaked" ]; then
      echo "FAIL  $name"
      [ -n "$ours" ] && echo "$ours" | sed 's/^/        /' | head -20
      [ -n "$added" ] && echo "$added" | sed 's/^/        /' | head -20
      [ -n "$leaked" ] && echo "$leaked" | sed 's/^/        /' | head -20
      fail=1
    elif [ "$any" -gt 0 ]; then
      echo "ok    $name  ($any error(s), none in AvatarBridge)"
    else
      echo "ok    $name"
    fi
  done
 done
done

echo
if [ "$fail" = 0 ]; then echo "all eight combinations compile"; else echo "a combination is broken: see above"; fi
exit "$fail"
