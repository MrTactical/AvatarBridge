#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using ABI.CCK.Components;
using ABI.CCK.Scripts;

namespace AvatarBridge
{
    // Setup mode: prepares ANY humanoid avatar for ChilloutVR, with **no VRChat SDK
    // involved at all**.
    //
    // The conversion path exists to translate VRChat data. Everything else AvatarBridge
    // does; the viewpoint, visemes and blink wiring, face tracking, the height scaler .
    // is CVR-side work that never needed VRChat in the first place. This runs exactly
    // those passes against features read straight off the rig and meshes, so it works on
    // a Booth model, an original avatar, or one that was already converted.
    //
    // What it deliberately does NOT do: anything requiring VRChat data (menus and
    // parameters from expression assets, PhysBone/contact conversion, animator merging).
    // Those need the VRChat SDK to read, so they live in the conversion path.
    public static class CvrSetup
    {
        const string Category = "CVR setup";

        static readonly string[] CckAnimatorPaths =
        {
            "Assets/CVR.CCK/Assets/Avatar/Animations/AvatarAnimator.controller", // CCK 4.x
            "Assets/ABI.CCK/Animations/AvatarAnimator.controller"                // CCK 3.x
        };

        public static BridgeReport Run(GameObject avatar, BridgeSettings settings)
        {
            var report = new BridgeReport();
            var ctx = new BridgeContext { Settings = settings, Report = report };

            try
            {
                PrepareOutputFolder(ctx, avatar);
                PrepareTarget(ctx, avatar);

                SetupCvrAvatar(ctx);

                var controller = BuildController(ctx);
                FaceTrackingConverter.Run(ctx);
                FaceTrackingInjector.Inject(controller, ctx);
                AvatarScalerInjector.Inject(controller, ctx);
                SaveController(ctx, controller);

                BridgeDiagnostics.Run(ctx, ctx.MergedController);
                ctx.Report.StoreDescription = AvatarDescription.Write(ctx);
                WriteReportFile(ctx);
                EditorUtility.SetDirty(ctx.CvrAvatar);
                AssetDatabase.SaveAssets();
                Selection.activeGameObject = ctx.Target;

                report.Converted(Category, "Finished",
                    $"\"{ctx.Target.name}\" is set up for ChilloutVR.");
            }
            catch (Exception e)
            {
                report.Error(Category, "Unhandled exception", e.Message);
                Debug.LogException(e);
            }
            return report;
        }

        // ------------------------------------------------------------------- target ----

        static void PrepareTarget(BridgeContext ctx, GameObject avatar)
        {
            if (ctx.Settings.cloneAvatar)
            {
                ctx.Target = UnityEngine.Object.Instantiate(avatar);
                ctx.Target.name = avatar.name + " (ChilloutVR)";
                ctx.Target.SetActive(true);
                Undo.RegisterCreatedObjectUndo(ctx.Target, "AvatarBridge setup");
                avatar.SetActive(false);
            }
            else
            {
                ctx.Target = avatar;
                Undo.RegisterFullObjectHierarchyUndo(ctx.Target, "AvatarBridge setup");
            }
        }

        // ---------------------------------------------------------------- CVRAvatar ----

