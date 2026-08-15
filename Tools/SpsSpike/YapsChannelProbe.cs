// Drives the plug prop's contact channel BY HAND, in the editor, with no
// contacts involved at all.
//
// WHY THIS EXISTS
//
// The channel has three links in series and a failure anywhere in it looks
// identical from the outside — a plug that does not bend:
//
//   1. contacts       socket pointer -> trigger -> CVRSpawnableValue
//   2. the animator   value -> parameter -> blend tree -> material property
//   3. the shader     material property -> reconstructed socket -> deform
//
// Link 1 cannot run in the editor: there is no client, so no ContactManager,
// no ContactReceiver, nothing. That is why a contact-only socket reads black
// here and always will, and why an editor reading has never been evidence
// either way.
//
// Links 2 and 3 need no client whatsoever. This writes the parameters the
// contacts WOULD have written and then reads back what the material actually
// holds — so a bend here means the only thing left that can be broken is
// contact delivery, and no bend here means contacts were never the problem.
//
// Press Play, select the plug prop (or leave nothing selected and it will
// find it), then run the menu item.
#if UNITY_EDITOR && CVR_CCK_EXISTS
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Spike
{
    public static class YapsChannelProbe
    {
        // Matches YapsPropRig: the trigger box is this many plug lengths on
        // each side, and the shader rebuilds the position against the same
        // figure. If the two ever disagree the socket lands in the wrong
        // place, so they are asserted rather than assumed.
        const float BoxLengths = 1.75f;

        // Where to pretend the socket is: six centimetres to the side, sixty
        // percent of the way up. On the axis would be a poor test — a socket
        // dead ahead barely bends anything, so a broken channel and a working
        // one would look the same.
        const float SideOffset = 0.06f;
        const float DepthFraction = 0.6f;

        [MenuItem("Tools/Avatar Bridge/Spike/Probe plug channel (Play mode)")]
        public static void Probe()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[YAPS] Enter Play mode first. A blend tree only evaluates while " +
                               "the game is running, and this reads what the animator produced.");
                return;
            }

            var renderer = FindPlug(out Animator animator, out string trouble);
            if (renderer == null)
            {
                Debug.LogError("[YAPS] " + trouble);
                return;
            }

            // The instance, not the shared asset. An animator writing a
            // material property instantiates the material, so the shared one
            // keeps its authoring values forever and reading it would report
            // a dead channel even on a healthy prop.
            var material = renderer.material;

            float length = material.HasProperty("_YAPS_Length")
                ? material.GetFloat("_YAPS_Length") : 0f;
            float extent = material.HasProperty("_YAPS_ChannelExtents")
                ? material.GetVector("_YAPS_ChannelExtents").x : 0f;
            if (length <= 0f || extent <= 0f)
            {
                Debug.LogError($"[YAPS] The plug material is not baked: length {length}, " +
                               $"channel extents {extent}. Rebuild the props.");
                return;
            }

            float expectedExtent = length * BoxLengths;
            if (Mathf.Abs(extent - expectedExtent) > 0.001f)
            {
                Debug.LogError($"[YAPS] The trigger box and the shader disagree about the " +
                               $"channel's size: the material says {extent:0.0000} and a " +
                               $"{length:0.0000} m plug at {BoxLengths} lengths wants " +
                               $"{expectedExtent:0.0000}. Every socket will land in the wrong " +
                               $"place by that ratio.");
            }

            // Contacts hand over a position in the receiver's own frame,
            // normalised across the box to 0..1. Build the same numbers the
            // client would have written for a socket at the chosen spot.
            Vector3 at = new Vector3(SideOffset, 0f, length * DepthFraction);
            Vector3 front = at + new Vector3(0f, 0f, 0.01f);   // both systems put it ~1 cm on
            Vector3 pos = Normalise(at, extent);
            Vector3 fpos = Normalise(front, extent);

            var wrote = new (string Name, float Value)[]
            {
                ("E", 1f), ("H", 0f),
                ("X", pos.x), ("Y", pos.y), ("Z", pos.z),
                ("FX", fpos.x), ("FY", fpos.y), ("FZ", fpos.z),
            };

            var have = animator.parameters.Select(p => p.name).ToHashSet();
            var missing = wrote.Where(w => !have.Contains(w.Name)).Select(w => w.Name).ToList();
            if (missing.Count > 0)
            {
                Debug.LogError("[YAPS] The controller has no parameter named " +
                               string.Join(", ", missing) + ". The channel cannot be driven at " +
                               "all, by contacts or otherwise — this is the bug, and it is ours.");
                return;
            }

            foreach (var (name, value) in wrote) animator.SetFloat(name, value);

            // One frame for the blend trees to run and write the material.
            animator.Update(Time.deltaTime > 0 ? Time.deltaTime : 0.02f);

            Vector4 flags = material.GetVector("_YAPS_SocketFlags");
            Vector4 read = material.GetVector("_YAPS_SocketPos");
            Vector4 readFront = material.GetVector("_YAPS_SocketFront");
            float channelSpace = material.GetFloat("_YAPS_ChannelSpace");

            var report = new System.Text.StringBuilder();
            report.AppendLine("[YAPS] Channel probe on \"" + renderer.name + "\".");
            report.AppendLine($"  wrote   E 1  H 0   pos {Fmt(pos)}   front {Fmt(fpos)}");
            report.AppendLine($"  material reads:");
            report.AppendLine($"    _YAPS_SocketFlags  {Fmt(flags)}   (x is engagement)");
            report.AppendLine($"    _YAPS_SocketPos    {Fmt(read)}");
            report.AppendLine($"    _YAPS_SocketFront  {Fmt(readFront)}");
            report.AppendLine($"    _YAPS_ChannelSpace {channelSpace}");

            bool engagementArrived = Mathf.Abs(flags.x - 1f) < 0.01f;
            bool positionArrived = Approximately(read, pos);
            bool frontArrived = Approximately(readFront, fpos);

            if (channelSpace < 0.5f)
            {
                report.AppendLine("  VERDICT: _YAPS_ChannelSpace is 0, so the shader treats the " +
                                  "channel's numbers as a WORLD position and reaches for the " +
                                  "world origin. The prop must set it to 1.");
            }
            else if (!engagementArrived && !positionArrived)
            {
                report.AppendLine("  VERDICT: nothing reached the material. The animator layers " +
                                  "are not writing it — contacts are exonerated, the break is " +
                                  "between the parameter and the material.");
            }
            else if (!engagementArrived)
            {
                report.AppendLine("  VERDICT: the position arrived and the ENGAGEMENT did not. " +
                                  "The E layer alone is broken, and with engagement at zero the " +
                                  "shader discards a position it actually has.");
            }
            else if (!positionArrived)
            {
                report.AppendLine("  VERDICT: engagement arrived and the POSITION did not. The " +
                                  "shader then measures a gap to a socket at the box centre, " +
                                  "which is over a plug length away, and correctly reports NOT " +
                                  "ENGAGED. This is the failure that looks exactly like a dead " +
                                  "channel.");
            }
            else
            {
                // Everything the channel carries is in the material, so what
                // remains is what the shader does with it. Recompute the
                // engagement curve here, because a channel that delivers
                // perfectly and then remaps itself to zero is still a plug
                // that does not move.
                Vector3 offset = new Vector3(read.x * 2f - 1f, read.y * 2f - 1f, read.z * 2f - 1f) * extent;
                float gap = offset.magnitude;
                float engaged = 1f - Smoothstep(length, length * 1.6f, gap);
                report.AppendLine($"  the shader will measure a gap of {gap:0.0000} m against a " +
                                  $"{length:0.0000} m plug and engage at {engaged:0.000}.");
                report.AppendLine(frontArrived
                    ? "  the front arrived too, so the plug has an axis to thread rather than a " +
                      "point to reach."
                    : "  the front did NOT arrive: the plug will aim at the socket instead of " +
                      "threading it, which reads as a weaker, blunter bend.");
                report.AppendLine(engaged > 0.01f
                    ? "  VERDICT: the channel is INTACT end to end and the plug should be visibly " +
                      "bent right now. If it is straight on screen the fault is in the deform, " +
                      "not the channel. If it is bent, then everything except contact delivery " +
                      "works and the remaining suspect is in game only."
                    : "  VERDICT: every value arrived and the shader's own curve still collapses " +
                      "to zero. The remap is the bug.");
            }

            Debug.Log(report.ToString());
        }

        // The client's normalisation, in reverse: a position in the
        // receiver's frame, divided by the box half-extent to -1..1, then
        // mapped to 0..1 the way SetFromPosition does.
        static Vector3 Normalise(Vector3 at, float extent)
        {
            return new Vector3(
                (at.x / extent + 1f) * 0.5f,
                (at.y / extent + 1f) * 0.5f,
                (at.z / extent + 1f) * 0.5f);
        }

        static MeshRenderer FindPlug(out Animator animator, out string trouble)
        {
            animator = null;
            trouble = null;

            var selected = Selection.activeGameObject;
            var candidates = selected != null
                ? selected.GetComponentsInChildren<MeshRenderer>(true)
                : Object.FindObjectsOfType<MeshRenderer>(true);

            var plug = candidates.FirstOrDefault(r =>
                r.sharedMaterial != null && r.sharedMaterial.HasProperty("_YAPS_ChannelExtents"));
            if (plug == null)
            {
                trouble = selected != null
                    ? "\"" + selected.name + "\" carries no YAPS plug material. Select the plug " +
                      "prop, or select nothing and the whole scene is searched."
                    : "No YAPS plug found in the scene. Spawn the plug prop first.";
                return null;
            }

            animator = plug.GetComponentInParent<Animator>();
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                trouble = "\"" + plug.name + "\" has no Animator with a controller above it, so " +
                          "the prop has no channel at all. Rebuild the props.";
                return null;
            }
            return plug;
        }

        static bool Approximately(Vector4 a, Vector3 b)
        {
            return Mathf.Abs(a.x - b.x) < 0.02f
                && Mathf.Abs(a.y - b.y) < 0.02f
                && Mathf.Abs(a.z - b.z) < 0.02f;
        }

        static float Smoothstep(float from, float to, float x)
        {
            float t = Mathf.Clamp01((x - from) / Mathf.Max(to - from, 1e-6f));
            return t * t * (3f - 2f * t);
        }

        static string Fmt(Vector3 v) => $"({v.x:0.000}, {v.y:0.000}, {v.z:0.000})";
        static string Fmt(Vector4 v) => $"({v.x:0.000}, {v.y:0.000}, {v.z:0.000}, {v.w:0.000})";
    }
}
#endif
