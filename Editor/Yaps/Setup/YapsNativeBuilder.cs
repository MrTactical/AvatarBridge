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

            // Patch its own shader — Poiyomi, whatever it wears. Already
            // wearing a patched one (a re-bake): keep it.
            //
            // Standard cannot be patched, and neither can anything else
            // built into Unity: there is no source on disk, the vertex
            // functions live in Unity's own includes, and the input struct
            // has no vertex id to address the bake with. A surface shader
            // has no vertex function of its own either. So when its own
            // shader refuses, the plug goes on YAPS's plain lit shader
            // instead — colour, albedo, normal map, metallic and smoothness
            // carried over by name — and the outcome says so. The one
            // refusal that does NOT fall back is a shader still carrying
            // VRChat's SPS: that is a conversion, and swapping Poiyomi for
            // a plain shader would be the wrong answer to it.
            Shader shader;
            string refusal = null;
            if (source.HasProperty("_YAPS_Bake"))
            {
                shader = source.shader;
            }
            else
            {
                shader = YapsShaderPatcher.Patch(source, dir, report, out refusal, out _);
                if (shader == null && !source.HasProperty("_SPS_Bake"))
                {
                    var plain = OnSimpleLit(source, out string why);
                    if (plain == null) { o.Message = "could not patch the shader: " + refusal + "; and " + why; return o; }
                    shader = YapsShaderPatcher.Patch(plain, dir, report, out string plainRefusal, out _);
                    if (shader == null) { o.Message = "could not patch the shader: " + refusal + "; and YAPS Simple Lit refused too: " + plainRefusal; return o; }
                    o.Notes.Add($"\"{source.shader.name}\" could not be patched ({refusal}), so the plug now wears " +
                                "YAPS Simple Lit with its colour, albedo, normal map, metallic and smoothness carried " +
                                "over. Its original material is untouched. Put a shader with source on it (Poiyomi, " +
                                "for one) and re-bake if you need more than that.");
                    source = plain;
                }
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

        public const string SimpleLitName = "YAPS/Simple Lit";

        // The same material on YAPS's plain lit shader, in memory only —
        // the bake clones it into the output folder. Unity keeps a
        // property's value across a shader change when the new shader
        // declares the same name, and Simple Lit deliberately uses
        // Standard's: _Color, _MainTex, _BumpMap, _BumpScale, _Metallic,
        // _Glossiness, _EmissionColor.
        static Material OnSimpleLit(Material source, out string why)
        {
            why = null;
            var shader = Shader.Find(SimpleLitName);
            if (shader == null)
            {
                why = "YAPS Simple Lit is not in the project (Editor/Yaps/YapsSimpleLit.shader)";
                return null;
            }
            var m = new Material(source) { name = source.name };
            m.shader = shader;
            return m;
        }

        // The material's YAPS panel calls this after a change: every YapsPlug
        // whose renderer wears this material takes the values back, so the
        // component and the material never disagree about a knob.
        public static void SyncPlugsFrom(Material m)
        {
            if (m == null) return;
            foreach (var plug in Object.FindObjectsOfType<YapsPlug>())
            {
                var r = plug.Target;
                if (r == null || !r.sharedMaterials.Contains(m)) continue;
                Undo.RecordObject(plug, "YAPS plug knobs");
                ReadKnobs(plug, m);
                EditorUtility.SetDirty(plug);
            }
        }

        public static void WriteKnobs(YapsPlug p, Material m)
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
            // On a static mesh the plug object IS the frame; on a skinned one
            // the baker measured a frame in world space, and the markers
            // must sit THERE, not on the object — which on a real avatar is
            // routinely a quarter of a metre from the shaft.
            AnnouncePlug(plug.transform, null, null, length, radius, plug.emitTipLight, plug.emitPointers);
        }

        // Make a plug visible to every socket family: a DPS tracker light at
        // the BASE with intensity = length, and the tip/root/width pointers
        // both contact families read. Called by the toolkit for a native plug
        // and by the converter for one arriving from VRChat, so the two are
        // identical to every reader. Idempotent: rebuild replaces.
        //
        // `worldOrigin`/`worldRotation` are the MEASURED frame when known
        // (skinned meshes); null means the parent's own transform is the
        // frame (a static plug object).
        public static GameObject AnnouncePlug(Transform parent, Vector3? worldOrigin, Quaternion? worldRotation,
            float length, float radius, bool tipLight = true, bool pointers = true)
        {
            var old = parent.Find(MarkersName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var go = new GameObject(MarkersName);
            go.transform.SetParent(parent, false);
            if (worldOrigin.HasValue && worldRotation.HasValue
                && (worldRotation.Value.x != 0f || worldRotation.Value.y != 0f
                    || worldRotation.Value.z != 0f || worldRotation.Value.w != 0f))
            {
                go.transform.SetPositionAndRotation(worldOrigin.Value, worldRotation.Value);
            }
            var m = go.transform;

            if (tipLight)
            {
                // Read out of Raliv's own functions, not guessed:
                //   penetratorLength = unity_LightColor[i].a
                //   depth = max(0, penetratorLength - distance(orifice, light))
                // so the light sits at the BASE and its intensity is the
                // length in metres. Range 0.49 exactly — SPS2 tags its own
                // lights with a fourth decimal of 5–7 to ignore them, and
                // this is the one light where wearing that tag would cost.
                var l = YapsSocketBuilder.MarkerLight(m, "DPS Tracker", TrackerRange, Vector3.zero);
                l.intensity = Mathf.Max(length, 0.01f);
            }
            if (pointers)
            {
                // Tip and root as separate points, because that is how a
                // socket measures depth. Both families' names, so a TPS
                // orifice and an SPS socket both see it — but ONLY the names
                // not already announced beneath the parent. VRCFury's bake
                // leaves TPS pen pointers, which the converter carries; a
                // second TPS_Pen_Penetrating beside them would have a socket
                // trigger reporting whichever entered last, tip and root
                // taking turns, and the depth value jumping between two
                // unrelated distances. That bug has been had once already.
                var have = new HashSet<string>(parent.GetComponentsInChildren<CVRPointer>(true)
                    .Where(p => p != null && p.transform != m && !p.transform.IsChildOf(m))
                    .Select(p => p.type));
                void Add(string name, string type, Vector3 at)
                {
                    if (!have.Contains(type)) YapsSocketBuilder.Pointer(m, name, type, at);
                }
                Add("Tip", "TPS_Pen_Penetrating", new Vector3(0, 0, length));
                Add("Tip (SPS)", "SPSLL_Pen_Penetrating", new Vector3(0, 0, length));
                Add("Root", "TPS_Pen_Root", Vector3.zero);
                Add("Root (SPS)", "SPSLL_Pen_Root", Vector3.zero);
                Add("Width", "TPS_Pen_Width", new Vector3(Mathf.Max(radius, 0.005f), 0, 0));
            }
            if (m.childCount == 0) { Object.DestroyImmediate(go); return null; }
            return go;
        }

        // --- adopting what the converter (or an older build) made --------------
        //
        // A converted avatar's sockets and plug are bare objects with lights
        // and pointers beneath — the converter made them and nothing lets a
        // user change them afterwards. Adoption puts the authoring component
        // ON them, filled in from what was built: kind from the light range,
        // the shape rows from what was baked, the knobs from the material.
        // After that a converted socket is as editable as one placed by hand,
        // and the user can differ from the author. Idempotent — an existing
        // component is left alone, so a re-run never resets someone's edits.

        public static YapsSocket AdoptSocket(Transform socketRoot, Renderer renderer, Material material,
            IList<string> bakedShapes)
        {
            if (socketRoot == null) return null;
            var comp = socketRoot.GetComponent<YapsSocket>();
            if (comp != null) return comp;
            comp = socketRoot.gameObject.AddComponent<YapsSocket>();

            // Kind: the root light's second decimal says it (1 hole, 2
            // ring); the SPS pointer name says it too. Ring if neither does.
            bool hole = false;
            foreach (var l in socketRoot.GetComponentsInChildren<Light>(true))
            {
                if (!YapsScanner.IsProtocolLight(l)) continue;
                int d = YapsScanner.LightDigit(l);
                if (d == 1) { hole = true; break; }
                if (d == 2) { hole = false; break; }
            }
            foreach (var p in socketRoot.GetComponentsInChildren<CVRPointer>(true))
            {
                if (p != null && p.type != null && p.type.StartsWith("SPSLL_Socket_Hole")) { hole = true; break; }
            }
            comp.kind = hole ? YapsSocket.SocketKind.Hole : YapsSocket.SocketKind.Ring;
            comp.renderer = renderer as SkinnedMeshRenderer;
            comp.emitLights = socketRoot.GetComponentsInChildren<Light>(true).Any(YapsScanner.IsProtocolLight);

            // The shape rows, from the bake, staged the way the material says.
            if (bakedShapes != null && material != null && material.HasProperty("_YAPS_SocketShapeStart"))
            {
                var starts = material.GetVector("_YAPS_SocketShapeStart");
                var fades = material.GetVector("_YAPS_SocketShapeFade");
                comp.shapes.Clear();
                for (int i = 0; i < bakedShapes.Count && i < 4; i++)
                {
                    comp.shapes.Add(new YapsSocket.ShapeStage
                    {
                        blendshape = bakedShapes[i], startsAt = starts[i], fadeOver = fades[i],
                    });
                }
                if (material.HasProperty("_YAPS_SocketPower")) comp.shapePower = material.GetFloat("_YAPS_SocketPower");
            }
            return comp;
        }

        public static YapsPlug AdoptPlug(Transform plugRoot, Renderer renderer, int slot, Material material,
            Transform rootBone, float lengthOverride = 0f)
        {
            if (plugRoot == null) return null;
            var comp = plugRoot.GetComponent<YapsPlug>();
            if (comp != null) return comp;
            comp = plugRoot.gameObject.AddComponent<YapsPlug>();
            comp.renderer = renderer;
            comp.materialSlot = slot;
            comp.rootBone = rootBone;
            comp.lengthOverride = lengthOverride;
            if (material != null) ReadKnobs(comp, material);
            comp.emitTipLight = plugRoot.GetComponentsInChildren<Light>(true)
                .Any(l => YapsScanner.IsProtocolLight(l) && (YapsScanner.LightDigit(l) == 8 || YapsScanner.LightDigit(l) == 9));
            comp.emitPointers = plugRoot.GetComponentsInChildren<CVRPointer>(true)
                .Any(p => p != null && p.type != null && (p.type.StartsWith("TPS_Pen_") || p.type.StartsWith("SPSLL_Pen_")));
            return comp;
        }

        // The mirror of WriteKnobs: what the material carries becomes the
        // component's fields, so a re-bake writes back the same values.
        static void ReadKnobs(YapsPlug p, Material m)
        {
            float F(string n, float d) => m.HasProperty(n) ? m.GetFloat(n) : d;
            p.overrun = F("_YAPS_Overrun", 1f) > 0.5f;
            p.taperStart = F("_YAPS_TaperStart", 0.10f);
            p.taperEnd = F("_YAPS_TaperEnd", 0.30f);
            p.curvature = F("_YAPS_Curvature", 0f);
            p.recurvature = F("_YAPS_ReCurvature", 0f);
            p.entranceStiffness = F("_YAPS_EntranceStiffness", 0f);
            p.squeeze = F("_YAPS_Squeeze", 0f);
            p.squeezeReach = F("_YAPS_SqueezeDistance", 0.15f);
            p.bulge = F("_YAPS_Bulge", 0f);
            p.bulgeReach = F("_YAPS_BulgeDistance", 0.2f);
            p.idleLength = F("_YAPS_IdleLength", 1f);
            p.idleWidth = F("_YAPS_IdleWidth", 1f);
            p.wriggle = F("_YAPS_WriggleStrength", 0f);
            p.wriggleSpeed = F("_YAPS_WriggleSpeed", 2f);
            p.pumping = F("_YAPS_PumpStrength", 0f);
            p.pumpingSpeed = F("_YAPS_PumpSpeed", 6f);
            p.pumpingWidth = F("_YAPS_PumpWidth", 1f);
            p.bezierSmoothness = F("_YAPS_BezierSmoothness", 1f);
            p.straightBeforeBend = F("_YAPS_BezierStart", 0f);
            p.easeIntoBend = F("_YAPS_SmoothStart", 0f);
            p.minimumSocketDistance = F("_YAPS_MinimumSocketDistance", 0f);
        }

        // --- the test plug ---------------------------------------------------

        // A capsule with a YapsPlug on it, wearing YAPS Simple Lit, baked
        // through the exact path a user's mesh takes. Building it proves the
        // path; having it proves a socket, since it will bend toward
        // whatever socket is near. Dropped in front of the scene camera.
        //
        // It wore Standard once, and Standard cannot be patched — the test
        // plug then spawned straight, unbaked, with the console saying why
        // and the scene saying "not baked". Simple Lit is the shader the
        // toolkit falls back to for exactly that case, so the test plug
        // wearing it from the start tests the fallback path too.
        public static GameObject BuildTestPlug(Transform parent = null, bool select = true)
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
            var shader = Shader.Find(SimpleLitName) ?? Shader.Find("Standard");
            var mat = new Material(shader) { name = "YAPS Test Plug", color = new Color(0.85f, 0.55f, 0.65f) };
            AssetDatabase.CreateAsset(mat, AssetDatabase.GenerateUniqueAssetPath(OutputRoot + "/Test Plug/YAPS Test Plug.mat"));
            mr.sharedMaterial = mat;

            var plug = root.AddComponent<YapsPlug>();
            plug.renderer = mr;
            var o = Bake(plug);
            Debug.Log("[YAPS] " + o.Message + (o.Notes.Count > 0 ? "\n  " + string.Join("\n  ", o.Notes) : ""));
            if (!o.Ok) Debug.LogError("[YAPS] test plug failed to bake: " + o.Message);
            Undo.RegisterCreatedObjectUndo(root, "YAPS test plug");
            if (select) Selection.activeGameObject = root;
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