        static void SetupCvrAvatar(BridgeContext ctx)
        {
            var cvrAvatar = ctx.Target.GetComponent<CVRAvatar>() ?? ctx.Target.AddComponent<CVRAvatar>();
            ctx.CvrAvatar = cvrAvatar;

            var animator = ctx.TargetAnimator;
            bool humanoid = animator != null && animator.isHuman;
            if (!humanoid)
            {
                ctx.Report.Warning(Category, "Avatar is not a humanoid rig",
                    "The viewpoint is estimated from the mesh bounds and eye tracking can't be wired. " +
                    "Set the rig to Humanoid in the model's import settings for a proper result.");
            }

            // --- viewpoint -----------------------------------------------------------
            // The CCK's own Auto placement first; one convention for every avatar; the
            // bounds estimate only covers rigs the Auto chain can't read.
            bool autoView = AvatarFeatureDetect.CckAutoViewPosition(ctx.Target, animator, out var viewAuto);
            var humanoidView = autoView
                ? viewAuto
                : AvatarFeatureDetect.EstimateViewPosition(ctx.Target, animator);

            // A decoy rig; the humanoid map pointing at a hidden stand-in skeleton, with
            // constraints relaying it onto the visible body; puts both markers on the stand-in
            // instead of on the avatar. See AvatarFeatureDetect.DecoyRigAnchors.
            bool decoyRig = AvatarFeatureDetect.DecoyRigPlacement(ctx.Target, animator,
                                out var decoyView, out var decoyVoice, out var visibleHead, out string decoyDetail)
                            && Vector3.Distance(decoyView, humanoidView) > 0.05f;
            cvrAvatar.viewPosition = decoyRig ? decoyView : humanoidView;
            cvrAvatar.voicePosition = cvrAvatar.viewPosition;

            if (decoyRig && AvatarFeatureDetect.ExcludeVisibleHeadFromFirstPerson(animator, visibleHead))
            {
                ctx.Report.Converted(Category, $"First-person head hiding moved to \"{visibleHead.name}\"",
                    "ChilloutVR hides your own head in first person by adding an FPRExclusion to the " +
                    "humanoid Head bone, which on a decoy rig skins nothing. One was added to the head " +
                    "you can actually see instead. It only affects YOUR camera.");
            }

            if (decoyRig)
            {
                ctx.Report.Approximated(Category, "Viewpoint & voice measured on the VISIBLE head",
                    "This avatar's humanoid rig is a decoy: the bones Unity's humanoid map points " +
                    "at are a hidden stand-in skeleton, and constraints relay them onto the body " +
                    "you can actually see. ChilloutVR parents both markers to the humanoid Head " +
                    $"bone, so the CCK's Auto placement lands {Vector3.Distance(humanoidView, decoyView):0.##} m " +
                    "from this avatar's face — usually inside its head, where looking down fills " +
                    $"the screen with the inside of its own mouth. Measured on the relayed bones " +
                    $"instead — {decoyDetail}. Check both with the CVRAvatar gizmo before uploading.");
            }
            else
            {
                ctx.Report.Converted(Category, "Viewpoint",
                    $"Viewpoint at {cvrAvatar.viewPosition.y:0.00} m " +
                    (autoView
                        ? "— the CCK's own Auto placement (between the eye bones)"
                        : (humanoid ? "estimated from the eye/head bones" : "estimated from the mesh bounds")) +
                    " — check it in the scene view and nudge if the first-person camera sits wrong.");
            }

            // --- face mesh -----------------------------------------------------------
            var face = AvatarFeatureDetect.FindFaceMesh(ctx.Target);
            if (face != null)
            {
                cvrAvatar.bodyMesh = face;
                ctx.Report.Converted(Category, "Face mesh", face.name);
            }
            else
            {
                ctx.Report.Warning(Category, "No face mesh found",
                    "No skinned mesh with blendshapes — visemes, blink and face tracking are skipped.");
            }

            // --- visemes -------------------------------------------------------------
            var mesh = face != null ? face.sharedMesh : null;
            var visemes = AvatarFeatureDetect.DetectVisemes(mesh);
            if (visemes != null)
            {
                cvrAvatar.useVisemeLipsync = true;
                cvrAvatar.visemeBlendshapes = visemes;
                int found = visemes.Count(v => !string.IsNullOrEmpty(v));
                ctx.Report.Converted(Category, "Visemes", $"{found} of 15 auto-detected on \"{face.name}\"");
            }
            else if (mesh != null)
            {
                ctx.Report.Warning(Category, "Visemes not detected",
                    "No standard viseme blendshapes (vrc.v_aa / v_aa / aa …) on the face mesh. " +
                    "Assign them by hand on the CVRAvatar if the avatar has them under other names.");
            }

            // --- voice position ------------------------------------------------------
            // The CCK's Auto placement (jaw bone, else head offset); viseme-measured mouth
            // only when the rig has neither bone.
            if (decoyRig)
            {
                cvrAvatar.voicePosition = decoyVoice;   // reported with the viewpoint above
            }
            else if (AvatarFeatureDetect.CckAutoVoicePosition(ctx.Target, animator, out var voiceAuto))
            {
                cvrAvatar.voicePosition = voiceAuto;
                ctx.Report.Converted(Category, "Voice position",
                    "The CCK's own Auto placement — the jaw bone, or just ahead of the head bone when " +
                    "there is no jaw.");
            }
            else
            {
                cvrAvatar.voicePosition = MouthLocator.Locate(ctx.Target, face, visemes, animator,
                    cvrAvatar.viewPosition, out var mouthMethod, out string mouthDetail,
                    out string badJaw);
                MouthLocator.Report(ctx, Category, cvrAvatar.voicePosition, mouthMethod, mouthDetail, badJaw);
            }

            // Skipped on a decoy rig: the check measures both markers against the humanoid head
            // bone, which on such a rig they are deliberately nowhere near.
            if (!decoyRig)
            {
                AvatarFeatureDetect.VerifyHeadPlacement(ctx, Category, animator,
                    cvrAvatar.viewPosition, cvrAvatar.voicePosition);
            }

            // --- blink ---------------------------------------------------------------
            WireBlink(ctx, cvrAvatar, mesh);

            // --- advanced settings container ----------------------------------------
            cvrAvatar.avatarUsesAdvancedSettings = true;
            cvrAvatar.avatarSettings = new CVRAdvancedAvatarSettings
            {
                settings = new List<CVRAdvancedSettingsEntry>(),
                initialized = true
            };
            EditorUtility.SetDirty(cvrAvatar);
        }

