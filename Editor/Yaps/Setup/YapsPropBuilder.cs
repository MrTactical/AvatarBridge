// Turns an object carrying a YAPS plug or socket into a ChilloutVR prop:
// spawnable, pickup and a collider to grab by. Takes out the contact
// channel an older build added, which plugs no longer read.
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
        const string HostPrefix = "YAPS Channel ";

        // What an older build's channel named its values, layers and
        // parameters, and the material property each drove.
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
            // Stamped so a prop built months ago can say which version
            // made it. Nothing revisits a prop, so every fix reaches new
            // ones and no existing one, and the two look identical.
            foreach (var s in root.GetComponentsInChildren<YapsSocket>(true))
            {
                s.builtBy = BridgeDefines.Version;
                EditorUtility.SetDirty(s);
            }
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

            // Transform move mode: physics only adds drift. Theft stays
            // allowed, or a socket's owner keeps the prop until respawn.
            bool fresh = root.GetComponent<CVRPickupObject>() == null;
            var pickup = root.GetComponent<CVRPickupObject>();
            if (pickup == null) pickup = Undo.AddComponent<CVRPickupObject>(root);
            pickup.moveMode = CVRPickupObject.MoveMode.Transform;
            pickup.maximumGrabDistance = 8f;
            // A user who ticked it themselves keeps their answer.
            if (fresh) pickup.disallowTheft = false;
            else if (pickup.disallowTheft)
                o.Notes.Add("Disallow Theft is on. Earlier builds set it, and on a prop with a contact channel it " +
                            "means whoever last had the prop in a socket keeps it: nobody else can pick it up until " +
                            "the prop is respawned. Untick it on the CVR Pickup Object unless you want that.");

            var spawnable = root.GetComponent<CVRSpawnable>();
            if (spawnable == null) spawnable = Undo.AddComponent<CVRSpawnable>(root);
            spawnable.spawnHeight = 1.2f;

            // On the root: the pickup cannot see a child's collider.
            if (root.GetComponent<Collider>() == null) AddCollider(root, plug, o);

            // No channel. The plug's shader stopped reading it, so one an
            // older build added only spends synced values and ownership.
            if (plug != null && HasChannel(root))
            {
                RemoveChannel(root);
                o.Notes.Add("Its old contact channel was taken out: plugs find sockets through the screen atlas " +
                            "and marker lights now, and the channel only spent synced values and handed the prop " +
                            "to whoever's socket touched it.");
            }
            else if (plug != null)
            {
                o.Notes.Add("The prop finds sockets through the screen atlas, with marker lights where the atlas " +
                            "cannot answer. Every client works those out for itself, so nobody fights over who owns it.");
            }
            if (socket != null && plug == null)
            {
                o.Notes.Add("A socket prop: found by plugs through its markers, no channel needed.");
            }

            o.Ok = true;
            o.Message = $"\"{root.name}\" is a prop: spawnable, pickup, collider.";
            return o;
        }

        // Does this prop carry the channel already: one of its hosts is here.
        public static bool HasChannel(GameObject root)
        {
            if (root == null) return false;
            for (int i = 0; i < root.transform.childCount; i++)
                if (root.transform.GetChild(i).name.StartsWith(HostPrefix)) return true;
            return false;
        }

        // And off again, for a prop that fights over ownership in use.
        public static Outcome DropChannel(GameObject root)
        {
            var o = new Outcome();
            if (root == null || !HasChannel(root)) { o.Message = "That prop has no contact channel."; return o; }
            Undo.RegisterFullObjectHierarchyUndo(root, "YAPS channel");
            RemoveChannel(root);
            o.Ok = true;
            o.Message = $"\"{root.name}\" lost its contact channel; it finds sockets by their marker lights now, " +
                        "which every client works out for itself, so nobody takes it off anyone.";
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

        // On the root, or no hand can find the pickup. A trigger, or it
        // shoves people. Capsule when the plug lines up, else a box.
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
            // The material still says its socket arrives in channel space,
            // and nothing writes it any more. Say what is true.
            var dropped = root.GetComponentInChildren<YapsPlug>(true);
            var droppedMaterial = dropped != null ? BakedMaterial(dropped) : null;
            if (droppedMaterial != null)
            {
                droppedMaterial.SetFloat("_YAPS_ChannelSpace", 0f);
                EditorUtility.SetDirty(droppedMaterial);
            }
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
    }
}
#endif
