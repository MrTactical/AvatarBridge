#!/usr/bin/env bash
# Builds AvatarBridge-<version>.unitypackage straight from the repo working tree.
# Version is read from BridgeDefines.cs so the package can never disagree with the
# version the tool reports at runtime.
#
# Lived in a session scratchpad until 2026-08-01, where it would have evaporated with the
# session. It is build tooling; it belongs with the code it builds.
#
# Usage:
#   build-package.sh            PUBLIC build  -> AvatarBridge-<version>-public.unitypackage
#   build-package.sh --dev      DEV build     -> AvatarBridge-<version>-dev.unitypackage
#
# Both sides are labelled, so a package's contents are never inferred from the ABSENCE of a
# suffix. Packages built before 2026-08-01 have no suffix and are all public — Tools/ did not
# exist yet, so nothing dev could have been in them. They keep their names because those names
# are what GitHub released; re-uploading a renamed asset would misstate the release history.
#
# A public package is what ships: Tools/ and Regression/ are pruned, so it carries no regression
# harness, no scene cleanup, no test-scene builder and no menu items a user should never see.
#
# A dev package carries those, remapped under Editor/DevTools/ so Unity compiles them as editor
# scripts. NEVER upload a "-dev" package to a release.
#
# The suffix is the marker, and only the dev side gets one, deliberately: the "never reuse a
# shipped version" guard below works by checking whether the public filename already exists, so
# renaming public packages — including the ~300 already built, every one of which predates Tools/
# and is therefore already clean — would blind the one rule this project has never broken.
set -euo pipefail

REPO="D:/AvatarBridge"
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

# The guard checks EVERY name this version could occupy, not just the one about to be written.
# Suffixes were added on 2026-08-01; before that a public package had no suffix at all, and ~300
# of those exist. Checking only "$OUT" would have let a suffixed build silently reuse a version
# already shipped unsuffixed, which is the one rule this project has never broken.
for taken in "$REPO/AvatarBridge-$VERSION.unitypackage" \
             "$REPO/AvatarBridge-$VERSION-public.unitypackage" \
             "$REPO/AvatarBridge-$VERSION-dev.unitypackage"; do
  # A dev build may be rebuilt over itself: it never ships, so nothing depends on it being
  # immutable, and forbidding it would mean burning a version number per test build.
  if [ "$taken" = "$REPO/AvatarBridge-$VERSION-dev.unitypackage" ] && [ "$MODE" = "dev" ]; then
    continue
  fi
  if [ -e "$taken" ]; then
    echo "REFUSING: $taken already exists — bump the version, never reuse one that shipped." >&2
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

# ---- dev extras -------------------------------------------------------------------------
# Remapped from Tools/Regression/ to Editor/DevTools/, because Unity only compiles a script as
# an editor script when an "Editor" folder is somewhere in its path — dropped at Tools/ it would
# land in the runtime assembly and fail on every UnityEditor reference it makes.
#
# Metas are synthesized rather than committed: these files never ship publicly, so a .meta in the
# repo would be dead weight that the public build has to remember to prune. The GUID is an md5 of
# the destination path, so it is stable across rebuilds — reimporting a newer dev package updates
# the script in place instead of leaving a duplicate behind.
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

# --force-local: a Windows "D:/..." path otherwise reads as a remote host to tar.
tar --force-local -czf "$OUT" -C "$STAGE" .
echo "built $OUT"
echo "  version : $VERSION"
echo "  mode    : $MODE$([ "$MODE" = dev ] && echo '   *** DEV TOOLS INCLUDED — DO NOT RELEASE ***')"
echo "  assets  : $count (+1 root folder)"
echo "  bytes   : $(stat -c%s "$OUT")"
