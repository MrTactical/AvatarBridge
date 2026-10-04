// Shapes on a mesh the shader cannot open: a depth trigger drives them
// instead. The depth syncs, since the client runs this contact on the
// wearer's machine only.
#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using ABI.CCK.Components;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge
{
    public static class YapsSocketReactions
    {
        const string HostName = "YAPS Depth";

        // The plug tips a socket answers to. VRCFury's variants included.
        static readonly string[] TipTypes =
        {
            "TPS_Pen_Penetrating", "TPS_Pen_Penetrating_SelfNotOnHips",
            "SPSLL_Pen_Penetrating", "SPSLL_Pen_Penetrating_SelfNotOnHips",
        };

        // When no plug on the avatar says how long a plug is, this deep is 1.
        public const float DefaultReach = 0.25f;

        // Synced, and named after the bone. No '#', no spaces: CVR takes
        // neither.
        public static string Parameter(YapsSocket socket) => "YAPS/" + Machine(Unique(socket)) + "/Depth";

        public static string LegacyParameter(YapsSocket socket) => "#YAPS/" + Sanitise(socket.name) + "/Depth";

        public static string LayerName(YapsSocket socket) => "YAPS " + Unique(socket) + " reactions";

        public static string AnimationsLayerName(YapsSocket socket) => "YAPS " + Unique(socket) + " animations";

        // The label, with " 2" and on for a later socket in the hierarchy
        // that shares it, the converter's numbering for its parameters. Two
        // same-kind sockets on one bone shared one layer, one parameter and
        // one set of clips, and removing either took the other's reactions.
        static string Unique(YapsSocket socket)
        {
            string label = YapsToggles.LabelFor(socket);
            if (socket == null) return label;
            var avatar = socket.GetComponentInParent<CVRAvatar>(true);
            var top = avatar != null ? avatar.transform : socket.transform.root;
            int earlier = top.GetComponentsInChildren<YapsSocket>(true).TakeWhile(s => s != socket)
                .Count(s => YapsToggles.LabelFor(s) == label);
            return earlier == 0 ? label : label + " " + (earlier + 1);
        }

        // Always 1: the weight each clip's tree gets in the animations layer's
        // direct tree. Local, since every client already agrees on 1.
        public const string One = "#YAPS/One";

        // The controllers this socket's layers go into, the one ChilloutVR
        // uploads and the base the CCK rebuilds it from, or the line saying
        // why there are none.
        static List<AnimatorController> Targets(YapsSocket socket, out Animator animator, out CVRAvatar avatar,
                                                out string failure)
        {
            avatar = socket.GetComponentInParent<CVRAvatar>(true);
            animator = socket.GetComponentInParent<Animator>(true);
            failure = null;
            if (avatar == null || animator == null)
            {
                failure = $"✗ {socket.name}: it needs a CVRAvatar and an Animator above the socket";
                return null;
            }
            var controllers = YapsOwner.Targets(avatar)
                .Where(c => !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(c))).ToList();
            if (controllers.Count == 0)
            {
                // Targets leaves the CCK's own controller out, which is not the same as none.
                failure = CvrSetup.SharedController(YapsOwner.Shipped(avatar))
                    ? $"✗ {socket.name}: the avatar runs the CCK's own controller, which YAPS never writes into: set a copy " +
                      "of it as the avatar's Base Controller and create its own from that in Advanced Avatar Settings"
                    : $"✗ {socket.name}: the avatar has no animator controller on disk to put the layer in";
                return null;
            }
            return controllers;
        }

        // Has the animations layer been built: it is in a controller.
        public static bool AnimationsExist(YapsSocket socket)
        {
            if (socket == null) return false;
            string layerName = AnimationsLayerName(socket);
            return Controllers(socket).Any(c => c.layers.Any(l => l.name == layerName
                || (!string.IsNullOrEmpty(socket.builtAnimations) && l.name == socket.builtAnimations)));
        }

        // The author's own clips, blended in by the same synced depth the
        // shapes use: one layer per socket, a direct tree holding a 1D tree
        // per clip, from an empty clip at its start to the clip at start +
        // fade. Direct, so each clip keeps its own range in one layer. Write
        // defaults on, so with no plug in the clip's bindings sit at rest,
        // which is why the inspector says to animate what nothing else does.
        //
        // A layer of its own rather than a branch of the reactions layer:
        // shapes can open in the shader and build no layer at all, and a clip
        // always needs the animator. The depth parameter and its trigger are
        // shared with the reactions, and moving the depth to another route
        // later changes nothing here.
        public static string BuildAnimations(YapsSocket socket)
        {
            if (socket == null) return null;
            var clips = socket.depthAnimations.Where(a => a != null && a.clip != null).ToList();
            if (clips.Count == 0 && !AnimationsExist(socket)) return null;
            var controllers = Targets(socket, out _, out var avatar, out string failure);
            if (controllers == null) return failure;

            string layerName = AnimationsLayerName(socket);
            string parameter = Parameter(socket);
            AnimationClip empty = null;
            if (clips.Count > 0)
            {
                string dir = YapsNativeBuilder.OutputRoot + "/" + Sanitise(avatar.name);
                YapsNativeBuilder.EnsureFolderPublic(dir);
                string emptyPath = dir + "/YAPS Empty.anim";
                empty = AssetDatabase.LoadAssetAtPath<AnimationClip>(emptyPath);
                if (empty == null)
                {
                    empty = new AnimationClip { name = "YAPS Empty" };
                    AssetDatabase.CreateAsset(empty, emptyPath);
                }
            }

            // A layer's trees and machine belong to one controller asset, so
            // each controller gets its own.
            foreach (var controller in controllers)
            {
                var layers = controller.layers.ToList();
                int existing = layers.FindIndex(l => l.name == layerName);
                if (existing < 0 && !string.IsNullOrEmpty(socket.builtAnimations) && socket.builtAnimations != layerName)
                    existing = layers.FindIndex(l => l.name == socket.builtAnimations);
                var old = existing >= 0 ? YapsOwner.Embedded(controller, new[] { layers[existing].stateMachine }) : null;

                // Every clip removed: the layer goes, and the parameters with it
                // once nothing else reads them.
                if (clips.Count == 0)
                {
                    if (existing >= 0) layers.RemoveAt(existing);
                    controller.layers = layers.ToArray();
                    YapsOwner.DropUnreached(controller, old);
                    foreach (string name in new[] { One, parameter })
                    {
                        var p = controller.parameters.FirstOrDefault(x => x.name == name);
                        if (p != null && !YapsRemover.ParameterUsed(controller, name)) controller.RemoveParameter(p);
                    }
                    EditorUtility.SetDirty(controller);
                    continue;
                }

                if (!controller.parameters.Any(p => p.name == parameter))
                    controller.AddParameter(parameter, AnimatorControllerParameterType.Float);
                if (!controller.parameters.Any(p => p.name == One))
                    controller.AddParameter(new AnimatorControllerParameter
                        { name = One, type = AnimatorControllerParameterType.Float, defaultFloat = 1f });

                var direct = new BlendTree
                {
                    name = layerName, blendType = BlendTreeType.Direct, hideFlags = HideFlags.HideInHierarchy,
                };
                foreach (var a in clips)
                {
                    float from = Mathf.Clamp(a.startsAt, 0f, 0.99f);
                    float to = Mathf.Min(1f, from + Mathf.Max(0.01f, a.fadeOver));
                    var ramp = new BlendTree
                    {
                        name = a.clip.name, blendType = BlendTreeType.Simple1D, blendParameter = parameter,
                        useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy,
                    };
                    ramp.AddChild(empty, from);
                    ramp.AddChild(a.clip, to);
                    direct.AddChild(ramp);
                }
                var children = direct.children;
                for (int i = 0; i < children.Length; i++) children[i].directBlendParameter = One;
                direct.children = children;

                var machine = new AnimatorStateMachine { name = layerName, hideFlags = HideFlags.HideInHierarchy };
                var state = machine.AddState("Depth");
                state.writeDefaultValues = true;
                state.motion = direct;
                machine.defaultState = state;
                var layer = new AnimatorControllerLayer { name = layerName, defaultWeight = 1f, stateMachine = machine };
                if (existing >= 0) layers[existing] = layer; else layers.Add(layer);
                controller.layers = layers.ToArray();
                AnimatorAssetSaver.EmbedLayer(layer, controller);
                YapsOwner.DropUnreached(controller, old);
                EditorUtility.SetDirty(controller);
            }

            if (clips.Count == 0)
            {
                Undo.RecordObject(socket, "YAPS socket animations");
                socket.builtAnimations = "";
                // Clear ran first and kept the contact for this layer, which
                // was still reading its parameter then.
                string host = DropUnreadHost(socket, controllers);
                AssetDatabase.SaveAssets();
                return $"✓ {socket.name}: no depth animations left, layer \"{layerName}\" taken out" +
                       (host != null ? ", and " + host : "");
            }
            // After the layer is replaced, so a name only its old self read
            // is not kept.
            var also = AlsoRead(socket, parameter, controllers);
            EnsureTrigger(socket, parameter, also);
            if (socket.builtAnimations != layerName)
            {
                Undo.RecordObject(socket, "YAPS socket animations");
                socket.builtAnimations = layerName;
                EditorUtility.SetDirty(socket);
            }
            AssetDatabase.SaveAssets();
            return $"✓ {socket.name}: {clips.Count} depth animation(s) in layer \"{layerName}\", driven by the " +
                   $"synced parameter {parameter}, depth 1 at {ReachOf(socket):0.00} m in" + AlsoNote(also);
        }

        // Sync tally for the report. Past the cap the client drops the
        // parameter and only the wearer sees the shapes.
        static string SyncRoom(CVRAvatar avatar)
        {
            if (avatar == null) return "";
            try
            {
                // (current overrides, after autogeneration): two totals for
                // the same avatar, not two halves. The CCK gates on the
                // second, so this does too.
                var usage = avatar.GetParameterSyncUsage();
                int used = usage.Item2;
                if (used >= 3200)
                    return $"; ⚠ THE AVATAR IS AT THE 3200-BIT SYNC CAP ({used}), so ChilloutVR will not register this " +
                           "parameter and only the wearer will see the shapes: free some bits and build again";
                if (used > 3100)
                    return $"; ⚠ {used} of 3200 sync bits used, so there is barely room for it";
                return $"; {used} of 3200 sync bits used";
            }
            catch
            {
                return "";
            }
        }

        // A YAPS depth parameter by shape, whoever wrote it: this build's
        // synced form, the local one older builds used, or a hand edit of
        // either.
        public static bool IsDepthName(string name) =>
            !string.IsNullOrEmpty(name)
            && (name.StartsWith("YAPS/", System.StringComparison.Ordinal) || name.StartsWith("#YAPS/", System.StringComparison.Ordinal))
            && name.EndsWith("/Depth", System.StringComparison.Ordinal);

        // A parameter-safe name: letters, digits, dash and underscore.
        static string Machine(string s)
        {
            string clean = System.Text.RegularExpressions.Regex.Replace(s ?? "", "[^A-Za-z0-9_-]+", "");
            return string.IsNullOrEmpty(clean) ? "Socket" : clean;
        }

        // How far in counts as depth 1, in metres. A contact cannot know a
        // visiting plug's length, so this stands in for it.
        public static float ReachOf(YapsSocket socket)
        {
            if (socket == null) return DefaultReach;
            if (socket.depthReach > 0f) return socket.depthReach;
            var avatar = socket.GetComponentInParent<CVRAvatar>(true);
            var top = avatar != null ? avatar.transform : socket.transform.root;
            float longest = LongestPlugOn(top);
            return longest > 0f ? longest : DefaultReach;
        }

        // The longest baked plug under an object, hidden ones included, in
        // metres; 0 when there is none.
        public static float LongestPlugOn(Transform top)
        {
            float longest = 0f;
            if (top == null) return 0f;
            foreach (var r in top.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || !m.HasProperty("_YAPS_Bake") || m.GetTexture("_YAPS_Bake") == null || !m.HasProperty("_YAPS_Length")) continue;
                    // A socket's own mesh carries a bake too, with the deform off and a power.
                    if (m.HasProperty("_YAPS_SocketPower") && m.GetFloat("_YAPS_SocketPower") > 0f
                        && m.HasProperty("_YAPS_Enabled") && m.GetFloat("_YAPS_Enabled") <= 0f) continue;
                    longest = Mathf.Max(longest, YapsNativeBuilder.WorldLength(r, m));
                }
            }
            return longest;
        }

        // The trigger's box, in the socket's frame: from the socket plane
        // to the reach behind it, the reach wide.
        public static void TriggerBox(YapsSocket socket, out Vector3 offset, out Vector3 size)
        {
            float reach = ReachOf(socket);
            float wide = Mathf.Max(reach, 0.15f);
            offset = new Vector3(0f, 0f, -reach * 0.5f);
            size = new Vector3(wide, wide, reach);
        }

        // The controllers a socket's avatar plays: the uploaded one and its
        // base, and the animator's own, where older builds wrote.
        static List<AnimatorController> Controllers(YapsSocket socket)
        {
            var avatar = socket.GetComponentInParent<CVRAvatar>(true);
            var animator = socket.GetComponentInParent<Animator>(true);
            var list = YapsOwner.Targets(avatar);
            // Through any override: it wraps a controller rather than being one.
            var own = animator != null ? BridgeContext.Underlying(animator.runtimeAnimatorController) : null;
            if (own != null && !list.Contains(own)) list.Add(own);
            return list;
        }

        // Has Build run for this socket: its layer is in a controller.
        public static bool Exists(YapsSocket socket)
        {
            if (socket == null) return false;
            string layerName = LayerName(socket);
            return Controllers(socket).Any(c => c.layers.Any(l => l.name == layerName || (!string.IsNullOrEmpty(socket.builtLayer) && l.name == socket.builtLayer)));
        }

        // Builds or rebuilds the reactions for one socket. Returns what
        // happened, or null when the socket has nothing to react with.
        // Strength lives in the clips, so changing it rebuilds. The layer
        // stays at full weight: a partial one creeps to full in game,
        // blending against its own last frame.
        // No shapes left to drive: the layer it was built as goes, and the
        // depth parameter and its contact go with it unless something else
        // still reads them. A converted avatar's own depth animations are
        // exactly that something, so the contact is not ours to assume.
        public static string Clear(YapsSocket socket)
        {
            if (socket == null) return null;
            var controllers = Targets(socket, out _, out _, out _);
            var host = socket.transform.Find(HostName);
            if (controllers == null) return null;

            bool played = socket.depthAnimations.Any(a => a != null && a.clip != null);
            var names = NamesOf(socket, host);
            var done = new List<string>();
            foreach (var controller in controllers)
            {
                string layerName = LayerName(socket);
                var layers = controller.layers.ToList();
                int at = layers.FindIndex(l => l.name == layerName);
                if (at < 0 && !string.IsNullOrEmpty(socket.builtLayer))
                {
                    layerName = socket.builtLayer;
                    at = layers.FindIndex(l => l.name == layerName);
                }
                if (at >= 0)
                {
                    var old = YapsOwner.Embedded(controller, new[] { layers[at].stateMachine });
                    layers.RemoveAt(at);
                    controller.layers = layers.ToArray();
                    YapsOwner.DropUnreached(controller, old);
                    EditorUtility.SetDirty(controller);
                    string line = $"layer \"{layerName}\" taken out";
                    if (!done.Contains(line)) done.Add(line);
                }
                if (played) continue;
                foreach (string parameter in names)
                {
                    if (YapsRemover.ParameterUsed(controller, parameter)) continue;
                    var p = controller.parameters.FirstOrDefault(x => x.name == parameter);
                    if (p != null)
                    {
                        controller.RemoveParameter(p);
                        EditorUtility.SetDirty(controller);
                        if (!done.Contains("its depth parameter")) done.Add("its depth parameter");
                    }
                }
            }
            // The contact only once no controller still reads anything it writes.
            if (!played)
            {
                string contact = DropUnreadHost(socket, controllers);
                if (contact != null) done.Add(contact);
            }
            if (!string.IsNullOrEmpty(socket.builtLayer) || !string.IsNullOrEmpty(socket.builtParameter))
            {
                Undo.RecordObject(socket, "YAPS socket reactions");
                socket.builtLayer = "";
                socket.builtParameter = "";
                EditorUtility.SetDirty(socket);
            }
            if (done.Count == 0) return null;
            AssetDatabase.SaveAssets();
            return string.Join(", ", done);
        }

        // Every name the depth contact may write: this build's, the last
        // build's, and the ones its trigger writes. A converted socket's
        // trigger writes the converter's name, "#" when local or numbered
        // for a second socket on the bone, and its author's layers read
        // that; asking for the bare name alone found nothing reading it and
        // deleted the contact they ran on.
        //
        // Never another socket's current name. Two sockets that swap places
        // in the hierarchy swap names too, and the old one, still read by
        // the other socket's layer, kept both contacts writing both
        // parameters for good.
        static List<string> NamesOf(YapsSocket socket, Transform host)
        {
            var avatar = socket.GetComponentInParent<CVRAvatar>(true);
            var top = avatar != null ? avatar.transform : socket.transform.root;
            var others = new HashSet<string>(top.GetComponentsInChildren<YapsSocket>(true)
                .Where(s => s != socket).Select(Parameter));
            return new[] { Parameter(socket), socket.builtParameter }.Concat(Written(host))
                .Where(n => !string.IsNullOrEmpty(n) && !others.Contains(n)).Distinct().ToList();
        }

        static List<string> Written(Transform host)
        {
            var trigger = host != null ? host.GetComponent<CVRAdvancedAvatarSettingsTrigger>() : null;
            if (trigger == null || trigger.stayTasks == null) return new List<string>();
            return trigger.stayTasks.Where(t => t != null && !string.IsNullOrEmpty(t.settingName))
                .Select(t => t.settingName).Distinct().ToList();
        }

        // The names besides this build's that the contact keeps writing
        // because a controller reads them: on a rebuilt conversion, the
        // converter's name its author's repointed layers read. Replacing
        // the trigger with the synced name alone silenced those layers.
        // The reader may also be this socket's own layer not rebuilt yet
        // after a rename, so the note does not say whose layer it is.
        static List<string> AlsoRead(YapsSocket socket, string parameter, List<AnimatorController> controllers) =>
            NamesOf(socket, socket.transform.Find(HostName))
                .Where(n => n != parameter && controllers.Any(c => YapsRemover.ParameterUsed(c, n))).ToList();

        static string AlsoNote(List<string> also) =>
            also.Count > 0 ? $"; the contact also writes {string.Join(", ", also)}, which a layer still reads" : "";

        // The contact once no controller reads anything it writes. Null when
        // it stays, then writing only the names still read, so a rebuilt
        // conversion goes back to the one name it was converted with.
        static string DropUnreadHost(YapsSocket socket, List<AnimatorController> controllers)
        {
            var host = socket.transform.Find(HostName);
            if (host == null) return null;
            var names = NamesOf(socket, host);
            if (controllers.Any(c => names.Any(n => YapsRemover.ParameterUsed(c, n))))
            {
                var written = Written(host);
                var read = written.Where(n => names.Contains(n) && controllers.Any(c => YapsRemover.ParameterUsed(c, n))).ToList();
                if (read.Count > 0 && read.Count < written.Count) EnsureTrigger(socket, read[0], read.Skip(1));
                return null;
            }
            Undo.DestroyObjectImmediate(host.gameObject);
            return "the depth contact";
        }

        public static string Build(YapsSocket socket)
        {
            if (socket == null) return null;
            var renderer = socket.renderer;
            var stages = socket.shapes.Where(s => s != null && !string.IsNullOrEmpty(s.blendshape)).ToList();
            if (renderer == null || stages.Count == 0 || renderer.sharedMesh == null) return Clear(socket);

            var controllers = Targets(socket, out var animator, out var avatar, out string failure);
            if (controllers == null) return failure;

            var known = new HashSet<string>(Enumerable.Range(0, renderer.sharedMesh.blendShapeCount)
                .Select(renderer.sharedMesh.GetBlendShapeName));
            var missing = stages.Where(s => !known.Contains(s.blendshape)).Select(s => s.blendshape).ToList();
            stages = stages.Where(s => known.Contains(s.blendshape)).ToList();
            if (stages.Count == 0)
                return $"✗ {socket.name}: none of the named shapes are on \"{renderer.name}\"";

            string parameter = Parameter(socket);

            // Depth parameters no socket owns and no layer reads go with
            // it, or they pile up one per rename.
            var owned = new HashSet<string>();
            foreach (var s in avatar.GetComponentsInChildren<YapsSocket>(true))
            {
                owned.Add(Parameter(s));
                owned.Add(LegacyParameter(s));
            }
            owned.Remove(LegacyParameter(socket));   // this socket's old one is ours to drop

            // One blend tree over depth, a breakpoint at every stage edge.
            // Strength is the layer's weight, so it moves without a rebuild.
            string dir = YapsNativeBuilder.OutputRoot + "/" + Sanitise(avatar.name);
            YapsNativeBuilder.EnsureFolderPublic(dir);
            string rendererPath = AnimationUtility.CalculateTransformPath(renderer.transform, animator.transform);
            var depths = new SortedSet<float> { 0f, 1f };
            foreach (var s in stages)
            {
                depths.Add(Mathf.Clamp01(s.startsAt));
                depths.Add(Mathf.Clamp01(s.startsAt + Mathf.Max(0.01f, s.fadeOver)));
            }

            string layerName = LayerName(socket);
            // Each shape opens from its authored weight, so an avatar with no
            // plug in it looks as its author left it. Read past the editor's
            // test, which may be holding the mesh at a test depth.
            var floor = new Dictionary<string, float>();
            foreach (var s in stages)
            {
                int shapeIndex = renderer.sharedMesh.GetBlendShapeIndex(s.blendshape);
                floor[s.blendshape] = shapeIndex >= 0 ? YapsShapeSim.AuthoredWeight(renderer, shapeIndex) : 0f;
            }
            var points = new List<(AnimationClip clip, float depth)>();
            int index = 0;
            foreach (float depth in depths)
            {
                var clip = new AnimationClip { name = $"{layerName} {index}" };
                foreach (var s in stages)
                {
                    float opening = Mathf.Clamp01((depth - s.startsAt) / Mathf.Max(0.01f, s.fadeOver));
                    // Strength scales how far the shape opens, in the CLIP. A layer at
                    // partial weight blends against what earlier layers wrote, and
                    // nothing else writes these shapes, so it creeps to full instead.
                    float weight = Mathf.Lerp(floor[s.blendshape], 100f, opening * Mathf.Clamp01(socket.shapePower));
                    clip.SetCurve(rendererPath, typeof(SkinnedMeshRenderer), "blendShape." + s.blendshape,
                        AnimationCurve.Constant(0f, 1f / 60f, weight));
                }
                string clipPath = $"{dir}/{Sanitise(layerName)} {index}.anim";
                var saved = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                if (saved != null) { EditorUtility.CopySerialized(clip, saved); clip = saved; }
                else AssetDatabase.CreateAsset(clip, clipPath);
                points.Add((clip, depth));
                index++;
            }

            // A layer's tree and machine belong to one controller asset, so
            // each controller gets its own.
            int cleared = 0;
            foreach (var controller in controllers)
            {
                // The parameter, synced, so remote viewers see the shapes move.
                if (!controller.parameters.Any(p => p.name == parameter))
                {
                    controller.AddParameter(parameter, AnimatorControllerParameterType.Float);
                }
                foreach (var p in controller.parameters.ToList())
                {
                    if (!IsDepthName(p.name) || p.name == parameter || owned.Contains(p.name)) continue;
                    if (YapsRemover.ParameterUsed(controller, p.name)) continue;
                    controller.RemoveParameter(p);
                    cleared++;
                }

                // What the last build called it, if the bone or the name has
                // moved since: the layer is replaced where it stands rather
                // than added a second time under the new name.
                var layers = controller.layers.ToList();
                int existing = layers.FindIndex(l => l.name == layerName);
                if (existing < 0 && !string.IsNullOrEmpty(socket.builtLayer) && socket.builtLayer != layerName)
                    existing = layers.FindIndex(l => l.name == socket.builtLayer);
                var tree = new BlendTree
                {
                    name = layerName,
                    blendType = BlendTreeType.Simple1D,
                    blendParameter = parameter,
                    useAutomaticThresholds = false,
                    hideFlags = HideFlags.HideInHierarchy,
                };
                foreach (var (clip, depth) in points) tree.AddChild(clip, depth);
                var machine = new AnimatorStateMachine { name = layerName, hideFlags = HideFlags.HideInHierarchy };
                var state = machine.AddState("Depth");
                state.writeDefaultValues = true;
                state.motion = tree;
                machine.defaultState = state;
                var layer = new AnimatorControllerLayer
                {
                    name = layerName, defaultWeight = 1f, stateMachine = machine,
                };
                var old = existing >= 0 ? YapsOwner.Embedded(controller, new[] { layers[existing].stateMachine }) : null;
                if (existing >= 0) layers[existing] = layer; else layers.Add(layer);
                controller.layers = layers.ToArray();
                AnimatorAssetSaver.EmbedLayer(layer, controller);
                YapsOwner.DropUnreached(controller, old);
                EditorUtility.SetDirty(controller);
            }
            // After the layer is replaced, so a name only its old self read
            // is not kept.
            var also = AlsoRead(socket, parameter, controllers);
            EnsureTrigger(socket, parameter, also);
            // What it is called now, so the next build can find it after a
            // rename and Remove knows what to take out.
            if (socket.builtLayer != layerName || socket.builtParameter != parameter)
            {
                Undo.RecordObject(socket, "YAPS socket reactions");
                socket.builtLayer = layerName;
                socket.builtParameter = parameter;
                EditorUtility.SetDirty(socket);
            }
            AssetDatabase.SaveAssets();

            string note = $"✓ {socket.name}: {stages.Count} shape(s) on \"{renderer.name}\" react to a plug's tip through a contact, " +
                          $"depth 1 at {ReachOf(socket):0.00} m in (layer \"{layerName}\", synced parameter {parameter}, 32 bits{SyncRoom(avatar)})" +
                          AlsoNote(also);
            if (cleared > 0) note += $"; cleared {cleared} stale depth parameter(s) no layer read";
            if (missing.Count > 0) note += $"; not on the mesh: {string.Join(", ", missing)}";
            return note;
        }

        // The channel without the generated reaction layer: trigger plus
        // parameter, for rebuilt conversions where the reactions are the
        // author's own layers and only the wire is generated. The trigger is a box
        // behind the socket plane, one reach deep, read by Set From Position on
        // Z: 0 at the plane, 1 a reach in.
        public static string EnsureDepthChannel(YapsSocket socket, UnityEditor.Animations.AnimatorController controller,
            string parameter = null)
        {
            if (socket == null) return null;
            if (string.IsNullOrEmpty(parameter)) parameter = Parameter(socket);
            EnsureTrigger(socket, parameter);
            if (controller != null && !controller.parameters.Any(p => p.name == parameter))
            {
                controller.AddParameter(parameter, AnimatorControllerParameterType.Float);
            }
            return parameter;
        }

        // One stay and one exit task per name, all fed the same depth.
        static void EnsureTrigger(YapsSocket socket, string parameter, IEnumerable<string> also = null)
        {
            var names = new[] { parameter }.Concat(also ?? Enumerable.Empty<string>())
                .Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();
            TriggerBox(socket, out var offset, out var size);
            var old = socket.transform.Find(HostName);
            if (old != null)
            {
                var have = old.GetComponent<CVRAdvancedAvatarSettingsTrigger>();
                if (have != null && have.stayTasks.Select(t => t.settingName).SequenceEqual(names)
                    && have.stayTasks.All(t => t.updateMethod == CVRAdvancedAvatarSettingsTriggerTaskStay.UpdateMethod.SetFromPosition)
                    && have.sampleDirection == CVRAdvancedAvatarSettingsTrigger.SampleDirection.ZNegative
                    && have.allowedTypes != null && have.allowedTypes.SequenceEqual(TipTypes)
                    && have.areaSize == size && have.areaOffset == offset)
                    return;
                Undo.DestroyObjectImmediate(old.gameObject);
            }
            var host = new GameObject(HostName);
            Undo.RegisterCreatedObjectUndo(host, "YAPS socket reactions");
            host.transform.SetParent(socket.transform, false);
            var trigger = host.AddComponent<CVRAdvancedAvatarSettingsTrigger>();
            trigger.useAdvancedTrigger = true;
            trigger.isLocalInteractable = true;
            trigger.isNetworkInteractable = true;
            trigger.allowedTypes = TipTypes.ToArray();
            trigger.areaSize = size;
            trigger.areaOffset = offset;
            trigger.sampleDirection = CVRAdvancedAvatarSettingsTrigger.SampleDirection.ZNegative;
            foreach (string name in names)
            {
                trigger.stayTasks.Add(new CVRAdvancedAvatarSettingsTriggerTaskStay
                {
                    updateMethod = CVRAdvancedAvatarSettingsTriggerTaskStay.UpdateMethod.SetFromPosition,
                    settingName = name,
                    minValue = 0f,
                    maxValue = 1f,
                });
                trigger.exitTasks.Add(new CVRAdvancedAvatarSettingsTriggerTask
                {
                    updateMethod = CVRAdvancedAvatarSettingsTriggerTask.UpdateMethod.Override,
                    settingName = name,
                    settingValue = 0f,
                });
            }
        }

        static string Sanitise(string s)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }
    }
}
#endif
