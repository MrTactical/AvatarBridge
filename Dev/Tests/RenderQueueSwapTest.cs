#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // A material's own render queue across a shader swap, at the two sites that copy a material
    // onto another shader outside YAPS: the rescue of a shader out of VRCFury's temp folder, and
    // the stereo patcher's copy. YAPS lost it once (Wear): a transparent material on a copy drew
    // in the opaque queue. A material on its shader's own queue follows the new shader instead.
    public static class RenderQueueSwapTest
    {
        const string Temp = "Packages/com.vrcfury.temp/__ABRenderQueueSwapTest";
        const string Output = "Assets/__ABRenderQueueSwapTest";

        [MenuItem("Tools/AvatarBridge Dev/Test: render queue across a shader swap")]
        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[RenderQueueSwapTest] {(ok ? "ok  " : "WRONG")} {label}");
                return ok ? 0 : 1;
            }

            Clean();
            OutputAssetPaths.Reset();
            GameObject avatar = null;
            try
            {
                Directory.CreateDirectory(Temp);
                Directory.CreateDirectory(Output);
                File.WriteAllText(Temp + "/Probe.shader",
                    "Shader \"Hidden/__ABRenderQueueSwapTest\" { SubShader { Tags { \"Queue\"=\"Geometry\" } Pass { } } }");
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                var tempShader = AssetDatabase.LoadAssetAtPath<Shader>(Temp + "/Probe.shader");

                var own = new Material(tempShader) { name = "Own queue" };
                own.renderQueue = 3000;
                AssetDatabase.CreateAsset(own, Temp + "/Own queue.mat");
                var follows = new Material(tempShader) { name = "Shader queue" };
                AssetDatabase.CreateAsset(follows, Temp + "/Shader queue.mat");
                AssetDatabase.SaveAssets();

                avatar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                avatar.name = "__ABRenderQueueSwapTest";
                var renderer = avatar.GetComponent<MeshRenderer>();
                renderer.sharedMaterials = new[] { own, follows };
                SceneAssetRehomer.Run(new BridgeContext
                {
                    Report = new BridgeReport(),
                    Settings = new BridgeSettings(),
                    Target = avatar,
                    OutputDir = Output,
                });
                var rescued = renderer.sharedMaterials;
                fail += Check("rescue: both materials and the shader come out of temp",
                    rescued.Length == 2 && rescued[0] != null && rescued[1] != null
                    && AssetDatabase.GetAssetPath(rescued[0]).StartsWith(Output)
                    && AssetDatabase.GetAssetPath(rescued[0].shader).StartsWith(Output));
                fail += Check($"rescue: an own queue stays 3000 (read {rescued[0]?.renderQueue})",
                    rescued[0] != null && rescued[0].renderQueue == 3000);
                fail += Check($"rescue: the shader's queue stays the shader's (read {rescued[1]?.renderQueue})",
                    rescued[1] != null && rescued[1].renderQueue == rescued[1].shader.renderQueue);

                var repoint = typeof(ShaderSpiPatcher).GetMethod("Repoint", BindingFlags.NonPublic | BindingFlags.Static);
                var standard = Shader.Find("Standard");
                var mine = new Material(standard) { name = "Stereo own queue" };
                mine.renderQueue = 3000;
                AssetDatabase.CreateAsset(mine, Output + "/Stereo own queue.mat");
                var plain = new Material(standard) { name = "Stereo shader queue" };
                AssetDatabase.CreateAsset(plain, Output + "/Stereo shader queue.mat");
                var transparent = Shader.Find("Unlit/Transparent");
                var clones = new Dictionary<Material, Material>();
                var a = (Material)repoint.Invoke(null, new object[] { mine, Shader.Find("Unlit/Color"), Output, clones });
                var b = (Material)repoint.Invoke(null, new object[] { plain, transparent, Output, clones });
                fail += Check($"stereo copy: an own queue stays 3000 (read {a?.renderQueue})", a != null && a.renderQueue == 3000);
                fail += Check($"stereo copy: the shader's queue follows the new shader, {transparent.renderQueue} (read {b?.renderQueue})",
                    b != null && b.renderQueue == transparent.renderQueue);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                fail++;
            }
            finally
            {
                if (avatar != null) Object.DestroyImmediate(avatar);
                Clean();
            }
            Debug.Log(fail == 0
                ? "[RenderQueueSwapTest] PASS: an own render queue survives both swaps, a shader's own follows the shader."
                : $"[RenderQueueSwapTest] FAIL: {fail} case(s) wrong.");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }

        static void Clean()
        {
            AssetDatabase.DeleteAsset(Output);
            AssetDatabase.DeleteAsset(Temp);
        }
    }
}
#endif
