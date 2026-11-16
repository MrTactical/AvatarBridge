// A plug on Poiyomi's built-in DPS penetrator, locked, upgraded the way the YAPS window's Build
// and a reconvert both do it: adopted, baked, and kept on its own shader with DPS's frozen bend
// turned off in the copy. It used to move to YAPS Simple Lit and lose the body's look, and the
// converter never upgraded it at all, so each reconvert threw the window's work away.
//
// Run: -executeMethod AvatarBridge.Regression.LegacyUpgradeTest.Run
#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ABI.CCK.Components;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AvatarBridge.Regression
{
    public static class LegacyUpgradeTest
    {
        const string Tag = "[LegacyUpgradeTest] ";
        const string Name = "__LegacyUpgradeTest";
        static string Folder => YapsNativeBuilder.OutputRoot + "/" + Name;
        // The plug's own assets, outside the generated folder: the builder takes anything
        // in there for one of its copies and re-patches it in place.
        const string Sources = "Assets/" + Name;
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Legacy plug upgrade")]
        public static void Run()
        {
            failures = 0;
            var generated = GeneratedFiles.Snapshot();
            string dir = Sources;
            Directory.CreateDirectory(dir);
            File.WriteAllText(dir + "/LegacyUpgradeTest.shader", Source);
            File.WriteAllText(dir + "/LegacyUpgradeTestPen.cginc", Include);
            AssetDatabase.ImportAsset(dir + "/LegacyUpgradeTestPen.cginc", ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(dir + "/LegacyUpgradeTest.shader", ImportAssetOptions.ForceSynchronousImport);
            try { RunOne(); }
            catch (Exception e) { Debug.LogException(e); failures++; }
            finally
            {
                AssetDatabase.DeleteAsset(dir);
                AssetDatabase.DeleteAsset(Folder);
                GeneratedFiles.DeleteNew(generated);
            }
            Debug.Log(failures == 0 ? Tag + "PASS" : $"{Tag}FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void RunOne()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(Folder);
            AssetDatabase.CreateFolder(YapsNativeBuilder.OutputRoot, Name);
            var shader = Shader.Find("Hidden/LegacyUpgradeTest");
            Check(shader != null, "the test's shader imported");
            if (shader == null) return;

            var root = new GameObject(Name);
            root.AddComponent<CVRAvatar>();
            var go = new GameObject("Shaft");
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = Shaft();
            var renderer = go.AddComponent<MeshRenderer>();
            var material = new Material(shader) { name = "Locked penetrator" };
            material.SetFloat("_PenetratorEnabled", 1f);
            material.SetFloat("_EntranceStiffness", 0.3f);
            material.SetFloat("_ReCurvature", 0.2f);
            AssetDatabase.CreateAsset(material, Sources + "/locked penetrator.mat");
            renderer.sharedMaterial = material;

            // The shared Build, as the window's button and the converter call it. Before it
            // existed, the window's own steps: adopt, then bake.
            var build = typeof(YapsNativeBuilder).GetMethod("BuildAvatar");
            Check(build != null, "the window and the converter share one Build");
            if (build != null)
            {
                var args = new object[build.GetParameters().Length];
                args[0] = root;
                var lines = (List<string>) build.Invoke(null, args);
                Log("build: " + args[1] + " " + string.Join(" | ", lines));
            }
            else
            {
                var found = YapsScanner.Scan(root).Plugs.FirstOrDefault();
                if (found != null) YapsNativeBuilder.AdoptPlug(found.Root, found.Renderer, found.MaterialSlot, found.Material, null);
                var adopted = root.GetComponentInChildren<YapsPlug>(true);
                if (adopted != null) Log("bake: " + YapsNativeBuilder.Bake(adopted).Message);
            }

            var plug = root.GetComponentInChildren<YapsPlug>(true);
            Check(plug != null, "the bare plug was made editable");
            var baked = renderer.sharedMaterial;
            Check(baked != null && baked.HasProperty("_YAPS_Bake"), $"the plug is baked (\"{baked?.shader.name}\")");
            if (baked == null || !baked.HasProperty("_YAPS_Bake")) return;

            string from = YapsShaderPatcher.SourceShaderOf(baked);
            Check(from == "Hidden/LegacyUpgradeTest", $"it keeps its own shader (patched from \"{from}\")");
            Check(baked.HasProperty("_PenetratorEnabled") && baked.GetFloat("_PenetratorEnabled") == 0f,
                "DPS's switch is off on the material");

            var copies = Directory.GetFiles(YapsShaderPatcher.SharedFolder, "LegacyUpgradeTestPen*.cginc");
            string text = copies.Length == 1 ? File.ReadAllText(copies[0]) : "";
            Check(copies.Length == 1 && text.Contains("(0 /*_PenetratorEnabled*/)") && !text.Contains("(1.0 /*_PenetratorEnabled*/)"),
                $"DPS's frozen bend is off in the copy ({copies.Length} copies of the include)");
            Check(File.ReadAllText(Sources + "/LegacyUpgradeTestPen.cginc").Contains("(1.0 /*_PenetratorEnabled*/)"),
                "the source include is untouched");

            // A plug that moves to Simple Lit is baked from a copy made in memory, and every
            // fresh bake of it, a reconvert's included, named a new material file beside the last.
            string first = YapsBaker.Tail(YapsNativeBuilder.OnSimpleLit(material, out _), renderer);
            string again = YapsBaker.Tail(YapsNativeBuilder.OnSimpleLit(material, out _), renderer);
            Check(first == again && first == YapsBaker.Tail(material, renderer),
                $"a Simple Lit stand-in names its bake after the material it stands in for ({first}, {again})");

            // Beside SPS. The plug above stands in for one the SPS route converted, and a second
            // left as DPS is what the upgrade must still take. It used to run only on an avatar
            // with no SPS at all, and Build rebuilds every plug and socket, SPS's own included.
            var leftover = new GameObject("Leftover shaft");
            leftover.transform.SetParent(root.transform, false);
            leftover.transform.localPosition = new Vector3(0.3f, 0f, 0f);
            leftover.AddComponent<MeshFilter>().sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
            var leftoverMaterial = new Material(shader) { name = "Leftover penetrator" };
            leftoverMaterial.SetFloat("_PenetratorEnabled", 1f);
            AssetDatabase.CreateAsset(leftoverMaterial, Sources + "/leftover penetrator.mat");
            leftover.AddComponent<MeshRenderer>().sharedMaterial = leftoverMaterial;

            // The two flags Run sets for an avatar it found SPS on; by name, so this builds
            // against a converter without them.
            var flags = BindingFlags.NonPublic | BindingFlags.Static;
            var sps = typeof(YapsConverter).GetField("SpsConverted", flags);
            Check(sps != null, "the upgrade knows when SPS was converted");
            typeof(YapsConverter).GetField("LegacyPending", flags)?.SetValue(null, true);
            sps?.SetValue(null, true);
            var ctx = new BridgeContext { Target = root, Report = new BridgeReport(), Settings = new BridgeSettings() };
            YapsConverter.UpgradeLegacy(ctx);
            var upgraded = ctx.Report.Entries.FirstOrDefault(e => e.Subject.StartsWith("Upgraded this avatar's older penetration"));
            Log("beside SPS: " + upgraded?.Detail);
            Check(leftover.GetComponent<YapsPlug>() != null, "the leftover DPS plug was made editable");
            Check(upgraded != null && upgraded.Detail.StartsWith("Built 1 of 1 plug"),
                "only the leftover plug was built, not the one already converted");
        }

        // A plain mesh along +Z, the frame a plug that is its own object bakes in.
        static Mesh Shaft()
        {
            const int rings = 8, segments = 8;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < segments; s++)
                {
                    float a = s * Mathf.PI * 2f / segments;
                    vertices.Add(new Vector3(Mathf.Cos(a) * 0.02f, Mathf.Sin(a) * 0.02f, r * 0.15f / (rings - 1)));
                }
            for (int r = 0; r < rings - 1; r++)
                for (int s = 0; s < segments; s++)
                {
                    int i = r * segments + s, j = r * segments + (s + 1) % segments;
                    triangles.AddRange(new[] { i, j, i + segments, j, j + segments, i + segments });
                }
            var mesh = new Mesh { name = "Legacy upgrade shaft" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(0, vertices.Select(v => new Vector2(v.x, v.z)).ToList());
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, Sources + "/shaft.asset");
            return mesh;
        }

        // Poiyomi 7.3 declares Raliv's orifice beside its penetrator, and its lock writes the
        // penetrator's switch into the include as a constant.
        const string Source = @"Shader ""Hidden/LegacyUpgradeTest""
{
    Properties
    {
        _Color (""Color"", Color) = (1,1,1,1)
        _PenetratorEnabled (""Penetrator"", Float) = 0
        _EntranceStiffness (""Entrance Stiffness"", Float) = 0.01
        _ReCurvature (""ReCurvature"", Float) = 0
        _Length (""Length"", Float) = 0.15
        _OrificeData (""Orifice Data"", 2D) = ""white"" {}
        _Shape1Depth (""Shape 1 Depth"", Float) = 0
    }
    SubShader
    {
        Tags { ""RenderType""=""Opaque"" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""UnityCG.cginc""
            #include ""LegacyUpgradeTestPen.cginc""
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float4 tangent : TANGENT; };
            struct v2f { float4 pos : SV_POSITION; };
            float4 _Color;
            v2f vert (appdata v) { v2f o; Penetrate(v.vertex.xyz); o.pos = UnityObjectToClipPos(v.vertex); return o; }
            fixed4 frag (v2f i) : SV_Target { return _Color; }
            ENDCG
        }
    }
}
";

        const string Include = @"float _PenetratorEnabled;
void Penetrate(inout float3 p)
{
    if((1.0 /*_PenetratorEnabled*/))
    {
        p.z *= 2;
    }
}
";

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Log((ok ? "PASS " : "FAIL ") + what);
        }

        static void Log(string s) => Debug.Log(Tag + s);
    }
}
#endif
