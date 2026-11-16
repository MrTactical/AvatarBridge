#if CVR_CCK_EXISTS
using System.Reflection;
using ABI.CCK.Components;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Strip takes the dead renderer it found, not the first object at its path. Beside a visible
    // sibling of the same name it took the visible one's renderer and left the dead one.
    public static class StripSameNameTest
    {
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Strip takes the dead one of two same-named")]
        public static void Run()
        {
            failures = 0;
            var root = new GameObject("StripSameNameAvatar");
            try
            {
                root.AddComponent<Animator>();
                var avatar = root.AddComponent<CVRAvatar>();
                var shown = GameObject.CreatePrimitive(PrimitiveType.Cube);
                shown.name = "Mesh";
                shown.transform.SetParent(root.transform, false);
                var dead = GameObject.CreatePrimitive(PrimitiveType.Cube);
                dead.name = "Mesh";
                dead.transform.SetParent(root.transform, false);
                dead.SetActive(false);

                var plan = new AvatarSlimmer.Plan();
                plan.Strip.Add(dead.GetComponent<Renderer>());
                typeof(AvatarSlimmer).GetMethod("StripDead", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, new object[] { avatar, plan, new BridgeReport() });

                Check(shown.GetComponent<Renderer>() != null, "the visible one keeps its renderer");
                Check(dead.GetComponent<Renderer>() == null, "the dead one loses its renderer");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
            Debug.Log(failures == 0 ? "[StripSameNameTest] PASS" : $"[StripSameNameTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[StripSameNameTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
