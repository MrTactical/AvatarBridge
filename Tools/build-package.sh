#!/usr/bin/env bash
# Builds AvatarBridge-<version>.unitypackage straight from the repo working tree.
# Version is read from BridgeDefines.cs so the package can never disagree with the
# version the tool reports at runtime.
#
# Lived in a session scratchpad until 2026-08-01, where it would have evaporated with the
# session. It is build tooling; it belongs with the code it builds.
set -euo pipefail

REPO="D:/AvatarBridge"
ROOT_GUID="979cbf9a9a344ae7ad3f8b3bb3381da0"   # the "Assets/AvatarBridge" folder entry
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

VERSION="$(sed -n 's/.*public const string Version = "\(.*\)".*/\1/p' "$REPO/Editor/BridgeDefines.cs")"
if [ -z "$VERSION" ]; then echo "could not read Version from BridgeDefines.cs" >&2; exit 1; fi
OUT="$REPO/AvatarBridge-$VERSION.unitypackage"

if [ -e "$OUT" ]; then
  echo "REFUSING: $OUT already exists — bump the version, never reuse one that shipped." >&2
  exit 1
fi

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
    .*) continue ;;                      # repo plumbing (.git, .github, .claude, .gitignore)
    *.unitypackage|*.meta) continue ;;   # prior builds, and metas ride with their asset
  esac
  meta="$rel.meta"
  if [ ! -f "$meta" ]; then
    echo "  !! no .meta for $rel — Unity has not imported it yet" >&2
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

# --force-local: a Windows "D:/..." path otherwise reads as a remote host to tar.
tar --force-local -czf "$OUT" -C "$STAGE" .
echo "built $OUT"
echo "  version : $VERSION"
echo "  assets  : $count (+1 root folder)"
echo "  bytes   : $(stat -c%s "$OUT")"
