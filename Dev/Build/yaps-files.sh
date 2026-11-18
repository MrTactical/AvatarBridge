# Sourced by build-package.sh and compile-check.sh. Paths are relative to the repo root.
#
# What the YAPS package carries: the penetration system, its window, the
# Toolkit, the Core files they need, and the conversion passes (only here;
# they compile out without the VRChat SDK).
# The Core list was settled by compiling exactly these against the CCK with no
# VRChat SDK; a new dependency fails that compile, not this script. Paths and
# GUIDs match the public package, so installing both overlays.
YAPS_CORE="BridgeContext BridgeReport BridgeSettings ShaderSpiPatcher \
AnimatorAssetSaver AnimatorDeepCopier AvatarScalerInjector OutputAssetPaths AvatarDescription \
AvatarHygiene BridgeDiagnostics CckDescriptionFiller CvrSetup AvatarFeatureDetect \
FaceTrackingConverter FaceTrackingInjector MouthLocator UnifiedBlendshapes FaceTrackingPackages \
FaceTrackingParameters CvrParameterNames GestureMap AvatarSurvey AvatarWeight AvatarSlimmer BridgeFinish DiagnosticsWriter HtmlReportWriter \
YapsMarks UploadPreflight ConversionDiff Physics/MagicaClothKeepOut"

yaps_files() {
  {
    # Folders whose contents come whole, and the folder entries themselves.
    find Editor/Yaps Editor/UI Editor/Toolkit Runtime AvatarScaler FaceTracking -mindepth 0
    printf '%s\n' Editor Editor/Core
    printf '%s\n' Editor/BridgeDefines.cs Editor/BridgeLinks.cs LICENSE.md README.md
    for name in $YAPS_CORE; do printf '%s\n' "Editor/Core/$name.cs"; done
  } | while IFS= read -r p; do
    case "$p" in *.meta) continue ;; esac
    [ -e "$p" ] || { echo "  !! missing from the YAPS list: $p" >&2; continue; }
    printf '%s\0' "$p"
  done
}
