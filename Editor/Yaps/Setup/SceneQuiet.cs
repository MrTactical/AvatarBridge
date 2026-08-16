// One switch that hides the CCK's icons, pointer spheres and cloth wires
// in the scene view while sockets are placed, and restores exactly what
// it found. An editor preference only.
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

        // Icons for every CCK type and Light. Gizmos only where drawn unselected.
        static IEnumerable<(Type type, bool hideGizmo)> Targets()
        {
            Type[] cck;
            try { cck = typeof(ABI.CCK.Components.CVRPointer).Assembly.GetTypes(); }
            catch (System.Reflection.ReflectionTypeLoadException e) { cck = e.Types.Where(t => t != null).ToArray(); }
            foreach (var t in cck)
            {
                if (t.Namespace != "ABI.CCK.Components" || t.IsAbstract || !typeof(MonoBehaviour).IsAssignableFrom(t)) continue;
                // Gizmos too for the things that draw big and solid: pointer spheres,
                // trigger boxes, spawnable trigger boxes.
                bool solid = t == typeof(ABI.CCK.Components.CVRPointer)
                             || t == typeof(ABI.CCK.Components.CVRAdvancedAvatarSettingsTrigger)
                             || t == typeof(ABI.CCK.Components.CVRSpawnableTrigger);
                yield return (t, solid);
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
            // Only what was recorded.
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
