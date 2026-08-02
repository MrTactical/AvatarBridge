#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge.Regression
{
    /// <summary>
    /// Probe: does flipping a clip's "Bake Into Pose" orientation flag actually move the turn out
    /// of root motion and into the bones?
    ///
    /// The theory to test — height already works this way. Tachy's clip carries
    /// LoopBlendPositionY = 1 (bake Y into pose), which is why the descent survives in game where
    /// root motion is discarded. Its orientation twin, LoopBlendOrientation, is unset, so the turn
    /// stayed root motion and died. If setting it moves the rotation into the hips, the fix is a
    /// clip-settings change rather than curve surgery.
    ///
    /// Measured, not assumed: samples the clip on the real avatar and reports the HIPS' rotation
    /// relative to the root. If the turn is in the pose, hip-vs-root angle sweeps ~90 degrees. If
    /// it is still root motion, that angle stays flat.
    /// </summary>
    public static class BakeOrientationProbe
    {
        [MenuItem("Tools/AvatarBridge Dev/Probe — bake orientation into pose")]
        public static void Run()
        {
            string prefabPath = System.Environment.GetEnvironmentVariable("AVATARBRIDGE_PREFAB");
            string controllerPath = System.Environment.GetEnvironmentVariable("AVATARBRIDGE_CONTROLLER");
            string clipName = System.Environment.GetEnvironmentVariable("AVATARBRIDGE_CLIP");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var source = AssetDatabase.LoadAllAssetsAtPath(controllerPath)
                .OfType<AnimationClip>().FirstOrDefault(c => c.name == clipName);
            if (prefab == null || source == null)
            {
                Debug.LogError($"[BakeProbe] missing prefab or clip \"{clipName}\"");
                if (Application.isBatchMode) EditorApplication.Exit(2);
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var animator = instance.GetComponent<Animator>();
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);

            void Sample(string label, AnimationClip clip)
            {
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                Debug.Log($"[BakeProbe] --- {label} · loopBlendOrientation={settings.loopBlendOrientation} " +
                          $"keepOriginalOrientation={settings.keepOriginalOrientation} " +
                          $"loopBlendPositionY={settings.loopBlendPositionY}");
                for (int i = 0; i <= 6; i++)
                {
                    float t = clip.length * i / 6f;
                    clip.SampleAnimation(instance, t);
                    // Hips rotation RELATIVE TO THE ROOT: this is the part that is pose, and the
                    // part ChilloutVR will actually show. Root's own rotation is what it discards.
                    var local = Quaternion.Inverse(instance.transform.rotation) * hips.rotation;
                    Debug.Log($"[BakeProbe]   t={t:0.00}s  hipsVsRoot={local.eulerAngles.x:0.#},{local.eulerAngles.y:0.#},{local.eulerAngles.z:0.#}  " +
                              $"rootY={instance.transform.eulerAngles.y:0.#}");
                }
            }

            Sample("AS CONVERTED", source);

            // Same clip, orientation baked into pose.
            var baked = Object.Instantiate(source);
            baked.name = source.name + " (baked)";
            var s = AnimationUtility.GetAnimationClipSettings(baked);
            s.loopBlendOrientation = true;
            s.keepOriginalOrientation = true;
            AnimationUtility.SetAnimationClipSettings(baked, s);
            Sample("ORIENTATION BAKED INTO POSE", baked);

            Object.DestroyImmediate(instance);
            Object.DestroyImmediate(baked);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
#endif
