#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;

namespace AvatarBridge
{
    // Support for Kafe's GrabbyBones ChilloutVR mod, which lets players grab DynamicBone
    // and MagicaCloth bones and exposes two animator parameters per grabbable object:
    //   <objectName>_IsGrabbed  (bool) ; any bone in that component is being grabbed
    //   <objectName>_Angle      (float). 0..1 normalised bend of the end bone
    //
    // Naming the cloth object after the PhysBone parameter lets the mod drive the existing
    // _IsGrabbed / _Angle logic (_Stretch / _Squish / _IsPosed have no equivalent). Kept synced:
    // the owner drives them.
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
