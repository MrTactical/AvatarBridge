# Sourced by build-package.sh and compile-check.sh. Paths are relative to the repo root.
#
# What the YAPS package carries: the penetration system whole, its setup
# window, the Toolkit, and the Core files each needs.
#
# The conversion passes ride here too, and only here. No public build has
# Editor/Yaps in it, so keeping a converted avatar's penetration means
# installing the add-on beside the converter. They compile out where there
# is no VRChat SDK, which is most projects this package lands in.
#
# The Core list is not guesswork. Compiling exactly these against the CCK
# with no VRChat SDK is what settled it. Anything added to the tool that YAPS
# reaches for will fail that compile, not this script, so check the closure
# before editing this list. Destination paths and GUIDs are the public
# package's, so installing both overlays rather than duplicates.
YAPS_CORE="BridgeContext BridgeReport BridgeSettings ShaderSpiPatcher \
AnimatorAssetSaver AnimatorDeepCopier AvatarScalerInjector OutputAssetPaths AvatarDescription \
AvatarHygiene BridgeDiagnostics CckDescriptionFiller CvrSetup AvatarFeatureDetect \
FaceTrackingConverter FaceTrackingInjector MouthLocator UnifiedBlendshapes FaceTrackingPackages \
FaceTrackingParameters CvrParameterNames GestureMap AvatarSurvey AvatarWeight AvatarSlimmer BridgeFinish DiagnosticsWriter HtmlReportWriter \
YapsMarks UploadPreflight ConversionDiff"

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
