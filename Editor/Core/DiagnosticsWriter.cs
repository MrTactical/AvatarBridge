#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using ABI.CCK.Components;

namespace AvatarBridge
{
    // Writes Diagnostics.md next to the conversion report: the measurements, not the advice.
    //
    // The report says what happened and what to do about it, in a user's words. This says what
    // the avatar IS; versions, settings, rig shape, where the head and eyes actually are, what
    // the constraint graph looks like, which asset references resolve. It exists because every
    // hard diagnosis on this project has come from someone extracting those facts by hand from a
    // .prefab afterwards, one awk script at a time: where the eye bones sit relative to the head
    // bone, how many humanoid bones deform mesh, whether a curve's path still resolves. The
    // conversion already knows all of it and used to throw it away.
    //
    // Written for attaching to a bug report. Nothing here is advice and nothing is a judgement .
    // numbers only, so a reader can reach their own conclusion, and so two conversions can be
    // diffed against each other.
    //
    // Every section is independently guarded: a section that throws is replaced by its error and
    // the rest of the file still gets written. A diagnostics file that fails to save is worth
    // nothing precisely when it is needed most.
    public static class DiagnosticsWriter
    {
        public static void Write(BridgeContext ctx)
        {
            var text = new StringBuilder();
            text.AppendLine($"# AvatarBridge diagnostics: {ctx.Target.name}");
            text.AppendLine();
            text.AppendLine($"*AvatarBridge v{BridgeDefines.Version} · Unity {Application.unityVersion} · " +
                            $"{DateTime.Now:yyyy-MM-dd HH:mm}*");
            text.AppendLine();
            text.AppendLine("Facts about this conversion, for attaching to a bug report. No advice here — ");
            text.AppendLine("that's in ConversionReport.md. Nothing in this file leaves your machine unless ");
            text.AppendLine("you send it.");
            text.AppendLine();

            Section(text, "Packages", () => Packages(ctx));
            Section(text, "Editor", () => EditorEnvironment());
            Section(text, "Settings used", () => SettingsDump(ctx));
            Section(text, "Rig", () => Rig(ctx));
            Section(text, "Head, eyes and the viewpoint", () => HeadGeometry(ctx));
            Section(text, "Constraints", () => Constraints(ctx));
            Section(text, "Animator", () => AnimatorFacts(ctx));
            Section(text, "Meshes", () => Meshes(ctx));
            Section(text, "Unresolved asset references", () => UnresolvedAssets(ctx));

            try
            {
                string path = ctx.OutputDir + "/Diagnostics.md";
                string absolute = Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
                File.WriteAllText(absolute, text.ToString());
                AssetDatabase.ImportAsset(path);
                Debug.Log($"[AvatarBridge] Diagnostics written to {path}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AvatarBridge] Diagnostics could not be saved: {e.Message}");
            }
        }

        static void Section(StringBuilder text, string title, Func<string> build)
        {
            text.AppendLine($"## {title}");
            text.AppendLine();
            try
            {
                text.AppendLine(build());
            }
            catch (Exception e)
            {
                // The whole point of this file is to survive a broken avatar, so a section that
                // throws reports the throw and the others carry on.
                text.AppendLine($"*Could not be collected: {e.GetType().Name}: {e.Message}*");
            }
            text.AppendLine();
        }

        // ------------------------------------------------------------------ packages ----

        static string Packages(BridgeContext ctx)
        {
            var rows = new List<string>
            {
                Row("VRChat SDK", VersionOf("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor")),
                Row("ChilloutVR CCK", VersionOf("ABI.CCK.Components.CVRAvatar")),
                Row("VRCFury", VersionOf("VF.Model.VRCFury")),
                Row("Modular Avatar", VersionOf("nadena.dev.modular_avatar.core.ModularAvatarInformation")),
                Row("MagicaCloth2", VersionOf("MagicaCloth2.MagicaCloth")),
                Row("DynamicBone", VersionOf("DynamicBone")),
            };
            return Table(new[] { "Package", "Assembly / version" }, rows);
        }

