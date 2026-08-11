#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using ABI.CCK.Components;
using VRC.SDK3.Avatars.Components;

namespace AvatarBridge.Regression
{
    // Drives every toggle a converted avatar exposes and reports
    // anything that does not come back. Reading the controller is not
    // enough; the sweep moves each parameter and looks at the avatar.
    //
    // Method:
    //   Each parameter measures against the state settled immediately
    //   before it moves, never a global baseline; the controller
    //   legitimately turns things off while settling.
    //   Nothing is put back by hand; the animator owns these objects.
    //   Game-driven parameters and "#" locals are left alone.
    //   Four kinds of state are watched: object activity, blendshape,
    //   material, Renderer.enabled.
    //
    // Edit mode, driving Animator.Update by hand; a runtime
    // MonoBehaviour cannot live in an Editor folder. Edit-mode
    // driving has reproduced a real in-game fault, but the subsets
    // differ, so a clean result means "found nothing", not "nothing
    // is wrong".
    //
    // The on direction is checked too: while a parameter is held
    // away from rest, every constant claim the playing clips make
    // (float curves and material swaps) is compared against the
    // scene. A masked layer dropping slots, or a write that never
    // lands, reports as NOT APPLIED. Intent stays invisible; only
    // claims the controller itself makes can be verified.
    //
    // This moves things in the open scene and does not put them back.
    // Reload the scene before doing anything else with it.
    public static class ToggleSweep
    {
        const int SettleFrames = 12;

        const int Warmup = 40;

        const float Epsilon = 0.01f;

        static readonly HashSet<string> GameOwned = new HashSet<string>
        {
            "MovementX", "MovementY", "Movement", "GestureLeft", "GestureRight",
            "GestureLeftWeight", "GestureRightWeight", "Grounded", "Sitting", "Crouching",
            "Prone", "Flying", "Swimming", "AFK", "Emote", "CancelEmote", "Toggle", "VRMode",
            "MuteSelf", "Voice", "IsLocal", "Zoom", "Blend", "Upright", "AngularY", "Velocity",
            "VelocityX", "VelocityY", "VelocityZ", "TrackingType", "Seated", "Earmuffs",
            "ScaleFactor", "ScaleModified", "Output", "Height",
        };

        [MenuItem("Tools/AvatarBridge Dev/Sweep every toggle for stuck objects")]
        public static void Run()
        {
            var avatar = Resolve();
            if (avatar == null)
            {
                Debug.LogError("[Sweep] No converted avatar found. Convert one first, or select it.");
                return;
            }
            Sweep(avatar);
        }

        public static void RunBatch()
        {
            string scene = System.Environment.GetEnvironmentVariable("AVATARBRIDGE_SWEEP_SCENE");
            if (string.IsNullOrEmpty(scene))
            {
                Debug.LogError("[Sweep] set AVATARBRIDGE_SWEEP_SCENE to the scene to convert and sweep.");
                if (Application.isBatchMode) EditorApplication.Exit(2);
                return;
            }

            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            var source = Object.FindObjectsOfType<VRCAvatarDescriptor>(true).FirstOrDefault();
            if (source == null)
            {
                Debug.LogError($"[Sweep] no VRCAvatarDescriptor in {scene}");
                if (Application.isBatchMode) EditorApplication.Exit(2);
                return;
            }

            var report = BridgeConverter.Convert(source, new BridgeSettings());
            int stuck = report.ConvertedRoot != null ? Sweep(report.ConvertedRoot) : -1;
            if (Application.isBatchMode) EditorApplication.Exit(stuck > 0 ? 1 : 0);
        }

