#!/bin/bash
# Presses every button in AvatarBridge's windows and inspectors (ButtonSmokeProbe.cs) on the
# given scenes, in a throttled batch Unity, and writes <out>.tsv with one verdict per control.
#   bash Dev/Probes/button-smoke.sh <out-dir> "Assets/a.unity;Assets/b.unity" [--keep <Assets/dir>]... [probe args]
# A press can write assets. Each scene's folder, and every --keep folder, is copied aside first
# and put back after, and files the run created there are deleted. A Convert press makes a new
# numbered folder in Assets/AvatarBridgeOutput; folders that appear there are removed. Whatever
# it wrote anywhere else is listed in <out>/smoke.assets.txt (what the AssetDatabase imported)
# for a person to judge.
# The probe needs Editor/DevTools/ButtonSmokeProbe.cs in the project: deploy.sh only refreshes
# harness files already there, so copy it in by hand the first time.
set -u
REPO="$(cd "$(dirname "$0")/../.." && pwd)"
. "$REPO/Dev/local.cfg"
PROJECT="$AVATARBRIDGE_PROJECT"
UNITY="$AVATARBRIDGE_UNITY"
mkdir -p "$1"; OUT="$(cd "$1" && pwd)"; SCENES="$2"; shift 2
keep=()
while [ $# -gt 0 ] && [ "$1" = "--keep" ]; do keep+=("$2"); shift 2; done

# Batch Unity dies on a project the GUI has open, and reads as a crash.
if [ -e "$PROJECT/Temp/UnityLockfile" ] && tasklist //FI "IMAGENAME eq Unity.exe" | grep -qi unity.exe; then
    echo "Unity has the project open; close it first"; exit 3
fi
mkdir -p "$OUT"
IFS=';' read -ra scene_list <<< "$SCENES"
for s in "${scene_list[@]}"; do keep+=("$(dirname "$s")"); done

backup="$OUT/backup"
rm -rf "$backup"; mkdir -p "$backup"
i=0
while IFS= read -r d; do
    mkdir -p "$backup/$i"
    cp -a "$PROJECT/$d/." "$backup/$i/" || { echo "backup of $d failed; not running"; exit 3; }
    echo "$d" > "$backup/$i.path"
    i=$((i + 1))
done < <(printf '%s\n' "${keep[@]}" | sort -u)
rm -f "$OUT/smoke.tsv" "$OUT/smoke.assets.txt"
outputs="$PROJECT/Assets/AvatarBridgeOutput"
ls -A "$outputs" > "$OUT/.outputs" 2>/dev/null
marker="$OUT/.began"; touch "$marker"; sleep 1

cmd //c start //BELOWNORMAL //WAIT "" "$UNITY" -batchmode -projectPath "$PROJECT" \
    -executeMethod AvatarBridge.Regression.ButtonSmokeProbe.Run \
    -bsScenes "$SCENES" -bsOut "$(cygpath -w "$OUT/smoke.tsv")" "$@" \
    -logFile "$(cygpath -w "$OUT/run.log")"
# start /WAIT hands back 0 whatever Unity exited with; the probe's own summary line is the result.

for p in "$backup"/*.path; do
    [ -e "$p" ] || continue
    n="${p%.path}"; d="$(cat "$p")"
    # Files the run added there go; files it changed come back from the copy.
    find "$PROJECT/$d" -newer "$marker" -type f 2>/dev/null | while read -r f; do
        [ -e "$n/${f#$PROJECT/$d/}" ] || rm -f "$f"
    done
    cp -a "$n/." "$PROJECT/$d/"
done
# Only what is both new to the listing and born after the run began.
ls -A "$outputs" 2>/dev/null | grep -vxFf "$OUT/.outputs" | while IFS= read -r f; do
    [ "$outputs/$f" -nt "$marker" ] && rm -rf "${outputs:?}/$f"
done
rm -rf "$backup" "$marker" "$OUT/.outputs"

summary=$(grep -a -m1 "^\[ButtonSmoke\] [0-9]* rows" "$OUT/run.log")
grep -a "error CS\|Exception:" "$OUT/run.log" | grep -v EmitExceptionAsError | sort -u | head -5
[ -e "$OUT/smoke.assets.txt" ] && echo "$(wc -l < "$OUT/smoke.assets.txt") asset(s) imported, listed in $OUT/smoke.assets.txt"
if [ -z "$summary" ]; then echo "the probe never reported; read $OUT/run.log"; exit 2; fi
echo "$summary"
case "$summary" in *"Every pressed control did something."*) exit 0;; *) exit 1;; esac
