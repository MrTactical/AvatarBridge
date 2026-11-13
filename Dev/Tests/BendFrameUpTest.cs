#if AVATARBRIDGE_YAPS
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // The bake took the bend's up from the plug's root bone, and a Blender bone's y runs
    // along the bone, so every Blender-rigged plug fell back to the bone's x, which its
    // roll decides. With roll 0 a forward plug's curvature bent sideways. Up is the
    // avatar's now, wherever the shaft is not pointing straight up or down.
    public static class BendFrameUpTest
    {
        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Plug bend frame takes the avatar's up")]
        public static void Run()
        {
            int fail = 0;
            void Check(string what, bool ok)
            {
                if (!ok) fail++;
                Debug.Log($"[BendFrameUpTest] {(ok ? "ok   " : "WRONG")} {what}");
            }

            var measure = typeof(YapsBaker).GetMethod("MeasureFrame", BindingFlags.NonPublic | BindingFlags.Static);
            if (measure == null)
            {
                Debug.Log("[BendFrameUpTest] WRONG no MeasureFrame");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            Quaternion Frame(Vector3 shaft, Quaternion bone)
            {
                // A rod 0.2 long and 0.02 round, its base at the origin.
                var positions = new List<Vector3>();
                var side = Vector3.Cross(shaft, Mathf.Abs(shaft.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
                var other = Vector3.Cross(shaft, side);
                for (int i = 0; i <= 20; i++)
                    for (int a = 0; a < 8; a++)
                    {
                        float t = a * Mathf.PI / 4f;
                        positions.Add(shaft * (0.01f * i) + (side * Mathf.Cos(t) + other * Mathf.Sin(t)) * 0.01f);
                    }
                var active = positions.Select(_ => 1f).ToList();
                var args = new List<object> { positions, active, Vector3.zero, bone, false };
                if (measure.GetParameters().Any(p => p.Name == "avatarUp")) args.Add(Vector3.up);
                args.AddRange(new object[] { null, null, null, null });
                // Invoke writes the out values into the array it was given.
                var call = args.ToArray();
                measure.Invoke(null, call);
                return (Quaternion)call[call.Length - 3];
            }

            // A Blender bone pointing forward with roll 0: its y runs along the shaft, its x sideways.
            var blender = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            var forward = Frame(Vector3.forward, blender);
            Check("a forward plug on an unrolled Blender bone bends in the avatar's vertical plane",
                Vector3.Angle(forward * Vector3.up, Vector3.up) < 1f);
            Check("its shaft axis is still the measured one",
                Vector3.Angle(forward * Vector3.forward, Vector3.forward) < 1f);

            // Angled down 45 degrees: up stays the avatar's, square to the shaft.
            var angled = Frame((Vector3.forward + Vector3.down).normalized, blender);
            Check("an angled plug's up is the avatar's up, square to its shaft",
                Vector3.Dot(angled * Vector3.up, Vector3.up) > 0.7f
                && Mathf.Abs(Vector3.Dot(angled * Vector3.up, angled * Vector3.forward)) < 0.01f);

            // Hanging straight down: the avatar's up runs along it, so the bone decides.
            var bone = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            var hanging = Frame(Vector3.down, bone);
            Check("a plug hanging straight down falls back to its bone's axes",
                Vector3.Angle(hanging * Vector3.up, Vector3.forward) < 1f);

            Debug.Log($"[BendFrameUpTest] {(fail == 0 ? "PASS" : fail + " WRONG")}");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
