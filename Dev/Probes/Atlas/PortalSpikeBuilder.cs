// Phase 4 of the portal plan: a hand-wired portal on a real avatar, for the
// in-game gate G2. Nothing here ships.
//
//   Tools/Avatar Bridge/Dev/Portal/Portal spike: build on the selection
//       the exit socket, and with Ctrl the bone the entrance goes on
//   Tools/Avatar Bridge/Dev/Portal/Portal spike: remove
//   -executeMethod AvatarBridge.Regression.PortalSpikeBuilder.Verify
//
// The build adds three things to the avatar:
//   - an entrance Hole through the production socket door, the window's Add
//     a hole then Build, so it is a plain protocol-9 hole;
//   - YAPS/Atlas Exit Spike under the exit, keyed by the owner and the
//     entrance's production number;
//   - YAPS/Portal Spike Indicator beside it, a square that reads the
//     directory back in every view (colours in the shader).
// Both spike renderers declare _YAPS_Owner, so YapsOwner.Wire puts them on
// the owner layer and a remote copy writes and reads its real, synced owner.
//
// Renumbering does not follow the key; that is phase 6, inside NumberWriters.
// Run the build last, and again after any socket is added, moved or renamed.
//
// Needs, together in one folder, the scratch Dev/Probes/Portal/yaps_atlas.cginc,
// YapsAtlasExitSpike.shader and PortalSpikeIndicator.shader.
//
// Verify builds a plain hole and the spike on two identical synthetic avatars
// and compares everything but the spike's own two objects, then renders the
// spike through the real grab: main view, HDR, a mirror camera, the 240 px
// floor and a 200 px view, plus controls that must not read green.
#if CVR_CCK_EXISTS && UNITY_EDITOR && AVATARBRIDGE_YAPS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ABI.CCK.Components;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AvatarBridge.Regression
{
    public static class PortalSpikeBuilder
    {
        const string Tag = "[PortalSpike] ";
        public const string EntranceName = "YAPS Portal Entrance";
        // "Portal Spike" names every object the spike adds and nothing else,
        // so Verify can tell them from the plain build by name.
        const string ExitName = "Portal Spike Exit";
        const string IndicatorName = "Portal Spike Indicator";
        const string Folder = "Assets/YapsPortalSpike";
        const string ExitShader = "YAPS/Atlas Exit Spike";
        const string IndicatorShader = "YAPS/Portal Spike Indicator";
        const string SocketShader = "YAPS/Atlas Socket";

        [MenuItem("Tools/Avatar Bridge/Dev/Portal/Portal spike: build on the selection")]
        static void BuildMenu()
        {
            var picked = Selection.gameObjects;
            var exit = picked.Select(g => g.GetComponent<YapsSocket>()).FirstOrDefault(s => s != null && s.name != EntranceName);
            var bone = picked.FirstOrDefault(g => g.GetComponent<YapsSocket>() == null);
            string refused = Build(exit, bone != null ? bone.transform : null, out string done);
            if (refused != null) Debug.LogWarning(Tag + refused);
            else Debug.Log(Tag + done);
        }

        [MenuItem("Tools/Avatar Bridge/Dev/Portal/Portal spike: remove")]
        static void RemoveMenu()
        {
            var at = Selection.activeGameObject;
            var avatar = at != null ? at.GetComponentInParent<CVRAvatar>(true) : null;
            if (avatar == null) { Debug.LogWarning(Tag + "select the avatar, or anything on it, first."); return; }
            Debug.Log(Tag + Remove(avatar));
        }

        static YapsSocket Entrance(CVRAvatar avatar) =>
            avatar.GetComponentsInChildren<YapsSocket>(true).FirstOrDefault(s => s.name == EntranceName);

        // Null on success, else why nothing was built.
        public static string Build(YapsSocket exit, Transform entranceParent, out string done)
        {
            done = null;
            if (exit == null) return "select the exit socket (a YAPS socket on the avatar) and, with Ctrl, the bone the entrance goes on.";
            var avatar = exit.GetComponentInParent<CVRAvatar>(true);
            if (avatar == null) return $"\"{exit.name}\" is not on an avatar.";
            var entrance = Entrance(avatar);
            if (entrance == null)
            {
                if (entranceParent == null) return "select the bone the entrance goes on too, with Ctrl.";
                if (entranceParent.GetComponentInParent<CVRAvatar>(true) != avatar) return "the exit and the entrance's bone must be on the same avatar.";
            }
            if (Shader.Find(ExitShader) == null || Shader.Find(IndicatorShader) == null)
                return $"{ExitShader} or {IndicatorShader} is missing: copy the spike shaders and the scratch yaps_atlas.cginc into one folder.";

            DropSpikeObjects(avatar.transform);
            var lines = new List<string>();
            if (entrance == null)
            {
                // The window's own door: Add a hole, then Build.
                var go = new GameObject(EntranceName);
                go.transform.SetParent(entranceParent, false);
                entrance = go.AddComponent<YapsSocket>();
                entrance.kind = YapsSocket.SocketKind.Hole;
                YapsSocketBuilder.Build(entrance);
                Undo.RegisterCreatedObjectUndo(go, "Portal spike entrance");
                lines.AddRange(YapsNativeBuilder.BuildSocket(entrance));
                YapsAtlas.AddClear(avatar.transform);
                YapsAtlas.AddGrab(avatar.transform);
            }

            // The production number, and what its writer actually publishes.
            var numbers = YapsOwner.NumberWriters(avatar.gameObject, true);
            int n = numbers.TryGetValue(entrance, out int got) ? got : 0;
            var writer = entrance.GetComponentsInChildren<Renderer>(true).Select(r => r.sharedMaterial)
                .FirstOrDefault(m => m != null && m.shader != null && m.shader.name == SocketShader);
            int published = writer != null ? Mathf.RoundToInt(writer.GetFloat("_YAPS_SocketIndex")) : -1;
            if (n < 1 || n > YapsOwner.MaxSelfSockets)
                return $"the entrance has no number (it is past the first {YapsOwner.MaxSelfSockets} sockets), so it can carry no portal.";
            if (published != n) return $"the entrance's writer publishes number {published}, the numbering says {n}.";

            Spike(exit.transform, ExitName, ExitQuads(), Mat(ExitShader, "Exit", n));
            Spike(exit.transform, IndicatorName, IndicatorQuad(), Mat(IndicatorShader, "Indicator", n));
            AssetDatabase.SaveAssets();

            string owner = YapsOwner.Wire(avatar) ?? "owner wiring unchanged";
            // BuildSocket's own tail: the wire re-adds its layer last, and the
            // readout layer goes after it again, so the order is production's.
            foreach (var controller in YapsOwner.Targets(avatar)) YapsDebugOverlayBuilder.Menu(avatar, controller);
            string shared = YapsOwner.SharedWarning(avatar);
            bool wired = YapsOwner.Targets(avatar).Count > 0;
            done = $"entrance \"{Path(entrance.transform, avatar.transform)}\" is socket #{n}; exit \"{Path(exit.transform, avatar.transform)}\"; " +
                   $"{owner}." + (wired ? "" : " NO CONTROLLER to wire the owner into: the square reads yellow everywhere.") +
                   (shared != null ? " " + shared + "." : "") +
                   (lines.Count > 0 ? " Entrance build: " + string.Join("; ", lines) : "") +
                   " Run this again after adding, moving or renaming any socket.";
            return null;
        }

        public static string Remove(CVRAvatar avatar)
        {
            int dropped = DropSpikeObjects(avatar.transform);
            var entrance = Entrance(avatar);
            // RemoveSocket rewires the owner itself, without the spike's renderers.
            string removed = entrance != null ? YapsRemover.RemoveSocket(entrance) : YapsOwner.Wire(avatar);
            return $"removed {dropped} spike object(s). {removed ?? ""} The files in {Folder} stay; delete the folder by hand when done.";
        }

        static int DropSpikeObjects(Transform root)
        {
            var spike = root.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name == ExitName || t.name == IndicatorName).ToList();
            foreach (var t in spike) Undo.DestroyObjectImmediate(t.gameObject);
            return spike.Count;
        }

        // Beside each other with the parent's pose, so the indicator's matrix
        // is the writer's.
        static void Spike(Transform exit, string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(exit, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            YapsAtlas.Quiet(r);
            Undo.RegisterCreatedObjectUndo(go, "Portal spike");
        }

        // Assets, not scene objects: an upload keeps only what is on disk.
        static Material Mat(string shader, string what, int n)
        {
            string path = $"{Folder}/{what} #{n}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                Directory.CreateDirectory(Folder);
                m = new Material(Shader.Find(shader)) { name = Path0(path) };
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = Shader.Find(shader);
            m.SetFloat("_YAPS_EntranceIndex", n);
            EditorUtility.SetDirty(m);
            return m;
        }

        static string Path0(string path) => System.IO.Path.GetFileNameWithoutExtension(path);

        // One quad per home, flagged by z; positions zero and corners in UV0,
        // with the writers' never-culled bounds. See YapsAtlas.LevelQuads.
        static Mesh ExitQuads() => Quads(Folder + "/Exit Quads.asset", 2);
        static Mesh IndicatorQuad() => Quads(Folder + "/Indicator Quad.asset", 1);

        static Mesh Quads(string path, int count)
        {
            var have = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (have != null) return have;
            var corners = new List<Vector3>();
            var tris = new List<int>();
            for (int q = 0; q < count; q++)
            {
                int b = corners.Count;
                corners.Add(new Vector3(-0.5f, -0.5f, q));
                corners.Add(new Vector3(0.5f, -0.5f, q));
                corners.Add(new Vector3(-0.5f, 0.5f, q));
                corners.Add(new Vector3(0.5f, 0.5f, q));
                tris.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
            }
            var mesh = new Mesh { name = Path0(path) };
            mesh.SetVertices(corners.Select(_ => Vector3.zero).ToList());
            mesh.SetUVs(0, corners);
            mesh.SetTriangles(tris, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
            Directory.CreateDirectory(Folder);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static string Path(Transform t, Transform root) =>
            t == root ? "" : AnimationUtility.CalculateTransformPath(t, root);

        // --- Verify -----------------------------------------------------------

        const string Dir = "Assets/__PortalSpikeProbe";
        const string AvatarName = "__PortalSpikeAvatar";
        static int _fail;

        [MenuItem("Tools/Avatar Bridge/Dev/Portal/Portal spike")]
        public static void Verify()
        {
            // It opens an empty scene and deletes the build's own material folder, so in a real
            // project one click would drop unsaved work and strip a built spike of its materials.
            if (!Application.isBatchMode)
            {
                Debug.LogWarning(Tag + "batch mode only: it replaces the open scene and deletes " + Dir);
                return;
            }
            _fail = 0;
            var before = Files("Assets/YAPS");
            try { Measure(); }
            catch (Exception e) { _fail++; Debug.LogError(Tag + "FAIL " + e); }
            finally { Clean(before); }
            Debug.Log(Tag + (_fail == 0 ? "RESULT PASS" : $"RESULT FAIL ({_fail})"));
            if (Application.isBatchMode) EditorApplication.Exit(_fail == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) _fail++;
            Debug.Log(Tag + (ok ? "PASS " : "FAIL ") + what);
        }

        static void Log(string s) => Debug.Log(Tag + s);

        static void Measure()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            foreach (var name in new[] { ExitShader, IndicatorShader })
            {
                var s = Shader.Find(name);
                if (s == null) throw new InvalidOperationException(name + " not found");
                foreach (var m in ShaderUtil.GetShaderMessages(s)) Log($"  {name}: {m.severity} {m.message} line {m.line}");
                if (!s.isSupported || ShaderUtil.ShaderHasError(s)) throw new InvalidOperationException(name + " did not compile");
            }

            // 1. The plain hole: the window's door alone, on a fresh avatar.
            var plain = MakeAvatar(out var exit, out var bone);
            var hole = new GameObject(EntranceName);
            hole.transform.SetParent(bone, false);
            var socket = hole.AddComponent<YapsSocket>();
            socket.kind = YapsSocket.SocketKind.Hole;
            YapsSocketBuilder.Build(socket);
            YapsNativeBuilder.BuildSocket(socket);
            YapsAtlas.AddClear(plain.transform);
            YapsAtlas.AddGrab(plain.transform);
            var plainDump = Dump(plain);
            int plainN = YapsOwner.NumberWriters(plain, true)[socket];
            Object.DestroyImmediate(plain);
            AssetDatabase.DeleteAsset(Dir);

            // 2. The same avatar again, with the spike.
            var avatar = MakeAvatar(out exit, out bone);
            string refused = Build(exit, bone, out string done);
            Check(refused == null, "the spike builds: " + (refused ?? done));
            if (refused != null) return;
            var spikeDump = Dump(avatar);
            var kept = spikeDump.Where(l => !l.Contains("Portal Spike") && !l.Contains(Folder)).ToList();
            Check(!plainDump.Any(l => l.Contains("Portal Spike") || l.Contains(Folder)), "the plain build has no spike lines");
            var differ = Enumerable.Range(0, Math.Max(kept.Count, plainDump.Count))
                .Where(i => i >= kept.Count || i >= plainDump.Count || kept[i] != plainDump[i]).ToList();
            int diff = differ.Count;
            foreach (int i in differ.Take(6))
                Log($"  line {i}: plain {(i < plainDump.Count ? plainDump[i] : "-")} | spike {(i < kept.Count ? kept[i] : "-")}");
            Check(diff == 0, $"everything but the spike's objects is identical to the plain hole's build: " +
                             $"{plainDump.Count} lines plain, {kept.Count} spike after taking out {spikeDump.Count - kept.Count} spike lines, {diff} differ");
            var entranceLines = plainDump.Count(l => l.Contains(EntranceName));
            Log($"  of them {entranceLines} name the entrance (its objects, components, materials and clip curves)");

            var entrance = Entrance(avatar.GetComponent<CVRAvatar>());
            int n = Mathf.RoundToInt(Mat(ExitShader, "Exit", YapsOwner.NumberWriters(avatar, true)[entrance]).GetFloat("_YAPS_EntranceIndex"));
            Check(n == plainN && n > 1, $"the key is the entrance's production number, #{n} (the plain hole's #{plainN}; #1 belongs to another socket)");

            // The owner layer reaches both spike renderers.
            var owned = YapsOwner.Targets(avatar.GetComponent<CVRAvatar>())
                .SelectMany(c => c.animationClips).Where(c => c != null && c.name == "YAPS owner")
                .SelectMany(AnimationUtility.GetCurveBindings)
                .Where(b => b.propertyName == "material._YAPS_Owner").Select(b => b.path).ToList();
            Check(owned.Any(p => p.EndsWith("/" + ExitName)) && owned.Any(p => p.EndsWith("/" + IndicatorName)),
                $"the owner layer drives both spike renderers ({owned.Count} owner curves: {string.Join(", ", owned.Where(p => p.Contains("Portal Spike")))})");

            Render(avatar, exit);
        }

        // The same synthetic avatar every time, posed off the origin: a ring
        // first, so the entrance's number is not 1, and the exit on its own bone.
        static GameObject MakeAvatar(out YapsSocket exit, out Transform entranceBone)
        {
            AssetDatabase.CreateFolder("Assets", Dir.Substring("Assets/".Length));
            var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(Dir + "/Avatar.controller");
            var root = new GameObject(AvatarName);
            root.transform.SetPositionAndRotation(new Vector3(2.3f, 0.4f, -1.7f), Quaternion.Euler(0f, 37f, 0f));
            root.AddComponent<Animator>().runtimeAnimatorController = controller;
            root.AddComponent<CVRAvatar>();
            var hips = Child(root.transform, "Hips", new Vector3(0f, 0.9f, 0f), Quaternion.identity);
            var chest = Child(hips, "Chest", new Vector3(0f, 0.3f, 0f), Quaternion.identity);
            var hand = Child(chest, "Hand", new Vector3(0.3f, 0.1f, 0.2f), Quaternion.Euler(0f, 0f, 90f));
            var exitBone = Child(chest, "Exit Bone", new Vector3(0.05f, 0.12f, 0.14f), Quaternion.Euler(20f, 50f, 10f));
            Socket(hand, "YAPS Ring", YapsSocket.SocketKind.Ring);
            exit = Socket(exitBone, "YAPS Hole", YapsSocket.SocketKind.Hole);
            YapsAtlas.AddClear(root.transform);
            YapsAtlas.AddGrab(root.transform);
            entranceBone = Child(hips, "Front", new Vector3(0f, -0.05f, 0.12f), Quaternion.identity);
            return root;
        }

        static Transform Child(Transform parent, string name, Vector3 at, Quaternion turn)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = at;
            t.localRotation = turn;
            return t;
        }

        static YapsSocket Socket(Transform parent, string name, YapsSocket.SocketKind kind)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var s = go.AddComponent<YapsSocket>();
            s.kind = kind;
            YapsSocketBuilder.Build(s);
            YapsNativeBuilder.BuildSocket(s);
            return s;
        }

        // --- the comparison ---------------------------------------------------

        // Every object, component and serialized field under the root, then
        // every asset they reach that the build writes, by path and name, never
        // by instance id, so two builds compare line for line. Clips go by
        // binding, sorted, so the owner curves the spike adds drop out as lines.
        static List<string> Dump(GameObject root)
        {
            var lines = new List<string>();
            var queue = new List<Object>();
            var seen = new HashSet<Object>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string path = Path(t, root.transform);
                lines.Add($"object \"{path}\" active {t.gameObject.activeSelf} layer {t.gameObject.layer} tag {t.tag}");
                foreach (var c in t.GetComponents<Component>())
                {
                    if (c == null) { lines.Add($"\"{path}\" missing script"); continue; }
                    // The CCK's random per-object identifier, not part of any build.
                    if (c is CVRAssetInfo) continue;
                    // Children are the hierarchy above, and the exit's list grows by the spike.
                    Fields(new SerializedObject(c), $"\"{path}\" {c.GetType().Name}", root.transform, lines, queue, seen,
                        p => c is Transform && p.name == "m_Children");
                }
            }
            for (int i = 0; i < queue.Count; i++)
            {
                var a = queue[i];
                string label = Label(a, root.transform);
                if (a is AnimationClip clip)
                {
                    lines.AddRange(AnimationUtility.GetCurveBindings(clip)
                        .Select(b => $"{label} curve \"{b.path}\" {b.type.Name} {b.propertyName} = " +
                                     string.Join(",", AnimationUtility.GetEditorCurve(clip, b).keys.Select(k => $"{k.time:R}:{k.value:R}")))
                        .Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip)
                            .Select(b => $"{label} pptr \"{b.path}\" {b.type.Name} {b.propertyName} = " +
                                         string.Join(",", AnimationUtility.GetObjectReferenceCurve(clip, b).Select(k => $"{k.time:R}:{Label(k.value, root.transform)}"))))
                        .OrderBy(l => l, StringComparer.Ordinal));
                    continue;
                }
                Fields(new SerializedObject(a), label, root.transform, lines, queue, seen, _ => false);
            }
            return lines;
        }

        static void Fields(SerializedObject so, string prefix, Transform root, List<string> lines, List<Object> queue,
                           HashSet<Object> seen, Func<SerializedProperty, bool> skip)
        {
            var it = so.GetIterator();
            bool more = it.Next(true);
            while (more)
            {
                bool skipped = skip(it);
                if (!skipped) lines.Add($"{prefix} {it.propertyPath} = {Value(it, root, queue, seen)}");
                // A reference's own children are its file ids, which differ
                // between any two scene objects; Value has already named it.
                more = it.Next(!skipped && it.propertyType != SerializedPropertyType.ObjectReference);
            }
        }

        static string Value(SerializedProperty p, Transform root, List<Object> queue, HashSet<Object> seen)
        {
            var inv = CultureInfo.InvariantCulture;
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer: return p.longValue.ToString(inv);
                case SerializedPropertyType.Boolean: return p.boolValue ? "true" : "false";
                case SerializedPropertyType.Float: return p.doubleValue.ToString("R", inv);
                case SerializedPropertyType.String: return "\"" + p.stringValue + "\"";
                case SerializedPropertyType.Enum: return p.enumValueIndex.ToString(inv);
                case SerializedPropertyType.ArraySize: return p.intValue.ToString(inv);
                case SerializedPropertyType.Color: return p.colorValue.ToString("R");
                case SerializedPropertyType.Vector2: return p.vector2Value.ToString("R");
                case SerializedPropertyType.Vector3: return p.vector3Value.ToString("R");
                case SerializedPropertyType.Vector4: return p.vector4Value.ToString("R");
                case SerializedPropertyType.Quaternion: return p.quaternionValue.ToString("R");
                case SerializedPropertyType.Rect: return p.rectValue.ToString("R");
                case SerializedPropertyType.Bounds: return p.boundsValue.ToString("R");
                case SerializedPropertyType.LayerMask: return p.intValue.ToString(inv);
                case SerializedPropertyType.ManagedReference: return p.managedReferenceFullTypename;
                case SerializedPropertyType.ObjectReference:
                    var o = p.objectReferenceValue;
                    if (o != null && Follows(o) && seen.Add(o)) queue.Add(o);
                    return Label(o, root);
                default: return p.propertyType.ToString();
            }
        }

        // What the build writes and so must compare: materials, controllers
        // and their parts, clips, masks. Meshes, shaders, textures and scripts
        // are named only.
        static bool Follows(Object o) =>
            AssetDatabase.Contains(o) && (o is Material || o is RuntimeAnimatorController || o is AnimationClip || o is AvatarMask
                || o is UnityEditor.Animations.AnimatorStateMachine || o is UnityEditor.Animations.AnimatorState
                || o is UnityEditor.Animations.AnimatorTransitionBase || o is UnityEditor.Animations.BlendTree
                || o is StateMachineBehaviour);

        static string Label(Object o, Transform root)
        {
            if (o == null) return "null";
            if (AssetDatabase.Contains(o)) return $"asset \"{AssetDatabase.GetAssetPath(o)}\" \"{o.name}\" {o.GetType().Name}";
            var t = o is Component c ? c.transform : o is GameObject g ? g.transform : null;
            if (t != null) return $"scene \"{(t.IsChildOf(root) ? Path(t, root) : "OUTSIDE " + t.name)}\" {o.GetType().Name}";
            return $"memory \"{o.name}\" {o.GetType().Name}";
        }

        // --- the render -------------------------------------------------------

        static void Render(GameObject avatar, YapsSocket exitSocket)
        {
            var writer = exitSocket.transform.Find(ExitName).GetComponent<MeshRenderer>();
            var indicator = exitSocket.transform.Find(IndicatorName);
            var at = writer.transform.position;
            Log($"exit at {at.ToString("F4")}, facing {writer.transform.forward.ToString("F4")}, " +
                $"GPU {SystemInfo.graphicsDeviceName}, {SystemInfo.graphicsDeviceType}");
            YapsOwnerStandIn.Apply();
            var owner = new MaterialPropertyBlock();
            writer.GetPropertyBlock(owner);
            Log($"owner id on the writer {owner.GetFloat("_YAPS_Owner"):0}, as the owner layer sets it in game");

            var cam = new GameObject("__PortalSpikeCamera").AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.1f, 0.15f, 0.2f, 1f);
            cam.renderingPath = RenderingPath.Forward;
            cam.allowMSAA = false;
            cam.nearClipPlane = 0.01f;
            cam.fieldOfView = 60f;
            var from = (Quaternion.Euler(10f, 25f, 0f) * Vector3.back).normalized;
            try
            {
                Color[] Shot(int w, int h, bool half, bool mirror)
                {
                    var rt = new RenderTexture(w, h, 24, half ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                    cam.targetTexture = rt;
                    cam.allowHDR = half;
                    cam.aspect = (float) w / h;
                    cam.ResetWorldToCameraMatrix();
                    cam.ResetProjectionMatrix();
                    if (!mirror)
                    {
                        cam.transform.position = at + from * 0.8f;
                        cam.transform.LookAt(at);
                    }
                    else
                    {
                        // A mirror 0.4 m behind the exit, the way a mirror script
                        // renders it: reflected view, oblique near plane, culling flipped.
                        var nrm = (Quaternion.Euler(0f, -30f, 0f) * Vector3.forward).normalized;
                        var p0 = at - nrm * 0.4f;
                        var image = at - nrm * 0.8f;
                        cam.transform.position = at + nrm * 0.6f + Vector3.Cross(nrm, Vector3.up).normalized * 0.2f;
                        cam.transform.LookAt(image);
                        cam.worldToCameraMatrix = cam.worldToCameraMatrix * Reflection(nrm, -Vector3.Dot(nrm, p0));
                        var w2c = cam.worldToCameraMatrix;
                        var cn = w2c.MultiplyVector(nrm).normalized;
                        var cp = w2c.MultiplyPoint(p0);
                        cam.projectionMatrix = cam.CalculateObliqueMatrix(new Vector4(cn.x, cn.y, cn.z, -Vector3.Dot(cp, cn)));
                    }
                    GL.invertCulling = mirror;
                    cam.Render();
                    GL.invertCulling = false;
                    var was = RenderTexture.active;
                    RenderTexture.active = rt;
                    var tex = new Texture2D(w, h, TextureFormat.RGBAFloat, false, true);
                    tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                    tex.Apply(false);
                    RenderTexture.active = was;
                    var px = tex.GetPixels();
                    Object.DestroyImmediate(tex);
                    cam.targetTexture = null;
                    rt.Release();
                    Object.DestroyImmediate(rt);
                    return px;
                }

                // The exit sits at the centre of every shot, so a flipped
                // readback still lands on it.
                string Seen(string view, int w, int h, bool half = false, bool mirror = false)
                {
                    var px = Shot(w, h, half, mirror);
                    var votes = new Dictionary<string, int>();
                    for (int y = h / 2 - 2; y <= h / 2 + 2; y++)
                        for (int x = w / 2 - 2; x <= w / 2 + 2; x++)
                        {
                            string c = Class(px[y * w + x]);
                            votes[c] = votes.TryGetValue(c, out int k) ? k + 1 : 1;
                        }
                    var top = votes.OrderByDescending(v => v.Value).First();
                    var mid = px[(h / 2) * w + w / 2];
                    Log($"  {view} {w}x{h}{(half ? " half" : " 8-bit")}: {top.Key} ({top.Value}/25), centre rgb {mid.r:0.000} {mid.g:0.000} {mid.b:0.000}");
                    return top.Key;
                }

                Check(Seen("main view", 1024, 768) == "green", "main view reads green");
                Check(Seen("mirror camera", 512, 512, mirror: true) == "green", "mirror camera reads green");
                Check(Seen("small view", 200, 200) == "grey", "a view under 240 px reads grey");
                Check(Seen("HDR view", 1920, 1080, half: true) == "green", "a large HDR view reads green");
                Check(Seen("floor view", 240, 240) == "green", "the 240 px floor reads green");
                Check(Seen("small mirror", 200, 200, mirror: true) == "grey", "a mirror under 240 px reads grey");

                // Controls: each must leave the green.
                foreach (var r in avatar.GetComponentsInChildren<Renderer>(true)) r.SetPropertyBlock(null);
                Check(Seen("owner unknown", 1024, 768) == "yellow", "with no owner yet it reads yellow, never green");
                YapsOwnerStandIn.Apply();
                writer.enabled = false;
                Check(Seen("writer off", 1024, 768) == "red", "with the exit writer off it reads red");
                writer.enabled = true;
                indicator.localPosition = new Vector3(0.005f, 0f, 0f);
                Check(Seen("5 mm off", 1024, 768) == "red", "5 mm from the published exit it reads red (the position gate is live)");
                indicator.localPosition = Vector3.zero;
                indicator.localRotation = Quaternion.Euler(2f, 0f, 0f);
                Check(Seen("2 deg off", 1024, 768) == "red", "2 deg from the published facing it reads red (the facing gate is live)");
                indicator.localRotation = Quaternion.identity;
                Check(Seen("restored", 1024, 768) == "green", "back in place it reads green again");

                // The entry itself, through the MeshRenderer path: every pixel
                // the writer adds lies in the 8 margin rows at one edge.
                var ind = indicator.GetComponent<MeshRenderer>();
                ind.enabled = false;
                var on = Shot(1024, 768, false, false);
                writer.enabled = false;
                var off = Shot(1024, 768, false, false);
                writer.enabled = true;
                ind.enabled = true;
                int changed = 0, inTop = 0, inBottom = 0, maxX = -1;
                for (int i = 0; i < on.Length; i++)
                {
                    if (on[i] == off[i]) continue;
                    changed++;
                    int y = i / 1024, x = i % 1024;
                    if (y < 8) inBottom++;
                    if (y >= 768 - 8) inTop++;
                    maxX = Math.Max(maxX, x);
                }
                Check(changed > 0 && (inTop == changed || inBottom == changed) && maxX < 932,
                    $"the writer changes {changed} pixels, all in the 8 margin rows at one edge ({inTop} at one, {inBottom} at the other), x up to {maxX} inside the 932 px rect");
            }
            finally
            {
                GL.invertCulling = false;
                Object.DestroyImmediate(cam.gameObject);
            }
        }

        static Matrix4x4 Reflection(Vector3 n, float d)
        {
            var m = Matrix4x4.identity;
            m.m00 = 1 - 2 * n.x * n.x; m.m01 = -2 * n.x * n.y; m.m02 = -2 * n.x * n.z; m.m03 = -2 * d * n.x;
            m.m10 = -2 * n.y * n.x; m.m11 = 1 - 2 * n.y * n.y; m.m12 = -2 * n.y * n.z; m.m13 = -2 * d * n.y;
            m.m20 = -2 * n.z * n.x; m.m21 = -2 * n.z * n.y; m.m22 = 1 - 2 * n.z * n.z; m.m23 = -2 * d * n.z;
            return m;
        }

        static string Class(Color c)
        {
            if (c.g > 0.6f && c.r < 0.3f && c.b < 0.3f) return "green";
            if (c.r > 0.6f && c.g < 0.3f && c.b < 0.3f) return "red";
            if (c.r > 0.6f && c.g > 0.6f && c.b < 0.3f) return "yellow";
            if (Mathf.Abs(c.r - c.g) < 0.05f && Mathf.Abs(c.g - c.b) < 0.05f && c.r > 0.2f && c.r < 0.9f) return "grey";
            return "other";
        }

        // --- cleanup ----------------------------------------------------------

        static HashSet<string> Files(string folder) =>
            Directory.Exists(folder)
                ? new HashSet<string>(Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                    .Where(f => !f.EndsWith(".meta")).Select(f => f.Replace('\\', '/')))
                : new HashSet<string>();

        // Only what this run made: the probe's folders, and anything new under
        // Assets/YAPS, the shared writer materials and the avatar's output.
        static void Clean(HashSet<string> before)
        {
            foreach (var go in Object.FindObjectsOfType<CVRAvatar>(true).Where(a => a.name == AvatarName)) Object.DestroyImmediate(go.gameObject);
            AssetDatabase.DeleteAsset(Dir);
            AssetDatabase.DeleteAsset(Folder);
            int removed = 0;
            foreach (var f in Files("Assets/YAPS").Except(before)) if (AssetDatabase.DeleteAsset(f)) removed++;
            foreach (var d in Directory.Exists("Assets/YAPS") ? Directory.GetDirectories("Assets/YAPS", "*", SearchOption.AllDirectories)
                         .OrderByDescending(x => x.Length) : Enumerable.Empty<string>())
                if (!Directory.EnumerateFileSystemEntries(d).Any(e => !e.EndsWith(".meta")) && !before.Any(b => b.StartsWith(d.Replace('\\', '/') + "/")))
                    AssetDatabase.DeleteAsset(d.Replace('\\', '/'));
            Log($"cleanup: {removed} new file(s) under Assets/YAPS removed, probe folders deleted");
        }
    }
}
#endif
