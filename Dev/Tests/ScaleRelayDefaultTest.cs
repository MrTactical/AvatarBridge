#if CVR_CCK_EXISTS
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;

namespace AvatarBridge.Regression
{
    // ChilloutVR resizes every avatar itself, so a parent constraint's offset must be re-anchored
    // with the height slider off too, the default. Tied to the slider, a hat drifted on every
    // default conversion.
    public static class ScaleRelayDefaultTest
    {
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Props follow the size with the height slider off")]
        public static void Run()
        {
            failures = 0;
            var avatar = new GameObject("ScaleRelayAvatar");
            try
            {
                var head = new GameObject("Head").transform;
                head.SetParent(avatar.transform, false);
                head.localPosition = new Vector3(0f, 1.5f, 0f);
                var hat = new GameObject("Hat").transform;
                hat.SetParent(avatar.transform, false);
                hat.position = head.position + new Vector3(0f, 0.15f, 0.02f);
                var pc = hat.gameObject.AddComponent<ParentConstraint>();
                pc.AddSource(new ConstraintSource { sourceTransform = head, weight = 1f });
                pc.SetTranslationOffset(0, head.InverseTransformPoint(hat.position));
                pc.locked = true;
                pc.constraintActive = true;

                var settings = new BridgeSettings();
                Check(!settings.addAvatarScaler, "the height slider is off by default");
                var ctx = new BridgeContext { Target = avatar, Report = new BridgeReport(), Settings = settings };
                ConstraintScaleRelay.Run(ctx);

                bool relayed = false;
                foreach (Transform child in head)
                    relayed |= child.name.StartsWith("AvatarBridge_ScaleRelay");
                Check(relayed, "the hat's offset is carried by a relay under the head");
                Check(pc.GetTranslationOffset(0).sqrMagnitude < 1e-8f, "and the constraint's own offset is zero");
            }
            finally
            {
                Object.DestroyImmediate(avatar);
            }
            Debug.Log(failures == 0 ? "[ScaleRelayDefaultTest] PASS" : $"[ScaleRelayDefaultTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[ScaleRelayDefaultTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
