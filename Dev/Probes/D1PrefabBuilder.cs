// Builds the D1 test rig: a drop-in prefab that puts a value on the GPU and
// shows, in game, how far into C# it gets.
//
// The editor can prove the blitter half and nothing else, because
// CVRTexturePropertyParser ships as an empty stub and the client carries the
// real one. So this makes the rest visible rather than measurable: the value
// drives a cube's height and a light, and either they move in game or the
// parser never runs.
//
// The shader emits a sawtooth on time, not a constant. A constant is
// indistinguishable from a default that was never written, and the phase of a
// sawtooth answers the second question too: a remote copy ramping in step with
// the wearer's means the value crossed the network, and out of step means that
// client computed its own.
//
//   -executeMethod AvatarBridge.Regression.D1PrefabBuilder.Build
#if CVR_CCK_EXISTS && UNITY_EDITOR
using ABI.CCK.Components;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    public static class D1PrefabBuilder
    {
        const string Folder = "Assets/AvatarBridge_D1";

        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
            {
                AssetDatabase.CreateFolder("Assets", "AvatarBridge_D1");
            }

            WriteShader();
            AssetDatabase.Refresh();

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(Folder + "/D1Value.shader");
            if (shader == null)
            {
                Debug.Log("BUILD ABORT: the shader did not import");
                EditorApplication.Exit(1);
                return;
            }

            var material = new Material(shader);
            Save(material, "/D1Blit.mat");

            var origin = MakeTexture("/D1_Origin.renderTexture");
            var result = MakeTexture("/D1_Result.renderTexture");

            var root = new GameObject("D1_Probe");

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
            lamp.color = Color.cyan;
            // Zero would drop the light from Unity's per-object list entirely,
            // so an unwritten value has to read as dim rather than as absent.
            lamp.intensity = 0.01f;

            var blitter = root.AddComponent<CVRBlitter>();
            blitter.originTexture = origin;
            blitter.destinationTexture = result;
            blitter.blitMaterial = material;

            var driver = root.AddComponent<CVRAnimatorDriver>();
            driver.animators.Add(null);   // slot 1, the carried value; Animator set by hand
            driver.animators.Add(null);   // slot 2, the pump; see the readme
            driver.animatorParameters.Add("GpuValue");
            driver.animatorParameters.Add("-none-");
            driver.animatorParameterType.Add(0);
            driver.animatorParameterType.Add(0);

            var parser = root.AddComponent<CVRTexturePropertyParser>();
            parser.textureType = CVRTexturePropertyParser.TextureType.LocalTexture;
            parser.texture = result;

            // Three sinks, cheapest first. The cube needs no light and no
            // animator, so it answers "does the parser run" on its own.
            parser.tasks.Add(Task(marker, marker.transform, "localScale", 4, 1, 0.03f, 0.4f));
            parser.tasks.Add(Task(lampObject, lamp, "intensity", 0, 0, 0.01f, 4f));
            parser.tasks.Add(Task(root, driver, "animatorParameter01", 0, 0, 0f, 1f));

            MakePumpClip();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, Folder + "/D1_Probe.prefab");
            Object.DestroyImmediate(root);

            WriteReadme();
            AssetDatabase.Refresh();

            Debug.Log(prefab != null
                ? $"BUILD ok: {Folder}/D1_Probe.prefab, {parser.tasks.Count} sinks"
                : "BUILD FAILED: the prefab did not save");
            EditorApplication.Exit(prefab != null ? 0 : 1);
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

        static RenderTexture MakeTexture(string file)
        {
            // Linear, never the default. A gamma render texture applies the
            // sRGB curve on the way in and out, and the parser samples raw
            // pixels: a probe that skipped this read 0.6431 back from 0.3725.
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

        // The driver only pushes its fields into animator parameters from
        // OnDidApplyAnimationProperties, which Unity raises when a clip
        // animates one of its properties. Nothing animates slot 1, so this clip
        // animates slot 2 purely to make that callback fire; slot 2 is wired to
        // "-none-" and the driver skips it.
        static void MakePumpClip()
        {
            var clip = new AnimationClip { frameRate = 60f };
            var curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(
                "", typeof(CVRAnimatorDriver), "animatorParameter02"), curve);
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            Save(clip, "/D1Pump.anim");
        }

        static void WriteShader()
        {
            var source = string.Join("\n", new[]
            {
                "Shader \"Hidden/AvatarBridge/D1Value\"",
                "{",
                "    // A sawtooth, not a constant: a constant reads the same whether",
                "    // the parser wrote it or never ran.",
                "    SubShader",
                "    {",
                "        Cull Off ZWrite Off ZTest Always",
                "        Pass",
                "        {",
                "            CGPROGRAM",
                "            #pragma vertex vert_img",
                "            #pragma fragment frag",
                "            #include \"UnityCG.cginc\"",
                "            fixed4 frag(v2f_img i) : SV_Target",
                "            {",
                "                return fixed4(frac(_Time.y * 0.2), 0, 0, 1);",
                "            }",
                "            ENDCG",
                "        }",
                "    }",
                "}",
                "",
            });
            System.IO.File.WriteAllText(Folder + "/D1Value.shader", source);
        }

        static void WriteReadme()
        {
            var text = string.Join("\n", new[]
            {
                "# D1 probe rig",
                "",
                "This answers one question the editor cannot: does a value computed on the GPU reach C#",
                "on a stock ChilloutVR client, with no contact receiver anywhere?",
                "",
                "A shader writes a sawtooth into a render texture, the CCK blitter copies it, and the CCK",
                "texture property parser is asked to read that pixel into three different places. The",
                "blitter half is already proven in the editor. The parser ships as an empty stub, so the",
                "rest can only be seen in game.",
                "",
                "## Setting it up",
                "",
                "1. Drop `D1_Probe.prefab` under the root of any avatar you can upload.",
                "2. Upload it. Nothing else is needed for the first two questions.",
                "",
                "For the third question, the one about parameters:",
                "",
                "3. Add a float parameter called `GpuValue` to the avatar's animator controller, and a",
                "   layer that does something visible with it.",
                "4. Add a second float parameter, any name, and put `D1Pump.anim` on a looping state. The",
                "   clip animates an unused slot on the animator driver, which is the only way to make",
                "   Unity raise the callback the driver pushes its values from.",
                "5. On the `D1_Probe` object, set both entries of the driver's Animator list to the",
                "   avatar's own Animator.",
                "",
                "## Reading the result",
                "",
                "| What you see | What it means |",
                "| --- | --- |",
                "| The cube's height ramps and resets, about every five seconds | The parser runs on a stock client. The transport is real. |",
                "| The cube never moves | The parser does not run on avatars, and this route is dead. |",
                "| The light pulses with the cube | Confirms it, and shows a property is written as well as a field. |",
                "| `GpuValue` drives your layer | The value reached an animator parameter. |",
                "",
                "Then have somebody else look at you, or watch yourself from a second client:",
                "",
                "| What they see | What it means |",
                "| --- | --- |",
                "| The cube ramps in step with yours | The value crossed the network. |",
                "| The cube ramps out of step | Their client computed its own copy. Nothing was transmitted, which is fine for a value every client can compute and fatal for one only the wearer can. |",
                "| The cube is still for them and moving for you | The parser runs for the wearer only. |",
                "",
                "The render textures are linear on purpose. A default render texture is sRGB, which puts",
                "the gamma curve on the value twice, and the parser samples raw pixels.",
                "",
            });
            System.IO.File.WriteAllText(Folder + "/README.md", text);
        }
    }
}
#endif
