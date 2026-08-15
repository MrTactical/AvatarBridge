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
// READ ON A LATER FRAME. The first version set the parameters, called
// Animator.Update and read the material in the same call stack, which cannot
// distinguish "the animator never writes this" from "the animator had not
// written it YET". Both report zeros. It now sets the parameters, lets the
// player loop run, and reports on a later tick — and it says which of the two
// it saw, because that difference is the whole question.
//
// Press Play, select the plug prop (or leave nothing selected and it will
// find it), then run the menu item.
#if UNITY_EDITOR && CVR_CCK_EXISTS
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
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

        // How many editor ticks to wait before believing a zero.
        const int SettleTicks = 8;

        static MeshRenderer _renderer;
        static Animator _animator;
        static Vector3 _pos, _front;
        static float _length, _extent;
        static int _ticks;

        [MenuItem("AvatarBridge/Spike/Probe plug channel (Play mode)")]
        public static void Probe()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[YAPS] Enter Play mode first. A blend tree only evaluates while " +
                               "the game is running, and this reads what the animator produced.");
                return;
            }

            _renderer = FindPlug(out _animator, out string trouble);
            if (_renderer == null)
            {
                Debug.LogError("[YAPS] " + trouble);
                return;
            }

            // sharedMaterial, NEVER .material. Touching .material
            // instantiates a copy, so the probe would be creating the very
            // divergence it then reports — and once instantiated,
            // sharedMaterial returns the instance too, which makes the
            // comparison between them meaningless rather than merely wrong.
            var material = _renderer.sharedMaterial;
            _length = material.HasProperty("_YAPS_Length") ? material.GetFloat("_YAPS_Length") : 0f;
            _extent = material.HasProperty("_YAPS_ChannelExtents")
                ? material.GetVector("_YAPS_ChannelExtents").x : 0f;
            if (_length <= 0f || _extent <= 0f)
            {
                Debug.LogError($"[YAPS] The plug material is not baked: length {_length}, " +
                               $"channel extents {_extent}. Rebuild the props.");
                return;
            }

            float expectedExtent = _length * BoxLengths;
            if (Mathf.Abs(_extent - expectedExtent) > 0.001f)
            {
                Debug.LogError($"[YAPS] The trigger box and the shader disagree about the " +
                               $"channel's size: the material says {_extent:0.0000} and a " +
                               $"{_length:0.0000} m plug at {BoxLengths} lengths wants " +
                               $"{expectedExtent:0.0000}.");
            }

            // Contacts hand over a position in the receiver's own frame,
            // normalised across the box to 0..1. Build the same numbers the
            // client would have written for a socket at the chosen spot.
            Vector3 at = new Vector3(SideOffset, 0f, _length * DepthFraction);
            _pos = Normalise(at, _extent);
            _front = Normalise(at + new Vector3(0f, 0f, 0.01f), _extent);

            var wrote = Values();
            var have = _animator.parameters.Select(p => p.name).ToHashSet();
            var missing = wrote.Where(w => !have.Contains(w.Name)).Select(w => w.Name).ToList();
            if (missing.Count > 0)
            {
                Debug.LogError("[YAPS] The controller has no parameter named " +
                               string.Join(", ", missing) + ". The channel cannot be driven at " +
                               "all, by contacts or otherwise — this is the bug, and it is ours.");
                return;
            }

            foreach (var (name, value) in wrote) _animator.SetFloat(name, value);

            // Let the player loop actually run. Reading now would be reading
            // before the animator has had a frame in which to apply anything.
            _ticks = 0;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Debug.Log("[YAPS] Parameters written. Waiting for the animator to apply them, then " +
                      "reporting. Watch the plug.");
        }

        static (string Name, float Value)[] Values() => new[]
        {
            ("E", 1f), ("H", 0f),
            ("X", _pos.x), ("Y", _pos.y), ("Z", _pos.z),
            ("FX", _front.x), ("FY", _front.y), ("FZ", _front.z),
        };

        static void Tick()
        {
            if (!Application.isPlaying || _renderer == null || _animator == null)
            {
                EditorApplication.update -= Tick;
                return;
            }

            // Re-asserted every tick. Nothing else writes these in the editor,
            // but a state with Write Defaults on will happily restore them the
            // moment it decides nothing is driving them.
            foreach (var (name, value) in Values()) _animator.SetFloat(name, value);

            // Report once settled, then KEEP HOLDING until play mode ends, so
            // the bend can be looked at rather than glimpsed. Run the menu
            // item again to re-report.
            if (++_ticks == SettleTicks) Report();
        }

        static void Report()
        {
            var shared = _renderer.sharedMaterial;

            // THE PROPERTY BLOCK IS WHERE ANIMATED VALUES LIVE. Unity
            // applies an animated material property through the renderer's
            // MaterialPropertyBlock, never through the material asset —
            // deliberately, so playing an animation cannot dirty the asset.
            // The material's own values are therefore the one place an
            // animated value is GUARANTEED never to appear, and the first
            // three versions of this probe read exactly that place and
            // declared a working channel dead. The shader reads the block at
            // draw time, so the block is the truth about what renders.
            var block = new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(block);

            Vector4 flags = block.GetVector("_YAPS_SocketFlags");
            Vector4 read = block.GetVector("_YAPS_SocketPos");
            Vector4 readFront = block.GetVector("_YAPS_SocketFront");

            var report = new System.Text.StringBuilder();
            report.AppendLine("[YAPS] Channel probe on \"" + _renderer.name + "\", after " +
                              SettleTicks + " ticks.");
            report.AppendLine($"  wrote    E 1  H 0   pos {Fmt(_pos)}   front {Fmt(_front)}");
            report.AppendLine($"  the renderer's property block — what the shader actually gets:");
            report.AppendLine($"    block {(block.isEmpty ? "EMPTY" : "populated")}");
            report.AppendLine($"    _YAPS_SocketFlags  {Fmt(flags)}   (x is engagement)");
            report.AppendLine($"    _YAPS_SocketPos    {Fmt(read)}");
            report.AppendLine($"    _YAPS_SocketFront  {Fmt(readFront)}");
            report.AppendLine($"  the material asset (animated values never appear here, by design):");
            report.AppendLine($"    _YAPS_SocketFlags  {Fmt(shared.GetVector("_YAPS_SocketFlags"))}");

            // --- where the chain actually stands -------------------------
            report.AppendLine("  animator:");
            report.AppendLine($"    object active {_renderer.gameObject.activeInHierarchy}, " +
                              $"animator enabled {_animator.enabled}, speed {_animator.speed}, " +
                              $"culling {_animator.cullingMode}");
            report.AppendLine($"    parameter E reads back {_animator.GetFloat("E"):0.000}, " +
                              $"Z reads back {_animator.GetFloat("Z"):0.000}");
            report.AppendLine($"    layers {_animator.layerCount}");

            var controller = _animator.runtimeAnimatorController as AnimatorController;
            for (int i = 0; i < _animator.layerCount; i++)
            {
                var info = _animator.GetCurrentAnimatorStateInfo(i);
                string layerName = _animator.GetLayerName(i);
                int states = controller != null && i < controller.layers.Length
                             && controller.layers[i].stateMachine != null
                    ? controller.layers[i].stateMachine.states.Length : -1;
                report.AppendLine($"      [{i}] {layerName}: weight {_animator.GetLayerWeight(i):0.00}, " +
                                  $"states {states}, playing hash {info.shortNameHash}, " +
                                  $"length {info.length:0.000}");
            }

            // DOES THE CURVE EVEN RESOLVE? Everything above says the animator
            // is healthy — layers weighted, states playing, parameters
            // correct — so what is left is whether the clip's binding finds
            // anything on this hierarchy. A binding whose path, component
            // type or property name misses resolves to nothing and is
            // discarded in silence, which looks exactly like an animator that
            // is not running.
            //
            // AnimationUtility answers it outright against the live
            // GameObject, which is worth more than reading the path and the
            // classID out of the asset and believing they match the scene.
            report.AppendLine("  clip bindings, resolved against the animator's own hierarchy:");
            var root = _animator.gameObject;
            report.AppendLine($"    animator on \"{root.name}\", renderer at \"" +
                              Path(root.transform, _renderer.transform) + "\", type " +
                              _renderer.GetType().Name);

            int resolved = 0, unresolved = 0;
            if (controller != null)
            {
                foreach (var clip in controller.animationClips.Distinct())
                {
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    {
                        bool ok = AnimationUtility.GetFloatValue(root, binding, out float live);
                        if (ok) resolved++; else unresolved++;
                        if (unresolved <= 3 || resolved <= 1)
                        {
                            report.AppendLine($"    {(ok ? "OK  " : "MISS")} path \"{binding.path}\" " +
                                              $"{binding.type.Name}.{binding.propertyName}" +
                                              (ok ? $" = {live:0.000}" : ""));
                        }
                    }
                }
            }
            report.AppendLine($"    {resolved} binding(s) resolve, {unresolved} do not.");

            bool engagementArrived = Mathf.Abs(flags.x - 1f) < 0.01f;
            bool positionArrived = Approximately(read, _pos);

            if (!engagementArrived && !positionArrived)
            {
                report.AppendLine(resolved > 0 && unresolved == 0
                    ? "  VERDICT: the bindings resolve but the property block is empty — the " +
                      "animator is evaluating and Unity is not applying the result to the " +
                      "renderer. That is a Unity-side application failure, not a wiring one."
                    : "  VERDICT: nothing reached the renderer, and it has had frames in which " +
                      "to. Contacts are exonerated — check the layer table and the binding " +
                      "resolution above; each zero names a different cause.");
            }
            else if (!positionArrived)
            {
                report.AppendLine("  VERDICT: engagement arrived, position did not. The shader " +
                                  "then measures a gap to the box centre, over a plug length " +
                                  "away, and correctly reports NOT ENGAGED.");
            }
            else
            {
                Vector3 offset = new Vector3(read.x * 2f - 1f, read.y * 2f - 1f, read.z * 2f - 1f) * _extent;
                float gap = offset.magnitude;
                float engaged = 1f - Smoothstep(_length, _length * 1.6f, gap);
                report.AppendLine($"  the shader will measure a gap of {gap:0.0000} m against a " +
                                  $"{_length:0.0000} m plug and engage at {engaged:0.000}.");
                report.AppendLine(engaged > 0.01f
                    ? "  VERDICT: the channel is INTACT end to end. The values are HELD until " +
                      "play mode ends, so the plug is bent towards +X right now and stays that " +
                      "way — look at it. Everything except contact delivery works, and contact " +
                      "delivery only exists in game."
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

        // The path a clip binding has to match, built the way Unity builds
        // it: names joined from the animator down, and empty for the
        // animator's own object.
        static string Path(Transform root, Transform of)
        {
            if (of == root) return "";
            string path = of.name;
            for (var t = of.parent; t != null && t != root; t = t.parent)
            {
                path = t.name + "/" + path;
            }
            return path;
        }

        static string Fmt(Vector3 v) => $"({v.x:0.000}, {v.y:0.000}, {v.z:0.000})";
        static string Fmt(Vector4 v) => $"({v.x:0.000}, {v.y:0.000}, {v.z:0.000}, {v.w:0.000})";
    }
}
#endif
