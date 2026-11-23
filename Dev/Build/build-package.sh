#!/usr/bin/env bash
# Builds the release packages from the working tree.
# Version comes from BridgeDefines.cs.
# Public = converter + Toolkit; the penetration system is the 18+ add-on only.
# Both share paths and GUIDs, so a project with both has one folder.
#
# Usage:
#   build-package.sh            public build -> AvatarBridge-<version>-public.unitypackage
#   build-package.sh --yaps     add-on build -> YAPS-<version>-adult.unitypackage
#   build-package.sh --dev      dev build    -> AvatarBridge-<version>-dev.unitypackage
#   build-package.sh --test TAG test build   -> AvatarBridge-<version>-test-TAG.unitypackage
#                               public contents, for select testers; a tag is spent once built
#   build-package.sh --yaps --test TAG       -> YAPS-<version>-adult-test-TAG.unitypackage
#
# A release (public or --yaps, no --test) refuses uncommitted changes unless
# --dirty, and refuses an already tagged version.
# Packages before 2026-08-01 have no suffix and are all public.
# Dev builds carry the add-on and the harness (Editor/DevTools/). Never release one.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
ROOT_GUID="979cbf9a9a344ae7ad3f8b3bb3381da0"   # the "Assets/AvatarBridge" folder entry
STAGE="$(mktemp -d)"
# Scratch for the checks. STAGE is tarred whole, so anything left in it ships.
WORK="$(mktemp -d)"
trap 'rm -rf "$STAGE" "$WORK"' EXIT

# Every argument is read: reading only the first let a misspelt flag build public.
MODE="public" TAG="" DEV=0 DIRTY=0
while [ $# -gt 0 ]; do
  case "$1" in
    --dev)   DEV=1 ;;
    --yaps)  MODE="yaps" ;;
    --test)
      TAG="${2:-}"
      if [ -z "$TAG" ] || [ "${TAG#-}" != "$TAG" ]; then echo "--test needs a tag, e.g. --test rebuild" >&2; exit 1; fi
      shift ;;
    --dirty) DIRTY=1 ;;
    *) echo "unknown argument: $1 (the usage is at the top of this script)" >&2; exit 1 ;;
  esac
  shift
done
if [ "$DEV" = 1 ]; then
  if [ "$MODE" = "yaps" ] || [ -n "$TAG" ]; then echo "--dev takes no other mode: it never ships" >&2; exit 1; fi
  MODE="dev"
fi

VERSION="$(sed -n 's/.*public const string Version = "\(.*\)".*/\1/p' "$REPO/Editor/BridgeDefines.cs")"
if [ -z "$VERSION" ]; then echo "could not read Version from BridgeDefines.cs" >&2; exit 1; fi
if [ "$MODE" = "dev" ]; then
  OUT="$REPO/AvatarBridge-$VERSION-dev.unitypackage"
elif [ "$MODE" = "yaps" ]; then
  # 18+ in the filename, so it says so before anyone opens it.
  OUT="$REPO/YAPS-$VERSION-adult${TAG:+-test-$TAG}.unitypackage"
elif [ -n "$TAG" ]; then
  OUT="$REPO/AvatarBridge-$VERSION-test-$TAG.unitypackage"
else
  OUT="$REPO/AvatarBridge-$VERSION-public.unitypackage"
fi

# Names THIS mode could occupy, incl. the pre-2026-08-01 unsuffixed ones.
# Public and YAPS ship at the same version, so they never block each other.
case "$MODE${TAG:+-test}" in
  public) taken_names=("$REPO/AvatarBridge-$VERSION.unitypackage" \
                       "$REPO/AvatarBridge-$VERSION-public.unitypackage") ;;
  yaps)   taken_names=("$REPO/YAPS-$VERSION.unitypackage" \
                       "$REPO/YAPS-$VERSION-adult.unitypackage") ;;
  *-test) taken_names=("$OUT") ;;
  dev)    taken_names=() ;;   # a dev build may be rebuilt over itself; it never ships
esac
for taken in ${taken_names+"${taken_names[@]}"}; do
  if [ -e "$taken" ]; then
    echo "REFUSING: $taken already exists. Bump the version; never reuse one that shipped." >&2
    exit 1
  fi
done

COMMIT="$(git -C "$REPO" rev-parse --short HEAD)"
# What the walk would read that HEAD does not hold. Dev and docs never ship;
# the add-on's README does, from Dev/Build.
DIRTY_TREE="$(git -C "$REPO" status --porcelain -- . ':!Dev' ':!docs'
  if [ "$MODE" = "yaps" ]; then git -C "$REPO" status --porcelain -- Dev/Build/README-yaps.md; fi)"
