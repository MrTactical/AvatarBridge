// Turns an object carrying a YAPS plug or socket into a ChilloutVR prop:
// spawnable, pickup with theft disallowed, a collider to grab by, and for
// a baked plug the synced contact channel that reaches remote viewers.
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
    public static class YapsPropBuilder
    {
        // The channel's trigger box, in plug lengths each way.
        const float BoxLengths = 1.75f;
        const string HostPrefix = "YAPS Channel ";

        static readonly string[] SocketTypes =
        {
            "TPS_Orf_Root", "TPS_Orf_Root_SelfNotOnHips",
            "SPSLL_Socket_Root", "SPSLL_Socket_Root_SelfNotOnHips",
            "SPSLL_Socket_Hole", "SPSLL_Socket_Hole_SelfNotOnHips",
            "SPSLL_Socket_Ring", "SPSLL_Socket_Ring_SelfNotOnHips",
        };
        static readonly string[] HoleTypes = { "SPSLL_Socket_Hole", "SPSLL_Socket_Hole_SelfNotOnHips" };
        static readonly string[] FrontTypes =
        {
            "TPS_Orf_Norm", "TPS_Orf_Norm_SelfNotOnHips",
            "SPSLL_Socket_Front", "SPSLL_Socket_Front_SelfNotOnHips",
        };

        // Value name, then the material property it drives.
        static readonly (string Value, string Property)[] Channel =
        {
            ("E", "material._YAPS_SocketFlags.x"),
            ("H", "material._YAPS_SocketFlags.y"),
            ("X", "material._YAPS_SocketPos.x"),
            ("Y", "material._YAPS_SocketPos.y"),
            ("Z", "material._YAPS_SocketPos.z"),
            ("FX", "material._YAPS_SocketFront.x"),
            ("FY", "material._YAPS_SocketFront.y"),
            ("FZ", "material._YAPS_SocketFront.z"),
        };

        public class Outcome
        {
            public bool Ok;
            public string Message;
            public List<string> Notes = new List<string>();
        }

        public static bool IsProp(GameObject root) => root != null && root.GetComponent<CVRSpawnable>() != null;

        public static Outcome MakeProp(GameObject root)
        {
            var o = new Outcome();
            if (root == null) { o.Message = "nothing selected"; return o; }
            var plug = root.GetComponentInChildren<YapsPlug>(true);
            var socket = root.GetComponentInChildren<YapsSocket>(true);
            if (plug == null && socket == null)
            {
                o.Message = $"\"{root.name}\" has no YAPS Plug or YAPS Socket under it. Make one first, then make it a prop.";
                return o;
            }

            Undo.RegisterFullObjectHierarchyUndo(root, "YAPS prop");

            // Grabbable. Move mode Transform: these are held against each
            // other, and physics would only add drift.
            //
            // Theft is ALLOWED, and that is the lesser of two faults. A
            // channel value is written by whoever's socket the prop met,
            // not by whoever holds it, and that write re-sends the prop's
            // position and marks it no longer remotely synced. With theft
            // disallowed, CanPickup refuses anyone but whoever GrabbedBy
            // names — and GrabbedBy only clears when updates stop arriving,
            // which a socket still touching the prop keeps from happening.
            // The prop then belongs to whoever last had it in a socket and
            // nobody else can pick it up until it is respawned. Allowing
            // theft costs the older fault instead: a prop can be tugged out
            // of a remote hand the moment a socket switches on. Tick
            // Disallow Theft on the CVR Pickup Object if you would rather
            // have that one.
            bool fresh = root.GetComponent<CVRPickupObject>() == null;
            var pickup = root.GetComponent<CVRPickupObject>();
            if (pickup == null) pickup = Undo.AddComponent<CVRPickupObject>(root);
            pickup.moveMode = CVRPickupObject.MoveMode.Transform;
            pickup.maximumGrabDistance = 8f;
            // Only on a prop this is making for the first time: a user who
            // ticked it themselves keeps their answer, and hears what it does.
            if (fresh) pickup.disallowTheft = false;
            else if (pickup.disallowTheft)
                o.Notes.Add("Disallow Theft is on. Earlier builds set it, and on a prop with a contact channel it " +
                            "means whoever last had the prop in a socket keeps it: nobody else can pick it up until " +
                            "the prop is respawned. Untick it on the CVR Pickup Object unless you want that.");

            var spawnable = root.GetComponent<CVRSpawnable>();
            if (spawnable == null) spawnable = Undo.AddComponent<CVRSpawnable>(root);
            spawnable.spawnHeight = 1.2f;

            // On the root, not in a child: a collider a child owns is one
            // the pickup cannot see, so a prop built by an early build
            // stayed ungrabbable no matter how often this ran.
            if (root.GetComponent<Collider>() == null) AddCollider(root, plug, o);

            if (plug != null)
            {
                var material = BakedMaterial(plug);
                if (material == null)
                {
                    o.Notes.Add("The plug is not baked, so no contact channel was built. Bake it, then make it a prop again.");
                }
                else
                {
                    RemoveChannel(root);
                    BuildChannel(root, plug, material, spawnable);
                    o.Notes.Add("Contact channel built: 8 synced values, one trigger per value. Run Verify prop before uploading; " +
                                "the CCK inspector can blank a value's parameter name if the Spawnable is left open.");
                }
            }
            if (socket != null && plug == null)
            {
                o.Notes.Add("A socket prop: found by plugs through its markers, no channel needed.");
            }

            o.Ok = true;
            o.Message = $"\"{root.name}\" is a prop: spawnable, pickup with theft off" + (plug != null ? ", channel" : "") + ".";
            return o;
        }

        // The repair a prop needs after the CCK inspector has been at it:
        // a value with a blank parameter name is skipped by the client.
        public static Outcome Verify(GameObject root)
        {
            var o = new Outcome();
            var spawnable = root != null ? root.GetComponent<CVRSpawnable>() : null;
            if (spawnable == null) { o.Message = "not a prop"; return o; }
            int repaired = 0, broken = 0;
            foreach (var value in spawnable.syncValues)
            {
                if (value.animator == null) continue;
                var controller = value.animator.runtimeAnimatorController as AnimatorController;
                if (controller == null) { broken++; o.Notes.Add($"\"{value.name}\" points at an animator with no controller."); continue; }
                var declared = new HashSet<string>(controller.parameters.Select(p => p.name));
                if (string.IsNullOrEmpty(value.animatorParameterName) || value.animatorParameterName == "-none-")
                {
                    if (declared.Contains(value.name)) { value.animatorParameterName = value.name; repaired++; }
                    else { broken++; o.Notes.Add($"\"{value.name}\" has a blank parameter name and nothing to restore it from."); }
                }
                else if (!declared.Contains(value.animatorParameterName))
                {
                    broken++;
                    o.Notes.Add($"\"{value.name}\" names \"{value.animatorParameterName}\", which the controller does not declare.");
                }
            }
            if (!spawnable.useAdditionalValues && spawnable.syncValues.Count > 0)
            {
                spawnable.useAdditionalValues = true;
                repaired++;
            }

            // The grab. The client reads a pickup's collider off the pickup's
            // own object, so one on a child leaves a prop nobody can hold.
            var pickup = root.GetComponent<CVRPickupObject>();
            if (pickup != null && root.GetComponent<Collider>() == null)
            {
                broken++;
                o.Notes.Add("Nothing can grab this prop: its pickup has no collider on the same object " +
                            "(an early build put one on a \"YAPS Grab\" child, which the game does not read). " +
                            "Make it a prop again.");
            }
            if (repaired > 0) EditorUtility.SetDirty(spawnable);
            o.Ok = broken == 0;
            o.Message = broken == 0
                ? (repaired > 0 ? $"Prop verified; {repaired} repaired." : "Prop verified.")
                : $"Prop has {broken} problem(s).";
            return o;
        }

        static Material BakedMaterial(YapsPlug plug)
        {
            var r = plug.Target;
            if (r == null) return null;
            return r.sharedMaterials.FirstOrDefault(m => m != null && m.HasProperty("_YAPS_Bake") && m.HasProperty("_YAPS_Length"));
        }

        // The collider a hand grabs by, and it has to be on the ROOT: the
        // client reads its pickup's collider with TryGetComponent on the
        // pickup's own object, and both grab paths — the interaction ray
        // and the proximity overlap — look the pickup up from the collider
        // they hit. A pickup with none can never be picked up.
        //
        // A trigger, and it must be: the client gives every pickup a
        // KINEMATIC rigidbody, so a solid collider never rests on anything
        // anyway; all it does is shove players about and hold the prop off
        // the socket it is being brought to. Both grab paths take triggers
        // — the proximity overlap asks for them, and the interaction ray
        // leaves Physics.queriesHitTriggers alone, which is on.
        //
        // A capsule along the plug when the plug's frame lines up with the
        // root, since a capsule has a direction but no rotation; a box
        // round the renderers otherwise.
        static void AddCollider(GameObject root, YapsPlug plug, Outcome o)
        {
            // Whatever an earlier build left, wherever it left it.
            var old = root.transform.Find("YAPS Grab");
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            foreach (var c in root.GetComponents<Collider>()) Undo.DestroyObjectImmediate(c);

            var frame = plug != null ? (plug.transform.Find("YAPS Markers") ?? plug.transform) : null;
            var material = plug != null ? BakedMaterial(plug) : null;
            if (frame != null && material != null
                && Quaternion.Angle(frame.rotation, root.transform.rotation) < 5f)
            {
                float length = material.GetFloat("_YAPS_Length");
                var capsule = Undo.AddComponent<CapsuleCollider>(root);
                capsule.isTrigger = true;
                capsule.direction = 2;
                capsule.height = length;
                capsule.radius = Mathf.Max(0.02f, length * 0.12f);
                capsule.center = root.transform.InverseTransformPoint(frame.position + frame.forward * (length * 0.5f));
                return;
            }
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) { o.Notes.Add("No renderer to size a collider from; add one by hand so the prop can be grabbed."); return; }
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            var box = Undo.AddComponent<BoxCollider>(root);
            box.isTrigger = true;
            box.center = root.transform.InverseTransformPoint(bounds.center);
            box.size = Vector3.Scale(bounds.size, new Vector3(
                1f / Mathf.Max(root.transform.lossyScale.x, 1e-4f),
                1f / Mathf.Max(root.transform.lossyScale.y, 1e-4f),
                1f / Mathf.Max(root.transform.lossyScale.z, 1e-4f)));
        }

        static void RemoveChannel(GameObject root)
        {
            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                var c = root.transform.GetChild(i);
                if (c.name.StartsWith(HostPrefix)) Object.DestroyImmediate(c.gameObject);
            }
            var spawnable = root.GetComponent<CVRSpawnable>();
            if (spawnable != null)
                spawnable.syncValues.RemoveAll(v => Channel.Any(c => c.Value == v.name));
            // The channel's layers and parameters, wherever they went.
            var animator = root.GetComponent<Animator>();
            var controller = animator != null ? animator.runtimeAnimatorController as AnimatorController : null;
            if (controller != null) StripChannel(controller);
        }

        static void StripChannel(AnimatorController controller)
        {
            var layers = controller.layers.ToList();
            int before = layers.Count;
            layers.RemoveAll(l => Channel.Any(c => c.Value == l.name));
            if (layers.Count != before) controller.layers = layers.ToArray();
            foreach (var (value, _) in Channel)
            {
                if (controller.parameters.Any(p => p.name == value)) controller.RemoveParameter(
                    controller.parameters.First(p => p.name == value));
            }
            EditorUtility.SetDirty(controller);
        }

        // The channel: eight synced values driven by triggers, each value a
        // one-parameter layer blending a material property between 0 and 1.
        // The material reads them in its own box, centred on the plug's base.
        static void BuildChannel(GameObject root, YapsPlug plug, Material material, CVRSpawnable spawnable)
        {
            var renderer = plug.Target;
            var frame = plug.transform.Find("YAPS Markers") ?? plug.transform;
            float length = Mathf.Max(material.GetFloat("_YAPS_Length"), 0.01f);
            float extent = length * BoxLengths;
            var box = new Vector3(extent * 2f, extent * 2f, extent * 2f);
            material.SetFloat("_YAPS_ChannelSpace", 1f);
            material.SetVector("_YAPS_ChannelExtents", new Vector4(extent, extent, extent, 0f));
            EditorUtility.SetDirty(material);

            string dir = YapsNativeBuilder.OutputRoot + "/" + Sanitise(root.name);
            if (!AssetDatabase.IsValidFolder(dir)) YapsNativeBuilder.EnsureFolderPublic(dir);
            string clipPath = AnimationUtility.CalculateTransformPath(renderer.transform, root.transform);

            // The prop's own controller keeps its layers and gains the
            // channel's; a prop without one gets the channel controller,
            // the same file every time rather than a numbered new one.
            var animator = root.GetComponent<Animator>();
            if (animator == null) animator = Undo.AddComponent<Animator>(root);
            var existing = animator.runtimeAnimatorController as AnimatorController;
            bool own = existing != null && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(existing));
            var controller = BuildController(dir, clipPath, renderer.GetType(), own ? existing : null);
            animator.runtimeAnimatorController = controller;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // The trigger tasks index the value list, so each value's slot
            // is the one it was given, not its place in our own list.
            spawnable.useAdditionalValues = true;
            var slot = new Dictionary<string, int>();
            foreach (var (value, _) in Channel)
            {
                slot[value] = spawnable.syncValues.Count;
                spawnable.syncValues.Add(new CVRSpawnableValue
                {
                    name = value,
                    startValue = 0f,
                    updatedBy = CVRSpawnableValue.UpdatedBy.None,
                    updateMethod = CVRSpawnableValue.UpdateMethod.Override,
                    animator = animator,
                    animatorParameterName = value,
                });
            }

            // One object per trigger: the client puts a receiver on the
            // trigger's own object and gives it that trigger's shape.
            GameObject Host(string name)
            {
                var host = new GameObject(HostPrefix + name);
                host.transform.SetParent(root.transform, false);
                host.transform.SetPositionAndRotation(frame.position, frame.rotation);
                return host;
            }

            var engage = Host("E").AddComponent<CVRSpawnableTrigger>();
            engage.areaSize = box * 0.5f;
            engage.useAdvancedTrigger = true;
            engage.allowedTypes = SocketTypes;
            engage.stayTasks.Add(new CVRSpawnableTriggerTaskStay
            {
                settingIndex = slot["E"],
                updateMethod = CVRSpawnableTriggerTaskStay.UpdateMethod.SetFromDistance,
                minValue = 0f, maxValue = 1f,
            });
            engage.exitTasks.Add(new CVRSpawnableTriggerTask
            {
                settingIndex = slot["E"], settingValue = 0f,
                updateMethod = CVRSpawnableTriggerTask.UpdateMethod.Override,
            });

            var hole = Host("H").AddComponent<CVRSpawnableTrigger>();
            hole.areaSize = box * 0.5f;
            hole.useAdvancedTrigger = true;
            hole.allowedTypes = HoleTypes;
            hole.enterTasks.Add(new CVRSpawnableTriggerTask
            {
                settingIndex = slot["H"], settingValue = 1f,
                updateMethod = CVRSpawnableTriggerTask.UpdateMethod.Override,
            });
            hole.exitTasks.Add(new CVRSpawnableTriggerTask
            {
                settingIndex = slot["H"], settingValue = 0f,
                updateMethod = CVRSpawnableTriggerTask.UpdateMethod.Override,
            });

            var axes = new[]
            {
                ("X", SocketTypes, CVRSpawnableTrigger.SampleDirection.XPositive),
                ("Y", SocketTypes, CVRSpawnableTrigger.SampleDirection.YPositive),
                ("Z", SocketTypes, CVRSpawnableTrigger.SampleDirection.ZPositive),
                ("FX", FrontTypes, CVRSpawnableTrigger.SampleDirection.XPositive),
                ("FY", FrontTypes, CVRSpawnableTrigger.SampleDirection.YPositive),
                ("FZ", FrontTypes, CVRSpawnableTrigger.SampleDirection.ZPositive),
            };
            foreach (var (name, types, direction) in axes)
            {
                var axis = Host(name).AddComponent<CVRSpawnableTrigger>();
                axis.areaSize = box;
                axis.sampleDirection = direction;
                axis.useAdvancedTrigger = true;
                axis.allowedTypes = types;
                axis.stayTasks.Add(new CVRSpawnableTriggerTaskStay
                {
                    settingIndex = slot[name],
                    updateMethod = CVRSpawnableTriggerTaskStay.UpdateMethod.SetFromPosition,
                    minValue = 0f, maxValue = 1f,
                });
            }
        }

        // One layer per value: a two-clip blend tree, 0 and 1, blended by
        // the parameter. Everything is embedded by walking the layer. Into
        // the prop's own controller when it has one; else the channel
        // controller, reused across runs rather than numbered anew.
        static AnimatorController BuildController(string dir, string clipPath, System.Type rendererType,
            AnimatorController into)
        {
            var controller = into;
            if (controller == null)
            {
                string path = dir + "/YAPS Prop Channel.controller";
                controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (controller == null)
                {
                    controller = AnimatorController.CreateAnimatorControllerAtPath(path);
                    for (int i = controller.layers.Length - 1; i >= 0; i--) controller.RemoveLayer(i);
                }
            }
            StripChannel(controller);

            foreach (var (value, property) in Channel)
            {
                controller.AddParameter(value, AnimatorControllerParameterType.Float);
                var tree = new BlendTree
                {
                    name = value,
                    blendType = BlendTreeType.Simple1D,
                    blendParameter = value,
                    useAutomaticThresholds = false,
                    hideFlags = HideFlags.HideInHierarchy,
                };
                tree.AddChild(PropertyClip(clipPath, rendererType, property, 0f), 0f);
                tree.AddChild(PropertyClip(clipPath, rendererType, property, 1f), 1f);

                var machine = new AnimatorStateMachine { name = value, hideFlags = HideFlags.HideInHierarchy };
                var state = machine.AddState("Blend Tree");
                state.writeDefaultValues = true;
                state.motion = tree;
                machine.defaultState = state;

                var layer = new AnimatorControllerLayer { name = value, defaultWeight = 1f, stateMachine = machine };
                var layers = controller.layers.ToList();
                layers.Add(layer);
                controller.layers = layers.ToArray();
                AnimatorAssetSaver.EmbedLayer(layer, controller);
            }
            AssetDatabase.SaveAssets();
            return controller;
        }

        static AnimationClip PropertyClip(string path, System.Type rendererType, string property, float value)
        {
            var clip = new AnimationClip { name = property + " " + value };
            clip.SetCurve(path, rendererType, property, AnimationCurve.Constant(0f, 1f / 60f, value));
            return clip;
        }

        static string Sanitise(string s)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Trim();
        }
    }
}
#endif
