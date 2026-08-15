// One switch that quietens the scene view while sockets are being placed.
//
// A converted avatar carries ninety-odd CCK components — every pointer,
// every trigger, every driver — and each draws an icon in the scene view,
// plus a blue sphere per pointer and MagicaCloth's collider wires. None of
// it is YAPS's and all of it buries a socket gizmo. This hides those, and
// only those, and puts back exactly what it found: it records each type's
// icon and gizmo state before touching it, so a user who had already
// hidden Light icons does not get them switched on by "restore".
//
// It changes what the scene view DRAWS — an editor preference — and
// nothing on the avatar. Unity's own colliders are left alone; those are
// the Gizmos menu's business.
#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    public static class SceneQuiet
    {
        const string OnKey = "YAPS.SceneQuiet.On";
        const string SavedKey = "YAPS.SceneQuiet.Saved";

        public static bool IsQuiet => EditorPrefs.GetBool(OnKey, false);

        public static void Toggle()
        {
            if (IsQuiet) Restore(); else Quiet();
            SceneView.RepaintAll();
        }

        // Icons go for every CCK component type and for Light (two marker
        // lights per socket add up). Gizmos go only where they are drawn
        // unselected and everywhere at once: the pointer's blue sphere and
        // MagicaCloth's wires. A trigger's box draws only when selected and
        // is useful then, so it stays.
        static IEnumerable<(Type type, bool hideGizmo)> Targets()
        {
            Type[] cck;
            try { cck = typeof(ABI.CCK.Components.CVRPointer).Assembly.GetTypes(); }
            catch (System.Reflection.ReflectionTypeLoadException e) { cck = e.Types.Where(t => t != null).ToArray(); }
            foreach (var t in cck)
            {
                if (t.Namespace != "ABI.CCK.Components" || t.IsAbstract || !typeof(MonoBehaviour).IsAssignableFrom(t)) continue;
                yield return (t, t == typeof(ABI.CCK.Components.CVRPointer));
            }
            yield return (typeof(Light), false);
            foreach (var name in new[] { "MagicaCloth2.MagicaCloth", "MagicaCloth2.MagicaSphereCollider",
                                         "MagicaCloth2.MagicaCapsuleCollider", "MagicaCloth2.MagicaPlaneCollider" })
            {
                var t = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(x => x != null);
                if (t != null) yield return (t, true);
            }
        }

        static void Quiet()
        {
            var saved = new List<string>();
            foreach (var (type, hideGizmo) in Targets())
            {
                if (!GizmoUtility.TryGetGizmoInfo(type, out var info)) continue;
                saved.Add(type.AssemblyQualifiedName + "|" + (info.iconEnabled ? 1 : 0) + "|" + (info.gizmoEnabled ? 1 : 0));
                if (info.hasIcon) GizmoUtility.SetIconEnabled(type, false);
                if (hideGizmo && info.hasGizmo) GizmoUtility.SetGizmoEnabled(type, false, false);
            }
            EditorPrefs.SetString(SavedKey, string.Join("\n", saved));
            EditorPrefs.SetBool(OnKey, true);
        }

        static void Restore()
        {
            // Exactly what was recorded, and nothing else: a type with no
            // record was never touched, and switching it on would undo a
            // choice the user made themselves.
            string saved = EditorPrefs.GetString(SavedKey, "");
            foreach (var line in saved.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('|');
                if (parts.Length != 3) continue;
                var type = Type.GetType(parts[0]);
                if (type == null) continue;
                if (!GizmoUtility.TryGetGizmoInfo(type, out var info)) continue;
                if (info.hasIcon) GizmoUtility.SetIconEnabled(type, parts[1] == "1");
                if (info.hasGizmo) GizmoUtility.SetGizmoEnabled(type, parts[2] == "1", false);
            }
            EditorPrefs.DeleteKey(SavedKey);
            EditorPrefs.SetBool(OnKey, false);
        }
    }
}
#endif