        public static int Sweep(GameObject root)
        {
            var animator = root.GetComponent<Animator>() ?? root.GetComponentInChildren<Animator>(true);
            // The CCK wraps the generated controller in an override controller, sometimes twice.
            var runtime = animator != null ? animator.runtimeAnimatorController : null;
            while (runtime is AnimatorOverrideController over)
            {
                runtime = over.runtimeAnimatorController;
            }
            if (!(runtime is AnimatorController controller))
            {
                Debug.LogError("[Sweep] the avatar's animator has no AnimatorController to read.");
                return -1;
            }

            var parameters = controller.parameters
                .Where(p => p.type != AnimatorControllerParameterType.Trigger)
                .Select(p => p.name)
                .Where(n => !n.StartsWith("#") && !GameOwned.Contains(n))
                .Distinct()
                .ToArray();
            if (parameters.Length == 0)
            {
                Debug.Log("[Sweep] no user-facing parameters to sweep.");
                return 0;
            }

            var watch = new Watchlist(root);
            // Without this an off-screen avatar simply is not evaluated, and every reading is the
            // pose it was built in.
            var culling = animator.cullingMode;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            Settle(animator, Warmup);

            // What the animator has actually made of the physics once it has settled, which is a
            // different question from what the prefab was saved as. A cloth saved disabled whose
            // toggle defaults ON should be running by now; one that is still off after the
            // animator has had its say is off in game too.
            var clothStates = new List<string>();
            foreach (var behaviour in root.GetComponentsInChildren<Behaviour>(true))
            {
                if (behaviour != null && behaviour.GetType().Name == "MagicaCloth")
                {
                    clothStates.Add($"{behaviour.name}={(behaviour.isActiveAndEnabled ? "on" : "OFF")}");
                }
            }
            if (clothStates.Count > 0)
            {
                Debug.Log($"[Sweep] cloth after settling ({clothStates.Count}): {string.Join(", ", clothStates)}");
            }

            Debug.Log($"[Sweep] \"{root.name}\": {parameters.Length} parameter(s) over {watch.Count} watched " +
                      "propert(ies) — object activity, renderer enable, blendshape weights and material " +
                      "slots. This moves things in the open scene and does not put them back — reload it " +
                      "afterwards.");

            var stuck = new List<string>();
            var notApplied = new List<string>();
            // How many parameters visibly did anything when driven.
            // Distinguishes "every toggle came back" from "nothing was
            // ever moved"; the two must not report identically.
            int responded = 0;

            // Mismatches already present at rest belong to the rest
            // pose, not to whichever parameter is swept first.
            var knownMismatches = new HashSet<string>(VerifyApplied(animator, controller, root));
            foreach (var finding in knownMismatches)
            {
                Debug.LogWarning($"[Sweep] NOT APPLIED at rest: {finding}");
            }

            foreach (var parameter in parameters)
            {
                float original = DefaultOf(controller, parameter);
                var before = watch.Capture();

                // Away from rest, not always to 1. Default-true toggles
                // are ordinary; driving one to the value it already
                // holds tests that 1 equals 1.
                float away = original > 0.5f ? 0f : 1f;
                Drive(controller, animator, parameter, away);
                Settle(animator, SettleFrames);
                var whileOn = watch.Differences(before);
                bool moves = whileOn.Count > 0;
                if (moves)
                {
                    // What a toggle does, not only what it fails to put
                    // back. Needed to answer "does this toggle reach
                    // the converted physics at all".
                    Debug.Log($"[Sweep] MOVES \"{parameter}\": {string.Join("; ", whileOn.Take(6))}" +
                              (whileOn.Count > 6 ? $" (+{whileOn.Count - 6} more)" : ""));
                }

                // What the playing clips claim versus what the scene
                // holds. This is the on-direction check: a masked layer
                // that drops material slots, or a curve that lands on
                // nothing, shows up here as a claim the scene refuses.
                bool claimsFailed = false;
                foreach (var finding in VerifyApplied(animator, controller, root))
                {
                    if (!knownMismatches.Add(finding))
                    {
                        continue;
                    }
                    claimsFailed = true;
                    Debug.LogWarning($"[Sweep] NOT APPLIED while \"{parameter}\" driven: {finding}");
                }
                if (claimsFailed)
                {
                    notApplied.Add(parameter);
                }

                Drive(controller, animator, parameter, original);
                Settle(animator, SettleFrames);

                if (moves)
                {
                    responded++;
                }
                var moved = watch.Differences(before);
                if (moved.Count == 0)
                {
                    continue;
                }
                stuck.Add(parameter);
                Debug.Log($"[Sweep] STUCK \"{parameter}\" ({original} → {away} → {original}): " +
                          string.Join("; ", moved.Take(8)) +
                          (moved.Count > 8 ? $" (+{moved.Count - 8} more)" : ""));
            }

            animator.cullingMode = culling;

            if (responded == 0)
            {
                // Not a clean bill of health; the opposite. Nothing
                // moving at all means nothing was really driven, and
                // every "came back fine" below is vacuous.
                Debug.LogError($"[Sweep] INVALID RUN: none of the {parameters.Length} parameters changed " +
                               "anything even while held on, so nothing was really being driven. Edit-mode " +
                               "Animator.Update is the likely culprit and this result says NOTHING about " +
                               "the avatar. Do not read the line below as a pass.");
            }
            else if (stuck.Count == 0 && notApplied.Count == 0)
            {
                Debug.Log($"[Sweep] found nothing — all {parameters.Length} toggle(s) came back to where " +
                          $"they started, every claim the playing clips made held on the scene, and " +
                          $"{responded} of them demonstrably moved something while on, so the sweep was " +
                          "really driving the avatar. Still not the same as \"nothing is wrong\": " +
                          "anything outside the watchlist is invisible to it.");
            }
            else
            {
                Debug.Log($"[Sweep] {stuck.Count} of {parameters.Length} left something stuck" +
                          (notApplied.Count > 0
                              ? $", {notApplied.Count} made a claim the scene refused ({string.Join(", ", notApplied)})"
                              : "") +
                          $" ({responded} moved something while on)" +
                          (stuck.Count > 0 ? $": {string.Join(", ", stuck)}" : "."));
            }
            return stuck.Count + notApplied.Count;
        }

