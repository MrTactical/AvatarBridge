// VRCFury patches the plug's shader with SPS during its own bake. That
// patched shader is VRChat-only, and our patcher correctly refuses to
// touch a shader that already carries it — so if the bake ran normally we
// would have no clean input to work from.
//
// Turning the plug's own SPS flag off before the bake solves it. The bake
// probe (Tools/SpsSpike/SpsBakeProbe.cs) measured exactly what that costs:
// nothing we need. The plug and socket objects, all 24 protocol lights and
// all 119 contacts still appear; only VRCFury's own bake texture is lost,
// which is what YapsBaker replaces.
//
// The flags are flipped on the user's own avatar in their own scene, so
// they are put back afterwards no matter how the bake ends.
#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    public class YapsBakePrep
    {
        const string Category = "YAPS";
        readonly List<(Component plug, bool was)> _flipped = new List<(Component, bool)>();

        // The author's own per-plug settings, read off the component while
        // it still exists. The bake destroys it, so anything not captured
        // here is lost and quietly replaced by a default — which is what
        // happened to overrun: VRCFury lets an author say whether the tip
        // may travel past the socket, and every converted plug was getting
        // "yes" regardless of what they chose.
        //
        // Keyed by the object the plug component sits on, which survives
        // the bake as the parent of BakedSpsPlug.
        public static readonly Dictionary<string, bool> AuthoredOverrun =
            new Dictionary<string, bool>();

        public static YapsBakePrep Begin(BridgeContext ctx, GameObject source)
        {
            var prep = new YapsBakePrep();
            AuthoredOverrun.Clear();
            if (ctx == null || !ctx.Settings.convertYapsSystems || source == null)
            {
                return prep;
            }

            foreach (var component in source.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component.GetType().Name != "VRCFuryHapticPlug")
                {
                    continue;
                }
                var overrun = component.GetType().GetField("spsOverrun",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (overrun != null && overrun.FieldType == typeof(bool))
                {
                    AuthoredOverrun[component.gameObject.name] = (bool) overrun.GetValue(component);
                }

                var field = component.GetType().GetField("enableSps",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field == null || field.FieldType != typeof(bool))
                {
                    continue;
                }
                bool was = (bool) field.GetValue(component);
                if (!was)
                {
                    continue;
                }
                field.SetValue(component, false);
                EditorUtility.SetDirty(component);
                prep._flipped.Add((component, true));
            }

            if (prep._flipped.Count > 0)
            {
                ctx.Report.Converted(Category,
                    $"Asked VRCFury not to patch {prep._flipped.Count} plug shader(s) before baking",
                    "Its deform is a VRChat shader, and one already carrying it cannot take ours. " +
                    "Everything else the bake produces — the plug and socket objects, the protocol " +
                    "lights, the contacts — is untouched by this; only VRCFury's own bake texture " +
                    "goes, and AvatarBridge writes its own. Your avatar's plugs are set back to how " +
                    "you had them as soon as the bake finishes.");
            }
            return prep;
        }

        public void Restore()
        {
            foreach (var (plug, was) in _flipped)
            {
                if (plug == null)
                {
                    continue;
                }
                var field = plug.GetType().GetField("enableSps",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    field.SetValue(plug, was);
                    EditorUtility.SetDirty(plug);
                }
            }
            _flipped.Clear();
        }
    }
}
#endif
