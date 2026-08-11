#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Known-answer test for root movement on transplanted Action poses; the SHIPPED 3.5.35
    // design: keepPose keeps height AND rotation curves and bakes both into the pose
    // (Unity's own AnimationClipSettings flags), because ChilloutVR's client owns the root and
    // discards root motion; only the baked form is visible in game.
    //
    // This file replaces RootVerticalTest, which asserted an earlier vertical-only design,
    // was never re-run after the bake landed, and sat in the repo NOT COMPILING; the blind
    // harness covers Editor/ only, so a Tools/ test rots silently unless executed. Caught by a
    // completion-verification pass, which is the reason this header explains itself.
    //
    // Both directions still matter: outside keepPose, a clip that ends displaced or turned
    // must lose its root curves, or it walks the wearer around with no input.
    public static class RootPoseTest
    {
        [MenuItem("Tools/AvatarBridge Dev/Test — root pose kept on Action poses")]
        public static void Run()
        {
            int fail = 0;

            // Travels, descends, and turns: 1 m along X, 0.87 m down, 90° of yaw. Deliberately
            // authored UNBAKED, like the avatar that found the bug.
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
                var s = AnimationUtility.GetAnimationClipSettings(clip);
                s.loopBlendOrientation = false;
                s.loopBlendPositionY = false;
                AnimationUtility.SetAnimationClipSettings(clip, s);
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

            int Check(string label, bool ok)
            {
                Debug.Log($"[RootPoseTest] {(ok ? "ok  " : "WRONG")} {label}");
                return ok ? 0 : 1;
            }

            // 1. Default behaviour: a moving clip loses everything.
            LocomotionGrafter.ResetClones();
            var plain = LocomotionGrafter.WithoutRootMotion(Moving(), onlyIfTravels: true);
            fail += Check("default: RootT.y flattened", !Varies(plain, "RootT.y"));
            fail += Check("default: RootQ.y flattened", !Varies(plain, "RootQ.y"));

            // 2. keepPose: height and rotation survive; travel still goes.
            LocomotionGrafter.ResetClones();
            var source = Moving();
            var kept = LocomotionGrafter.WithoutRootMotion(source, onlyIfTravels: true,
                keepPose: true);
            fail += Check("keepPose: RootT.y KEPT", Varies(kept, "RootT.y"));
            fail += Check("keepPose: RootQ.y KEPT", Varies(kept, "RootQ.y"));
            fail += Check("keepPose: RootQ.w KEPT", Varies(kept, "RootQ.w"));
            fail += Check("keepPose: RootT.x still stripped", !Varies(kept, "RootT.x"));

            // 3. keepPose bakes the kept movement into the pose; the half that decides whether
            //    the game shows it at all; and does it on a CLONE, never the source.
            var ks = AnimationUtility.GetAnimationClipSettings(kept);
            fail += Check("keepPose: orientation baked into pose",
                ks.loopBlendOrientation && ks.keepOriginalOrientation);
            fail += Check("keepPose: position Y baked into pose",
                ks.loopBlendPositionY && ks.keepOriginalPositionY);
            fail += Check("keepPose: result is a clone, source untouched",
                !ReferenceEquals(kept, source)
                && !AnimationUtility.GetAnimationClipSettings(source).loopBlendOrientation);

            // 4. A clip that returns home is spared the strip entirely.
            LocomotionGrafter.ResetClones();
            var homebound = new AnimationClip { name = "Homebound" };
            AnimationUtility.SetEditorCurve(homebound,
                EditorCurveBinding.FloatCurve("", typeof(Animator), "RootQ.y"),
                new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.5f, 0.5f), new Keyframe(1f, 0f)));
            var spared = LocomotionGrafter.WithoutRootMotion(homebound, onlyIfTravels: true);
            fail += Check("homebound clip untouched", Varies(spared, "RootQ.y"));

            Debug.Log(fail == 0
                ? "[RootPoseTest] PASS — pose kept and baked for Action poses, travel stripped, sources never mutated."
                : $"[RootPoseTest] FAIL — {fail} case(s) wrong.");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
