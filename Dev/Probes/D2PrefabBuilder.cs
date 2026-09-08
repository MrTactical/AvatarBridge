// Builds the D2 test rig: does a named GrabPass reach a CVRBlitter?
//
// D1 proved the second half of the GPU bridge in game: a blit into a render
// texture, read by CVRTexturePropertyParser, drives real component properties
// on every client including remote copies. What it did NOT answer is where the
// value can come from. A blit shader has no object transforms, so it cannot
// know where anything on the avatar IS, which rules it out as the source for
// anything measured off the rig.
//
// The way round it is the atlas pattern: geometry ON the avatar computes the
// value, because geometry has a matrix, and publishes it into a named screen
// grab. A named GrabPass is a global texture, so a blit can sample it without
// knowing anything about the scene. That would put the whole socket resolve
// within reach of C# for the price of one blit.
//
// The question this rig asks is only the first link: a blit runs on the main
// camera's pre-render, so it can only ever see the PREVIOUS frame's grab, and
// "previous frame" and "never" look identical from the editor. So the source
// writes a sawtooth. A cube that ramps means the grab reached the blit; a cube
// that sits still means it did not, and the route is dead.
//
// Two blocks are written, one at each end of the screen, and the blit reads
// both and takes the larger. Which way up a grab lands is a convention this
// probe should not spend an in-game run discovering.
//
//   -executeMethod AvatarBridge.Regression.D2PrefabBuilder.Build
#if CVR_CCK_EXISTS && UNITY_EDITOR
using System.Collections.Generic;
using ABI.CCK.Components;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace AvatarBridge.Regression
{
    public static class D2PrefabBuilder
    {
        const string Folder = "Assets/AvatarBridge_D2";

        // The block is 16 pixels so a reader landing a few pixels out still
        // hits it. One pixel would make a rounding difference look like a
        // dead transport.
        const int BlockPx = 16;

        // Batch for the corpus machine, a menu item for a project already
        // open: the editor here is usually the one holding the avatar.
        [MenuItem("Tools/YAPS/Dev/Build the D2 grab probe")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
            {
                AssetDatabase.CreateFolder("Assets", "AvatarBridge_D2");
            }

            WriteShaders();
            AssetDatabase.Refresh();

            var source = Shader.Find("Hidden/AvatarBridge/D2Source");
            var grab = Shader.Find("Hidden/AvatarBridge/D2Grab");
            var blit = Shader.Find("Hidden/AvatarBridge/D2Blit");
            if (source == null || grab == null || blit == null)
            {
                Debug.Log("BUILD ABORT: a shader did not import");
                Done(false);
                return;
            }

            var sourceMaterial = new Material(source) { name = "D2Source" };
            Save(sourceMaterial, "/D2Source.mat");
            var grabMaterial = new Material(grab) { name = "D2Grab" };
            Save(grabMaterial, "/D2Grab.mat");
            var blitMaterial = new Material(blit) { name = "D2Blit" };
            Save(blitMaterial, "/D2Blit.mat");

            var origin = MakeTexture("/D2_Origin.renderTexture");
            var result = MakeTexture("/D2_Result.renderTexture");
            var quads = Quads();

            var root = new GameObject("D2_Probe");

            // The value, written by geometry on the avatar. In the real thing
            // this is where the socket resolve goes; here it is a clock, so
            // that a working chain is visibly different from a stuck one.
            Draw(root, "Source", quads, sourceMaterial);
            // Straight after, in its own queue, so the grab holds it.
            Draw(root, "Grab", quads, grabMaterial);

            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = "Marker";
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            marker.transform.SetParent(root.transform, false);
            marker.transform.localPosition = new Vector3(0f, 0.25f, 0.35f);
            marker.transform.localScale = Vector3.one * 0.08f;

            var lampObject = new GameObject("Lamp");
            lampObject.transform.SetParent(root.transform, false);
            lampObject.transform.localPosition = new Vector3(0f, 0.25f, 0.2f);
            var lamp = lampObject.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.range = 2f;
            lamp.color = Color.magenta;
            // Zero drops the light from Unity's per-object list entirely, so
            // an unwritten value has to read as dim rather than as absent.
            lamp.intensity = 0.01f;

            var blitter = root.AddComponent<CVRBlitter>();
            blitter.originTexture = origin;
            blitter.destinationTexture = result;
            blitter.blitMaterial = blitMaterial;

            var parser = root.AddComponent<CVRTexturePropertyParser>();
            parser.textureType = CVRTexturePropertyParser.TextureType.LocalTexture;
            parser.texture = result;
            parser.tasks.Add(Task(marker, marker.transform, "localScale", 4, 1, 0.03f, 0.4f));
            parser.tasks.Add(Task(lampObject, lamp, "intensity", 0, 0, 0.01f, 4f));

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, Folder + "/D2_Probe.prefab");
            Object.DestroyImmediate(root);

            WriteReadme();
            AssetDatabase.Refresh();

            Debug.Log(prefab != null
                ? $"BUILD ok: {Folder}/D2_Probe.prefab"
                : "BUILD FAILED: the prefab did not save");
            Done(prefab != null);
        }

        // Exit is the batch run's verdict and would take a working editor
        // down with it from the menu.
        static void Done(bool ok)
        {
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(ok ? 0 : 1);
            }
        }

        static void Draw(GameObject root, string name, Mesh mesh, Material material)
        {
            var host = new GameObject(name);
            host.transform.SetParent(root.transform, false);
            host.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = host.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        static CVRTexturePropertyParserTask Task(GameObject target, Component component,
            string property, int typeIndex, int targetIndex, float min, float max)
        {
            return new CVRTexturePropertyParserTask
            {
                x = 0,
                y = 0,
                channel = CVRTexturePropertyParserTask.Channel.r,
                minValue = min,
                maxValue = max,
                target = target,
                component = component,
                propertyName = property,
                typeIndex = typeIndex,
                targetIndex = targetIndex,
            };
        }

        // Two quads, corners in UV0 and every position zero, exactly as the
        // atlas writes them: a viewer with custom shaders off gets the
        // replacement shader and the mesh, so a mesh that draws nothing at the
        // origin is the only mesh that stays invisible to them.
        static Mesh Quads()
        {
            var verts = new List<Vector3>();
            var corners = new List<Vector3>();
            var tris = new List<int>();
            for (int q = 0; q < 2; q++)
            {
                int b = verts.Count;
                corners.Add(new Vector3(-0.5f, -0.5f, q));
                corners.Add(new Vector3(0.5f, -0.5f, q));
                corners.Add(new Vector3(-0.5f, 0.5f, q));
                corners.Add(new Vector3(0.5f, 0.5f, q));
                for (int c = 0; c < 4; c++) verts.Add(Vector3.zero);
                tris.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
            }
            var mesh = new Mesh { name = "D2 Quads" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, corners);
            mesh.SetTriangles(tris, 0);
            // Culling on renderer bounds would stop the whole thing the moment
            // the wearer left the frame, and the wearer is behind the camera
            // for anyone reading this.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
            Save(mesh, "/D2 Quads.asset");
            return AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/D2 Quads.asset");
        }

        static RenderTexture MakeTexture(string file)
        {
            // Linear, never the default. A gamma render texture applies the
            // sRGB curve on the way in and again on the way out, and the
            // parser samples raw pixels: D1 read 0.6431 back from 0.3725
            // before this was understood, and it looked like a dead transport.
            var rt = new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear)
            {
                filterMode = FilterMode.Point,
                useMipMap = false,
            };
            Save(rt, file);
            return AssetDatabase.LoadAssetAtPath<RenderTexture>(Folder + file);
        }

        static void Save(Object asset, string file)
        {
            AssetDatabase.DeleteAsset(Folder + file);
            AssetDatabase.CreateAsset(asset, Folder + file);
        }

        static void WriteShaders()
        {
            System.IO.File.WriteAllText(Folder + "/D2Source.shader", string.Join("\n", new[]
            {
                "// The value, written by geometry that has a matrix. A sawtooth,",
                "// because a constant reads the same whether it arrived or never did.",
                "//",
                "// Two blocks, one at each end of the screen. A grab's row order is",
                "// a convention, and reading the wrong end would look exactly like a",
                "// grab that never reached the blit.",
                "Shader \"Hidden/AvatarBridge/D2Source\"",
                "{",
                "    SubShader",
                "    {",
                "        // Before the grab, and before the scene, which then covers it.",
                "        Tags { \"Queue\" = \"Background-946\" \"RenderType\" = \"Opaque\" }",
                "        ZTest Always ZWrite Off Cull Off",
                "        Pass",
                "        {",
                "            CGPROGRAM",
                "            #pragma vertex vert",
                "            #pragma fragment frag",
                "            #pragma multi_compile_instancing",
                "            #include \"UnityCG.cginc\"",
                "",
                "            struct appdata { float4 vertex : POSITION; float3 corner : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };",
                "            struct v2f { float4 pos : SV_POSITION; float value : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };",
                "",
                "            v2f vert (appdata v)",
                "            {",
                "                v2f o;",
                "                UNITY_SETUP_INSTANCE_ID(v);",
                "                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);",
                "",
                "                float2 unit = v.corner.xy + 0.5;",
                "                float2 span = " + BlockPx + ".0 / _ScreenParams.xy * 2.0;",
                "                // Quad 0 top left, quad 1 bottom left.",
                "                float top = v.corner.z < 0.5 ? 1.0 : -1.0 + span.y;",
                "                float2 clipPos = float2(-1.0 + unit.x * span.x, top - unit.y * span.y);",
                "                o.pos = float4(clipPos, UNITY_NEAR_CLIP_VALUE, 1);",
                "",
                "                // Four second period. Local time is fine here: this rig",
                "                // asks whether a value MOVES, not whether two people agree",
                "                // on it, which D1 already settled.",
                "                o.value = frac(_Time.y * 0.25);",
                "                return o;",
                "            }",
                "",
                "            fixed4 frag (v2f i) : SV_Target { return fixed4(i.value, i.value, i.value, 1); }",
                "            ENDCG",
                "        }",
                "    }",
                "}",
            }));

            System.IO.File.WriteAllText(Folder + "/D2Grab.shader", string.Join("\n", new[]
            {
                "// The named grab, and nothing else. A named GrabPass runs once per",
                "// frame per name and leaves a global texture behind it.",
                "Shader \"Hidden/AvatarBridge/D2Grab\"",
                "{",
                "    SubShader",
                "    {",
                "        // After the source, before the scene.",
                "        Tags { \"Queue\" = \"Background-945\" \"RenderType\" = \"Opaque\" }",
                "",
                "        GrabPass { \"_YAPS_Probe\" }",
                "",
                "        // A SubShader with no pass is never drawn, so the grab never runs.",
                "        Pass",
                "        {",
                "            ColorMask 0 ZWrite Off ZTest Always Cull Off",
                "            CGPROGRAM",
                "            #pragma vertex vert",
                "            #pragma fragment frag",
                "            #pragma multi_compile_instancing",
                "            #include \"UnityCG.cginc\"",
                "            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };",
                "            struct v2f { float4 pos : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };",
                "            v2f vert (appdata v)",
                "            {",
                "                v2f o;",
                "                UNITY_SETUP_INSTANCE_ID(v);",
                "                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);",
                "                o.pos = UnityObjectToClipPos(v.vertex);",
                "                return o;",
                "            }",
                "            fixed4 frag (v2f i) : SV_Target { return 0; }",
                "            ENDCG",
                "        }",
                "    }",
                "}",
            }));

            System.IO.File.WriteAllText(Folder + "/D2Blit.shader", string.Join("\n", new[]
            {
                "// The blit: sample the named grab, write it into the render texture",
                "// the parser reads. Knows nothing about the scene, which is the point.",
                "Shader \"Hidden/AvatarBridge/D2Blit\"",
                "{",
                "    SubShader",
                "    {",
                "        Cull Off ZWrite Off ZTest Always",
                "        Pass",
                "        {",
                "            CGPROGRAM",
                "            #pragma vertex vert_img",
                "            #pragma fragment frag",
                "            #include \"UnityCG.cginc\"",
                "",
                "            sampler2D _YAPS_Probe;",
                "            float4 _YAPS_Probe_TexelSize;",
                "",
                "            fixed4 frag (v2f_img i) : SV_Target",
                "            {",
                "                // _ScreenParams here is the 4x4 target, not the screen,",
                "                // so the grab's own texel size is the only scale available.",
                "                float2 texel = _YAPS_Probe_TexelSize.xy;",
                "                float2 at = float2(4.5, 4.5) * texel;",
                "                float top = tex2D(_YAPS_Probe, at).r;",
                "                float bottom = tex2D(_YAPS_Probe, float2(at.x, 1.0 - at.y)).r;",
                "                // Whichever end the block landed on. The other reads the",
                "                // cleared frame, because the grab happens before the scene.",
                "                float v = max(top, bottom);",
                "                return fixed4(v, v, v, 1);",
                "            }",
                "            ENDCG",
                "        }",
                "    }",
                "}",
            }));
        }

        static void WriteReadme()
        {
            System.IO.File.WriteAllText(Folder + "/README.txt", string.Join("\n", new[]
            {
                "D2: does a named GrabPass reach a CVRBlitter?",
                "",
                "Drop D2_Probe.prefab on an avatar, upload it, wear it.",
                "",
                "WHAT TO WATCH",
                "  The cube grows and resets on a four second cycle, and the magenta",
                "  light brightens with it.",
                "",
                "  Ramping   : the grab reached the blit reached the parser. The socket",
                "              resolve can be read into C# for the price of one blit,",
                "              because geometry on the avatar can compute anything the",
                "              plug shader can and publish it through a grab.",
                "  Still     : it did not. The blit sees no grab, or only an empty one,",
                "              and the value has to come from a camera rendering the",
                "              avatar into a render texture instead, which costs a lot",
                "              more and needs its own pass.",
                "",
                "  D1 already proved blit -> render texture -> parser -> component, so a",
                "  still cube here is the grab link failing and nothing else.",
                "",
                "ALSO WORTH NOTING",
                "  Whether anyone else sees it move. Every client runs its own blit off",
                "  its own grab, so it should move for everyone, out of phase, since the",
                "  sawtooth is on each viewer's own clock.",
                "",
                "  Whether it keeps moving in a mirror. Every camera takes its own grab",
                "  and the last one wins; the payload here does not depend on the view,",
                "  so it should not matter, but nobody has watched it.",
            }));
        }
    }
}
#endif