        // Face only, on an avatar that already has its CVRAvatar: the face
        // mesh, visemes and blink. The toolkit's card. Nothing else moves.
        public static BridgeReport WireFace(GameObject avatar, BridgeSettings settings)
        {
            var report = new BridgeReport();
            var cvrAvatar = avatar != null ? avatar.GetComponent<CVRAvatar>() : null;
            if (cvrAvatar == null)
            {
                report.Warning(Category, "No CVRAvatar", "Add the CVRAvatar component first, or run Setup mode in AvatarBridge.");
                return report;
            }
            var ctx = new BridgeContext { Settings = settings, Report = report, Target = avatar, CvrAvatar = cvrAvatar };
            Undo.RecordObject(cvrAvatar, "Wire face");
            var face = AvatarFeatureDetect.FindFaceMesh(avatar);
            if (face == null)
            {
                report.Warning(Category, "No face mesh found", "No skinned mesh with blendshapes; nothing to wire.");
                return report;
            }
            cvrAvatar.bodyMesh = face;
            report.Converted(Category, "Face mesh", face.name);
            var mesh = face.sharedMesh;
            var visemes = AvatarFeatureDetect.DetectVisemes(mesh);
            if (visemes != null)
            {
                cvrAvatar.useVisemeLipsync = true;
                cvrAvatar.visemeBlendshapes = visemes;
                report.Converted(Category, "Visemes", $"{visemes.Count(v => !string.IsNullOrEmpty(v))} of 15 detected on \"{face.name}\"");
            }
            else
            {
                report.Warning(Category, "Visemes not detected", "No standard viseme blendshapes on the face mesh.");
            }
            WireBlink(ctx, cvrAvatar, mesh);
            EditorUtility.SetDirty(cvrAvatar);
            return report;
        }

        static void WireBlink(BridgeContext ctx, CVRAvatar cvrAvatar, Mesh mesh)
        {
            if (!ctx.Settings.wireBlinkBlendshapes || mesh == null)
            {
                return;
            }
            AvatarFeatureDetect.DetectBlinkShapes(mesh, out string left, out string right, out string combined);
            if (left == null && right == null && combined == null)
            {
                return;
            }

            cvrAvatar.useBlinkBlendshapes = true;
            if (cvrAvatar.blinkBlendshape == null || cvrAvatar.blinkBlendshape.Length < 4)
            {
                cvrAvatar.blinkBlendshape = new string[4];
            }

            if (left != null && right != null)
            {
                cvrAvatar.blinkBlendshape[0] = left;
                cvrAvatar.blinkBlendshape[1] = right;
                AvatarFeatureDetect.SetBlinkMode(cvrAvatar, "Separate");
                ctx.Report.Converted(Category, "Blink blendshapes",
                    $"Wired to \"{left}\" / \"{right}\" (Separate). Verify L/R aren't swapped.");
            }
            else
            {
                string single = combined ?? left ?? right;
                cvrAvatar.blinkBlendshape[0] = single;
                AvatarFeatureDetect.SetBlinkMode(cvrAvatar, "Combined");
                ctx.Report.Converted(Category, "Blink blendshape", $"Wired to \"{single}\" (Combined).");
            }
        }

