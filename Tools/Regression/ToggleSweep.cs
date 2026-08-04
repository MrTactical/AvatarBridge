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
    /// <summary>
    /// Drives every toggle a converted avatar exposes and reports anything that does not come back.
    ///
    /// This exists because reading the controller is not enough. A wardrobe toggle that switches on
    /// and never off again was chased three times through the generated asset — blend tree shapes,
    /// layer indices, Write Defaults, ownership rules — and every static explanation was wrong. The
    /// sweep found the real failures on the first run, in about fifteen minutes, by the crude method
    /// of moving each parameter and looking at the avatar afterwards.
    ///
    /// Method, and each part of it is there because the first version got it wrong:
    ///
    ///   Each parameter is measured against the state settled IMMEDIATELY BEFORE that parameter
    ///   moves, never against one baseline taken at the start. The controller legitimately turns
    ///   things off as it reaches its resting pose, and a global baseline reads all of that as
    ///   damage — the first attempt reported twenty-seven failures, every one of them an artefact.
    ///
    ///   Nothing is ever put back by hand. The animator owns these objects; setting one behind its
    ///   back means the next parameter is measured against a state the animator is still fighting,
    ///   and the contamination spreads down the whole run.
    ///
    ///   Parameters the GAME drives (movement, gestures, AFK…) and local machinery ("#"-prefixed,
    ///   which includes the constant-1 weights a Direct tree needs) are left alone. Sweeping those
    ///   measures ChilloutVR, or breaks the avatar outright.
    ///
    ///   Four kinds of state are watched, not just object activity. The first version only checked
    ///   GameObject.activeSelf and came back clean on an avatar whose toggles were reported broken —
    ///   which proves nothing either way, because a toggle that drives a BLENDSHAPE, swaps a
    ///   material or flips Renderer.enabled was invisible to it. "Toggles something off and not back
    ///   on" describes all four equally.
    ///
    /// Edit mode rather than play mode, driving <c>Animator.Update</c> by hand: the harness lives in
    /// an Editor folder, so a runtime MonoBehaviour cannot ship with it.
    ///
    /// THAT SUBSTITUTION IS NOT YET PROVEN. The failures this was written from were found in play
    /// mode, and edit-mode evaluation is a different code path — <c>Animator.Update</c> outside play
    /// mode is not obliged to apply everything the player would. Until a run here reproduces the
    /// play-mode answer on an avatar with known failures, a clean result from this tool means
    /// "found nothing", NOT "nothing is wrong". The known answer to check against, Saavi_NSFW:
    /// WhiskersOff, WhiskerSwap, Unsheath, HeadPat and Tail leave something stuck.
    ///
    /// This MOVES THINGS in the open scene and does not put them back. Nothing is saved, but reload
    /// the scene before doing anything else with it.
    /// </summary>
    public static class ToggleSweep
    {
        /// <summary>Frames to let the controller settle after each change. 12 at 60 Hz is plenty for
        /// a toggle; transitions with real duration need more, and are reported as noise if not.</summary>
        const int SettleFrames = 12;

        /// <summary>Frames before the first measurement, so the controller reaches its resting pose.</summary>
        const int Warmup = 40;

        /// <summary>Blendshape weights run 0..100, so this is a hundredth of a percent.</summary>
        const float Epsilon = 0.01f;

        /// <summary>
        /// Driven by ChilloutVR itself. Sweeping one of these measures the game rather than the
        /// avatar, and several of them (Grounded, Sitting) put the avatar into a pose that changes
        /// half the rig on its own.
        /// </summary>
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

        /// <summary>
        /// Batch entry: opens AVATARBRIDGE_SWEEP_SCENE, converts the VRChat avatar in it, and sweeps
        /// the result. The scene is never written.
        /// </summary>
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

        /// <summary>Returns how many parameters left something stuck, or -1 if it could not run.</summary>
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

            Debug.Log($"[Sweep] \"{root.name}\": {parameters.Length} parameter(s) over {watch.Count} watched " +
                      "propert(ies) — object activity, renderer enable, blendshape weights and material " +
                      "slots. This moves things in the open scene and does not put them back — reload it " +
                      "afterwards.");

            var stuck = new List<string>();
            // How many parameters visibly did ANYTHING when driven. Without this the tool cannot
            // tell "every toggle came back" from "I never moved a thing", and reports the two
            // identically — which is how it came back clean twice on an avatar with a toggle that
            // is broken in game, and neither result meant anything.
            int responded = 0;
            foreach (var parameter in parameters)
            {
                float original = animator.GetFloat(parameter);
                var before = watch.Capture();

                animator.SetFloat(parameter, 1f);
                Settle(animator, SettleFrames);
                bool moves = watch.Differences(before).Count > 0;

                animator.SetFloat(parameter, original);
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
                Debug.Log($"[Sweep] STUCK \"{parameter}\" ({original} → 1 → {original}): " +
                          string.Join("; ", moved.Take(8)) +
                          (moved.Count > 8 ? $" (+{moved.Count - 8} more)" : ""));
            }

            animator.cullingMode = culling;

            if (responded == 0)
            {
                // Not a clean bill of health — the opposite. Driving 65 parameters and observing
                // nothing move even WHILE they were held at 1 means the avatar was never actually
                // animated, so every "came back fine" below it is vacuous.
                Debug.LogError($"[Sweep] INVALID RUN — none of the {parameters.Length} parameters changed " +
                               "anything even while held on, so nothing was really being driven. Edit-mode " +
                               "Animator.Update is the likely culprit and this result says NOTHING about " +
                               "the avatar. Do not read the line below as a pass.");
            }
            else if (stuck.Count == 0)
            {
                Debug.Log($"[Sweep] found nothing — all {parameters.Length} toggle(s) came back to where " +
                          $"they started, and {responded} of them demonstrably moved something while on, " +
                          "so the sweep was really driving the avatar. Still not the same as \"nothing is " +
                          "wrong\": anything outside the watchlist is invisible to it.");
            }
            else
            {
                Debug.Log($"[Sweep] {stuck.Count} of {parameters.Length} left something stuck " +
                          $"({responded} moved something while on): {string.Join(", ", stuck)}");
            }
            return stuck.Count;
        }

        static void Settle(Animator animator, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                animator.Update(1f / 60f);
            }
        }

        /// <summary>
        /// Everything a toggle can plausibly leave behind, addressed once so each sweep is a cheap
        /// array read rather than another walk of the hierarchy.
        ///
        /// Labels are built once and reused, because they are only wanted when something has already
        /// gone wrong — building a few thousand strings per parameter to describe a clean result is
        /// most of the run time for no benefit.
        /// </summary>
        sealed class Watchlist
        {
            readonly string[] labels;
            readonly Transform[] objects;
            readonly Renderer[] renderers;
            readonly SkinnedMeshRenderer[] skins;
            readonly int blendShapeTotal;

            public int Count => labels.Length;

            public Watchlist(GameObject root)
            {
                objects = root.GetComponentsInChildren<Transform>(true);
                renderers = root.GetComponentsInChildren<Renderer>(true);
                skins = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);

                var names = new List<string>();
                foreach (var t in objects)
                {
                    names.Add($"{t.name} active");
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

            /// <summary>
            /// One reading of every watched property. Numbers and object references are kept apart
            /// so a material swap is compared by identity rather than by anything derived from it.
            /// </summary>
            public Reading Capture()
            {
                var numbers = new float[objects.Length + renderers.Length + blendShapeTotal];
                var references = new Object[labels.Length - numbers.Length];
                int n = 0, r = 0;

                foreach (var t in objects)
                {
                    numbers[n++] = t != null && t.gameObject.activeSelf ? 1f : 0f;
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

            /// <summary>
            /// What changed since that reading, named. The label array interleaves numbers and
            /// references, so both cursors walk it in the order the constructor built it.
            /// </summary>
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

        /// <summary>
        /// The converted avatar in the scene. A conversion scene usually holds the greyed-out
        /// original as well, so the one with a controller wins — same rule CckAnimatorTester uses.
        /// </summary>
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
