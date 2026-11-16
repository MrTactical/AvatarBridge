#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS && AVATARBRIDGE_MAGICA
using System.Linq;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Here, not at the top: MagicaCloth 1's global namespace MagicaCloth beats a file-level using.
    using MagicaCloth2;

    // Taking the jelly off puts each pressing collider back into the cloth it came out of, and
    // no other cloth of the pair. Put back into every cloth, a collider one side never had
    // started pushing that side's bones.
    public static class JellyUnwornCollidersTest
    {
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Jelly off puts colliders back where they were")]
        public static void Run()
        {
            failures = 0;
            var root = new GameObject("JellyUnwornAvatar");
            try
            {
                root.AddComponent<Animator>();
                MagicaCloth Cloth(string bone)
                {
                    var t = new GameObject(bone).transform;
                    t.SetParent(root.transform, false);
                    var holder = new GameObject(bone + " cloth");
                    holder.transform.SetParent(root.transform, false);
                    var cloth = holder.AddComponent<MagicaCloth>();
                    cloth.SerializeData.rootBones.Add(t);
                    return cloth;
                }
                var left = Cloth("Thighs_L");
                var right = Cloth("Thighs_R");
                var collider = new GameObject("Knee").AddComponent<MagicaSphereCollider>();
                collider.transform.SetParent(root.transform, false);
                // As the jelly leaves it: out of the left cloth's list, which alone had it, its ends
                // under it, the a end marking where it came from.
                var a = new GameObject("Jelly collider 0 a Thighs").transform;
                a.SetParent(collider.transform, false);
                new GameObject("Jelly collider 0 b Thighs").transform.SetParent(collider.transform, false);
                new GameObject("Jelly listed in MagicaCloth 0 " + AnimationUtility.CalculateTransformPath(left.transform, root.transform))
                    .transform.SetParent(a, false);

                YapsJelly.Unworn(root.transform, "Thighs");
                Check(left.SerializeData.colliderCollisionConstraint.colliderList.Contains(collider), "back in the cloth it came out of");
                Check(!right.SerializeData.colliderCollisionConstraint.colliderList.Contains(collider), "not added to the other side's cloth");
                Check(collider.transform.childCount == 0, "its ends and marks go");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
            Debug.Log(failures == 0 ? "[JellyUnwornCollidersTest] PASS" : $"[JellyUnwornCollidersTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[JellyUnwornCollidersTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
