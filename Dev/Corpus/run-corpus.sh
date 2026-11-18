#!/usr/bin/env bash
# Launch a corpus run, throttled: BelowNormal and short of all cores, so the
# machine stays usable (87 avatars, over an hour). About double the wall clock,
# on purpose.
#
#   Dev/Corpus/run-corpus.sh                 the default, YAPS on
#   Dev/Corpus/run-corpus.sh --yaps-off      the opt-out path
#   Dev/Corpus/run-corpus.sh --label 392     name the log
#   Dev/Corpus/run-corpus.sh --advisor       each avatar converted the way Apply all
#                                            would, into its own digest folder
#   Dev/Corpus/run-corpus.sh --allow-stale   run even though the deploy differs from the repo
#   Dev/Corpus/run-corpus.sh --subset FILE   only the scenes FILE lists, one Assets/ path per line
#   Dev/Corpus/run-corpus.sh --quick         the [quickset] from Regression/corpus.cfg
#   Dev/Corpus/run-corpus.sh --dynbone       the DynamicBone fallback solver, into its own folder
#   Dev/Corpus/run-corpus.sh --soft          soft parts on, into its own folder; pair with --subset,
#                                            since every body shader compiles the jelly
#   Dev/Corpus/run-corpus.sh --per-process N at most N conversions per Unity (default 30, 0 = one Unity)
#
# AVATARBRIDGE_YAPS=1 compares against Regression/Yaps; unset measures
# convertYapsSystems false against Regression/. Both baselines must hold.
set -u

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

# Machine paths live in the gitignored Dev/local.cfg. Environment values are
# held first because local.cfg assigns plainly and would overwrite them.
env_project="${AVATARBRIDGE_PROJECT:-}"
env_unity="${AVATARBRIDGE_UNITY:-}"
env_cores="${AVATARBRIDGE_CORES_TO_LEAVE:-}"
env_per="${AVATARBRIDGE_PER_PROCESS:-}"
# shellcheck source=/dev/null
[ -f "$REPO/Dev/local.cfg" ] && . "$REPO/Dev/local.cfg"
PROJECT="${env_project:-${AVATARBRIDGE_PROJECT:-}}"
UNITY="${env_unity:-${AVATARBRIDGE_UNITY:-/c/Program Files/Unity/Hub/Editor/2022.3.22f1/Editor/Unity.exe}}"
CORES_TO_LEAVE="${env_cores:-${AVATARBRIDGE_CORES_TO_LEAVE:-4}}"
per_process="${env_per:-${AVATARBRIDGE_PER_PROCESS:-30}}"

[ -n "$PROJECT" ] || {
    echo "set AVATARBRIDGE_PROJECT to the corpus project, in Dev/local.cfg or the environment" >&2
    exit 1
}

yaps=1
advisor=0
allow_stale=0
dynbone=0
soft=0
subset=""
scope=all
label=""
while [ $# -gt 0 ]; do
    case "$1" in
        --yaps-off) yaps=0 ;;
        --advisor) advisor=1 ;;
        --allow-stale) allow_stale=1 ;;
        --dynbone) dynbone=1 ;;
        --soft) soft=1 ;;
        --quick) scope=quick ;;
        --subset) shift; subset="${1:-}"; scope=subset ;;
        --label) shift; label="${1:-}" ;;
        --per-process) shift; per_process="${1:-}" ;;
        *) echo "unknown argument: $1" >&2; exit 2 ;;
    esac
    shift
done

# The advisor picks the solver itself and files under Advisor/ regardless.
if [ "$advisor" = "1" ] && [ "$dynbone" = "1" ]; then
    echo "--dynbone does not combine with --advisor: the advisor chooses the solver" >&2
    exit 2
fi
case "$per_process" in
    ""|*[!0-9]*) echo "--per-process needs a whole number (0 for one Unity)" >&2; exit 2 ;;
esac
if [ "$scope" = "subset" ] && [ ! -f "$subset" ]; then
    echo "--subset needs a file of scene paths; no file at '$subset'" >&2
    exit 2
