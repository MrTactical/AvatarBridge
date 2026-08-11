#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;

namespace AvatarBridge
{
    // Support for Kafe's GrabbyBones ChilloutVR mod, which lets players grab DynamicBone
    // and MagicaCloth bones and exposes two animator parameters per grabbable object:
    //   <objectName>_IsGrabbed  (bool) ; any bone in that component is being grabbed
    //   <objectName>_Angle      (float). 0..1 normalised bend of the end bone
    //
    // VRChat PhysBones expose the analogous _IsGrabbed / _Angle (plus _Stretch / _Squish /
    // _IsPosed, which GrabbyBones does not provide). By naming the converted cloth object
    // after the PhysBone's parameter, the avatar's existing grab-reactive FX logic is
    // driven by GrabbyBones for anyone running the mod. The parameters are kept synced
    // (not "#"-local) because GrabbyBones has the owner drive them and the game syncs them.
    public static class GrabbyBonesSupport
    {
        static readonly HashSet<string> _usedNames = new HashSet<string>();

        public static void Reset() => _usedNames.Clear();

        public static string RegisterAndName(BridgeContext ctx, string physBoneParameter)
        {
            string name = physBoneParameter;
            int suffix = 2;
            while (!_usedNames.Add(name))
            {
                name = $"{physBoneParameter}_{suffix++}";
            }

            // GrabbyBones drives these; keep their exact names (no local "#" prefix).
            ctx.PreserveParameters.Add(name + "_IsGrabbed");
            ctx.PreserveParameters.Add(name + "_Angle");
            return name;
        }
    }
}
#endif
