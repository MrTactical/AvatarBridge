#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    /// <summary>
    /// Known-answer test for keeping VERTICAL root movement on transplanted Action poses — and for
    /// rotation deliberately NOT being kept.
    ///
    /// The history matters, because this test replaces one that asserted the opposite. Keeping
    /// RootQ made a transforming avatar turn correctly in the Unity editor and did nothing in
    /// game: ChilloutVR's character controller owns the capsule and the capsule is always upright,
    /// so a root rotation curve is dead on arrival there. An exemption that only editor scenes can
    /// see is worse than none — it makes the editor lie about the game. Height is different: Y is
    /// baked into the pose by the clip's import settings, so it survives both places, and
    /// flattening it held a transforming avatar at standing height until the final snap.
    ///
    /// If rotation ever needs to work, it has to become POSE — bake the root rotation into the
    /// hips — not an exemption here. Plan in Regression/subset.txt.
    /// </summary>
    public static class RootVerticalTest
    {
        [MenuItem("Tools/AvatarBridge Dev/Test — vertical kept on Action poses")]
        public static void Run()
        {
            int fail = 0;

            // A clip that travels, descends, and turns: 1 m along X, 0.87 m down, 90° of yaw.
            AnimationClip Moving()
            {
                var clip = new AnimationClip { name = "MoveDescendTurn" };
                void Set(string prop, float a, float b) => AnimationUtility.SetEditorCurve(clip,
                    EditorCurveBinding.FloatCurve("", typeof(Animator), prop),
                    AnimationCurve.Linear(0f, a, 1f, b));
                Set("RootT.x", 0f, 1f);
                Set("RootT.y", 0.98f, 0.11f);
                Set("RootT.z", 0f, 0f);
                Set("RootQ.x", 0f, 0f);
                Set("RootQ.y", 0f, 0.707f);
                Set("RootQ.z", 0f, 0f);
                Set("RootQ.w", 1f, 0.707f);
                return clip;
            }

            bool Varies(AnimationClip clip, string prop)
            {
                var b = AnimationUtility.GetCurveBindings(clip)
                    .FirstOrDefault(x => x.propertyName == prop && string.IsNullOrEmpty(x.path));
                if (string.IsNullOrEmpty(b.propertyName)) return false;
                var c = AnimationUtility.GetEditorCurve(clip, b);
                if (c == null || c.keys.Length == 0) return false;
                return Mathf.Abs(c.keys.Max(k => k.value) - c.keys.Min(k => k.value)) > 0.001f;
            }

            int Check(string label, bool actual, bool expected)
            {
                bool ok = actual == expected;
                Debug.Log($"[RootVerticalTest] {(ok ? "ok  " : "WRONG")} {label} — expected " +
                          $"{(expected ? "MOVES" : "flattened")}, got {(actual ? "MOVES" : "flattened")}");
                return ok ? 0 : 1;
            }

            // 1. Default behaviour, unchanged: a moving clip loses everything.
            LocomotionGrafter.ResetClones();
            var plain = LocomotionGrafter.WithoutRootMotion(Moving(), onlyIfTravels: true);
            fail += Check("default: RootT.y", Varies(plain, "RootT.y"), false);
            fail += Check("default: RootQ.y", Varies(plain, "RootQ.y"), false);

            // 2. Transplanted Action pose: height survives; travel AND rotation still go.
            LocomotionGrafter.ResetClones();
            var kept = LocomotionGrafter.WithoutRootMotion(Moving(), onlyIfTravels: true,
                keepVertical: true);
            fail += Check("keepVertical: RootT.y KEPT — height is the pose", Varies(kept, "RootT.y"), true);
            fail += Check("keepVertical: RootT.x still stripped", Varies(kept, "RootT.x"), false);
            fail += Check("keepVertical: RootQ.y still stripped — dead in game", Varies(kept, "RootQ.y"), false);
            fail += Check("keepVertical: RootQ.w still stripped", Varies(kept, "RootQ.w"), false);

            // 3. A clip that returns home is spared entirely, flag or no flag.
            LocomotionGrafter.ResetClones();
            var homebound = new AnimationClip { name = "Homebound" };
            AnimationUtility.SetEditorCurve(homebound,
                EditorCurveBinding.FloatCurve("", typeof(Animator), "RootQ.y"),
                new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.5f, 0.5f), new Keyframe(1f, 0f)));
            var spared = LocomotionGrafter.WithoutRootMotion(homebound, onlyIfTravels: true);
            fail += Check("homebound clip untouched", Varies(spared, "RootQ.y"), true);

            Debug.Log(fail == 0
                ? "[RootVerticalTest] PASS — height kept for Action poses, travel and rotation stripped everywhere."
                : $"[RootVerticalTest] FAIL — {fail} case(s) wrong.");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
