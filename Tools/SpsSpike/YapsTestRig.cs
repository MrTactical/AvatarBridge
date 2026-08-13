// Phase 1d. Builds a complete, self-contained YAPS test subject: a
// procedural plug mesh, a bake texture written in our own format, the
// test material, the driver and a socket to drag.
//
// Generating the bake ourselves rather than borrowing one from a converted
// avatar is deliberate. It means the harness depends on nothing external,
// and it exercises our WRITER against our READER — if the two disagree the
// plug will visibly mangle, which is a far better test than either alone.
// The writer here is the seed of the real baker (Phase 1c).
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Spike
{
    static class YapsTestRig
    {
        const int Rings = 40;      // along the shaft
        const int Segments = 16;   // around it
        const float Radius = 0.05f;
        const float Length = 0.6f;

        // The honest test of frame recovery. The renderer sits at the
        // origin with an identity transform — exactly like a real avatar's
        // body mesh — while a bone somewhere else carries the plug. If the
        // deform follows the BONE, the frame is genuinely being recovered
        // from the vertex rather than read off the renderer.
        [MenuItem("AvatarBridge/Spike/Build YAPS SKINNED test rig (frame recovery)")]
        static void BuildSkinned()
        {
            Build(skinned: true);
        }

        [MenuItem("AvatarBridge/Spike/Build YAPS test rig (plug + socket)")]
        static void Build()
        {
            Build(skinned: false);
        }

        static void Build(bool skinned)
        {
            var mesh = BuildPlugMesh(out var positions, out var normals, out var tangents);
            var bake = BuildBakeTexture(positions, normals, tangents);

            string dir = "Assets/SpsSpike";
            if (!AssetDatabase.IsValidFolder(dir))
            {
                AssetDatabase.CreateFolder("Assets", "SpsSpike");
            }
            AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath(dir + "/YapsTestPlug.asset"));
            AssetDatabase.CreateAsset(bake, AssetDatabase.GenerateUniqueAssetPath(dir + "/YapsTestBake.asset"));

            var shader = Shader.Find("AvatarBridge/YAPS Test Plug");
            if (shader == null)
            {
                Debug.LogError("[YAPS] Test shader not found.");
                return;
            }
            var material = new Material(shader) { name = "YAPS Test Plug" };
            material.SetTexture("_YAPS_Bake", bake);
            material.SetFloat("_YAPS_VertexCount", positions.Count);
            material.SetFloat("_YAPS_Length", Length);
            material.SetFloat("_YAPS_BakeScale", 1f);
            material.SetFloat("_YAPS_Enabled", 1f);
            material.SetFloat("_YAPS_Overrun", 1f);
            AssetDatabase.CreateAsset(material, AssetDatabase.GenerateUniqueAssetPath(dir + "/YapsTestPlug.mat"));
            AssetDatabase.SaveAssets();

            var root = new GameObject(skinned ? "YAPS Skinned Test Rig" : "YAPS Test Rig");
            Undo.RegisterCreatedObjectUndo(root, "Build YAPS test rig");

            GameObject plug;
            if (skinned)
            {
                material.SetFloat("_YAPS_FrameFromVertex", 1f);

                // Renderer at the origin with an identity transform, the
                // way an avatar's body mesh sits, and a bone elsewhere
                // carrying the plug. Every vertex is bound rigidly to that
                // one bone, so the recovered frame should be exact.
                var bone = new GameObject("Plug Bone (move me)");
                bone.transform.SetParent(root.transform, false);
                bone.transform.localPosition = new Vector3(0f, 0.4f, 0f);
                bone.transform.localRotation = Quaternion.Euler(0f, 35f, 0f);

                mesh.bindposes = new[] { bone.transform.worldToLocalMatrix * root.transform.localToWorldMatrix };
                var weights = new BoneWeight[mesh.vertexCount];
                for (int i = 0; i < weights.Length; i++)
                {
                    weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
                }
                mesh.boneWeights = weights;

                plug = new GameObject("Plug (skinned)");
                plug.transform.SetParent(root.transform, false);
                var skin = plug.AddComponent<SkinnedMeshRenderer>();
                skin.sharedMesh = mesh;
                skin.bones = new[] { bone.transform };
                skin.rootBone = bone.transform;
                skin.sharedMaterial = material;
                skin.updateWhenOffscreen = true;
            }
            else
            {
                plug = new GameObject("Plug");
                plug.transform.SetParent(root.transform, false);
                plug.AddComponent<MeshFilter>().sharedMesh = mesh;
                plug.AddComponent<MeshRenderer>().sharedMaterial = material;
            }

            var socket = new GameObject("Socket (drag me)");
            socket.transform.SetParent(root.transform, false);
            // Start it just beyond the tip, slightly off-axis, so the very
            // first thing you see is a bend rather than a straight rod.
            socket.transform.localPosition = new Vector3(0.25f, 0f, 0.75f);
            socket.transform.localRotation = Quaternion.Euler(0f, -60f, 0f);

            // Protocol lights on the socket, in OUR inverted encoding —
            // root above front, so roots win the four vertex slots instead
            // of being evicted by their own fronts. Switching the driver to
            // Protocol Lights makes the shader find these on its own.
            // Digits 7 and 0 — the only two legacy DPS never claimed, and
            // root above front so roots win the four vertex slots.
            AddSocketLight(socket.transform, "Root 0.4706", 0.4706f, Vector3.zero);
            AddSocketLight(socket.transform, "Front 0.4006", 0.4006f, Vector3.forward * 0.02f);

            var driver = plug.AddComponent<YapsTestDriver>();
            driver.socket = socket.transform;
            driver.plugLength = Length;
            driver.bakeScale = 1f;
            if (skinned)
            {
                // So the gizmos draw from the bone, not from the renderer
                // sitting at the avatar root — otherwise the drawn curve
                // would disagree with the one the shader builds, and a
                // lying gizmo is worse than none.
                driver.frameSource = root.transform.Find("Plug Bone (move me)");
            }

            Selection.activeGameObject = socket;
            Debug.Log($"[YAPS] Test rig built: {positions.Count} vertices, plug {Length} m along +Z. " +
                      "Drag \"Socket (drag me)\" around and the plug should follow it. " +
                      "The mesh is shaded by world normal, so any fold or pinch is obvious.");
        }

        // Black, shadowless, vertex-only: the light carries a position and
        // a range that says what it is, never any illumination.
        static void AddSocketLight(Transform parent, string name, float range, Vector3 offset)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = offset;

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = range;
            light.color = Color.black;
            light.intensity = 1f;
            light.bounceIntensity = 0f;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForceVertex;
            light.cullingMask = ~0;
        }

        // A capped cylinder running along +Z from the origin, which is the
        // space the deform expects: Z is distance along the shaft.
        static Mesh BuildPlugMesh(out List<Vector3> positions, out List<Vector3> normals,
            out List<Vector4> tangents)
        {
            positions = new List<Vector3>();
            normals = new List<Vector3>();
            tangents = new List<Vector4>();
            var triangles = new List<int>();

            for (int ring = 0; ring <= Rings; ring++)
            {
                float t = (float) ring / Rings;
                float z = t * Length;
                // Round the last stretch off into a dome so the tip reads
                // as a tip when it tapers.
                float taper = t < 0.85f ? 1f : Mathf.Cos((t - 0.85f) / 0.15f * Mathf.PI * 0.5f);
                float radius = Radius * Mathf.Max(taper, 0.001f);

                for (int seg = 0; seg <= Segments; seg++)
                {
                    float a = (float) seg / Segments * Mathf.PI * 2f;
                    var outward = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                    positions.Add(outward * radius + new Vector3(0f, 0f, z));
                    normals.Add(outward);
                    var along = Vector3.Cross(Vector3.forward, outward).normalized;
                    tangents.Add(new Vector4(along.x, along.y, along.z, 1f));
                }
            }

            int stride = Segments + 1;
            for (int ring = 0; ring < Rings; ring++)
            {
                for (int seg = 0; seg < Segments; seg++)
                {
                    int a = ring * stride + seg;
                    int b = a + 1;
                    int c = a + stride;
                    int d = c + 1;
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
            }

            var mesh = new Mesh { name = "YAPS Test Plug" };
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetTangents(tangents);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            // The deform throws vertices well outside the rest bounds, so
            // a tight box would let Unity cull the plug mid-bend.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 20f);
            return mesh;
        }

        // The format, written by us: one header pixel, then ten floats per
        // vertex — position, normal, tangent, active — each float stored as
        // the four bytes of one RGBA32 pixel.
        static Texture2D BuildBakeTexture(List<Vector3> positions, List<Vector3> normals,
            List<Vector4> tangents)
        {
            int floats = 1 + positions.Count * 10;
            const int width = 8192;
            int height = Mathf.Max(1, Mathf.CeilToInt((float) floats / width));

            var pixels = new Color32[width * height];
            int at = 0;
            Write(pixels, ref at, 0f);   // header
            for (int i = 0; i < positions.Count; i++)
            {
                Write(pixels, ref at, positions[i].x);
                Write(pixels, ref at, positions[i].y);
                Write(pixels, ref at, positions[i].z);
                Write(pixels, ref at, normals[i].x);
                Write(pixels, ref at, normals[i].y);
                Write(pixels, ref at, normals[i].z);
                Write(pixels, ref at, tangents[i].x);
                Write(pixels, ref at, tangents[i].y);
                Write(pixels, ref at, tangents[i].z);
                // Feather the first 10% so the base stays welded to the
                // body instead of shearing off — the same thing the real
                // bone mask does.
                float along = Mathf.Clamp01(positions[i].z / (Length * 0.1f));
                Write(pixels, ref at, along);
            }

            // Point filtering and no mips are not cosmetic: the shader does
            // exact integer loads, and any interpolation would corrupt the
            // bit patterns being read back as floats.
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
            {
                name = "YAPS Bake",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        static void Write(Color32[] pixels, ref int at, float value)
        {
            var bytes = System.BitConverter.GetBytes(value);
            pixels[at++] = new Color32(bytes[0], bytes[1], bytes[2], bytes[3]);
        }
    }
}
#endif
