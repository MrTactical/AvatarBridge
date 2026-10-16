#if CVR_CCK_EXISTS
using System;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Known-answer test for which slots the squash copies. A slot whose vertices all store a
    // zero share draws the same on its own material, so it keeps it and nothing compiles for it.
    // The judgement is on the stored byte, not the float: a share too small to round to one
    // step reads as zero in the shader.
    public static class SquashSlotTest
    {
        [MenuItem("Tools/AvatarBridge Dev/Test: squash slots")]
        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[SquashSlotTest] {(ok ? "ok  " : "WRONG")} {label}");
                return ok ? 0 : 1;
            }

            // Two submeshes of three vertices each, and one vertex used by neither.
            var mesh = new Mesh { vertices = new Vector3[7] };
            mesh.subMeshCount = 2;
            mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
            mesh.SetTriangles(new[] { 3, 4, 5 }, 1);
            var shares = new Vector4[7];
            try
            {
                fail += Check("no share anywhere: nothing moves",
                    !YapsSquash.Moves(mesh, 0, shares) && !YapsSquash.Moves(mesh, 1, shares));

                shares[6] = Vector4.one;
                fail += Check("a share on a vertex no slot draws moves nothing", !YapsSquash.Moves(mesh, 0, shares));

                shares[1] = new Vector4(0f, 0f, 0.5f, 0f);
                fail += Check("a share on one vertex of the first slot moves it",
                    YapsSquash.Moves(mesh, 0, shares) && !YapsSquash.Moves(mesh, 1, shares));

                shares[4] = new Vector4(0.001f, 0f, 0f, 0f);
                fail += Check("a share that rounds to zero does not", !YapsSquash.Moves(mesh, 1, shares));

                shares[4] = new Vector4(0f, 0f, 0f, 0.003f);
                fail += Check("one that rounds to a step does", YapsSquash.Moves(mesh, 1, shares));

                shares[4] = Vector4.zero;
                fail += Check("a slot past the last submesh is judged as the last",
                    YapsSquash.Moves(mesh, 2, shares) == YapsSquash.Moves(mesh, 1, shares)
                    && !YapsSquash.Moves(mesh, 2, shares));
                shares[5] = new Vector4(0f, 1f, 0f, 0f);
                fail += Check("...and moves with it", YapsSquash.Moves(mesh, 2, shares));

                fail += Check("shares shorter than the mesh read as zero past their end",
                    !YapsSquash.Moves(mesh, 1, new Vector4[4]));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                fail++;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }

            Debug.Log(fail == 0
                ? "[SquashSlotTest] PASS: only slots that store a share are copied."
                : $"[SquashSlotTest] FAIL: {fail} case(s) wrong.");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
