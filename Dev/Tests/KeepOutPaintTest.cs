#if CVR_CCK_EXISTS && AVATARBRIDGE_MAGICA
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    using MagicaCloth2;

    // Keeping a bone out of a cloth must leave the rest of a hand-painted selection as painted,
    // and Undo must bring the old selection back.
    public static class KeepOutPaintTest
    {
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Keep out keeps the painted selection")]
        public static void Run()
        {
            failures = 0;
            var holder = new GameObject("KeepOutPaintTest");
            try
            {
                Transform Bone(string n, Transform parent)
                {
                    var t = new GameObject(n).transform;
                    t.SetParent(parent, false);
                    t.localPosition = new Vector3(0f, 0.1f, 0f);
                    return t;
                }
                var root = Bone("Root", holder.transform);
                var a = Bone("A", root);
                var b = Bone("B", a);
                var c = Bone("C", b);
                var cloth = holder.AddComponent<MagicaCloth>();
                cloth.SerializeData.clothType = ClothProcess.ClothType.BoneCloth;
                cloth.SerializeData.rootBones.Add(root);
                var toCloth = cloth.ClothTransform.worldToLocalMatrix;
                Unity.Mathematics.float3 At(Transform t) => toCloth.MultiplyPoint3x4(t.position);
                // A held still by hand, though it is not a root.
                cloth.GetSerializeData2().selectionData = new SelectionData
                {
                    positions = new[] { At(root), At(a), At(b), At(c) },
                    attributes = new[] { VertexAttribute.Fixed, VertexAttribute.Fixed, VertexAttribute.Move, VertexAttribute.Move },
                    maxConnectionDistance = 0.1f,
                    userEdit = true,
                };
                Undo.IncrementCurrentGroup();

                MagicaClothKeepOut.KeepOut(cloth, new[] { c });
                VertexAttribute Of(Transform t)
                {
                    var sel = cloth.GetSerializeData2().selectionData;
                    int i = System.Array.FindIndex(sel.positions, p => ((Vector3) (p - At(t))).sqrMagnitude < 1e-6f);
                    return i < 0 ? VertexAttribute.Invalid : sel.attributes[i];
                }
                Check(Of(c).IsInvalid(), "the kept-out bone is invalid");
                Check(Of(a).IsFixed(), "a bone painted fixed stays fixed");
                Check(Of(b).IsMove(), "a moving bone still moves");

                Undo.PerformUndo();
                Check(Of(c).IsMove() && Of(a).IsFixed(), "Undo brings the old selection back");
            }
            finally
            {
                Object.DestroyImmediate(holder);
            }
            Debug.Log(failures == 0 ? "[KeepOutPaintTest] PASS" : $"[KeepOutPaintTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[KeepOutPaintTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
