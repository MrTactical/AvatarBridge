#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;
using ABI.CCK.Components;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // A MeshCloth has no root bones, so the cost card counted it as nothing. Its ceiling is the
    // vertices of its source meshes, counted apart from transforms; a BoneCloth adds none.
    // Skips when MagicaCloth 2 is not in the project.
    public static class MeshClothCostTest
    {
        [MenuItem("Tools/AvatarBridge Dev/Test: MeshCloth cost")]
        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[MeshClothCostTest] {(ok ? "ok  " : "WRONG")} {label}");
                return ok ? 0 : 1;
            }

            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("MagicaCloth2.MagicaCloth")).FirstOrDefault(t => t != null);
            if (type == null)
            {
                Debug.Log("[MeshClothCostTest] SKIP: no MagicaCloth 2 in this project");
                if (Application.isBatchMode) EditorApplication.Exit(0);
                return;
            }

            var root = new GameObject("MeshClothCostTest");
            try
            {
                root.AddComponent<Animator>();
                var avatar = root.AddComponent<CVRAvatar>();
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.SetParent(root.transform, false);
                int vertices = cube.GetComponent<MeshFilter>().sharedMesh.vertexCount;

                var cloth = (MonoBehaviour) cube.AddComponent(type);
                var data = type.GetProperty("SerializeData").GetValue(cloth);
                var field = data.GetType().GetField("clothType");
                void Kind(string kind) => field.SetValue(data, Enum.Parse(field.FieldType, kind));
                Kind("MeshCloth");
                ((List<Renderer>) data.GetType().GetField("sourceRenderers").GetValue(data)).Add(cube.GetComponent<Renderer>());

                var r = AvatarWeight.Measure(avatar);
                fail += Check($"a MeshCloth counts its mesh's {vertices} vertices ({r.ClothMeshVertices})", r.ClothMeshVertices == vertices);
                fail += Check($"and no transforms ({r.ClothParticles})", r.ClothParticles == 0);

                Kind("BoneCloth");
                r = AvatarWeight.Measure(avatar);
                fail += Check($"a BoneCloth adds no vertices ({r.ClothMeshVertices})", r.ClothMeshVertices == 0);
            }
            catch (Exception e)
            {
                fail++;
                Debug.LogError("[MeshClothCostTest] threw: " + e);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
            Debug.Log(fail == 0 ? "[MeshClothCostTest] PASS" : $"[MeshClothCostTest] FAIL: {fail}");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
