// Two at once through the toolkit's door: a bake with the copy, a re-bake
// that does not compound, the copy turned off and on again, a swapped-in
// look, the refusal on a shared mesh, and Remove. A mesh built here: one
// shaft on four bones and a plate on the base bone, on a plain shader of
// the test's own, so nothing heavy compiles. Editor only; whether the
// appended vertices survive upload is DG1, in game.
//
// Run: -executeMethod AvatarBridge.Regression.DuplicateRoundTripTest.Run
#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using System;
using System.Collections.Generic;
using System.Linq;
using ABI.CCK.Components;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AvatarBridge.Regression
{
    public static class DuplicateRoundTripTest
    {
        const string Tag = "[DuplicateRoundTripTest] ";
        const string Name = "__DuplicateRoundTripTest";
        const int Rings = 16, Segments = 12;
        static string Folder => YapsNativeBuilder.OutputRoot + "/" + Name;
        static int failures;

        [MenuItem("Tools/AvatarBridge Dev/Test: two at once round trip")]
        public static void Run()
        {
            failures = 0;
            string shaderPath = Folder + "Shader/Probe.shader";
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(shaderPath));
            System.IO.File.WriteAllText(shaderPath, ProbeShader);
            AssetDatabase.ImportAsset(shaderPath, ImportAssetOptions.ForceSynchronousImport);
            try { RunOne(); }
            catch (Exception e) { Debug.LogException(e); failures++; }
            AssetDatabase.DeleteAsset(Folder + "Shader");
            AssetDatabase.DeleteAsset(Folder);
            Debug.Log(failures == 0 ? Tag + "PASS" : $"{Tag}FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void RunOne()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(Folder);
            AssetDatabase.CreateFolder(YapsNativeBuilder.OutputRoot, Name);
            var root = new GameObject(Name);
            root.AddComponent<CVRAvatar>();
            var shader = Shader.Find("Hidden/DuplicateRoundTripTest");
            Check(shader != null, "the test's shader imported");
            if (shader == null) return;
            var skin = Build(root, shader, out var shaft);
            var source = skin.sharedMesh;
            var original = skin.sharedMaterial;
            int n = source.vertexCount, shaftVertices = Rings * Segments;

            // A look a toggle swaps into the slot, so the variant path runs.
            var variant = new Material(shader) { name = "Swapped look" };
            AssetDatabase.CreateAsset(variant, Folder + "/swapped look.mat");
            var clip = new AnimationClip { name = "Swap" };
            AnimationUtility.SetObjectReferenceCurve(clip,
                EditorCurveBinding.PPtrCurve("Mesh", typeof(SkinnedMeshRenderer), "m_Materials.Array.data[0]"),
                new[] { new ObjectReferenceKeyframe { time = 0, value = variant } });
            AssetDatabase.CreateAsset(clip, Folder + "/swap.anim");
            var controller = AnimatorController.CreateAnimatorControllerAtPath(Folder + "/test.controller");
            controller.layers[0].stateMachine.AddState("Swap").motion = clip;
            root.AddComponent<Animator>().runtimeAnimatorController = controller;

            var plug = shaft.gameObject.AddComponent<YapsPlug>();
            plug.renderer = skin;
            plug.rootBone = shaft;
            plug.twoAtOnce = true;

            // On.
            var o = YapsNativeBuilder.Bake(plug);
            Check(o.Ok, $"bakes with the copy: {o.Message}");
            Log("notes: " + string.Join(" | ", o.Notes));
            var dup = Base(plug, skin);
            var m = skin.sharedMaterials[0];
            int total = dup != null ? dup.vertexCount : -1;
            Check(YapsDuplicate.Is(dup) && plug.dupFrom == source && total == n + shaftVertices,
                $"the mesh under the readout is the duplicate of the source, {total} vertices for {n} + {shaftVertices} " +
                $"(\"{dup?.name}\", dupFrom {(plug.dupFrom == source ? "the source" : plug.dupFrom?.name ?? "none")})");
            Check(o.Notes.Any(x => x.StartsWith("Two at once: a second copy", StringComparison.Ordinal)), "the bake says it made the copy");
            var copies = dup != null ? dup.vertices.Skip(n).ToArray() : new Vector3[0];
            Check(copies.Length > 0 && copies.All(v => v == copies[0]), $"every copy on one point ({copies.Distinct().Count()} distinct)");
            var bake = m.GetTexture("_YAPS_Bake") as Texture2D;
            int wantRows = Mathf.CeilToInt((1 + total * 10f) / 8192);
            Check(Copy(m) == n && m.GetFloat("_YAPS_VertexCount") == total && bake != null && bake.height >= wantRows,
                $"the material: copy start {Copy(m)} (want {n}), vertex count {m.GetFloat("_YAPS_VertexCount")} (want {total}), " +
                $"bake {bake?.width}x{bake?.height} (at least {wantRows} rows)");
            string plainName = PlainName(m), copyName = YapsShaderPatcher.CurrentNameFor(m);
            Check(m.shader.name == copyName && copyName != plainName && !YapsShaderPatcher.IsStale(m),
                $"the copy's own shader: \"{m.shader.name}\", current \"{copyName}\", plain \"{plainName}\"");
            var swapped = plug.variants.FirstOrDefault(v => v != null && v.slot == 0)?.material;
            Check(swapped != null && Copy(swapped) == n && swapped.shader.name == YapsShaderPatcher.CurrentNameFor(swapped)
                  && swapped.shader.name != PlainName(swapped),
                $"the swapped-in look carries the copy and its shader ({swapped?.name}: start {Copy(swapped)}, \"{swapped?.shader.name}\")");
            Check(plug.readoutReplaced == dup && skin.sharedMesh != dup && skin.sharedMesh.vertexCount > total,
                $"the readout sits on the duplicate ({skin.sharedMesh.vertexCount} vertices over {total})");
            string dupPath = AssetDatabase.GetAssetPath(dup);

            // Again: built from dupFrom, never on itself.
            o = YapsNativeBuilder.Bake(plug);
            var again = Base(plug, skin);
            Check(o.Ok && again != null && again.vertexCount == total && plug.dupFrom == source
                  && AssetDatabase.GetAssetPath(again) == dupPath && Copy(skin.sharedMaterials[0]) == n,
                $"a re-bake does not compound: {again?.vertexCount} vertices, \"{AssetDatabase.GetAssetPath(again)}\", start {Copy(skin.sharedMaterials[0])}");

            // Off.
            plug.twoAtOnce = false;
            o = YapsNativeBuilder.Bake(plug);
            m = skin.sharedMaterials[0];
            swapped = plug.variants.FirstOrDefault(v => v != null && v.slot == 0)?.material;
            Check(o.Ok && Base(plug, skin) == source && plug.dupFrom == null && Copy(m) == 0 && m.GetFloat("_YAPS_VertexCount") == n
                  && m.shader.name == plainName && !YapsShaderPatcher.IsStale(m),
                $"ticked off: back on the source ({Base(plug, skin)?.name}), start {Copy(m)}, vertex count {m.GetFloat("_YAPS_VertexCount")}, \"{m.shader.name}\"");
            Check(swapped != null && Copy(swapped) == 0 && !YapsShaderPatcher.IsStale(swapped),
                $"and the swapped-in look back on the plain shader (\"{swapped?.shader.name}\")");

            // Refused while another plug claims the mesh.
            plug.twoAtOnce = true;
            var otherGo = new GameObject("Other plug");
            otherGo.transform.SetParent(root.transform, false);
            otherGo.AddComponent<YapsPlug>().renderer = skin;
            o = YapsNativeBuilder.Bake(plug);
            Check(o.Ok && Base(plug, skin) == source && Copy(skin.sharedMaterials[0]) == 0
                  && o.Notes.Any(x => x.Contains("another plug shares its mesh")),
                $"refused on a shared mesh, and says so: {o.Notes.FirstOrDefault(x => x.StartsWith("Two at once", StringComparison.Ordinal))}");
            Object.DestroyImmediate(otherGo);

            // On again, then Remove.
            o = YapsNativeBuilder.Bake(plug);
            Check(o.Ok && YapsDuplicate.Is(Base(plug, skin)), "on again");
            Log(YapsRemover.RemovePlug(plug));
            Check(skin.sharedMesh == source && skin.sharedMaterials.Length == 1 && skin.sharedMaterials[0] == original,
                $"Remove puts the source mesh and material back ({skin.sharedMesh.name}, {skin.sharedMaterials.Length} slots, {skin.sharedMaterials[0]?.name})");
            Object.DestroyImmediate(root);
        }

        // The mesh under any readout.
        static Mesh Base(YapsPlug plug, SkinnedMeshRenderer skin) =>
            plug.readoutRenderer == skin && plug.readoutReplaced != null ? plug.readoutReplaced : skin.sharedMesh;

        static float Copy(Material m) => m != null && m.HasProperty("_YAPS_CopyStart") ? m.GetFloat("_YAPS_CopyStart") : -1;

        static string PlainName(Material m)
        {
            var plain = new Material(m);
            plain.SetFloat("_YAPS_CopyStart", 0);
            try { return YapsShaderPatcher.CurrentNameFor(plain); }
            finally { Object.DestroyImmediate(plain); }
        }

        const string ProbeShader = @"Shader ""Hidden/DuplicateRoundTripTest""
{
    Properties { _Color (""Color"", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { ""RenderType""=""Opaque"" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""UnityCG.cginc""
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; };
            float4 _Color;
            v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); return o; }
            fixed4 frag (v2f i) : SV_Target { return _Color; }
            ENDCG
        }
    }
}
";

        // A shaft along +Z, 15 cm long on four bones, and a plate on the base bone.
        static SkinnedMeshRenderer Build(GameObject root, Shader shader, out Transform shaft)
        {
            var armature = new GameObject("Armature").transform;
            armature.SetParent(root.transform, false);
            var basis = new GameObject("Base").transform;
            basis.SetParent(armature, false);
            var bones = new List<Transform> { basis };
            Transform parent = basis;
            for (int k = 0; k < 4; k++)
            {
                var bone = new GameObject("Shaft" + k).transform;
                bone.SetParent(parent, false);
                bone.localPosition = k == 0 ? Vector3.zero : new Vector3(0f, 0f, 0.05f);
                bones.Add(bone);
                parent = bone;
            }
            shaft = bones[1];

            var vertices = new List<Vector3>();
            var weights = new List<BoneWeight>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            for (int ring = 0; ring < Rings; ring++)
            {
                float z = 0.15f * ring / (Rings - 1);
                int bone = 1 + Mathf.Min(3, (int) (z / 0.05f));
                for (int seg = 0; seg < Segments; seg++)
                {
                    float angle = Mathf.PI * 2f * seg / Segments;
                    vertices.Add(new Vector3(Mathf.Cos(angle) * 0.01f, Mathf.Sin(angle) * 0.01f, z));
                    weights.Add(new BoneWeight { boneIndex0 = bone, weight0 = 1f });
                    uvs.Add(new Vector2((float) seg / Segments, (float) ring / Rings));
                }
            }
            for (int ring = 0; ring < Rings - 1; ring++)
            for (int seg = 0; seg < Segments; seg++)
            {
                int i0 = ring * Segments + seg, i1 = ring * Segments + (seg + 1) % Segments;
                int i2 = i0 + Segments, i3 = i1 + Segments;
                triangles.AddRange(new[] { i0, i2, i1, i1, i2, i3 });
            }
            int p = vertices.Count;
            foreach (var corner in new[] { new Vector3(-0.05f, -0.03f, -0.01f), new Vector3(0.05f, -0.03f, -0.01f),
                         new Vector3(-0.05f, 0.03f, -0.01f), new Vector3(0.05f, 0.03f, -0.01f) })
            {
                vertices.Add(corner);
                weights.Add(new BoneWeight { boneIndex0 = 0, weight0 = 1f });
                uvs.Add(Vector2.zero);
            }
            triangles.AddRange(new[] { p, p + 2, p + 1, p + 1, p + 2, p + 3 });

            var go = new GameObject("Mesh");
            go.transform.SetParent(root.transform, false);
            var skin = go.AddComponent<SkinnedMeshRenderer>();
            var mesh = new Mesh { name = "Two at once test" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.boneWeights = weights.ToArray();
            mesh.bindposes = bones.Select(b => b.worldToLocalMatrix * go.transform.localToWorldMatrix).ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            skin.sharedMesh = mesh;
            skin.bones = bones.ToArray();
            skin.rootBone = basis;
            var material = new Material(shader) { name = "Two at once test" };
            AssetDatabase.CreateAsset(mesh, Folder + "/source mesh.asset");
            AssetDatabase.CreateAsset(material, Folder + "/source material.mat");
            skin.sharedMaterial = material;
            return skin;
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Log((ok ? "PASS " : "FAIL ") + what);
        }

        static void Log(string s) => Debug.Log(Tag + s);
    }
}
#endif
