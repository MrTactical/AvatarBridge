#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Known-answer test for rescuing a material's textures out of VRCFury's temp folder.
    //
    // Fury keeps baked textures as sub-assets of whatever file it is writing, and one user's bake
    // put four of them inside its FX controller. The rescue copied the container whole, so the
    // output carried Fury's entire FX controller, layers and clips included, for the sake of the
    // pictures in it. A texture inside anything else now comes out alone; a file of nothing but
    // textures is still copied whole, byte for byte.
    //
    // The textures are made the way Fury's arrive: compressed, and not readable, after a reload
    // from disk. A non-readable texture has no pixels a script can read, and an earlier fix
    // avoided cloning on the grounds that the clone would come out blank, so the pixels are
    // compared on the GPU, original against copy. A bake run in the same session hands over
    // live textures that were never reloaded, so one of those goes through too, made unreadable
    // the way a script makes it.
    //
    // Meshes and materials are written in one batch, with one delete for what a previous run
    // left. So a mesh and a second material slot go through too, and the conversion runs twice
    // into one folder: the second must replace the first's files in place, not number new ones.
    //
    // A second texture file of the same name, from another folder, is copied under a numbered
    // name, and the import renames its main texture to match. That one is found as the main
    // asset, since no name in the copy matches the source any more.
    public static class TextureRehomeTest
    {
        const string Temp = "Packages/com.vrcfury.temp/__ABTextureRehomeTest";
        const string Output = "Assets/__ABTextureRehomeTest";

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Texture rescue out of temp")]
        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[TextureRehomeTest] {(ok ? "ok  " : "WRONG")} {label}");
                return ok ? 0 : 1;
            }

            Clean();
            OutputAssetPaths.Reset();
            GameObject avatar = null;
            try
            {
                Directory.CreateDirectory(Temp);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Directory.CreateDirectory(Output);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                var controller = AnimatorController.CreateAnimatorControllerAtPath(Temp + "/Probe FX.controller");
                controller.AddMotion(new AnimationClip { name = "Some clip" });
                var inController = MakeTexture("In controller", 1);
                AssetDatabase.AddObjectToAsset(inController, controller);
                var live = MakeTexture("Live", 4);
                live.Apply(false, true);
                AssetDatabase.AddObjectToAsset(live, controller);

                string otherPath = Temp + "/Probe Other.asset";
                var mainOther = MakeTexture("Other main", 2);
                AssetDatabase.CreateAsset(mainOther, otherPath);
                var subOther = MakeTexture("Other sub", 3);
                AssetDatabase.AddObjectToAsset(subOther, mainOther);
                AssetDatabase.SaveAssets();

                // Not readable, then reloaded, as a texture from a bake arrives.
                foreach (var t in new[] { inController, mainOther, subOther })
                {
                    var so = new SerializedObject(t);
                    so.FindProperty("m_IsReadable").boolValue = false;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                AssetDatabase.SaveAssets();
                Resources.UnloadAsset(inController);
                Resources.UnloadAsset(mainOther);
                Resources.UnloadAsset(subOther);
                inController = Load(Temp + "/Probe FX.controller", "In controller");
                // By main asset, not by name: the import renames a file's main object after the file.
                mainOther = AssetDatabase.LoadMainAssetAtPath(otherPath) as Texture2D;
                subOther = Load(otherPath, "Other sub");
                string twinPath = Temp + "/Twin/Probe Other.asset";
                Directory.CreateDirectory(Temp + "/Twin");
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.CreateAsset(MakeTexture("Twin main", 5), twinPath);
                AssetDatabase.SaveAssets();
                Resources.UnloadAsset(AssetDatabase.LoadMainAssetAtPath(twinPath));
                var twin = AssetDatabase.LoadMainAssetAtPath(twinPath) as Texture2D;
                fail += Check("the source textures reload not readable",
                    inController != null && mainOther != null && subOther != null
                    && !inController.isReadable && !mainOther.isReadable && !subOther.isReadable);

                var material = new Material(Shader.Find("Standard")) { name = "Probe material" };
                material.SetTexture("_MainTex", inController);
                material.SetTexture("_DetailAlbedoMap", subOther);
                material.SetTexture("_EmissionMap", live);
                AssetDatabase.CreateAsset(material, Temp + "/Probe material.mat");
                var secondMaterial = new Material(Shader.Find("Standard")) { name = "Probe material B" };
                secondMaterial.SetTexture("_MainTex", mainOther);
                secondMaterial.SetTexture("_DetailAlbedoMap", twin);
                AssetDatabase.CreateAsset(secondMaterial, Temp + "/Probe material B.mat");

                avatar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                avatar.name = "__ABTextureRehomeTest";
                var filter = avatar.GetComponent<MeshFilter>();
                var renderer = avatar.GetComponent<MeshRenderer>();
                var mesh = Object.Instantiate(filter.sharedMesh);
                mesh.name = "Probe mesh";
                AssetDatabase.CreateAsset(mesh, Temp + "/Probe mesh.asset");
                AssetDatabase.SaveAssets();

                void Convert()
                {
                    // Back to the originals in temp: after a run the avatar wears the copies.
                    filter.sharedMesh = mesh;
                    renderer.sharedMaterials = new[] { material, secondMaterial };
                    OutputAssetPaths.Reset();
                    SceneAssetRehomer.Run(new BridgeContext
                    {
                        Report = new BridgeReport(),
                        Settings = new BridgeSettings(),
                        Target = avatar,
                        OutputDir = Output,
                    });
                }
                Convert();

                var rescued = renderer.sharedMaterial;
                fail += Check("the material comes out of temp",
                    rescued != null && AssetDatabase.GetAssetPath(rescued).StartsWith(Output));
                var mainTex = rescued != null ? rescued.GetTexture("_MainTex") : null;
                var detail = rescued != null ? rescued.GetTexture("_DetailAlbedoMap") : null;
                var liveCopy = rescued != null ? rescued.GetTexture("_EmissionMap") : null;
                string mainPath = AssetDatabase.GetAssetPath(mainTex);
                string detailPath = AssetDatabase.GetAssetPath(detail);
                string livePath = AssetDatabase.GetAssetPath(liveCopy);
                // From disk, not the clone still in memory: a blank save would only show here.
                AssetDatabase.SaveAssets();
                if (mainTex != null) Resources.UnloadAsset(mainTex);
                if (detail != null) Resources.UnloadAsset(detail);
                if (liveCopy != null) Resources.UnloadAsset(liveCopy);
                mainTex = Load(mainPath, "In controller");
                detail = Load(detailPath, "Other sub");
                liveCopy = Load(livePath, "Live");
                Debug.Log($"[TextureRehomeTest] _MainTex at {mainPath}, _DetailAlbedoMap at {detailPath}");

                fail += Check("the controller's texture is out of temp", mainPath.StartsWith(Output));
                fail += Check("no controller was copied",
                    !Directory.GetFiles(Output, "*.controller", SearchOption.AllDirectories).Any());
                fail += Check("the controller's texture keeps its pixels", Same(inController, mainTex));
                fail += Check("the live texture is out of temp", livePath.StartsWith(Output));
                fail += Check("the live texture keeps its pixels", Same(live, liveCopy));
                fail += Check("the controller's texture stays not readable",
                    mainTex is Texture2D t2 && !t2.isReadable);

                fail += Check("the texture file's sub texture is out of temp", detailPath.StartsWith(Output));
                fail += Check("the texture file was copied whole",
                    detailPath.EndsWith("Probe Other.asset")
                    && AssetDatabase.LoadAllAssetsAtPath(detailPath).OfType<Texture2D>().Count() == 2);
                fail += Check("the texture file's sub texture keeps its pixels", Same(subOther, detail));

                var second = renderer.sharedMaterials.Length > 1 ? renderer.sharedMaterials[1] : null;
                var secondTex = second != null ? second.GetTexture("_MainTex") : null;
                fail += Check("the second slot comes out of temp", Saved(second));
                fail += Check("the second slot's texture is in the one container copy",
                    Saved(secondTex) && AssetDatabase.GetAssetPath(secondTex) == detailPath);
                string twinCopy = AssetDatabase.GetAssetPath(second != null ? second.GetTexture("_DetailAlbedoMap") : null);
                Debug.Log($"[TextureRehomeTest] the same-named file's texture at {twinCopy}");
                fail += Check("a same-named texture file comes out under its own name",
                    twinCopy.StartsWith(Output) && twinCopy.EndsWith("Probe Other 1.asset"));
                string meshPath = AssetDatabase.GetAssetPath(filter.sharedMesh);
                fail += Check("the mesh comes out of temp", Saved(filter.sharedMesh));
                Resources.UnloadAsset(filter.sharedMesh);
                var meshOnDisk = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                fail += Check("the mesh copy keeps its vertices on disk",
                    meshOnDisk != null && meshOnDisk.vertexCount == mesh.vertexCount);

                var firstFiles = Written();
                Convert();
                var secondFiles = Written();
                Debug.Log($"[TextureRehomeTest] first run wrote {string.Join(", ", firstFiles)}");
                Debug.Log($"[TextureRehomeTest] second run left {string.Join(", ", secondFiles)}");
                fail += Check("a second run replaces the files in place", firstFiles.SequenceEqual(secondFiles));
                fail += Check("a second run rescues both slots and the mesh again",
                    Saved(renderer.sharedMaterials[0]) && Saved(renderer.sharedMaterials[1]) && Saved(filter.sharedMesh));
                fail += Check("a second run's material keeps its textures",
                    Saved(renderer.sharedMaterials[0].GetTexture("_MainTex"))
                    && AssetDatabase.GetAssetPath(renderer.sharedMaterials[1].GetTexture("_MainTex")) == detailPath);
            }
            finally
            {
                if (avatar != null) Object.DestroyImmediate(avatar);
                Clean();
            }

            Debug.Log(fail == 0
                ? "[TextureRehomeTest] PASS: textures, meshes and materials leave temp, pixels intact, a rerun replaces in place."
                : $"[TextureRehomeTest] FAIL: {fail} case(s) wrong.");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }

        static void Clean()
        {
            AssetDatabase.DeleteAsset(Output);
            AssetDatabase.DeleteAsset(Temp);
        }

        // Contains as well as the path: a copy written in a batch that never persisted still has
        // its object, and only the database can say it is not an asset.
        static bool Saved(Object o) =>
            o != null && AssetDatabase.Contains(o) && AssetDatabase.GetAssetPath(o).StartsWith(Output);

        static string[] Written() =>
            Directory.GetFiles(Output + "/RehomedAssets").Where(f => !f.EndsWith(".meta"))
                .Select(Path.GetFileName).OrderBy(f => f, System.StringComparer.Ordinal).ToArray();

        static Texture2D MakeTexture(string name, int seed)
        {
            var t = new Texture2D(64, 64, TextureFormat.RGBA32, true) { name = name };
            var pixels = new Color32[64 * 64];
            for (int i = 0; i < pixels.Length; i++)
            {
                int x = i % 64, y = i / 64;
                pixels[i] = new Color32((byte)(x * 4 * seed), (byte)(y * 4), (byte)((x ^ y) * 4), 255);
            }
            t.SetPixels32(pixels);
            t.Apply(true);
            EditorUtility.CompressTexture(t, TextureFormat.DXT1, TextureCompressionQuality.Normal);
            return t;
        }

        static Texture2D Load(string path, string name) =>
            AssetDatabase.LoadAllAssetsAtPath(path).OfType<Texture2D>().FirstOrDefault(t => t.name == name);

        // Drawn through the GPU, since neither side may be read from a script. The top mip only;
        // the rest must at least match in count.
        static bool Same(Texture a, Texture b)
        {
            if (a == null || b == null) return false;
            if (a is Texture2D ta && b is Texture2D tb
                && (ta.format != tb.format || ta.mipmapCount != tb.mipmapCount
                    || ta.filterMode != tb.filterMode || ta.wrapMode != tb.wrapMode))
            {
                Debug.Log($"[TextureRehomeTest] {a.name}: {ta.format}/{ta.mipmapCount} became {tb.format}/{tb.mipmapCount}");
                return false;
            }
            var pa = Pixels(a);
            var pb = Pixels(b);
            int differ = 0;
            bool anyColour = false;
            for (int i = 0; i < pa.Length; i++)
            {
                if (!pa[i].Equals(pb[i])) differ++;
                if (pa[i].r > 8 || pa[i].g > 8 || pa[i].b > 8) anyColour = true;
            }
            Debug.Log($"[TextureRehomeTest] {a.name}: {differ} of {pa.Length} pixels differ, source has colour: {anyColour}");
            return differ == 0 && anyColour;
        }

        static Color32[] Pixels(Texture t)
        {
            var rt = RenderTexture.GetTemporary(t.width, t.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var was = RenderTexture.active;
            Graphics.Blit(t, rt);
            RenderTexture.active = rt;
            var read = new Texture2D(t.width, t.height, TextureFormat.RGBA32, false, true);
            read.ReadPixels(new Rect(0, 0, t.width, t.height), 0, 0);
            read.Apply();
            RenderTexture.active = was;
            RenderTexture.ReleaseTemporary(rt);
            var result = read.GetPixels32();
            Object.DestroyImmediate(read);
            return result;
        }
    }
}
#endif
