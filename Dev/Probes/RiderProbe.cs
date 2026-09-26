// A plug rooted at the armature, with a second mesh riding it: a body and a
// collar on bones under one armature, the plug on the armature itself.
//
// The collar must come out patched and switched on, and the window must list
// it as part of the plug. Then the state a hand revert leaves: the collar's
// copy still in its slot but back on its own shader, switched off, and no
// record of it on the plug. A rebuild must patch that copy again and switch it
// on, as it does for the plug's own mesh.
//
// Run: -executeMethod AvatarBridge.Regression.RiderProbe.Run
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
    public static class RiderProbe
    {
        const string Name = "__RiderProbe";
        // Outside the generated folder, so the materials read as originals.
        const string Sources = "Assets/__RiderProbeSources";
        static int fail;

        public static void Run()
        {
            fail = 0;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(YapsNativeBuilder.OutputRoot + "/" + Name);
                AssetDatabase.DeleteAsset(Sources);
                AssetDatabase.CreateFolder("Assets", Sources.Substring("Assets/".Length));
                // A foreign shader the patcher takes: it needs SV_VertexID in the
                // vertex input. Refused, the collar keeps its own shader and
                // never rides at all, which tests nothing here.
                string shaderPath = Sources + "/Probe.shader";
                System.IO.File.WriteAllText(shaderPath, ProbeShader);
                AssetDatabase.ImportAsset(shaderPath, ImportAssetOptions.ForceSynchronousImport);
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
                Check(shader != null, "the probe shader imports");
                if (shader == null) return;

                var root = new GameObject(Name);
                root.AddComponent<CVRAvatar>();
                var armature = new GameObject("Armature").transform;
                armature.SetParent(root.transform, false);
                var hips = Bone("Hips", armature, Vector3.zero);
                var spine = Bone("Spine", hips, new Vector3(0f, 0.2f, 0f));
                var chest = Bone("Chest", spine, new Vector3(0f, 0.2f, 0f));
                var collarBone = Bone("Collar", chest, new Vector3(0f, 0.15f, 0f));
                var tag = Bone("Tag", collarBone, new Vector3(0f, -0.03f, 0.05f));
                var body = Tube(root, "Body", new[] { hips, spine, chest }, 0.08f, 0f, 0.55f, shader);
                var collar = Tube(root, "Collar", new[] { collarBone, tag }, 0.09f, 0.53f, 0.58f, shader);
                var bodyOriginal = body.sharedMaterial;
                var collarOriginal = collar.sharedMaterial;

                var plug = armature.gameObject.AddComponent<YapsPlug>();
                plug.renderer = body;
                plug.rootBone = armature;

                var o = YapsNativeBuilder.Bake(plug);
                Check(o.Ok, $"the plug bakes: {o.Message}");
                Log("  notes: " + string.Join(" | ", o.Notes));
                var copy = collar.sharedMaterial;
                Check(copy != collarOriginal && Baked(copy) && On(copy),
                    $"the collar rides the plug, patched and switched on ({Describe(copy)})");
                Check(Carried(root, collar, plug), "the window lists the collar as part of the plug");

                // Back on its own shader, switched off, with no record of it.
                plug.bakedSlots.RemoveAll(b => b != null && b.renderer == collar);
                copy.shader = shader;
                copy.SetFloat("_YAPS_Enabled", 0f);
                Check(!Baked(copy) && !Carried(root, collar, plug),
                    $"set up: the copy is on its own shader and the window no longer lists it ({Describe(copy)})");

                o = YapsNativeBuilder.Bake(plug);
                Check(o.Ok, $"the plug bakes again: {o.Message}");
                Check(collar.sharedMaterial == copy && Baked(copy) && On(copy),
                    $"a rebuild patches the copy again and switches it on ({Describe(collar.sharedMaterial)})");
                Check(Carried(root, collar, plug), "the window lists the collar again");
                Check(Baked(body.sharedMaterial) && On(body.sharedMaterial), "the body is still baked and switched on");

                UnityEngine.Object.DestroyImmediate(root);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                fail++;
            }
            AssetDatabase.DeleteAsset(YapsNativeBuilder.OutputRoot + "/" + Name);
            AssetDatabase.DeleteAsset(Sources);
            Log(fail == 0 ? "PASS" : $"FAIL: {fail} check(s)");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }

        const string ProbeShader = @"Shader ""Hidden/RiderProbe""
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
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; uint vid : SV_VertexID; };
            struct v2f { float4 pos : SV_POSITION; };
            float4 _Color;
            v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); return o; }
            fixed4 frag (v2f i) : SV_Target { return _Color; }
            ENDCG
        }
    }
}
";

        static bool Baked(Material m) => m != null && m.HasProperty("_YAPS_Bake") && m.GetTexture("_YAPS_Bake") != null;

        static bool On(Material m) => m != null && m.HasProperty("_YAPS_Enabled") && m.GetFloat("_YAPS_Enabled") > 0.5f;

        static string Describe(Material m) => m == null ? "none"
            : $"{m.name} on {m.shader?.name}, enabled {(m.HasProperty("_YAPS_Enabled") ? m.GetFloat("_YAPS_Enabled").ToString() : "n/a")}";

        static bool Carried(GameObject root, Renderer mesh, YapsPlug plug) =>
            YapsScanner.Scan(root).Plugs.Any(f => f.Renderer == mesh && f.CarriedBy == plug);

        static Transform Bone(string name, Transform parent, Vector3 local)
        {
            var bone = new GameObject(name).transform;
            bone.SetParent(parent, false);
            bone.localPosition = local;
            return bone;
        }

        // A tube up +Y from y0 to y1, each ring weighted to the bone nearest it.
        static SkinnedMeshRenderer Tube(GameObject root, string name, Transform[] bones, float radius,
            float y0, float y1, Shader shader)
        {
            var vertices = new List<Vector3>();
            var weights = new List<BoneWeight>();
            var triangles = new List<int>();
            const int rings = 12, segments = 10;
            for (int ring = 0; ring < rings; ring++)
            {
                float y = Mathf.Lerp(y0, y1, ring / (rings - 1f));
                int bone = 0;
                for (int b = 1; b < bones.Length; b++)
                    if (Mathf.Abs(bones[b].position.y - y) < Mathf.Abs(bones[bone].position.y - y)) bone = b;
                for (int seg = 0; seg < segments; seg++)
                {
                    float angle = Mathf.PI * 2f * seg / segments;
                    vertices.Add(new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius));
                    weights.Add(new BoneWeight { boneIndex0 = bone, weight0 = 1f });
                }
            }
            for (int ring = 0; ring < rings - 1; ring++)
            for (int seg = 0; seg < segments; seg++)
            {
                int i0 = ring * segments + seg, i1 = ring * segments + (seg + 1) % segments;
                triangles.AddRange(new[] { i0, i0 + segments, i1, i1, i0 + segments, i1 + segments });
            }
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            var skin = go.AddComponent<SkinnedMeshRenderer>();
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.boneWeights = weights.ToArray();
            mesh.bindposes = bones.Select(b => b.worldToLocalMatrix * go.transform.localToWorldMatrix).ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            skin.sharedMesh = mesh;
            skin.bones = bones;
            skin.rootBone = bones[0];
            var material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(mesh, Sources + "/" + name + " mesh.asset");
            AssetDatabase.CreateAsset(material, Sources + "/" + name + ".mat");
            skin.sharedMaterial = material;
            return skin;
        }

        static void Check(bool ok, string what)
        {
            if (!ok) fail++;
            Log((ok ? "ok   " : "FAIL ") + what);
        }

        static void Log(string s) => Debug.Log("[RiderProbe] " + s);
    }
}
#endif
