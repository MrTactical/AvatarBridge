#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using ABI.CCK.Components;

namespace AvatarBridge
{
    /// <summary>
    /// Re-exposes material properties that a locked Poiyomi/Thry shader baked away.
    ///
    /// Locking ("optimising") inlines every property that was not flagged animated AT LOCK TIME
    /// as a literal constant and deletes it from the shader. Anything animating that property
    /// then writes to a uniform that does not exist: the toggle appears, the parameter syncs,
    /// the layer plays its clip at full weight, and nothing happens on screen. One avatar
    /// arrived with fifteen properties in that state — wetness, hue shift, saturation,
    /// brightness, AudioLink, the RGBA masks — every one of them equally dead in VRChat.
    ///
    /// The fix Poiyomi documents is unlock, flag the property, lock again. This does that,
    /// driven by the animations rather than by hand, so no property is missed and none is
    /// flagged that nothing animates.
    ///
    /// It is deliberately NOT part of conversion. Re-locking recompiles shaders — minutes, not
    /// seconds — and it reaches into Poiyomi's internals, which move between versions. A slow,
    /// version-coupled shader rebuild should be something you trigger, watch, and can retry,
    /// rather than something a conversion does to you.
    ///
    /// Originals are never touched: every affected material is copied beside the avatar and the
    /// copies are what get relocked. The source avatar keeps working in VRChat, and materials
    /// shared with other avatars are unaffected.
    /// </summary>
    public static class MaterialRelocker
    {
        const string Menu = "Tools/Avatar Bridge/Fix locked material properties";

