#!/usr/bin/env bash
# Launch a corpus run, throttled.
#
# A run is 87 avatars and well over an hour on a machine somebody is
# trying to use. Unthrottled it takes the whole box: Unity at Normal
# priority across every core makes the editor, the browser and the game
# stutter, and the run is never urgent enough to be worth that. So every
# corpus run goes through here, at BelowNormal and short of a full core
# count, and takes longer on purpose.
#
# Costs about double the wall clock. That is the trade being made
# deliberately, not an accident: nobody is waiting on the result inside a
# minute, and the machine stays usable the whole time.
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
#
# AVATARBRIDGE_YAPS=1 is what a user gets and what Regression/Yaps
# compares against; unset measures convertYapsSystems false, which lands
# in Regression/. Both baselines are real and both have to hold.
set -u

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

# Machine-specific paths live in Dev/local.cfg, which is gitignored: the
# corpus project and the Unity install are wherever this machine put them.
#
# Held first, because local.cfg assigns plainly and is sourced after: a
# value passed for one run was being overwritten by the file and the run
# went ahead on the file's number, saying so in a line nobody rereads.
env_project="${AVATARBRIDGE_PROJECT:-}"
env_unity="${AVATARBRIDGE_UNITY:-}"
env_cores="${AVATARBRIDGE_CORES_TO_LEAVE:-}"
# shellcheck source=/dev/null
[ -f "$REPO/Dev/local.cfg" ] && . "$REPO/Dev/local.cfg"
PROJECT="${env_project:-${AVATARBRIDGE_PROJECT:-}}"
UNITY="${env_unity:-${AVATARBRIDGE_UNITY:-/c/Program Files/Unity/Hub/Editor/2022.3.22f1/Editor/Unity.exe}}"
CORES_TO_LEAVE="${env_cores:-${AVATARBRIDGE_CORES_TO_LEAVE:-4}}"

[ -n "$PROJECT" ] || {
    echo "set AVATARBRIDGE_PROJECT to the corpus project, in Dev/local.cfg or the environment" >&2
    exit 1
}

yaps=1
advisor=0
allow_stale=0
dynbone=0
subset=""
scope=all
label=""
while [ $# -gt 0 ]; do
    case "$1" in
        --yaps-off) yaps=0 ;;
        --advisor) advisor=1 ;;
        --allow-stale) allow_stale=1 ;;
        --dynbone) dynbone=1 ;;
        --quick) scope=quick ;;
        --subset) shift; subset="${1:-}"; scope=subset ;;
        --label) shift; label="${1:-}" ;;
        *) echo "unknown argument: $1" >&2; exit 2 ;;
    esac
    shift
done

# The advisor picks the physics target itself, and the harness files an
# advisor run under Advisor/ whatever AVATARBRIDGE_PHYSICS says.
if [ "$advisor" = "1" ] && [ "$dynbone" = "1" ]; then
    echo "--dynbone does not combine with --advisor: the advisor chooses the solver" >&2
    exit 2
fi
if [ "$scope" = "subset" ] && [ ! -f "$subset" ]; then
    echo "--subset needs a file of scene paths; no file at '$subset'" >&2
    exit 2
fi

[ -n "$label" ] || label=$(date +%Y%m%d-%H%M)
suffix=$([ "$yaps" = "1" ] && echo "yaps-on" || echo "yaps-off")
[ "$advisor" = "1" ] && suffix="$suffix-advisor"
[ "$dynbone" = "1" ] && suffix="$suffix-dynbone"
[ "$scope" != "all" ] && suffix="$suffix-$scope"
log="$REPO/Regression/corpus-run-$label-$suffix.log"

[ -x "$UNITY" ] || { echo "no Unity at $UNITY" >&2; exit 1; }
[ -d "$PROJECT" ] || { echo "no project at $PROJECT" >&2; exit 1; }

# Batch Unity cannot open a project the GUI has open: it dies in
# HandleProjectAlreadyOpenInAnotherInstance, which reads as a crash and
# exits 1 like a changed digest. An open editor holds Temp/UnityLockfile,
# so an exclusive open fails; one a crash left behind opens fine and is no
# obstacle. Exit 3 is the only refusal, so a missing powershell never blocks.
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

# The deployed copy, not the repo: the corpus tests what was installed.
# A run against a stale deploy measures yesterday's code and looks clean.
# Every shipped folder deploy.sh copies, not one file: a fix in Editor/Core
# alone passed a one-file check. Repo-side only, since deploy copies rather
# than mirrors and the deploy legitimately holds more (Editor/DevTools).
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
# Unset unless asked for: a value left in the environment would otherwise
# turn a normal run into the advisor profile and compare it against nothing.
if [ "$advisor" = "1" ]; then export AVATARBRIDGE_PROFILE=advisor; else unset AVATARBRIDGE_PROFILE; fi
# Same trap: a leftover value files the run under DynamicBone/ while the
# log still says it was the default.
if [ "$dynbone" = "1" ]; then export AVATARBRIDGE_PHYSICS=DynamicBone; else unset AVATARBRIDGE_PHYSICS; fi
case "$scope" in
    subset)
        # Unity resolves the path itself, so hand it an absolute Windows one.
        export AVATARBRIDGE_SUBSET="$(cygpath -aw "$subset" 2>/dev/null || echo "$subset")"
        method=RunSubsetBatch ;;
    quick) method=RunQuickBatch ;;
    *) method=RunAllBatch ;;
esac

echo "corpus: $suffix -> $log"
"$UNITY" -batchmode -quit \
    -projectPath "$PROJECT" \
    -executeMethod "AvatarBridge.Regression.RegressionRunner.$method" \
    -logFile "$log" &
unity_pid=$!

# Throttle the process just launched, by its Windows pid. Matching every
# process named Unity also caught an editor open on another project, left it
# slowed after the run, and could stop before the batch Unity was even up.
# Helpers it spawns afterwards inherit both settings.
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
code=$?
# Unity exits 1 for its own failures too, so the code alone cannot say a
# digest changed. The harness logs its summary as "[Regression/<scope>]";
# without that line the run never got as far as comparing.
if [ "$code" = "0" ]; then
    echo "corpus finished, exit 0: no digest changed"
elif grep -q '^\[Regression/' "$log" 2>/dev/null; then
    echo "corpus finished, exit $code: digests changed or vanished, or nothing ran; the summary is in the log"
else
    echo "corpus FAILED, exit $code: Unity stopped before the run reported. Not a digest change; read the log."
fi
echo "log: $log"
exit $code
