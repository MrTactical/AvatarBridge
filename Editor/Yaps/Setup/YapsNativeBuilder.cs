// Turns a YapsPlug into a plug: measures the mesh, bakes, patches the
// material's OWN shader, writes the knobs onto it, and announces the plug
// so sockets can see it. This is what "Make this a plug" does, and what
// the test plug is built through — the same path a user's mesh takes, so
// building the test plug IS a test of the path.
//
// TODAY (2026-08-15) it covers a static mesh and the tip/pointer markers.
// The contact channel for an avatar's own controller, and skinned-mesh
// bone chains, are next; the baker already handles skinned meshes and the
// prop rig already builds a prop channel, so both are wiring rather than
// invention. Everything it does not yet do, it says.
#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ABI.CCK.Components;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    public static class YapsNativeBuilder
    {
        public const string OutputRoot = "Assets/YAPS/Generated";
        const string MarkersName = "YAPS Markers";

        // DPS's tracker: range 0.49, intensity = length, at the BASE.
        public const float TrackerRange = 0.49f;

        public class Outcome
        {
            public bool Ok;
            public string Message;
            public List<string> Notes = new List<string>();
            public Material Material;
            public float Length, Radius;
        }

        // --- make this a plug ------------------------------------------------

        public static Outcome Bake(YapsPlug plug)
        {
            var o = new Outcome();
            if (plug == null) { o.Message = "no plug"; return o; }
            var renderer = plug.Target;
            if (renderer == null) { o.Message = "the plug has no renderer"; return o; }

            string dir = OutputRoot + "/" + Sanitise(TopName(plug.transform));
            EnsureFolder(dir);

            // Measure and bake. The plug object IS the frame on a static
            // mesh; on a skinned one the frame is recovered per vertex.
            var report = new BridgeReport();
            var result = YapsBaker.Bake(renderer, plug.transform, dir, report, out string failure);
            if (result == null) { o.Message = "could not bake: " + failure; return o; }
            o.Length = plug.lengthOverride > 0 ? plug.lengthOverride : result.Length;
            o.Radius = result.Radius;

            // Which material. The named slot, else the first.
            var mats = renderer.sharedMaterials;
            int slot = plug.materialSlot >= 0 && plug.materialSlot < mats.Length ? plug.materialSlot : 0;
            var source = mats[slot];
            if (source == null) { o.Message = $"material slot {slot} is empty"; return o; }

            // Patch its own shader — Standard, Poiyomi, whatever it wears.
            // Already wearing a patched one (a re-bake): keep it.
            Shader shader;
            string refusal = null;
            if (source.shader.name.StartsWith("Hidden/YAPS/"))
            {
                shader = source.shader;
            }
            else
            {
                shader = YapsShaderPatcher.Patch(source, dir, report, out refusal, out _);
            }
            if (shader == null) { o.Message = "could not patch the shader: " + refusal; return o; }

            var patched = source.HasProperty("_YAPS_Bake") ? source
                : YapsBaker.Apply(result, source, shader, dir, result.FromSkinnedMesh);
            if (patched != source)
            {
                mats[slot] = patched;
                renderer.sharedMaterials = mats;
            }
            else
            {
                // Re-bake onto an already-patched material: refresh the bake.
                patched.SetTexture("_YAPS_Bake", result.Bake);
                patched.SetFloat("_YAPS_VertexCount", result.VertexCount);
                patched.SetFloat("_YAPS_ShapeCount", result.Shapes.Count);
            }
            if (plug.lengthOverride > 0) patched.SetFloat("_YAPS_Length", plug.lengthOverride);
            patched.SetFloat("_YAPS_Enabled", 1f);
            patched.SetFloat("_YAPS_SelfTag", -1f);   // native: self-exclusion is by body, no tag
            WriteKnobs(plug, patched);
            EditorUtility.SetDirty(patched);
            o.Material = patched;

            // Announce: tip light for DPS, pointers for TPS/SPS.
            BuildMarkers(plug, o.Length, o.Radius);

            o.Ok = true;
            o.Message = $"Baked \"{renderer.name}\": {o.Length:0.###} m, {result.VertexCount} vertices, " +
                        $"{result.Shapes.Count} shape(s), material \"{patched.name}\".";
            o.Notes.Add("Contact channel not built yet — this plug reads sockets by their marker lights. " +
                        "Contact-only sockets (TPS orifices) will not move it until the channel lands.");
            if (result.FromSkinnedMesh) o.Notes.Add("Skinned mesh: frame recovered per vertex.");
            return o;
        }

        static void WriteKnobs(YapsPlug p, Material m)
        {
            m.SetFloat("_YAPS_Overrun", p.overrun ? 1f : 0f);
            m.SetFloat("_YAPS_TaperStart", p.taperStart);
            m.SetFloat("_YAPS_TaperEnd", p.taperEnd);
            m.SetFloat("_YAPS_Curvature", p.curvature);
            m.SetFloat("_YAPS_ReCurvature", p.recurvature);
            m.SetFloat("_YAPS_EntranceStiffness", p.entranceStiffness);
            m.SetFloat("_YAPS_Squeeze", p.squeeze);
            m.SetFloat("_YAPS_SqueezeDistance", p.squeezeReach);
            m.SetFloat("_YAPS_Bulge", p.bulge);
            m.SetFloat("_YAPS_BulgeDistance", p.bulgeReach);
            m.SetFloat("_YAPS_IdleLength", p.idleLength);
            m.SetFloat("_YAPS_IdleWidth", p.idleWidth);
            m.SetFloat("_YAPS_WriggleStrength", p.wriggle);
            m.SetFloat("_YAPS_WriggleSpeed", p.wriggleSpeed);
            m.SetFloat("_YAPS_PumpStrength", p.pumping);
            m.SetFloat("_YAPS_PumpSpeed", p.pumpingSpeed);
            m.SetFloat("_YAPS_PumpWidth", p.pumpingWidth);
            m.SetFloat("_YAPS_BezierSmoothness", p.bezierSmoothness);
            m.SetFloat("_YAPS_BezierStart", p.straightBeforeBend);
            m.SetFloat("_YAPS_SmoothStart", p.easeIntoBend);
            m.SetFloat("_YAPS_MinimumSocketDistance", p.minimumSocketDistance);
            m.SetFloat("_YAPS_TagInclude", TagNumber(p.onlySocketsTagged));
            m.SetFloat("_YAPS_TagExclude", TagNumber(p.neverSocketsTagged));
        }

        // A tag string becomes a small stable integer, 0 for none. The
        // shader compares rounded floats, so keep it under a few thousand.
        public static float TagNumber(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return 0f;
            int h = 0;
            foreach (char c in tag.Trim().ToLowerInvariant()) h = (h * 31 + c) & 0x7fffffff;
            return 1 + (h % 4000);
        }

        static void BuildMarkers(YapsPlug plug, float length, float radius)
        {
            var t = plug.transform;
            var old = t.Find(MarkersName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var go = new GameObject(MarkersName);
            go.transform.SetParent(t, false);
            var m = go.transform;

            if (plug.emitTipLight)
            {
                // At the BASE, intensity = length. Read out of Raliv's own
                // functions: depth = length - distance(orifice, light).
                var l = YapsSocketBuilder.MarkerLight(m, "DPS Tracker", TrackerRange, Vector3.zero);
                l.intensity = Mathf.Max(length, 0.01f);
            }
            if (plug.emitPointers)
            {
                YapsSocketBuilder.Pointer(m, "Tip", "TPS_Pen_Penetrating", new Vector3(0, 0, length));
                YapsSocketBuilder.Pointer(m, "Tip (SPS)", "SPSLL_Pen_Penetrating", new Vector3(0, 0, length));
                YapsSocketBuilder.Pointer(m, "Root", "TPS_Pen_Root", Vector3.zero);
                YapsSocketBuilder.Pointer(m, "Root (SPS)", "SPSLL_Pen_Root", Vector3.zero);
                YapsSocketBuilder.Pointer(m, "Width", "TPS_Pen_Width", new Vector3(radius, 0, 0));
            }
            if (m.childCount == 0) Object.DestroyImmediate(go);
        }

        // --- the test plug ---------------------------------------------------

        // A capsule with a YapsPlug on it, wearing STANDARD, baked through the
        // exact path a user's mesh takes. Building it proves the path; having
        // it proves a socket, since it will bend toward whatever socket is
        // near. Dropped in front of the scene camera.
        public static GameObject BuildTestPlug(Transform parent = null)
        {
            const float length = 0.25f, radius = 0.028f;
            var root = new GameObject("YAPS Test Plug");
            if (parent != null) root.transform.SetParent(parent, false);
            else
            {
                var cam = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : null;
                if (cam != null) root.transform.position = cam.transform.position + cam.transform.forward * 0.6f;
            }
            var mf = root.AddComponent<MeshFilter>();
            var mr = root.AddComponent<MeshRenderer>();
            EnsureFolder(OutputRoot + "/Test Plug");
            var mesh = CapsuleMesh(length, radius);
            AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath(OutputRoot + "/Test Plug/YAPS Test Plug Mesh.asset"));
            mf.sharedMesh = mesh;
            var mat = new Material(Shader.Find("Standard")) { name = "YAPS Test Plug", color = new Color(0.85f, 0.55f, 0.65f) };
            AssetDatabase.CreateAsset(mat, AssetDatabase.GenerateUniqueAssetPath(OutputRoot + "/Test Plug/YAPS Test Plug.mat"));
            mr.sharedMaterial = mat;

            var plug = root.AddComponent<YapsPlug>();
            plug.renderer = mr;
            var o = Bake(plug);
            Debug.Log("[YAPS] " + o.Message + (o.Notes.Count > 0 ? "\n  " + string.Join("\n  ", o.Notes) : ""));
            if (!o.Ok) Debug.LogError("[YAPS] test plug failed to bake: " + o.Message);
            Undo.RegisterCreatedObjectUndo(root, "YAPS test plug");
            Selection.activeGameObject = root;
            return root;
        }

        static Mesh CapsuleMesh(float length, float radius)
        {
            const int around = 20, along = 28;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var tri = new List<int>();
            for (int ring = 0; ring <= along; ring++)
            {
                float t = ring / (float) along;
                float z = t * length;
                float r = t < 0.8f ? radius : radius * Mathf.Cos((t - 0.8f) / 0.2f * Mathf.PI * 0.5f);
                for (int a = 0; a < around; a++)
                {
                    float ang = a / (float) around * Mathf.PI * 2f;
                    var off = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f);
                    v.Add(off * r + new Vector3(0, 0, z)); n.Add(off);
                }
            }
            for (int ring = 0; ring < along; ring++)
                for (int a = 0; a < around; a++)
                {
                    int nx = (a + 1) % around, here = ring * around, up = (ring + 1) * around;
                    tri.Add(here + a); tri.Add(up + a); tri.Add(up + nx);
                    tri.Add(here + a); tri.Add(up + nx); tri.Add(here + nx);
                }
            var mesh = new Mesh { name = "YAPS Test Plug Mesh" };
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetTriangles(tri, 0);
            mesh.RecalculateTangents(); mesh.RecalculateBounds();
            return mesh;
        }

        // --- helpers ------------------------------------------------------------

        static string TopName(Transform t)
        {
            var top = t; while (top.parent != null) top = top.parent;
            return top.name;
        }

        static string Sanitise(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Trim();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
#endif
