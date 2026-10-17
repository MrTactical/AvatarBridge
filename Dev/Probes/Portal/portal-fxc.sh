#!/usr/bin/env bash
# The S1 budget: the plug deform compiled from the shipped includes and from
# the spike copies beside this script, judged on temps and listing length.
#
#   portal-fxc.sh [spike-dir]   YAPS_FXC_FLAGS=-DPORTAL_STAGE=n attributes a part
#
# Temps are registers the driver must find on every host shader; lines are the
# proxy for what the plug adds to a Poiyomi pass already near its ceiling.
set -u
HERE="$(cd "$(dirname "$0")" && pwd)"
FXC_SH="$HERE/../Hlsl/yaps-fxc.sh"
SPIKE="${1:-$HERE}"
MAX_TEMPS=52
MAX_GROWTH=3

stat() { echo "$1" | grep -m1 "^stats deform" | awk -v k="$2" '{for (i = 1; i < NF; i++) if ($i == k) print $(i + 1)}'; }

base="$(YAPS_FXC_FLAGS= bash "$FXC_SH")" || { echo "FAIL baseline does not compile"; echo "$base"; exit 1; }
spike="$(bash "$FXC_SH" "$SPIKE")" || { echo "FAIL spike does not compile"; echo "$spike"; exit 1; }
echo "$base" | grep -E "^stats|warning" | sed 's/^/baseline /'
echo "$spike" | grep -E "^stats|warning" | sed 's/^/spike    /'

bl="$(stat "$base" lines)"; sl="$(stat "$spike" lines)"; st="$(stat "$spike" temps)"
limit=$(( bl * (100 + MAX_GROWTH) / 100 ))
fail=0
if [ "$st" -le "$MAX_TEMPS" ]; then echo "PASS temps $st <= $MAX_TEMPS"
else echo "FAIL temps $st > $MAX_TEMPS"; fail=1; fi
growth="$(awk -v a="$sl" -v b="$bl" 'BEGIN { printf "%+.2f", (a - b) * 100 / b }')"
if [ "$sl" -le "$limit" ]; then echo "PASS lines $sl <= $limit ($growth% over $bl)"
else echo "FAIL lines $sl > $limit ($growth% over $bl, $(( sl - limit )) over)"; fail=1; fi
exit $fail
