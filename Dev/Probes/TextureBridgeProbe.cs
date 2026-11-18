// D1: can a value cross from the GPU into a synced parameter with no contact?
// Chain: atlas shader -> RenderTexture -> CVRTexturePropertyParser -> component
// field -> animator parameter -> AAS. Only the texture half is provable here:
// CVRBlitter is real CCK code, but the parser is a stub (empty Update, the
// client has the real one), so the parser, field write and sync need an upload.
//
//   -executeMethod AvatarBridge.Regression.TextureBridgeProbe.Run
#if CVR_CCK_EXISTS && UNITY_EDITOR
using System.Linq;
using ABI.CCK.Components;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    public static class TextureBridgeProbe
    {
        // Exactly representable in 8 bits, so a mismatch is the transport
        // rather than a rounding step nobody controls.
        const float Known = 95f / 255f;

        public static void Run()
        {
            // Linear: a gamma RT read 0.6431 back from a written 0.3725 (the sRGB
            // curve). The parser reads raw pixels, so every RT in the chain must be linear.
            var origin = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear);
            var destination = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear);
            origin.Create();
            destination.Create();

            var previous = RenderTexture.active;
            RenderTexture.active = origin;
            GL.Clear(true, true, new Color(Known, 0f, 0f, 1f));
            RenderTexture.active = previous;

            var shader = Shader.Find("Hidden/BlitCopy");
            if (shader == null)
            {
                Debug.Log("PROBE ABORT: Hidden/BlitCopy is not in this project");
                EditorApplication.Exit(0);
                return;
            }

            var go = new GameObject("Bridge");
            var blitter = go.AddComponent<CVRBlitter>();
            blitter.originTexture = origin;
            blitter.destinationTexture = destination;
            blitter.blitMaterial = new Material(shader);

            // Called directly rather than waited for: Execute is public, and
            // Camera.onPreRender needs a camera actually rendering, which a
            // batch editor does not reliably have.
            blitter.Execute();

            float read = ReadRed(destination);
            Debug.Log($"PROBE blitter: wrote r={Known:0.####}, read back r={read:0.####} -> " +
                (Mathf.Abs(read - Known) < 0.005f
                    ? "the CCK blitter moves a value into a RenderTexture in the editor"
                    : "MISMATCH, the texture half does not hold"));

            // The parser, as far as it can be taken here: the task can be
            // built and pointed at a field, and the component accepts it.
            // Whether the client ever runs it is the upload's question.
            var parser = go.AddComponent<CVRTexturePropertyParser>();
            parser.textureType = CVRTexturePropertyParser.TextureType.LocalTexture;
            parser.texture = destination;
            var sink = go.AddComponent<Animator>();
            parser.tasks.Add(new CVRTexturePropertyParserTask
            {
                x = 0,
                y = 0,
                channel = CVRTexturePropertyParserTask.Channel.r,
                minValue = 0f,
                maxValue = 1f,
                target = go,
                component = sink,
                propertyName = "speed",
            });
            Debug.Log($"PROBE parser: {parser.tasks.Count} task(s) accepted, texture {parser.texture.width}x" +
                $"{parser.texture.height}");

            // Read off the type rather than assumed.
            var update = typeof(CVRTexturePropertyParser).GetMethod("Update",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var body = update?.GetMethodBody();
            Debug.Log($"PROBE parser Update: {(update == null ? "absent" : $"{body?.GetILAsByteArray()?.Length ?? -1} bytes of IL")}" +
                " (a stub here; the client carries the real one)");

            EditorApplication.Exit(0);
        }

        static float ReadRed(RenderTexture rt)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, true);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            return tex.GetPixel(0, 0).r;
        }
    }
}
#endif
