#!/usr/bin/env bash
# The S1 budget: the plug deform compiled from the shipped includes and from
# the spike copies beside this script, judged on temps; listing length is
# reported only.
#
#   portal-fxc.sh [spike-dir]   YAPS_FXC_FLAGS=-DPORTAL_STAGE=n attributes a part
#
# Temps are registers the driver must find on every host shader. Lines were the
# first gate and failed as a proxy: a divide is one listing line and a branch
# skipped on most vertices counts in full, so the cost is gated on measured GPU
# time (Dev/Probes/Perf/PortalDeformPerf) and lines are printed for the record.
set -u
HERE="$(cd "$(dirname "$0")" && pwd)"
FXC_SH="$HERE/../Hlsl/yaps-fxc.sh"
SPIKE="${1:-$HERE}"
MAX_TEMPS=52

stat() { echo "$1" | grep -m1 "^stats deform" | awk -v k="$2" '{for (i = 1; i < NF; i++) if ($i == k) print $(i + 1)}'; }

base="$(YAPS_FXC_FLAGS= bash "$FXC_SH")" || { echo "FAIL baseline does not compile"; echo "$base"; exit 1; }
spike="$(bash "$FXC_SH" "$SPIKE")" || { echo "FAIL spike does not compile"; echo "$spike"; exit 1; }
echo "$base" | grep -E "^stats|warning" | sed 's/^/baseline /'
echo "$spike" | grep -E "^stats|warning" | sed 's/^/spike    /'

bl="$(stat "$base" lines)"; sl="$(stat "$spike" lines)"; st="$(stat "$spike" temps)"
fail=0
if [ "$st" -le "$MAX_TEMPS" ]; then echo "PASS temps $st <= $MAX_TEMPS"
else echo "FAIL temps $st > $MAX_TEMPS"; fail=1; fi
growth="$(awk -v a="$sl" -v b="$bl" 'BEGIN { printf "%+.2f", (a - b) * 100 / b }')"
echo "INFO lines $sl ($growth% over $bl, $(( sl - bl )) more)"
exit $fail