fi

[ -n "$label" ] || label=$(date +%Y%m%d-%H%M)
suffix=$([ "$yaps" = "1" ] && echo "yaps-on" || echo "yaps-off")
[ "$advisor" = "1" ] && suffix="$suffix-advisor"
[ "$dynbone" = "1" ] && suffix="$suffix-dynbone"
[ "$soft" = "1" ] && suffix="$suffix-soft"
[ "$scope" != "all" ] && suffix="$suffix-$scope"
log="$REPO/Regression/corpus-run-$label-$suffix.log"

[ -x "$UNITY" ] || { echo "no Unity at $UNITY" >&2; exit 1; }
[ -d "$PROJECT" ] || { echo "no project at $PROJECT" >&2; exit 1; }

# Batch Unity cannot open a project the GUI has open: it dies in
# HandleProjectAlreadyOpenInAnotherInstance and exits 1 like a changed digest.
# An open editor holds Temp/UnityLockfile exclusively; a crash's leftover does
# not. Only exit 3 refuses, so a missing powershell never blocks.
lock="$PROJECT/Temp/UnityLockfile"
if [ -f "$lock" ]; then
    powershell.exe -NoProfile -Command "
      try { [IO.File]::Open('$(cygpath -w "$lock" 2>/dev/null || echo "$lock")', 'Open', 'ReadWrite', 'None').Close() }
      catch [IO.IOException] { exit 3 }" >/dev/null 2>&1
    if [ $? -eq 3 ]; then
        echo "the project is open in another Unity ($lock is held). Close it first." >&2
        exit 1
    fi
fi

# The corpus tests the deploy, so a stale one measures old code and looks clean.
# Every shipped folder (a one-file check missed an Editor/Core fix). Repo-side
# only: the deploy legitimately holds more (Editor/DevTools).
dest="$PROJECT/Assets/AvatarBridge"
stale=$(for d in AvatarScaler Editor FaceTracking Presets Runtime; do
    [ -d "$REPO/$d" ] || continue
    if [ -d "$dest/$d" ]; then
        # diff single-quotes a path holding a space, so match both forms.
        diff -rq -x '*.meta' "$REPO/$d" "$dest/$d" 2>&1 | grep -vF -e "Only in $dest/" -e "Only in '$dest/"
    else
        echo "missing: $d"
    fi
done)
if [ -n "$stale" ]; then
    echo "the deployed toolkit differs from the repo:" >&2
    echo "$stale" | sed 's/^/  /' >&2
    if [ "$allow_stale" = "1" ]; then
        echo "WARNING: running anyway (--allow-stale); this measures whatever is installed there." >&2
    else
        echo "Deploy first (Dev/Build/deploy.sh), or pass --allow-stale to measure the deploy as it is." >&2
        exit 1
    fi
fi

export AVATARBRIDGE_REPO="$(cygpath -w "$REPO" 2>/dev/null || echo "$REPO")"
if [ "$yaps" = "1" ]; then export AVATARBRIDGE_YAPS=1; else unset AVATARBRIDGE_YAPS; fi
# Unset unless asked: a leftover value silently changes the profile.
if [ "$advisor" = "1" ]; then export AVATARBRIDGE_PROFILE=advisor; else unset AVATARBRIDGE_PROFILE; fi
# Same trap: a leftover files the run under DynamicBone/.
if [ "$dynbone" = "1" ]; then export AVATARBRIDGE_PHYSICS=DynamicBone; else unset AVATARBRIDGE_PHYSICS; fi
if [ "$soft" = "1" ]; then export AVATARBRIDGE_SOFT=1; else unset AVATARBRIDGE_SOFT; fi
case "$scope" in
    subset)
        # Unity resolves the path itself, so hand it an absolute Windows one.
        export AVATARBRIDGE_SUBSET="$(cygpath -aw "$subset" 2>/dev/null || echo "$subset")"
        method=RunSubsetBatch ;;
    quick) method=RunQuickBatch ;;
    *) method=RunAllBatch ;;
