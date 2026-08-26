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
#
# AVATARBRIDGE_YAPS=1 is what a user gets and what Regression/Yaps
# compares against; unset measures convertYapsSystems false, which lands
# in Regression/. Both baselines are real and both have to hold.
set -u

REPO="D:/AvatarBridge"
PROJECT="D:/UnityVRCCrap/Attempt Conversion"
UNITY="/c/Program Files/Unity/Hub/Editor/2022.3.22f1/Editor/Unity.exe"
CORES_TO_LEAVE=4

yaps=1
label=""
while [ $# -gt 0 ]; do
    case "$1" in
        --yaps-off) yaps=0 ;;
        --label) shift; label="${1:-}" ;;
        *) echo "unknown argument: $1" >&2; exit 2 ;;
    esac
    shift
done

[ -n "$label" ] || label=$(date +%Y%m%d-%H%M)
suffix=$([ "$yaps" = "1" ] && echo "yaps-on" || echo "yaps-off")
log="$REPO/Regression/corpus-run-$label-$suffix.log"

[ -x "$UNITY" ] || { echo "no Unity at $UNITY" >&2; exit 1; }
[ -d "$PROJECT" ] || { echo "no project at $PROJECT" >&2; exit 1; }

# The deployed copy, not the repo: the corpus tests what was installed.
# A run against a stale deploy measures yesterday's code and looks clean.
deployed="$PROJECT/Assets/AvatarBridge/Editor/Yaps/YapsBaker.cs"
if [ -f "$deployed" ] && ! cmp -s "$REPO/Editor/Yaps/YapsBaker.cs" "$deployed"; then
    echo "WARNING: the deployed toolkit differs from the repo. Deploy before running," >&2
    echo "         or this measures whatever is installed over there." >&2
fi

export AVATARBRIDGE_REPO="D:\\AvatarBridge"
if [ "$yaps" = "1" ]; then export AVATARBRIDGE_YAPS=1; else unset AVATARBRIDGE_YAPS; fi

echo "corpus: $suffix -> $log"
"$UNITY" -batchmode -quit \
    -projectPath "$PROJECT" \
    -executeMethod AvatarBridge.Regression.RegressionRunner.RunAllBatch \
    -logFile "$log" &
unity_pid=$!

# Throttle as soon as it exists. Unity spawns helpers, so every one gets
# it, and the loop retries because the process is not up immediately.
powershell.exe -NoProfile -Command "
  \$cores = (Get-CimInstance Win32_ComputerSystem).NumberOfLogicalProcessors
  \$mask = [int64]0
  for (\$i = 0; \$i -lt (\$cores - $CORES_TO_LEAVE); \$i++) { \$mask = \$mask -bor ([int64]1 -shl \$i) }
  for (\$try = 0; \$try -lt 30; \$try++) {
    \$found = \$false
    foreach (\$p in Get-Process Unity -ErrorAction SilentlyContinue) {
      try { \$p.PriorityClass = 'BelowNormal'; \$p.ProcessorAffinity = [IntPtr]\$mask; \$found = \$true } catch { }
    }
    if (\$found) { Write-Output \"throttled: BelowNormal, \$(\$cores - $CORES_TO_LEAVE) of \$cores cores\"; break }
    Start-Sleep -Seconds 2
  }
" 2>/dev/null

wait $unity_pid
code=$?
echo "corpus finished, exit $code (0 = no digest changed)"
echo "log: $log"
exit $code