        [MenuItem(Menu)]
        static void Run()
        {
            var avatar = ResolveAvatar();
            if (avatar == null)
            {
                EditorUtility.DisplayDialog("Fix locked material properties",
                    "Select a ChilloutVR avatar in the scene first — the one whose animations " +
                    "aren't taking effect.", "OK");
                return;
            }

            var optimizer = FindOptimizer();
            if (optimizer == null)
            {
                EditorUtility.DisplayDialog("Fix locked material properties",
                    "Poiyomi (Thry's shader optimizer) isn't in this project, so nothing can be " +
                    "unlocked or re-locked here.\n\nThis tool only helps with materials using a " +
                    "locked Poiyomi shader.", "OK");
                return;
            }

            // material -> the animated properties its shader has no room for
            var work = CollectDeadProperties(avatar);
            if (work.Count == 0)
            {
                EditorUtility.DisplayDialog("Fix locked material properties",
                    $"Nothing to fix on \"{avatar.name}\".\n\nEvery property its animations drive " +
                    "already exists on the shader that receives it.", "OK");
                return;
            }

            int propertyCount = work.Sum(pair => pair.Value.Count);
            string sample = string.Join("\n  ", work
                .SelectMany(pair => pair.Value)
                .Distinct()
                .OrderBy(p => p)
                .Take(10));
            bool go = EditorUtility.DisplayDialog("Fix locked material properties",
                $"{work.Count} material(s) on \"{avatar.name}\" have {propertyCount} animated " +
                $"propert(ies) their locked shader doesn't expose:\n\n  {sample}" +
                (propertyCount > 10 ? "\n  …" : "") +
                "\n\nEach will be COPIED beside the avatar, unlocked, flagged, and locked again. " +
                "The originals are not modified.\n\nRe-locking recompiles shaders and can take " +
                "several minutes.", "Fix them", "Cancel");
            if (!go)
            {
                return;
            }

            var copies = new List<Material>();
            var tags = new Dictionary<Material, List<string>>();
            try
            {
                if (!CopyAndReassign(avatar, work, copies, tags))
                {
                    return;
                }

                if (!Call(optimizer, "UnlockMaterials", copies))
                {
                    EditorUtility.DisplayDialog("Fix locked material properties",
                        "Poiyomi's unlock step failed or isn't available in this version.\n\n" +
                        "The copied materials are beside your avatar and are already assigned to " +
                        "it; you can unlock and lock them by hand from the material inspector.",
                        "OK");
                    return;
                }

                // Flag AFTER unlocking: the tag has to be on the material the locker will read
                // when it regenerates the shader.
                foreach (var pair in tags)
                {
                    foreach (string property in pair.Value)
                    {
                        pair.Key.SetOverrideTag(property + "Animated", "1");
                    }
                    EditorUtility.SetDirty(pair.Key);
                }
                AssetDatabase.SaveAssets();

                if (!Call(optimizer, "LockMaterials", copies))
                {
                    EditorUtility.DisplayDialog("Fix locked material properties",
                        "The properties are flagged and the materials are UNLOCKED, which already " +
                        "makes the animations work — locking is an optimisation, not a " +
                        "requirement.\n\nPoiyomi's lock step failed or isn't available in this " +
                        "version; you can lock them from the material inspector when convenient.",
                        "OK");
                    return;
                }

                AssetDatabase.SaveAssets();
                EditorUtility.DisplayDialog("Fix locked material properties",
                    $"Done. {copies.Count} material(s) copied, flagged and re-locked, with " +
                    $"{propertyCount} propert(ies) re-exposed.\n\n\"{avatar.name}\" now uses the " +
                    "copies. Test the affected toggles — they should take effect.\n\nYour original " +
                    "materials are unchanged.", "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        [MenuItem(Menu, true)]
        static bool Validate()
        {
            return ResolveAvatar() != null;
        }

        static CVRAvatar ResolveAvatar()
        {
            var selected = Selection.activeGameObject;
            var fromSelection = selected != null ? selected.GetComponentInParent<CVRAvatar>() : null;
            if (fromSelection != null)
            {
                return fromSelection;
            }
            CVRAvatar best = null;
            foreach (var candidate in UnityEngine.Object.FindObjectsOfType<CVRAvatar>(true))
            {
                if (candidate.gameObject.activeInHierarchy)
                {
                    return candidate;
                }
                best = best ?? candidate;
            }
            return best;
        }

        /// <summary>
        /// Every material under the avatar that an animation writes a property to which its
        /// shader does not have. Driven by the animations, so a property nothing animates is
        /// never flagged — flagging everything would undo the point of locking.
        /// </summary>
        static Dictionary<Material, List<string>> CollectDeadProperties(CVRAvatar avatar)
        {
            var result = new Dictionary<Material, List<string>>();
            var animator = avatar.GetComponentInChildren<Animator>(true);
            var runtime = animator != null ? animator.runtimeAnimatorController : null;
            while (runtime is AnimatorOverrideController over)
            {
                runtime = over.runtimeAnimatorController;
            }
            if (!(runtime is AnimatorController controller))
            {
                return result;
            }

            var root = avatar.transform;
            var seen = new HashSet<AnimationClip>();

            void Visit(Motion motion)
            {
                if (motion is BlendTree tree)
                {
                    foreach (var child in tree.children)
                    {
                        Visit(child.motion);
                    }
                    return;
                }
                if (!(motion is AnimationClip clip) || !seen.Add(clip))
                {
                    return;
                }
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (!binding.propertyName.StartsWith("material."))
                    {
                        continue;
                    }
                    string property = binding.propertyName.Substring("material.".Length);
                    // Colour and vector channels arrive per component ("material._Color.r").
                    int dot = property.LastIndexOf('.');
                    if (dot > 0 && property.Length - dot == 2)
                    {
                        property = property.Substring(0, dot);
                    }
                    var target = string.IsNullOrEmpty(binding.path) ? root : root.Find(binding.path);
                    var renderer = target != null ? target.GetComponent<Renderer>() : null;
                    if (renderer == null)
                    {
                        continue;
                    }
                    // If ANY material in the slot list has the property, the animation has
                    // somewhere to land and nothing here is broken.
                    if (renderer.sharedMaterials.Any(m => m != null && m.HasProperty(property)))
                    {
                        continue;
                    }
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (material == null || !IsLocked(material))
                        {
                            continue;
                        }
                        if (!result.TryGetValue(material, out var list))
                        {
                            result[material] = list = new List<string>();
                        }
                        if (!list.Contains(property))
                        {
                            list.Add(property);
                        }
                    }
                }
            }

            foreach (var layer in controller.layers)
            {
                VisitMachine(layer.stateMachine, Visit);
            }
            return result;
        }

        static void VisitMachine(AnimatorStateMachine machine, Action<Motion> visit)
        {
            if (machine == null)
            {
                return;
            }
            foreach (var child in machine.states)
            {
                visit(child.state != null ? child.state.motion : null);
            }
            foreach (var sub in machine.stateMachines)
            {
                VisitMachine(sub.stateMachine, visit);
            }
        }

        /// <summary>Thry writes its output under Hidden/Locked/ and marks the material with its
        /// own toggle; either is enough to know unlocking is even possible.</summary>
        static bool IsLocked(Material material)
        {
            return material.shader != null
                   && (material.shader.name.StartsWith("Hidden/Locked/")
                       || material.HasProperty("_ShaderOptimizerEnabled")
                       && material.GetFloat("_ShaderOptimizerEnabled") > 0.5f);
        }

        /// <summary>
        /// Copies each affected material beside the avatar and points the avatar's renderers at
        /// the copies, so everything that follows happens to assets this avatar owns.
        /// </summary>
        static bool CopyAndReassign(CVRAvatar avatar, Dictionary<Material, List<string>> work,
            List<Material> copies, Dictionary<Material, List<string>> tags)
        {
            string folder = $"Assets/AvatarBridgeOutput/{Sanitize(avatar.name)}/RelockedMaterials";
            if (!AssetDatabase.IsValidFolder(folder))
            {
                System.IO.Directory.CreateDirectory(folder);
                AssetDatabase.Refresh();
            }

            var replacement = new Dictionary<Material, Material>();
            foreach (var pair in work)
            {
                string source = AssetDatabase.GetAssetPath(pair.Key);
                if (string.IsNullOrEmpty(source))
                {
                    // An embedded material has no file to copy; it is already unique to this
                    // avatar, so relock it where it stands.
                    replacement[pair.Key] = pair.Key;
                    copies.Add(pair.Key);
                    tags[pair.Key] = pair.Value;
                    continue;
                }
                string destination = AssetDatabase.GenerateUniqueAssetPath(
                    $"{folder}/{pair.Key.name}.mat");
                if (!AssetDatabase.CopyAsset(source, destination))
                {
                    EditorUtility.DisplayDialog("Fix locked material properties",
                        $"Couldn't copy \"{pair.Key.name}\".\n\nNothing has been changed.", "OK");
                    return false;
                }
                var copy = AssetDatabase.LoadAssetAtPath<Material>(destination);
                replacement[pair.Key] = copy;
                copies.Add(copy);
                tags[copy] = pair.Value;
            }
            AssetDatabase.SaveAssets();

            foreach (var renderer in avatar.GetComponentsInChildren<Renderer>(true))
            {
                var slots = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i] != null && replacement.TryGetValue(slots[i], out var copy)
                        && copy != slots[i])
                    {
                        slots[i] = copy;
                        changed = true;
                    }
                }
                if (changed)
                {
                    Undo.RecordObject(renderer, "Fix locked material properties");
                    renderer.sharedMaterials = slots;
                    EditorUtility.SetDirty(renderer);
                }
            }
            return true;
        }

        static string Sanitize(string name)
        {
            foreach (char bad in System.IO.Path.GetInvalidFileNameChars())
            {
                name = name.Replace(bad, '_');
            }
            return name;
        }

        // ---------------------------------------------------------------- Thry, at arm's length ----

        /// <summary>
        /// Poiyomi is optional and its internals move between versions, so it is reached by
        /// reflection and every failure is survivable — the worst case leaves unlocked materials,
        /// which WORK, just without the optimisation.
        /// </summary>
        static Type FindOptimizer()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = assembly.GetType("Thry.ShaderOptimizer", false);
                    if (type != null)
                    {
                        return type;
                    }
                }
                catch
                {
                    // Broken or reflection-only assemblies; ignore.
                }
            }
            return null;
        }

        static bool Call(Type optimizer, string method, List<Material> materials)
        {
            try
            {
                var target = optimizer.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == method
                                         && m.GetParameters().Length >= 1
                                         && m.GetParameters()[0].ParameterType
                                             .IsAssignableFrom(typeof(List<Material>)));
                if (target == null)
                {
                    return false;
                }
                var parameters = target.GetParameters();
                var args = new object[parameters.Length];
                args[0] = materials;
                for (int i = 1; i < parameters.Length; i++)
                {
                    args[i] = parameters[i].HasDefaultValue
                        ? parameters[i].DefaultValue
                        : parameters[i].ParameterType.IsValueType
                            ? Activator.CreateInstance(parameters[i].ParameterType)
                            : null;
                }
                object result = target.Invoke(null, args);
                return !(result is bool ok) || ok;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }
        }
    }
}
#endif