        static string VersionOf(string fullTypeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (assembly.GetType(fullTypeName, false) != null)
                    {
                        var name = assembly.GetName();
                        return $"`{name.Name}` {name.Version}";
                    }
                }
                catch
                {
                    // Reflection-only or broken assemblies; keep looking.
                }
            }
            return "not installed";
        }

        // ------------------------------------------------------------------ settings ----

        static string EditorEnvironment()
        {
            var rows = new List<string>
            {
                Row("Unity", Application.unityVersion),
                Row("Colour space", PlayerSettings.colorSpace.ToString()),
                Row("Enter Play Mode Options",
                    EditorSettings.enterPlayModeOptionsEnabled ? "**ON**" : "off")
            };
            if (EditorSettings.enterPlayModeOptionsEnabled)
            {
                var options = EditorSettings.enterPlayModeOptions;
                rows.Add(Row("· Reload Domain",
                    options.HasFlag(EnterPlayModeOptions.DisableDomainReload) ? "**disabled**" : "on"));
                rows.Add(Row("· Reload Scene",
                    options.HasFlag(EnterPlayModeOptions.DisableSceneReload) ? "**disabled**" : "on"));
            }
            return Table(new[] { "", "" }, rows);
        }

        static string SettingsDump(BridgeContext ctx)
        {
            // Reflected, not hand-listed, so a new option can never go missing from diagnostics.
            var rows = new List<string>();
            foreach (var field in typeof(BridgeSettings).GetFields(BindingFlags.Public | BindingFlags.Instance)
                         .OrderBy(f => f.Name))
            {
                object value = field.GetValue(ctx.Settings);
                rows.Add(Row($"`{field.Name}`", $"`{value}`"));
            }
            return rows.Count == 0 ? "*no settings found*" : Table(new[] { "Option", "Value" }, rows);
        }

        // ---------------------------------------------------------------------- rig ----

        static string Rig(BridgeContext ctx)
        {
            var animator = ctx.TargetAnimator;
            var text = new StringBuilder();
            var root = ctx.Target.transform;

            text.AppendLine(Table(new[] { "", "" }, new List<string>
            {
                Row("Rig type", animator == null ? "no Animator"
                    : animator.isHuman ? "Humanoid" : "Generic / none"),
                Row("Root localScale", Vec(root.localScale)),
                Row("Root lossyScale", Vec(root.lossyScale)),
                Row("Transforms", root.GetComponentsInChildren<Transform>(true).Length.ToString()),
            }));

            if (animator == null || !animator.isHuman)
            {
                return text.ToString();
            }

            // The decoy score. A humanoid stand-in skeleton deforms nothing, which is what
            // distinguishes a decoy-rig quadruped from an ordinary avatar, and it took four
            // attempts to work that out because nobody had measured it.
            var deforming = DeformingBones(ctx.Target);
            var mapped = new List<Transform>();
            foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone)
                {
                    continue;
                }
                var t = animator.GetBoneTransform(bone);
                if (t != null)
                {
                    mapped.Add(t);
                }
            }
            int deformingMapped = mapped.Count(deforming.Contains);
            var negative = root.GetComponentsInChildren<Transform>(true)
                .Where(t => t.localScale.x < 0f || t.localScale.y < 0f || t.localScale.z < 0f)
                .ToList();

            text.AppendLine(Table(new[] { "Humanoid rig", "" }, new List<string>
            {
                Row("Mapped bones", mapped.Count.ToString()),
                Row("...that deform mesh", $"{deformingMapped} " +
                    $"({(mapped.Count == 0 ? 0f : 100f * deformingMapped / mapped.Count):0}%)"),
                Row("Bones deforming mesh (whole avatar)", deforming.Count.ToString()),
                Row("Negatively scaled transforms", negative.Count.ToString() +
                    (negative.Count > 0 ? $" — e.g. `{negative[0].name}`" : "")),
            }));
            return text.ToString();
        }

        static HashSet<Transform> DeformingBones(GameObject root)
        {
            var deforming = new HashSet<Transform>();
            foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = skin.sharedMesh;
                var bones = skin.bones;
                if (mesh == null || bones == null || bones.Length == 0)
                {
                    continue;
                }
                try
                {
                    var weights = mesh.GetAllBoneWeights();
                    for (int i = 0; i < weights.Length; i++)
                    {
                        var w = weights[i];
                        if (w.weight > 0f && w.boneIndex >= 0 && w.boneIndex < bones.Length
                            && bones[w.boneIndex] != null)
                        {
                            deforming.Add(bones[w.boneIndex]);
                        }
                    }
                }
                catch
                {
                    // Unreadable mesh: count every listed bone rather than none.
                    foreach (var bone in bones)
                    {
                        if (bone != null)
                        {
                            deforming.Add(bone);
                        }
                    }
                }
            }
            return deforming;
        }

        // ------------------------------------------------------- head and viewpoint ----

        static string HeadGeometry(BridgeContext ctx)
        {
            var animator = ctx.TargetAnimator;
            var cvr = ctx.CvrAvatar;
            if (cvr == null)
            {
                return "*No CVRAvatar on the converted avatar.*";
            }
            var root = ctx.Target.transform;
            var rows = new List<string>
            {
                Row("`viewPosition` (stored)", Vec(cvr.viewPosition)),
                Row("`voicePosition` (stored)", Vec(cvr.voicePosition)),
            };

            if (animator != null && animator.isHuman)
            {
                var head = animator.GetBoneTransform(HumanBodyBones.Head);
                var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                var leftEye = animator.GetBoneTransform(HumanBodyBones.LeftEye);
                var rightEye = animator.GetBoneTransform(HumanBodyBones.RightEye);
                var jaw = animator.GetBoneTransform(HumanBodyBones.Jaw);

                rows.Add(Row("Humanoid Head bone", Bone(ctx, root, head)));
                rows.Add(Row("Humanoid Hips bone", Bone(ctx, root, hips)));
                rows.Add(Row("Humanoid LeftEye", Bone(ctx, root, leftEye)));
                rows.Add(Row("Humanoid RightEye", Bone(ctx, root, rightEye)));
                // Flagged rather than just listed: the Jaw slot is optional and unchecked, riggers
                // fill it with whatever, and a Jaw sitting above the eyes is a rig fault that
                // otherwise only shows up as a voice coming out of the top of someone's head.
                string jawNote = "";
                if (jaw != null && (leftEye != null || rightEye != null))
                {
                    Vector3 eyes = leftEye != null && rightEye != null
                        ? (leftEye.position + rightEye.position) * 0.5f
                        : (leftEye != null ? leftEye.position : rightEye.position);
                    float above = Vector3.Dot(jaw.position - eyes, root.up);
                    if (above > 0f)
                    {
                        jawNote = $"<br>**{above:0.##} m ABOVE the eyes — not a jaw**";
                    }
                }
                rows.Add(Row("Humanoid Jaw", Bone(ctx, root, jaw) + jawNote));

                if (head != null)
                {
                    float tolerance = hips != null
                        ? Mathf.Max(0.15f, Vector3.Distance(hips.position, head.position) * 0.5f)
                        : 0.5f;
                    rows.Add(Row("Head-distance tolerance", $"{tolerance:0.###} m"));
                    rows.Add(Row("Viewpoint to head bone",
                        $"{Vector3.Distance(AvatarFeatureDetect.CckGizmoWorldPoint(ctx.Target, cvr.viewPosition), head.position):0.###} m"));
                    rows.Add(Row("Voice to head bone",
                        $"{Vector3.Distance(AvatarFeatureDetect.CckGizmoWorldPoint(ctx.Target, cvr.voicePosition), head.position):0.###} m"));
                }
            }

            var text = new StringBuilder(Table(new[] { "", "Avatar-local position" }, rows));

            // Bones named like eyes, wherever they live. On more than one rig these are nowhere
            // near the humanoid map; parked in a cloned chain, or under a face rig; and knowing
            // that instantly is the difference between a five-minute answer and an afternoon.
            var mappedEyes = new HashSet<Transform>();
            if (animator != null && animator.isHuman)
            {
                foreach (var bone in new[] { HumanBodyBones.LeftEye, HumanBodyBones.RightEye })
                {
                    var t = animator.GetBoneTransform(bone);
                    if (t != null)
                    {
                        mappedEyes.Add(t);
                    }
                }
            }
            var eyeish = ctx.Target.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.IndexOf("eye", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
            if (eyeish.Count > 0)
            {
                // Mapped ones first, and never truncated away. The first version listed the first
                // twelve in hierarchy order, which on a rig carrying a duplicate skeleton showed
                // the template chain and hid the pair the humanoid map actually uses; the one
                // fact the table exists to establish.
                var ordered = eyeish.OrderByDescending(mappedEyes.Contains).Take(20).ToList();
                text.AppendLine();
                text.AppendLine($"Transforms with \"eye\" in the name ({eyeish.Count} found" +
                                (eyeish.Count > ordered.Count ? $", showing {ordered.Count}" : "") +
                                "; **mapped** = the humanoid rig points at it):");
                text.AppendLine();
                text.AppendLine(Table(new[] { "Name", "Avatar-local position", "Mapped", "Path" },
                    ordered.Select(t => Row($"`{t.name}`", Vec(root.InverseTransformPoint(t.position)),
                        mappedEyes.Contains(t) ? "**yes**" : "", $"`{ctx.PathInTarget(t)}`")).ToList()));
            }
            return text.ToString();
        }

        static string Bone(BridgeContext ctx, Transform root, Transform bone)
        {
            if (bone == null)
            {
                return "*unmapped*";
            }
            return $"`{bone.name}` at {Vec(root.InverseTransformPoint(bone.position))}<br>`{ctx.PathInTarget(bone)}`";
        }

        // -------------------------------------------------------------- constraints ----

        static string Constraints(BridgeContext ctx)
        {
            var byType = new SortedDictionary<string, int>();
            int total = 0;
            foreach (var component in ctx.Target.GetComponentsInChildren<Component>(true))
            {
                if (component == null)
                {
                    continue;
                }
                string name = component.GetType().Name;
                bool isConstraint = component is IConstraint
                    || (name.StartsWith("VRC", StringComparison.Ordinal)
                        && name.EndsWith("Constraint", StringComparison.Ordinal));
                if (!isConstraint)
                {
                    continue;
                }
                byType[name] = byType.TryGetValue(name, out int n) ? n + 1 : 1;
                total++;
            }
            if (total == 0)
            {
                return "*No constraints on the converted avatar.*";
            }
            return Table(new[] { "Component", "Count" },
                byType.Select(p => Row($"`{p.Key}`", p.Value.ToString())).ToList());
        }

        // ----------------------------------------------------------------- animator ----

        static string AnimatorFacts(BridgeContext ctx)
        {
            var controller = ctx.MergedController;
            if (controller == null)
            {
                return "*No merged controller.*";
            }
            int states = 0;
            foreach (var layer in controller.layers)
            {
                states += CountStates(layer.stateMachine);
            }
            var clips = new HashSet<AnimationClip>(controller.animationClips.Where(c => c != null));
            return Table(new[] { "", "" }, new List<string>
            {
                Row("Layers", controller.layers.Length.ToString()),
                Row("States", states.ToString()),
                Row("Parameters", controller.parameters.Length.ToString()),
                Row("Distinct clips", clips.Count.ToString()),
                Row("Total curves", clips.Sum(c => AnimationUtility.GetCurveBindings(c).Length).ToString()),
            });
        }

        static int CountStates(UnityEditor.Animations.AnimatorStateMachine machine)
        {
            if (machine == null)
            {
                return 0;
            }
            int n = machine.states.Length;
            foreach (var child in machine.stateMachines)
            {
                n += CountStates(child.stateMachine);
            }
            return n;
        }

        // ------------------------------------------------------------------- meshes ----

        static string Meshes(BridgeContext ctx)
        {
            var skins = ctx.Target.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var filters = ctx.Target.GetComponentsInChildren<MeshRenderer>(true);
            int blendShapes = skins.Sum(s => s.sharedMesh != null ? s.sharedMesh.blendShapeCount : 0);
            var materials = new HashSet<Material>();
            foreach (var renderer in ctx.Target.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material != null)
                    {
                        materials.Add(material);
                    }
                }
            }
            return Table(new[] { "", "" }, new List<string>
            {
                Row("Skinned meshes", skins.Length.ToString()),
                Row("Static meshes", filters.Length.ToString()),
                Row("Blendshapes (total)", blendShapes.ToString()),
                Row("Distinct materials", materials.Count.ToString()),
                Row("Shaders", materials.Select(m => m.shader != null ? m.shader.name : "(null)")
                    .Distinct().Count().ToString()),
            });
        }

        // ------------------------------------------------- unresolved asset lookups ----

        static string UnresolvedAssets(BridgeContext ctx)
        {
            string path = ctx.MergedController != null
                ? AssetDatabase.GetAssetPath(ctx.MergedController)
                : null;
            if (string.IsNullOrEmpty(path) || !File.Exists(Path.GetFullPath(
                    Path.Combine(Application.dataPath, "..", path))))
            {
                return "*Controller file not found; nothing to scan.*";
            }

            string body = File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", path)));
            var counts = new Dictionary<string, int>();
            // Shares AnimatorMerger's reference matcher deliberately. When these two disagreed,
            // the report said an avatar's controller was fine while the crash guard refused to
            // assign it; two answers to one question, from two copies of the same scan.
            foreach (string guid in AnimatorMerger.ReferencedGuids(body))
            {
                counts[guid] = counts.TryGetValue(guid, out int n) ? n + 1 : 1;
            }

            var rows = new List<string>();
            foreach (var pair in counts.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal))
            {
                if (!string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(pair.Key)))
                {
                    continue;
                }
                rows.Add(Row($"`{pair.Key}`", $"{pair.Value} reference(s)"));
            }
            return rows.Count == 0
                ? $"All {counts.Count} referenced GUID(s) in the saved controller resolve to assets in this project."
                : $"{rows.Count} of {counts.Count} referenced GUID(s) resolve to nothing:\n\n"
                  + Table(new[] { "GUID", "Times referenced" }, rows);
        }

        // ---------------------------------------------------------------- formatting ----

        static string Row(params string[] cells) => "| " + string.Join(" | ", cells) + " |";

        static string Table(string[] headers, List<string> rows)
        {
            if (rows.Count == 0)
            {
                return "*none*";
            }
            var text = new StringBuilder();
            text.AppendLine("| " + string.Join(" | ", headers) + " |");
            text.AppendLine("|" + string.Concat(headers.Select(_ => "---|")));
            foreach (string row in rows)
            {
                text.AppendLine(row);
            }
            return text.ToString();
        }

        static string Vec(Vector3 v) => $"({v.x:0.####}, {v.y:0.####}, {v.z:0.####})";
    }
}
#endif