        // Compares what the currently playing clips assert against what
        // the scene actually holds. Constant curves only; a toggle's
        // claim is a constant. Catches a masked layer dropping material
        // slots, and any curve whose write never lands.
        static List<string> VerifyApplied(Animator animator, AnimatorController controller, GameObject root)
        {
            var findings = new List<string>();
            var claimed = new HashSet<string>();
            var layers = controller.layers;
            int count = Mathf.Min(layers.Length, animator.layerCount);

            // Top-down: where several layers animate one property, the
            // highest live layer owns the result.
            for (int i = count - 1; i >= 0; i--)
            {
                var layer = layers[i];
                if (layer == null || animator.IsInTransition(i))
                {
                    continue;
                }
                float weight = i == 0 ? 1f : animator.GetLayerWeight(i);
                if (weight < 0.5f || layer.blendingMode == AnimatorLayerBlendingMode.Additive)
                {
                    continue;
                }
                foreach (var info in animator.GetCurrentAnimatorClipInfo(i))
                {
                    if (info.clip == null || info.weight < 0.99f)
                    {
                        continue;
                    }
                    foreach (var binding in AnimationUtility.GetCurveBindings(info.clip))
                    {
                        if (binding.type == typeof(Animator))
                        {
                            continue;   // parameters and muscles, not scene properties
                        }
                        string key = binding.path + "|" + binding.type.Name + "|" + binding.propertyName;
                        if (!claimed.Add(key))
                        {
                            continue;
                        }
                        var curve = AnimationUtility.GetEditorCurve(info.clip, binding);
                        if (curve == null || curve.keys.Length == 0)
                        {
                            continue;
                        }
                        float want = curve.keys[0].value;
                        bool constant = true;
                        foreach (var k in curve.keys)
                        {
                            if (Mathf.Abs(k.value - want) > 0.0001f)
                            {
                                constant = false;
                                break;
                            }
                        }
                        if (!constant)
                        {
                            continue;   // animated over time; no single claim to hold
                        }
                        if (!AnimationUtility.GetFloatValue(root, binding, out float live))
                        {
                            continue;   // dead path; the reference audits own that
                        }
                        if (Mathf.Abs(live - want) > 0.05f * Mathf.Max(1f, Mathf.Abs(want)))
                        {
                            findings.Add($"{binding.path} ({binding.propertyName}): clip " +
                                         $"\"{info.clip.name}\" says {want:0.###}, scene reads {live:0.###} " +
                                         $"(layer \"{layer.name}\")");
                        }
                    }
                    foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(info.clip))
                    {
                        string key = binding.path + "|" + binding.type.Name + "|" + binding.propertyName;
                        if (!claimed.Add(key))
                        {
                            continue;
                        }
                        var keys = AnimationUtility.GetObjectReferenceCurve(info.clip, binding);
                        if (keys == null || keys.Length == 0)
                        {
                            continue;
                        }
                        var want = keys[0].value;
                        bool constant = true;
                        foreach (var k in keys)
                        {
                            if (k.value != want)
                            {
                                constant = false;
                                break;
                            }
                        }
                        if (!constant || want == null)
                        {
                            continue;
                        }
                        if (!AnimationUtility.GetObjectReferenceValue(root, binding, out var live))
                        {
                            continue;
                        }
                        if (live != want)
                        {
                            findings.Add($"{binding.path} ({binding.propertyName}): clip " +
                                         $"\"{info.clip.name}\" assigns \"{want.name}\", scene holds " +
                                         $"\"{(live != null ? live.name : "nothing")}\" (layer \"{layer.name}\")");
                        }
                    }
                }
            }
            return findings;
        }

