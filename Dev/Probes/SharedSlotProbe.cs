// Two plugs on different shafts sharing one material slot, through the
// toolkit's door. A mesh built here: two shafts, each on a chain of its own,
// and a base plate, all in one submesh on one material.
//
// The second bake must move its shaft into a slot of its own rather than
// take the material over; re-bakes must not split again; and a third plug on
// the first shaft must take its slot over as before, since it is the same
// shaft baked twice.
//
// Run: -executeMethod AvatarBridge.Regression.SharedSlotProbe.Run
#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;
using ABI.CCK.Components;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AvatarBridge.Regression
{
    public static class SharedSlotProbe
    {
        const string Name = "__SharedSlotProbe";
        static int fail;

        public static void Run()
        {
            fail = 0;
            // A shader the patcher takes, whose bakes land on generated
            // materials keyed by source and renderer; and a material already on
            // a YAPS shader, which is baked in place and records no original.
            // Standard would test neither: the patcher refuses it, and each
            // bake falls back to a fresh Simple Lit copy of its own.
            // The project's Poiyomi may be patched in place already, so a plain
            // shader of the probe's own is the one guaranteed to be foreign.
            string shaderPath = YapsNativeBuilder.OutputRoot + "/__SharedSlotProbeShader/Probe.shader";
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(shaderPath));
            System.IO.File.WriteAllText(shaderPath, ProbeShader);
            AssetDatabase.ImportAsset(shaderPath, ImportAssetOptions.ForceSynchronousImport);
            foreach (var shader in new[] { "Hidden/SharedSlotProbe", ".poiyomi/Poiyomi Toon", YapsNativeBuilder.SimpleLitName })
            {
                Log($"--- {shader}");
                RunOne(shader);
            }
            AssetDatabase.DeleteAsset(System.IO.Path.GetDirectoryName(shaderPath).Replace('\\', '/'));
            Log(fail == 0 ? "PASS" : $"FAIL: {fail} check(s)");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }

        const string ProbeShader = @"Shader ""Hidden/SharedSlotProbe""
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

        static void RunOne(string shaderName)
        {
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(YapsNativeBuilder.OutputRoot + "/" + Name);
                var root = new GameObject(Name);
                root.AddComponent<CVRAvatar>();
                var shader = Shader.Find(shaderName);
                Check(shader != null, $"{shaderName} is in the project");
                if (shader == null) return;
                var skin = Build(root, shader, out var shaftA, out var shaftB);
                var original = skin.sharedMaterial;
                var sourceMesh = skin.sharedMesh;

                var a = Plug(shaftA, skin, "Plug A");
                var b = Plug(shaftB, skin, "Plug B");
                var oa = YapsNativeBuilder.Bake(a);
                Check(oa.Ok, $"plug A bakes: {oa.Message}");
                var aMaterial = PlugMaterials(skin).FirstOrDefault();
                var aBake = aMaterial != null ? aMaterial.GetTexture("_YAPS_Bake") : null;

                var ob = YapsNativeBuilder.Bake(b);
                Check(ob.Ok, $"plug B bakes: {ob.Message}");
                Log("  B's notes: " + string.Join(" | ", ob.Notes));
                var plugs = PlugMaterials(skin);
                foreach (var m in plugs)
                {
                    var t = m.GetTexture("_YAPS_Bake");
                    Log($"  {m.name}: bake {(t != null ? AssetDatabase.GetAssetPath(t) + " " + t.GetInstanceID() : "none")}");
                }
                Check(plugs.Count == 2 && plugs.All(m => m.GetTexture("_YAPS_Bake") != null)
                      && plugs[0].GetTexture("_YAPS_Bake") != plugs[1].GetTexture("_YAPS_Bake"),
                    $"two plug materials, each with a bake of its own ({plugs.Count})");
                Check(b.readoutSource == skin.sharedMaterials[1] && b.materialSlot < 0 && aBake != null
                      && skin.sharedMaterials[0] == aMaterial && aMaterial.GetTexture("_YAPS_Bake") == aBake,
                    $"B moved to slot 1 without an explicit slot ({b.materialSlot}); A keeps its material and bake in slot 0");
                var maskA = YapsSlotSplit.Mask(skin, shaftA);
                var maskB = YapsSlotSplit.Mask(skin, shaftB);
                var mesh = skin.sharedMesh;
                bool slot1OnlyB = mesh.GetTriangles(1).All(v => maskB[v]);
                bool slot0NoB = !mesh.GetTriangles(0).Any(v => maskB[v]);
                bool slot0HasA = mesh.GetTriangles(0).Any(v => maskA[v]);
                Check(slot1OnlyB && slot0NoB && slot0HasA,
                    $"slot 1 holds B's shaft alone ({slot1OnlyB}), slot 0 keeps A and the base without B ({slot0NoB}, {slot0HasA})");
                int readouts = skin.sharedMaterials.Count(YapsDebugOverlayBuilder.IsReadout);
                Check(readouts == 2, $"a readout each ({readouts})");

                int subs = skin.sharedMesh.subMeshCount;
                Check(YapsNativeBuilder.Bake(a).Ok && YapsNativeBuilder.Bake(b).Ok, "both bake again");
                Check(PlugMaterials(skin).Count == 2 && skin.sharedMesh.subMeshCount == subs
                      && b.readoutSource == skin.sharedMaterials[1] && a.readoutSource == skin.sharedMaterials[0],
                    $"a re-bake splits nothing more ({PlugMaterials(skin).Count} plug materials, {skin.sharedMesh.subMeshCount} submeshes for {subs})");

                var c = Plug(shaftA, skin, "Plug C");
                var oc = YapsNativeBuilder.Bake(c);
                Check(oc.Ok && PlugMaterials(skin).Count == 2 && c.materialSlot < 0,
                    $"a third plug on A's shaft takes A's slot over, no split ({PlugMaterials(skin).Count} plug materials, slot {c.materialSlot})");

                // Removing one plug leaves the others on the mesh bending.
                var avatar = root.GetComponent<CVRAvatar>();
                int toggles = Toggles(avatar);
                Log("  " + YapsRemover.RemovePlug(b));
                int plain = skin.sharedMaterials.Count(m => !YapsDebugOverlayBuilder.IsReadout(m));
                // The remaining readout is built again, over the mesh underneath.
                bool unsplit = a.readoutReplaced == sourceMesh && c.readoutReplaced == sourceMesh
                               && skin.sharedMesh.subMeshCount == skin.sharedMaterials.Length;
                Check(unsplit && plain == 1 && Baked(skin.sharedMaterials[0]) && Toggles(avatar) == toggles && toggles > 0,
                    $"removing B puts the unsplit mesh back under the readouts ({unsplit}, {plain} slot) and leaves A's bake and the mesh's toggle ({Toggles(avatar)} of {toggles})");
                Log("  " + YapsRemover.RemovePlug(c));
                Check(Baked(skin.sharedMaterials[0]), "removing C leaves slot 0 baked for A, whose slot it took over");
                Log("  " + YapsRemover.RemovePlug(a));
                Check(skin.sharedMaterials.All(m => m == original) && skin.sharedMesh == sourceMesh && Toggles(avatar) == 0,
                    $"removing the last plug puts the mesh and its material back and takes the toggle ({Toggles(avatar)})");
                UnityEngine.Object.DestroyImmediate(root);
                AssetDatabase.DeleteAsset(YapsNativeBuilder.OutputRoot + "/" + Name);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                fail++;
            }
        }

        static bool Baked(Material m) => m != null && m.HasProperty("_YAPS_Bake") && m.GetTexture("_YAPS_Bake") != null;

        static int Toggles(CVRAvatar avatar) => avatar.avatarSettings?.settings?.Count(e => e != null && e.toggleSettings != null
            && e.toggleSettings.useAnimationClip && e.toggleSettings.animationClip != null
            && AnimationUtility.GetCurveBindings(e.toggleSettings.animationClip).Any(x => x.propertyName.EndsWith("_YAPS_Enabled"))) ?? 0;

        static List<Material> PlugMaterials(Renderer r) =>
            r.sharedMaterials.Where(m => m != null && m.HasProperty("_YAPS_Bake") && !YapsDebugOverlayBuilder.IsReadout(m)).ToList();

        static YapsPlug Plug(Transform shaft, SkinnedMeshRenderer skin, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(shaft, false);
            var plug = go.AddComponent<YapsPlug>();
            plug.renderer = skin;
            plug.rootBone = shaft;
            return plug;
        }

        // Two shafts along +Z, 5 cm either side of centre, 15 cm long, four
        // bones each; a plate on the base bone joining them.
        static SkinnedMeshRenderer Build(GameObject root, Shader shader, out Transform shaftA, out Transform shaftB)
        {
            var armature = new GameObject("Armature").transform;
            armature.SetParent(root.transform, false);
            var bones = new List<Transform>();
            var basis = new GameObject("Base").transform;
            basis.SetParent(armature, false);
            bones.Add(basis);
            var chains = new List<List<int>>();
            foreach (float x in new[] { -0.05f, 0.05f })
            {
                var chain = new List<int>();
                Transform parent = basis;
                for (int k = 0; k < 4; k++)
                {
                    var bone = new GameObject((x < 0 ? "A" : "B") + k).transform;
                    bone.SetParent(parent, false);
                    bone.localPosition = k == 0 ? new Vector3(x, 0f, 0f) : new Vector3(0f, 0f, 0.05f);
                    chain.Add(bones.Count);
                    bones.Add(bone);
                    parent = bone;
                }
                chains.Add(chain);
            }
            shaftA = bones[chains[0][0]];
            shaftB = bones[chains[1][0]];

            var vertices = new List<Vector3>();
            var weights = new List<BoneWeight>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            const int rings = 16, segments = 12;
            for (int s = 0; s < 2; s++)
            {
                float x = s == 0 ? -0.05f : 0.05f;
                int start = vertices.Count;
                for (int ring = 0; ring < rings; ring++)
                {
                    float z = 0.15f * ring / (rings - 1);
                    int bone = chains[s][Mathf.Min(3, (int) (z / 0.05f))];
                    for (int seg = 0; seg < segments; seg++)
                    {
                        float angle = Mathf.PI * 2f * seg / segments;
                        vertices.Add(new Vector3(x + Mathf.Cos(angle) * 0.01f, Mathf.Sin(angle) * 0.01f, z));
                        weights.Add(new BoneWeight { boneIndex0 = bone, weight0 = 1f });
                        uvs.Add(new Vector2((float) seg / segments, (float) ring / rings));
                    }
                }
                for (int ring = 0; ring < rings - 1; ring++)
                for (int seg = 0; seg < segments; seg++)
                {
                    int i0 = start + ring * segments + seg, i1 = start + ring * segments + (seg + 1) % segments;
                    int i2 = i0 + segments, i3 = i1 + segments;
                    triangles.AddRange(new[] { i0, i2, i1, i1, i2, i3 });
                }
            }
            int p = vertices.Count;
            foreach (var corner in new[] { new Vector3(-0.1f, -0.03f, -0.01f), new Vector3(0.1f, -0.03f, -0.01f),
                         new Vector3(-0.1f, 0.03f, -0.01f), new Vector3(0.1f, 0.03f, -0.01f) })
            {
                vertices.Add(corner);
                weights.Add(new BoneWeight { boneIndex0 = 0, weight0 = 1f });
                uvs.Add(Vector2.zero);
            }
            triangles.AddRange(new[] { p, p + 2, p + 1, p + 1, p + 2, p + 3 });

            var go = new GameObject("Mesh");
            go.transform.SetParent(root.transform, false);
            var skin = go.AddComponent<SkinnedMeshRenderer>();
            var mesh = new Mesh { name = "Shared slot probe" };
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
            var material = new Material(shader) { name = "Shared slot probe" };
            AssetDatabase.CreateFolder(YapsNativeBuilder.OutputRoot, Name);
            AssetDatabase.CreateAsset(mesh, YapsNativeBuilder.OutputRoot + "/" + Name + "/source mesh.asset");
            AssetDatabase.CreateAsset(material, YapsNativeBuilder.OutputRoot + "/" + Name + "/source material.mat");
            skin.sharedMaterial = material;
            return skin;
        }

        static void Check(bool ok, string what)
        {
            if (!ok) fail++;
            Log((ok ? "ok   " : "FAIL ") + what);
        }

        static void Log(string s) => Debug.Log("[SharedSlotProbe] " + s);
    }
}
#endif
