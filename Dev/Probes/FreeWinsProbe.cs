// Proves the free-wins pass removes what it claims and nothing else.
//
// A converted avatar has neither problem, because the converter already
// removes both, so the target is dirtied on purpose first: one layer with
// no states, one parameter nothing touches, and one parameter nothing
// touches that the game writes, which must SURVIVE.
//
//   -executeMethod AvatarBridge.Regression.FreeWinsProbe.RunBatch
//   AVATARBRIDGE_SURVEY_SCENE = the scene or prefab to read
#if CVR_CCK_EXISTS
using System;
using System.Linq;
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AvatarBridge.Regression
{
    public static class FreeWinsProbe
    {
        public static void RunBatch()
        {
            string target = Environment.GetEnvironmentVariable("AVATARBRIDGE_SURVEY_SCENE");
            if (string.IsNullOrEmpty(target))
            {
                Debug.LogError("[FreeWins] set AVATARBRIDGE_SURVEY_SCENE");
                EditorApplication.Exit(2);
                return;
            }
            if (target.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(target);
                if (asset != null) PrefabUtility.InstantiatePrefab(asset);
            }
            else
            {
                EditorSceneManager.OpenScene(target, OpenSceneMode.Single);
            }

            // The first avatar that actually carries a controller: a scene can
            // hold more than one, and the others are why this used to give up.
            var avatar = UnityEngine.Object.FindObjectsOfType<CVRAvatar>(true)
                .OrderBy(a => a.name, StringComparer.Ordinal)
                .FirstOrDefault(a => Base(a) != null);
            var animator = avatar != null ? avatar.GetComponent<Animator>() : null;
            var original = Base(avatar);
            if (original == null)
            {
                Debug.LogError("[FreeWins] no avatar with an AnimatorController");
                EditorApplication.Exit(3);
                return;
            }

            // The avatar runs an override controller wrapping the base. That
            // override is a shared project ASSET, not a scene object: writing
            // to it edits the converted avatar itself, and the first version
            // of this probe did exactly that, then deleted what it had pointed
            // the asset at. Everything below works on copies, and the only
            // thing assigned to anything real is on the scene instance, which
            // is thrown away with the scene.
            var sharedOverride = animator.runtimeAnimatorController as AnimatorOverrideController;

            // Dirty a copy, so the corpus avatar's own controller is untouched.
            string dirtyPath = AssetDatabase.GenerateUniqueAssetPath("Assets/FreeWinsProbe.controller");
            AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(original), dirtyPath);
            var dirty = AssetDatabase.LoadAssetAtPath<AnimatorController>(dirtyPath);
            dirty.AddLayer("ZZ_ProbeEmptyLayer");
            dirty.AddParameter("ZZ_ProbeJunk", AnimatorControllerParameterType.Float);

            // A game-written name the controller does not already carry.
            // Adding one it has gets silently renamed ("GestureLeft 0"), and
            // then the guard under test is never the thing being tested.
            string[] candidates = { "AFK", "Swimming", "IsFriend", "CancelEmote", "VisemeLoudness", "Prone" };
            string gameDriven = candidates.FirstOrDefault(c => dirty.parameters.All(p => p.name != c));
            if (gameDriven == null)
            {
                Debug.LogError("[FreeWins] no unused game-driven name left to test the guard with");
                EditorApplication.Exit(6);
                return;
            }
            dirty.AddParameter(gameDriven, AnimatorControllerParameterType.Bool);
            EditorUtility.SetDirty(dirty);
            AssetDatabase.SaveAssets();
            string overridePath = null;
            if (sharedOverride != null)
            {
                overridePath = AssetDatabase.GenerateUniqueAssetPath("Assets/FreeWinsProbeOverride.overrideController");
                AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(sharedOverride), overridePath);
                var copy = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(overridePath);
                copy.runtimeAnimatorController = dirty;
                EditorUtility.SetDirty(copy);
                AssetDatabase.SaveAssets();
                animator.runtimeAnimatorController = copy;
            }
            else
            {
                animator.runtimeAnimatorController = dirty;
            }

            int layersBefore = dirty.layers.Length, paramsBefore = dirty.parameters.Length;

            var report = new BridgeReport();
            var plan = FreeWins.Find(avatar, AvatarSurvey.Build(avatar));
            string outPath = AssetDatabase.GenerateUniqueAssetPath("Assets/FreeWinsProbeOut.controller");
            var into = FreeWins.Apply(avatar, plan, outPath, report);

            Debug.Log($"[FreeWins] planned layers: {string.Join(", ", plan.Layers)}");
            Debug.Log($"[FreeWins] planned parameters: {string.Join(", ", plan.Parameters)}");
            Debug.Log($"[FreeWins] spared: {string.Join(" | ", plan.Spared)}");
            Debug.Log($"[FreeWins] placeholders reported: {plan.PlaceholderStates}");

            if (into == null)
            {
                Debug.LogError("[FreeWins] FAIL: nothing written");
                EditorApplication.Exit(4);
                return;
            }

            bool emptyGone = into.layers.All(l => l.name != "ZZ_ProbeEmptyLayer");
            bool junkGone = into.parameters.All(p => p.name != "ZZ_ProbeJunk");
            bool gameKept = into.parameters.Any(p => p.name == gameDriven);
            int layersLost = layersBefore - into.layers.Length;
            int paramsLost = paramsBefore - into.parameters.Length;

            Debug.Log($"[FreeWins] empty layer removed={emptyGone} junk removed={junkGone} " +
                      $"{gameDriven} kept={gameKept} layers lost={layersLost} parameters lost={paramsLost}");
            Debug.Log($"[FreeWins] report:\n{report.ToMarkdown(avatar.name)}");

            // The game-driven guard cannot be reached through a real survey:
            // MarkGameDriven gives those parameters a writer, so they are
            // never flagged unused and never offered for removal. It is a
            // second line of defence, so it gets tested as one, against a
            // model that claims the thing the survey will not claim.
            var pretend = new AvatarSurvey.Model();
            pretend.Findings.Add(new AvatarSurvey.Finding
            {
                Kind = "unused parameter", Subject = gameDriven, Detail = "pretend",
            });
            var guarded = FreeWins.Find(avatar, pretend);
            bool guardHeld = guarded.Parameters.Count == 0 && guarded.Spared.Count == 1;
            Debug.Log($"[FreeWins] guard held for {gameDriven}={guardHeld} " +
                      $"spared: {string.Join(" | ", guarded.Spared)}");

            // The shared override must still point where it always did.
            bool sharedIntact = sharedOverride == null
                                || sharedOverride.runtimeAnimatorController == original;

            AssetDatabase.DeleteAsset(dirtyPath);
            AssetDatabase.DeleteAsset(outPath);
            if (overridePath != null) AssetDatabase.DeleteAsset(overridePath);
            Debug.Log($"[FreeWins] shared override untouched={sharedIntact}");

            bool pass = emptyGone && junkGone && gameKept && layersLost == 1 && paramsLost == 1
                        && guardHeld && sharedIntact;
            Debug.Log(pass ? "[FreeWins] PASS" : "[FreeWins] FAIL");
            EditorApplication.Exit(pass ? 0 : 5);
        }

        static AnimatorController Base(CVRAvatar avatar)
        {
            var animator = avatar != null ? avatar.GetComponent<Animator>() : null;
            RuntimeAnimatorController runtime = animator != null ? animator.runtimeAnimatorController : null;
            for (int guard = 0; runtime != null && guard < 8; guard++)
            {
                if (runtime is AnimatorController controller) return controller;
                if (runtime is AnimatorOverrideController over) { runtime = over.runtimeAnimatorController; continue; }
                break;
            }
            return null;
        }
    }
}
#endif
