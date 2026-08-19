#!/usr/bin/env bash
# Builds AvatarBridge-<version>.unitypackage from the working tree.
# Version comes from BridgeDefines.cs. Package and tool always agree.
#
# Usage:
#   build-package.sh            public build -> AvatarBridge-<version>-public.unitypackage
#   build-package.sh --dev      dev build    -> AvatarBridge-<version>-dev.unitypackage
#
# Both modes are labelled. Never infer contents from a missing suffix.
# Packages built before 2026-08-01 have no suffix and are all public.
# They keep those names; GitHub released them under those names.
#
# Public builds prune Dev/ and Regression/. That is what ships.
# Dev builds carry the harness under Editor/DevTools/. Never release a dev build.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
ROOT_GUID="979cbf9a9a344ae7ad3f8b3bb3381da0"   # the "Assets/AvatarBridge" folder entry
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

MODE="public"
if [ "${1:-}" = "--dev" ]; then MODE="dev"; fi
if [ "${1:-}" = "--yaps" ]; then MODE="yaps"; fi

VERSION="$(sed -n 's/.*public const string Version = "\(.*\)".*/\1/p' "$REPO/Editor/BridgeDefines.cs")"
if [ -z "$VERSION" ]; then echo "could not read Version from BridgeDefines.cs" >&2; exit 1; fi
if [ "$MODE" = "dev" ]; then
  OUT="$REPO/AvatarBridge-$VERSION-dev.unitypackage"
elif [ "$MODE" = "yaps" ]; then
  OUT="$REPO/YAPS-$VERSION.unitypackage"
else
  OUT="$REPO/AvatarBridge-$VERSION-public.unitypackage"
fi

# Every name THIS mode could already occupy. The full package and the YAPS
# one ship together at the same version, so they do not block each other;
# each only refuses to overwrite itself. Packages before 2026-08-01 shipped
# with no suffix, and ~300 of those exist.
case "$MODE" in
  public) taken_names=("$REPO/AvatarBridge-$VERSION.unitypackage" \
                       "$REPO/AvatarBridge-$VERSION-public.unitypackage") ;;
  yaps)   taken_names=("$REPO/YAPS-$VERSION.unitypackage") ;;
  dev)    taken_names=() ;;   # a dev build may be rebuilt over itself; it never ships
esac
for taken in ${taken_names+"${taken_names[@]}"}; do
  if [ -e "$taken" ]; then
    echo "REFUSING: $taken already exists. Bump the version; never reuse one that shipped." >&2
    exit 1
  fi
done
rm -f "$OUT"

guid_of() { sed -n 's/^guid: \([0-9a-f]*\).*/\1/p' "$1" | head -1; }

# What the YAPS package carries: the penetration system, its setup window,
# the Toolkit, and the support each needs. NOT the converter.
#
# The list is not guesswork. Compiling exactly these against the CCK with no
# VRChat SDK is what settled it, and the four conversion passes below are the
# only files under Editor/Yaps that fall outside it. Anything added to the
# tool that YAPS reaches for will fail that compile, not this script, so
# check the closure before editing this list. Destination paths and GUIDs are
# the full package's, so installing both overlays rather than duplicates.
YAPS_CORE="BridgeContext BridgeReport BridgeSettings ShaderSpiPatcher ShaderFixRecipes \
AnimatorAssetSaver AnimatorDeepCopier AvatarScalerInjector OutputAssetPaths AvatarDescription \
AvatarHygiene BridgeDiagnostics CckDescriptionFiller CvrSetup AvatarFeatureDetect \
FaceTrackingConverter FaceTrackingInjector MouthLocator UnifiedBlendshapes FaceTrackingPackages \
CvrParameterNames GestureMap AvatarSurvey AvatarWeight"
# Conversion passes: VRChat-only, and dead weight in a ChilloutVR project.
YAPS_SKIP="YapsConverter YapsBakePrep YapsRename YapsReapply"

yaps_files() {
  {
    # Folders whose contents come whole, and the folder entries themselves.
    find Editor/Yaps Editor/UI Editor/Toolkit Runtime AvatarScaler FaceTracking -mindepth 0
    printf '%s\n' Editor Editor/Core
    printf '%s\n' Editor/BridgeDefines.cs Editor/BridgeLinks.cs LICENSE.md README.md
    for name in $YAPS_CORE; do printf '%s\n' "Editor/Core/$name.cs"; done
  } | while IFS= read -r p; do
    for skip in $YAPS_SKIP; do
      case "$p" in */$skip.cs) continue 2 ;; esac
    done
    case "$p" in *.meta) continue ;; esac
    [ -e "$p" ] || { echo "  !! missing from the YAPS list: $p" >&2; continue; }
    printf '%s\0' "$p"
  done
}

