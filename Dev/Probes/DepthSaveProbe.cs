#if CVR_CCK_EXISTS
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Whether a generated float texture flagged non-readable keeps its pixels on disk when it is
    // saved again later, as the tissue's depth map is. Each case writes the map the way
    // YapsJelly.DepthMap does, then saves on the trigger named, and reads the bytes back.
    //   -executeMethod AvatarBridge.Regression.DepthSaveProbe.Run -depthOut <file>
    public static class DepthSaveProbe
    {
        const string Folder = "Assets/AvatarBridge_DepthProbe";
        const int G = 16;

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            string outFile = args[Array.IndexOf(args, "-depthOut") + 1];
            var lines = new System.Collections.Generic.List<string>();
            try
            {
                if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "AvatarBridge_DepthProbe");
                void Case(string name, bool inPlace, Action later, bool fixedWrite = false)
                {
                    string path = $"{Folder}/{name}.asset";
                    if (inPlace) { Write(path, 1f, false); AssetDatabase.SaveAssets(); }
                    var tex = Write(path, 2f, fixedWrite);
                    AssetDatabase.SaveAssets();
                    string first = Read(path);
                    later?.Invoke();
                    EditorUtility.SetDirty(AssetDatabase.LoadAssetAtPath<Texture2D>(path));
                    AssetDatabase.SaveAssets();
                    lines.Add($"{name}: after the save {first}, after a later save {Read(path)}");
                }
                Case("new", false, null);
                Case("inplace", true, null);
                Case("new-unload", false, () => EditorUtility.UnloadUnusedAssetsImmediate());
                Case("inplace-unload", true, () => EditorUtility.UnloadUnusedAssetsImmediate());
                Case("inplace-render", true, () =>
                {
                    var t = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Folder}/inplace-render.asset");
                    var rt = RenderTexture.GetTemporary(G, 12 * G, 0, RenderTextureFormat.RFloat);
                    Graphics.Blit(t, rt);
                    RenderTexture.ReleaseTemporary(rt);
                });
                // Loaded fresh from disk, as after an editor restart: a non-readable texture has no
                // CPU copy then. Dirtied by anything and saved.
                Action Reload(string name, bool flag) => () =>
                {
                    string path = $"{Folder}/{name}.asset";
                    Resources.UnloadAsset(AssetDatabase.LoadAssetAtPath<Texture2D>(path));
                    var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if (flag)
                    {
                        var so = new SerializedObject(t);
                        so.FindProperty("m_StreamingMipmaps").boolValue = true;
                        so.ApplyModifiedPropertiesWithoutUndo();
                    }
                };
                Case("new-reload", false, Reload("new-reload", false));
                Case("inplace-reload", true, Reload("inplace-reload", false));
                Case("inplace-reload-flag", true, Reload("inplace-reload-flag", true));
                Case("fixed-new-unload", false, () => EditorUtility.UnloadUnusedAssetsImmediate(), true);
                Case("fixed-inplace-unload", true, () => EditorUtility.UnloadUnusedAssetsImmediate(), true);
            }
            catch (Exception e) { lines.Add("EXCEPTION " + e); }
            finally
            {
                AssetDatabase.DeleteAsset(Folder);
                File.WriteAllLines(outFile, lines);
                Debug.Log("[DepthSave]\n" + string.Join("\n", lines));
                EditorApplication.Exit(0);
            }
        }

        // As DepthMap: kept in place when it exists, readable while it is rewritten, then not.
        // fixedWrite saves to disk while the pixels are still in memory, before the flag goes off.
        static Texture2D Write(string path, float value, bool fixedWrite)
        {
            var pixels = Enumerable.Range(0, G * 12 * G).Select(i => new Color(value + i * 0.001f, 0, 0, 0)).ToArray();
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            void Flags(bool readable)
            {
                var flags = new SerializedObject(tex);
                flags.FindProperty("m_IsReadable").boolValue = readable;
                flags.FindProperty("m_StreamingMipmaps").boolValue = false;
                flags.ApplyModifiedPropertiesWithoutUndo();
            }
            if (tex == null)
            {
                tex = new Texture2D(G, 12 * G, TextureFormat.RFloat, false, true);
                AssetDatabase.CreateAsset(tex, path);
            }
            else
            {
                Flags(true);
                tex.Reinitialize(G, 12 * G, TextureFormat.RFloat, false);
            }
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.SetPixels(pixels);
            tex.Apply(false);
            if (fixedWrite) { EditorUtility.SetDirty(tex); AssetDatabase.SaveAssetIfDirty(tex); }
            Flags(false);
            EditorUtility.SetDirty(tex);
            if (fixedWrite) AssetDatabase.SaveAssetIfDirty(tex);
            return tex;
        }

        // The first float of the image data and whether the data is all one byte.
        static string Read(string path)
        {
            string text = File.ReadAllText(Path.Combine(Application.dataPath, "..", path));
            int at = text.IndexOf("_typelessdata: ", StringComparison.Ordinal);
            if (at < 0) return "no data";
            string hex = text.Substring(at + 15).Split('\n')[0].Trim();
            bool flat = hex.Length > 0 && hex.All(c => c == hex[0]);
            float first = hex.Length >= 8 ? BitConverter.ToSingle(Enumerable.Range(0, 4).Select(i => System.Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray(), 0) : float.NaN;
            return flat ? $"FLAT '{hex.Substring(0, Math.Min(8, hex.Length))}' x{hex.Length / 2}" : $"ok first {first:0.000} ({hex.Length / 2} bytes)";
        }
    }
}
#endif