if [ "$MODE" != "dev" ] && [ -z "$TAG" ]; then
  # Artefacts are gitignored, so another worktree or a fresh clone has none
  # and the file check above passes there. The tag is in every checkout.
  if git -C "$REPO" rev-parse -q --verify "refs/tags/v$VERSION" >/dev/null; then
    echo "REFUSING: v$VERSION is already tagged. Bump the version; never reuse one that shipped." >&2
    exit 1
  fi
  if [ -n "$DIRTY_TREE" ] && [ "$DIRTY" = 0 ]; then
    echo "REFUSING: uncommitted changes would ship in this release:" >&2
    echo "$DIRTY_TREE" >&2
    echo "Commit them, or pass --dirty to build this tree anyway." >&2
    exit 1
  fi
fi
rm -f "$OUT"

guid_of() { sed -n 's/^guid: \([0-9a-f]*\).*/\1/p' "$1" | head -1; }

# What the YAPS package carries, shared with compile-check.sh.
. "$REPO/Dev/Build/yaps-files.sh"

# Would the YAPS package compile on its own? It ships a hand-picked subset of
# Editor/Core, so a Toolkit file can reach a Core type left behind: CS0246 on
# import (how 4.1.0 and 4.1.1 shipped broken).
# Closure check, not a compiler. Comments and plain strings are stripped
# (prose and reflection names are false alarms); only namespace-level
# public/internal types count. For a real compile use MonoBleedingEdge mono
# with lib/mono/4.5/csc.exe: the Roslyn csc under msbuild fails silently.
# Strings before comments, so a URL's "//" does not cut its line.
# An interpolated string keeps its text, since its holes are code.
strip_comments() { sed -E 's/(^|[^$])"[^"]*"/\1""/g; s://.*::' "$1" | awk '/\/\*/{b=1} !b{print} /\*\//{b=0}'; }

check_yaps_closure() { # $1 = newline-delimited list of shipped paths
  local shipped="$1" bad=0 t s hit
  # Strip once so each type below costs one grep.
  while IFS= read -r s; do
    case "$s" in *.cs) ;; *) continue ;; esac
    mkdir -p "$WORK/stripped/$(dirname "$s")"
    strip_comments "$s" > "$WORK/stripped/$s"
  done < "$shipped"

  # All of Editor, not only Editor/Core's top level: the physics writers and
  # the windows at the Editor root stay behind too.
  find Editor -name '*.cs' | while IFS= read -r f; do
    grep -qxF "$f" "$shipped" && continue
    # grep exits 1 on a file with no type; under set -e + pipefail that ends the build.
    { grep -ohE "^    (public|internal)( static| sealed| abstract| partial)* (class|struct|enum|interface) [A-Z][A-Za-z0-9_]*" "$f" || true; } \
      | awk '{print $NF}'
  done | sort -u > "$WORK/absent.txt"

  while IFS= read -r t; do
    hit="$({ grep -rlE "\b$t\b" "$WORK/stripped" || true; } | head -1)"
    if [ -n "$hit" ]; then
      echo "  !! \"$t\" is used by ${hit#"$WORK/stripped/"} but its file is not in the YAPS package" >&2
      bad=1
    fi
  done < "$WORK/absent.txt"

  if [ "$bad" -ne 0 ]; then
    echo "ABORT: the YAPS package would not compile on its own." >&2
    echo "       Add the missing file(s) to YAPS_CORE, or keep the reference out of what YAPS ships." >&2
    exit 1
  fi
  echo "  yaps closure: every Editor type the package uses travels with it"
}

emit() { # $1=guid  $2=pathname  $3=meta  $4=asset(optional)
  mkdir -p "$STAGE/$1"
  cp "$3" "$STAGE/$1/asset.meta"
  printf '%s' "$2" > "$STAGE/$1/pathname"
  if [ -n "${4:-}" ]; then cp "$4" "$STAGE/$1/asset"; fi
}

# Root folder entry.
mkdir -p "$STAGE/$ROOT_GUID"
cat > "$STAGE/$ROOT_GUID/asset.meta" <<EOF
fileFormatVersion: 2
guid: $ROOT_GUID
folderAsset: yes
DefaultImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
EOF
printf '%s' "Assets/AvatarBridge" > "$STAGE/$ROOT_GUID/pathname"

count=0 missing=0
cd "$REPO"

# One list for both the closure check and the packer, so they never disagree.
if [ "$MODE" = "yaps" ]; then
  yaps_files > "$WORK/yaps.list"
  tr '\0' '\n' < "$WORK/yaps.list" > "$WORK/yaps.txt"
  check_yaps_closure "$WORK/yaps.txt"
