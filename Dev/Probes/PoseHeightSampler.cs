#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Samples a clip on the real converted avatar and reports how high the HIPS actually sit at
    // each point through it.
    //
    // Written because every indirect signal disagreed: the RootT.y curve varies by 87 cm, root
    // motion is enabled, and the clip's Y is set to Bake Into Pose; yet the tester sees no
    // descent. Curves and flags describe intent; this measures the outcome.
    public static class PoseHeightSampler
    {
        [MenuItem("Tools/AvatarBridge Dev/Inspect — pose height through a clip")]
        public static void Run()
        {
            string prefabPath = System.Environment.GetEnvironmentVariable("AVATARBRIDGE_PREFAB");
            string clipName = System.Environment.GetEnvironmentVariable("AVATARBRIDGE_CLIP");
            string controllerPath = System.Environment.GetEnvironmentVariable("AVATARBRIDGE_CONTROLLER");
            if (string.IsNullOrEmpty(prefabPath) || string.IsNullOrEmpty(clipName))
            {
                Debug.LogError("[PoseHeight] need AVATARBRIDGE_PREFAB, AVATARBRIDGE_CLIP, AVATARBRIDGE_CONTROLLER");
                if (Application.isBatchMode) EditorApplication.Exit(2);
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (prefab == null || controller == null)
            {
                Debug.LogError("[PoseHeight] could not load prefab or controller");
                if (Application.isBatchMode) EditorApplication.Exit(2);
                return;
            }

            var clip = AssetDatabase.LoadAllAssetsAtPath(controllerPath)
                .OfType<AnimationClip>().FirstOrDefault(c => c.name == clipName);
            if (clip == null)
            {
                Debug.LogError($"[PoseHeight] no clip \"{clipName}\" inside the controller. Present: "
                    + string.Join(", ", AssetDatabase.LoadAllAssetsAtPath(controllerPath)
                        .OfType<AnimationClip>().Select(c => c.name).Take(30)));
                if (Application.isBatchMode) EditorApplication.Exit(2);
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var animator = instance.GetComponent<Animator>();
            var hips = animator != null && animator.isHuman
                ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
            if (hips == null)
            {
                Debug.LogError("[PoseHeight] no humanoid hips on the instance");
                Object.DestroyImmediate(instance);
                if (Application.isBatchMode) EditorApplication.Exit(2);
                return;
            }

            Debug.Log($"[PoseHeight] clip \"{clip.name}\" length={clip.length:0.###}s  " +
                      $"rootMotion={animator.applyRootMotion}");
            for (int i = 0; i <= 10; i++)
            {
                float t = clip.length * i / 10f;
                clip.SampleAnimation(instance, t);
                Debug.Log($"[PoseHeight]   t={t:0.00}s  hipsLocalY={hips.position.y - instance.transform.position.y:0.####}  " +
                          $"rootY={instance.transform.position.y:0.####}");
            }
            Object.DestroyImmediate(instance);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
#endif