        // --------------------------------------------------------------- controller ----

        static AnimatorController BuildController(BridgeContext ctx)
        {
            var master = new AnimatorController();
            AnimatorController source = null;
            foreach (var path in CckAnimatorPaths)
            {
                source = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (source != null)
                {
                    break;
                }
            }

            if (source == null)
            {
                ctx.Report.Warning(Category, "CCK AvatarAnimator.controller not found",
                    "Starting from an empty controller; the CCK usually regenerates its locomotion layers on upload.");
                return master;
            }

            var copier = new AnimatorDeepCopier();
            master.parameters = source.parameters.Select(AnimatorDeepCopier.CloneParameter).ToArray();
            master.layers = source.layers.Select(copier.CloneLayer).ToArray();
            ctx.Report.Converted(Category, "CCK base animator",
                $"Copied {master.layers.Length} layer(s) — locomotion, hand poses and emotes stay CVR-native.");
            return master;
        }

        static void SaveController(BridgeContext ctx, AnimatorController master)
        {
            master.name = SanitizeFileName(ctx.Target.name) + "_CVR";

            // Save hands back the persisted asset, which is a different object whenever an
            // earlier run's controller was overwritten in place to keep its GUID.
            string controllerPath = $"{ctx.OutputDir}/{master.name}.controller";
            master = AnimatorAssetSaver.Save(master, controllerPath);
            ctx.MergedController = master;

            var overrides = new AnimatorOverrideController(master) { name = master.name + "_Overrides" };
            string overridesPath = $"{ctx.OutputDir}/{overrides.name}.overrideController";
            overrides = AnimatorAssetSaver.SaveOverride(overrides, overridesPath);

            ctx.CvrAvatar.avatarSettings.baseController = master;
            ctx.CvrAvatar.overrides = overrides;

            var animator = ctx.TargetAnimator;
            if (animator != null)
            {
                animator.runtimeAnimatorController = master;
            }
            EditorUtility.SetDirty(ctx.CvrAvatar);
        }

        // ------------------------------------------------------------------ output ----

        static void PrepareOutputFolder(BridgeContext ctx, GameObject avatar)
        {
            string safeName = SanitizeFileName(avatar.name).Trim().Trim('.');
            if (string.IsNullOrWhiteSpace(safeName))
            {
                safeName = "Avatar";
            }

            string folder = (ctx.Settings.outputFolder ?? "").Trim().Replace('\\', '/').TrimEnd('/');
            if (folder != "Assets" && !folder.StartsWith("Assets/") || folder.Contains(".."))
            {
                ctx.Report.Warning(Category, $"Output folder \"{ctx.Settings.outputFolder}\" is not inside Assets",
                    "Using the default \"Assets/AvatarBridge/Output\" instead.");
                folder = "Assets/AvatarBridge/Output";
            }
            ctx.OutputDir = folder + "/" + safeName;

            Directory.CreateDirectory(Path.GetFullPath(Path.Combine(Application.dataPath, "..", ctx.OutputDir)));
            AssetDatabase.Refresh();
        }

        static void WriteReportFile(BridgeContext ctx)
        {
            string path = ctx.OutputDir + "/SetupReport.md";
            string absolute = Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
            File.WriteAllText(absolute, ctx.Report.ToMarkdown(ctx.Target.name));
            AssetDatabase.ImportAsset(path);
            ctx.Report.SavedReportPath = path;
            Debug.Log($"[AvatarBridge] Setup report written to {path}");
        }

        static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }
    }
}
#endif
