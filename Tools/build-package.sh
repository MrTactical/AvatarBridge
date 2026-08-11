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
# Public builds prune Tools/ and Regression/. That is what ships.
# Dev builds carry them under Editor/DevTools/. Never release a dev build.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ROOT_GUID="979cbf9a9a344ae7ad3f8b3bb3381da0"   # the "Assets/AvatarBridge" folder entry
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

MODE="public"
if [ "${1:-}" = "--dev" ]; then MODE="dev"; fi

VERSION="$(sed -n 's/.*public const string Version = "\(.*\)".*/\1/p' "$REPO/Editor/BridgeDefines.cs")"
if [ -z "$VERSION" ]; then echo "could not read Version from BridgeDefines.cs" >&2; exit 1; fi
if [ "$MODE" = "dev" ]; then
  OUT="$REPO/AvatarBridge-$VERSION-dev.unitypackage"
else
  OUT="$REPO/AvatarBridge-$VERSION-public.unitypackage"
fi

# Check every name this version could occupy. Packages before
# 2026-08-01 shipped with no suffix, and ~300 of those exist.
for taken in "$REPO/AvatarBridge-$VERSION.unitypackage" \
             "$REPO/AvatarBridge-$VERSION-public.unitypackage" \
             "$REPO/AvatarBridge-$VERSION-dev.unitypackage"; do
  # A dev build may be rebuilt over itself. It never ships.
  if [ "$taken" = "$REPO/AvatarBridge-$VERSION-dev.unitypackage" ] && [ "$MODE" = "dev" ]; then
    continue
  fi
  if [ -e "$taken" ]; then
    echo "REFUSING: $taken already exists. Bump the version; never reuse one that shipped." >&2
    exit 1
  fi
done
rm -f "$OUT"

guid_of() { sed -n 's/^guid: \([0-9a-f]*\).*/\1/p' "$1" | head -1; }

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
while IFS= read -r -d '' path; do
  rel="${path#./}"
  case "$rel" in
    .*) continue ;;                      # repo plumbing
    *.unitypackage|*.meta) continue ;;   # prior builds; metas ride with their asset
  esac
  meta="$rel.meta"
  if [ ! -f "$meta" ]; then
    echo "  !! no .meta for $rel: Unity has not imported it yet" >&2
    missing=$((missing+1)); continue
  fi
  g="$(guid_of "$meta")"
  if [ -z "$g" ]; then echo "  !! no guid in $meta" >&2; missing=$((missing+1)); continue; fi

  if [ -d "$rel" ]; then
    emit "$g" "Assets/AvatarBridge/$rel" "$meta"
  else
    emit "$g" "Assets/AvatarBridge/$rel" "$meta" "$rel"
  fi
  count=$((count+1))
done < <(find . -mindepth 1 \
  \( -name '.*' \
     -o -path './docs' \
     -o -path './Tools' \
     -o -path './Regression' \
     -o -name 'CLAUDE.md' \) -prune -o -print0)

if [ "$missing" -gt 0 ]; then
  echo "ABORT: $missing asset(s) had no usable .meta" >&2
  exit 1
fi

# ---- dev extras -------------------------------------------------------------------------
# Remapped from Tools/Regression/ to Editor/DevTools/. Unity only
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

  for src in "$REPO"/Tools/Regression/*.cs; do
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