# Would the YAPS package compile on its own?
#
# It ships a hand-written subset of Editor/Core and the whole of
# Editor/Toolkit, so a card added to the Toolkit can reach a Core type the
# list has never heard of. The package then fails on import with CS0246
# while the full package is perfectly fine, which is exactly how 4.1.0 and
# 4.1.1 shipped broken for anyone installing YAPS on its own.
#
# Comments are stripped first, because half of Core is named in prose and
# every one of those would be a false alarm. Only namespace-level public and
# internal types count, so a private nested enum that happens to share a name
# with somebody's field is not one either.
#
# This is a closure check, not a compiler: it catches a shipped file
# referencing a type that stayed behind. For the real thing, compile the
# package's own file list against the CCK with no VRChat SDK defined — and
# use MonoBleedingEdge/bin/mono.exe with lib/mono/4.5/csc.exe, since the
# Roslyn csc under lib/mono/msbuild fails to start and prints no "error CS"
# lines, which reads as a clean compile.
strip_comments() { sed 's://.*::' "$1" | awk '/\/\*/{b=1} !b{print} /\*\//{b=0}'; }

check_yaps_closure() { # $1 = newline-delimited list of shipped paths
  local shipped="$1" bad=0 t s
  for f in Editor/Core/*.cs; do
    grep -qxF "$f" "$shipped" && continue
    # A Core file with no namespace-level type is ordinary, and grep says so
    # by exiting 1. Under `set -e` with pipefail that would end the build.
    { grep -ohE "^    (public|internal)( static| sealed| abstract| partial)* (class|struct|enum|interface) [A-Z][A-Za-z0-9_]*" "$f" || true; } \
      | awk '{print $NF}'
  done | sort -u > "$STAGE/absent.txt"

  while IFS= read -r t; do
    while IFS= read -r s; do
      case "$s" in *.cs) ;; *) continue ;; esac
      if strip_comments "$s" | grep -qE "\b$t\b"; then
        echo "  !! \"$t\" is used by $s but its file is not in the YAPS package" >&2
        bad=1
        break
      fi
    done < "$shipped"
  done < "$STAGE/absent.txt"

  if [ "$bad" -ne 0 ]; then
    echo "ABORT: the YAPS package would not compile on its own." >&2
    echo "       Add the missing file(s) to YAPS_CORE, or keep the reference out of what YAPS ships." >&2
    exit 1
  fi
  echo "  yaps closure: every Core type the package uses travels with it"
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

# Built once: the closure check reads the same list the packer walks, so the
# two can never disagree about what ships.
if [ "$MODE" = "yaps" ]; then
  yaps_files > "$STAGE/yaps.list"
  tr '\0' '\n' < "$STAGE/yaps.list" > "$STAGE/yaps.txt"
  check_yaps_closure "$STAGE/yaps.txt"
fi
while IFS= read -r -d '' path; do
  rel="${path#./}"
  case "$rel" in
    .*) continue ;;                      # repo plumbing
    *.unitypackage|*.unitypackage.superseded-*|*.meta) continue ;;   # prior builds; metas ride with their asset
  esac
  # Anything git ignores is not part of the package: probe output lands in
  # the repo root by design, and a build must not stop for it, let alone
  # ship it. survey.md was the one that found this.
  if git check-ignore -q -- "$rel" 2>/dev/null; then continue; fi
  meta="$rel.meta"
  if [ ! -f "$meta" ]; then
    echo "  !! no .meta for $rel: Unity has not imported it yet" >&2
    missing=$((missing+1)); continue
  fi
  g="$(guid_of "$meta")"
  if [ -z "$g" ]; then echo "  !! no guid in $meta" >&2; missing=$((missing+1)); continue; fi

  # The YAPS package explains itself in its own words, at the same path and
  # the same GUID, so installing the full package later just replaces it.
  src="$rel"
  if [ "$MODE" = "yaps" ] && [ "$rel" = "README.md" ]; then src="Dev/Build/README-yaps.md"; fi

  if [ -d "$rel" ]; then
    emit "$g" "Assets/AvatarBridge/$rel" "$meta"
  else
    emit "$g" "Assets/AvatarBridge/$rel" "$meta" "$src"
  fi
  count=$((count+1))
done < <(if [ "$MODE" = "yaps" ]; then cat "$STAGE/yaps.list"; else
  find . -mindepth 1 \
    \( -name '.*' \
       -o -path './docs' \
       -o -path './Dev' \
       -o -path './Regression' \
       -o -name 'CLAUDE.md' \) -prune -o -print0
fi)

if [ "$missing" -gt 0 ]; then
  echo "ABORT: $missing asset(s) had no usable .meta" >&2
  exit 1
fi

# ---- dev extras -------------------------------------------------------------------------
# Remapped from Dev/ to Editor/DevTools/. Unity only
# compiles editor scripts under an "Editor" folder.
#
# Metas are synthesized, not committed. The GUID is an md5 of the
# destination path, so it stays stable across rebuilds.
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

  for src in "$REPO"/Dev/Corpus/*.cs "$REPO"/Dev/Tests/*.cs "$REPO"/Dev/Probes/*.cs "$REPO"/Dev/Scenes/*.cs; do
    [ -e "$src" ] || continue
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
  done
fi

# --force-local: a drive-letter path otherwise reads as a remote host to tar.
tar --force-local -czf "$OUT" -C "$STAGE" .
echo "built $OUT"
echo "  version : $VERSION"
echo "  mode    : $MODE$([ "$MODE" = dev ] && echo '   *** DEV TOOLS INCLUDED. DO NOT RELEASE ***')"
echo "  assets  : $count (+1 root folder)"
echo "  bytes   : $(stat -c%s "$OUT")"