        static void Drive(AnimatorController controller, Animator animator, string name, float value)
        {
            var parameters = controller.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name != name)
                {
                    continue;
                }
                parameters[i].defaultBool = value > 0.5f;
                parameters[i].defaultInt = Mathf.RoundToInt(value);
                parameters[i].defaultFloat = value;
            }
            controller.parameters = parameters;

            var runtime = animator.runtimeAnimatorController;
            animator.runtimeAnimatorController = null;
            animator.runtimeAnimatorController = runtime;
            animator.Rebind();
        }

        static float DefaultOf(AnimatorController controller, string name)
        {
            foreach (var p in controller.parameters)
            {
                if (p.name != name)
                {
                    continue;
                }
                switch (p.type)
                {
                    case AnimatorControllerParameterType.Bool: return p.defaultBool ? 1f : 0f;
                    case AnimatorControllerParameterType.Int: return p.defaultInt;
                    default: return p.defaultFloat;
                }
            }
            return 0f;
        }

        static void Settle(Animator animator, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                animator.Update(1f / 60f);
            }
        }

        // Everything a toggle can plausibly leave behind, addressed
        // once so each sweep is a cheap array read. Labels are built
        // once; they are only wanted when something went wrong.
        sealed class Watchlist
        {
            readonly string[] labels;
            readonly Transform[] objects;
            readonly Renderer[] renderers;
            readonly Behaviour[] behaviours;
            readonly SkinnedMeshRenderer[] skins;
            readonly int blendShapeTotal;

            public int Count => labels.Length;

            public Watchlist(GameObject root)
            {
                objects = root.GetComponentsInChildren<Transform>(true);
                renderers = root.GetComponentsInChildren<Renderer>(true);
                skins = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                // Components switched on and off, which is how converted
                // physics is toggled: the clip drives the cloth's
                // enabled flag, not the object's.
                behaviours = root.GetComponentsInChildren<Behaviour>(true);

                var names = new List<string>();
                foreach (var t in objects)
                {
                    names.Add($"{t.name} active");
                }
                foreach (var b in behaviours)
                {
                    names.Add($"{b.name}.{b.GetType().Name} enabled");
                }
                foreach (var r in renderers)
                {
                    names.Add($"{r.name} renderer");
                    for (int i = 0; i < r.sharedMaterials.Length; i++)
                    {
                        names.Add($"{r.name} material[{i}]");
                    }
                }
                blendShapeTotal = 0;
                foreach (var s in skins)
                {
                    var mesh = s.sharedMesh;
                    int shapes = mesh != null ? mesh.blendShapeCount : 0;
                    for (int i = 0; i < shapes; i++)
                    {
                        names.Add($"{s.name}.{mesh.GetBlendShapeName(i)}");
                        blendShapeTotal++;
                    }
                }
                labels = names.ToArray();
            }

            public Reading Capture()
            {
                var numbers = new float[objects.Length + behaviours.Length + renderers.Length + blendShapeTotal];
                var references = new Object[labels.Length - numbers.Length];
                int n = 0, r = 0;

                foreach (var t in objects)
                {
                    numbers[n++] = t != null && t.gameObject.activeSelf ? 1f : 0f;
                }
                foreach (var b in behaviours)
                {
                    numbers[n++] = b != null && b.enabled ? 1f : 0f;
                }
                foreach (var renderer in renderers)
                {
                    numbers[n++] = renderer != null && renderer.enabled ? 1f : 0f;
                    var mats = renderer != null ? renderer.sharedMaterials : new Material[0];
                    foreach (var m in mats)
                    {
                        references[r++] = m;
                    }
                }
                foreach (var s in skins)
                {
                    var mesh = s != null ? s.sharedMesh : null;
                    int shapes = mesh != null ? mesh.blendShapeCount : 0;
                    for (int i = 0; i < shapes; i++)
                    {
                        numbers[n++] = s.GetBlendShapeWeight(i);
                    }
                }
                return new Reading { Numbers = numbers, References = references };
            }

            public List<string> Differences(Reading before)
            {
                var now = Capture();
                var moved = new List<string>();
                int n = 0, r = 0;

                for (int i = 0; i < labels.Length; i++)
                {
                    bool isReference = labels[i].Contains(" material[");
                    if (isReference)
                    {
                        if (before.References[r] != now.References[r])
                        {
                            string was = before.References[r] != null ? before.References[r].name : "none";
                            string got = now.References[r] != null ? now.References[r].name : "none";
                            moved.Add($"{labels[i]} {was} → {got}");
                        }
                        r++;
                    }
                    else
                    {
                        if (Mathf.Abs(before.Numbers[n] - now.Numbers[n]) > Epsilon)
                        {
                            moved.Add($"{labels[i]} {before.Numbers[n]:0.##} → {now.Numbers[n]:0.##}");
                        }
                        n++;
                    }
                }
                return moved;
            }
        }

        public struct Reading
        {
            public float[] Numbers;
            public Object[] References;
        }

        static GameObject Resolve()
        {
            var selected = Selection.activeGameObject;
            var fromSelection = selected != null ? selected.GetComponentInParent<CVRAvatar>() : null;
            if (fromSelection != null)
            {
                return fromSelection.gameObject;
            }
            foreach (var candidate in Object.FindObjectsOfType<CVRAvatar>(true))
            {
                var animator = candidate.GetComponent<Animator>();
                if (animator != null && animator.runtimeAnimatorController != null)
                {
                    return candidate.gameObject;
                }
            }
            return null;
        }
    }
}
#endif