fi
while IFS= read -r -d '' path; do
  rel="${path#./}"
  case "$rel" in
    .*) continue ;;                      # repo plumbing
    *.unitypackage|*.unitypackage.superseded-*|*.meta) continue ;;   # prior builds; metas ride with their asset
  esac
  # Ignored files never ship: probe output lands in the repo root by design.
  if git check-ignore -q -- "$rel" 2>/dev/null; then continue; fi
  meta="$rel.meta"
  if [ ! -f "$meta" ]; then
    echo "  !! no .meta for $rel: Unity has not imported it yet" >&2
    missing=$((missing+1)); continue
  fi
  g="$(guid_of "$meta")"
  if [ -z "$g" ]; then echo "  !! no guid in $meta" >&2; missing=$((missing+1)); continue; fi

  # Own README at the same path and GUID, so the public package replaces it.
  src="$rel"
  if [ "$MODE" = "yaps" ] && [ "$rel" = "README.md" ]; then src="Dev/Build/README-yaps.md"; fi

  if [ -d "$rel" ]; then
    emit "$g" "Assets/AvatarBridge/$rel" "$meta"
  else
    emit "$g" "Assets/AvatarBridge/$rel" "$meta" "$src"
  fi
  count=$((count+1))
done < <(if [ "$MODE" = "yaps" ]; then cat "$WORK/yaps.list"; else
  # Editor/Yaps and Runtime are the add-on. YapsMarks stays: the survey meets
  # its materials on avatars converted with the add-on. Dev keeps all, the
  # harness uses add-on types.
  addon=(-o -path './Editor/Yaps' -o -path './Runtime')
  if [ "$MODE" = "dev" ]; then addon=(); fi
  find . -mindepth 1 \
    \( -name '.*' \
       -o -path './docs' \
       -o -path './Dev' \
       -o -path './Regression' \
       ${addon+"${addon[@]}"} \
       -o -name 'CLAUDE.md' -o -name 'AGENTS.md' -o -name 'Review.md' \) -prune -o -print0
fi)

if [ "$missing" -gt 0 ]; then
  echo "ABORT: $missing asset(s) had no usable .meta" >&2
  exit 1
fi

# ---- dev extras -------------------------------------------------------------------------
# Dev/ goes to Editor/DevTools/: Unity only compiles editor scripts under "Editor".
# Metas are synthesized; GUID = md5 of the destination path, stable across rebuilds.
if [ "$MODE" = "dev" ]; then
  devguid() { printf '%s' "$1" | md5sum | cut -c1-32; }

  for folder in "Editor/DevTools"; do
    g="$(devguid "folder:$folder")"
    mkdir -p "$STAGE/$g"
    cat > "$STAGE/$g/asset.meta" <<EOF
fileFormatVersion: 2
guid: $g
folderAsset: yes
DefaultImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
EOF
    printf '%s' "Assets/AvatarBridge/$folder" > "$STAGE/$g/pathname"
    count=$((count+1))
  done

  # The harness folders deploy.sh refreshes, subfolders included, flat into DevTools. Shaders the
  # capture probes load are not packed: those probes need a deploy.
  while IFS= read -r src; do
    base="$(basename "$src")"
    dest="Editor/DevTools/$base"
    g="$(devguid "$dest")"
    mkdir -p "$STAGE/$g"
    cat > "$STAGE/$g/asset.meta" <<EOF
fileFormatVersion: 2
guid: $g
MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
  userData:
  assetBundleName:
  assetBundleVariant:
EOF
    printf '%s' "Assets/AvatarBridge/$dest" > "$STAGE/$g/pathname"
    cp "$src" "$STAGE/$g/asset"
    count=$((count+1))
    echo "  + dev: $dest"
  done < <(find "$REPO"/Dev/{Corpus,Tests,Probes,Scenes,Tester} -name '*.cs' 2>/dev/null | sort)
fi

# --force-local: a drive-letter path otherwise reads as a remote host to tar.
tar --force-local -czf "$OUT" -C "$STAGE" .
echo "built $OUT"
echo "  version : $VERSION"
echo "  mode    : $MODE${TAG:+ test $TAG}$([ "$MODE" = dev ] && echo '   *** DEV TOOLS INCLUDED. DO NOT RELEASE ***')"
echo "  commit  : $COMMIT${DIRTY_TREE:+ plus uncommitted changes}"
echo "  assets  : $count (+1 root folder)"
echo "  bytes   : $(stat -c%s "$OUT")"