esac

echo "corpus: $suffix -> $log"

# At most this many avatars per Unity, then a fresh one resumes from the
# digests: one process died in the native allocator past ~70 at 24 GB. Still
# one run: parts share Current/, the last writes the summary. 0 = one process.
export AVATARBRIDGE_PER_PROCESS="$per_process"
unset AVATARBRIDGE_RESUME

launch() {
    "$UNITY" -batchmode -quit \
        -projectPath "$PROJECT" \
        -executeMethod "AvatarBridge.Regression.RegressionRunner.$method" \
        -logFile "$1" &
    local unity_pid=$!

    # By Windows pid: matching by name also throttled other open editors.
    # Helpers it spawns inherit both settings.
    local winpid
    winpid=$(cat "/proc/$unity_pid/winpid" 2>/dev/null || echo 0)
    powershell.exe -NoProfile -Command "
      if ($winpid -lt 1) { Write-Output 'NOT throttled: no Windows pid for the launched Unity'; exit }
      \$cores = (Get-CimInstance Win32_ComputerSystem).NumberOfLogicalProcessors
      \$use = \$cores - $CORES_TO_LEAVE
      if (\$use -lt 1) { Write-Output \"NOT throttled: leaving $CORES_TO_LEAVE cores of \$cores leaves none to run on\"; exit }
      \$mask = [int64]0
      for (\$i = 0; \$i -lt \$use; \$i++) { \$mask = \$mask -bor ([int64]1 -shl \$i) }
      \$why = 'process $winpid never appeared'
      for (\$try = 0; \$try -lt 30; \$try++) {
        try {
          \$p = Get-Process -Id $winpid -ErrorAction Stop
          \$p.PriorityClass = 'BelowNormal'; \$p.ProcessorAffinity = [IntPtr]\$mask
          Write-Output \"throttled: BelowNormal, \$use of \$cores cores\"; exit
        } catch { \$why = \$_.Exception.Message }
        Start-Sleep -Seconds 2
      }
      Write-Output \"NOT throttled: \$why\"
    " 2>/dev/null

    wait $unity_pid
}

# Progress is digests written since the run began, wherever the profile files them.
began="$REPO/Regression/.run-began"
touch "$began"
digests() { find "$REPO/Regression/" -name '*.txt' -newer "$began" 2>/dev/null | wc -l; }

part=1
had=0
while :; do
    partlog="$log"
    [ "$part" -gt 1 ] && partlog="${log%.log}.part$part.log"
    launch "$partlog"
    code=$?
    if [ "$part" -gt 1 ]; then
        cat "$partlog" >> "$log" && rm -f "$partlog"
    fi
    grep -q '^\[Regression/' "$log" 2>/dev/null && break
    now=$(digests)
    if [ "$code" = "4" ]; then
        echo "part $part: stopped at its cap, $now digests so far; going on in a fresh Unity"
    elif [ "$now" -gt "$had" ] && [ "$part" -lt 20 ]; then
        # Crash after progress: retry in a fresh process. No progress stops the run.
        echo "part $part: Unity stopped (exit $code) without a summary after $now digests; resuming in a fresh Unity"
    else
        break
    fi
    had=$now
    part=$((part + 1))
    export AVATARBRIDGE_RESUME=1
done
rm -f "$began"

# Unity exits 1 for its own failures too; without a "[Regression/<scope>]"
# summary line the run never got to comparing.
if [ "$code" = "0" ]; then
    echo "corpus finished, exit 0: no digest changed"
elif grep -q '^\[Regression/' "$log" 2>/dev/null; then
    echo "corpus finished, exit $code: digests changed or vanished, or nothing ran; the summary is in the log"
else
    echo "corpus FAILED, exit $code: Unity stopped before the run reported. Not a digest change; read the log."
fi
[ "$part" -gt 1 ] && echo "ran in $part Unity processes"
echo "log: $log"
exit $code
