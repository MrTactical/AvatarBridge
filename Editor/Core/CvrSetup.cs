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
    // does (the viewpoint, visemes and blink wiring, face tracking, the height scaler)
    // is CVR-side work that never needed VRChat in the first place. This runs exactly
    // those passes against features read straight off the rig and meshes, so it works on
    // a Booth model or an original avatar. Not on one that already has a menu:
    // it builds the controller and the menu fresh, and says so when it replaces one.
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

            // As the converter does: the report is read on other machines, and comma decimals
            // paste badly.
            var previousCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
            System.Threading.Thread.CurrentThread.CurrentCulture =
                System.Globalization.CultureInfo.InvariantCulture;

            try
            {
                PrepareOutputFolder(ctx, avatar, Category);
                PrepareTarget(ctx, avatar);

                SetupCvrAvatar(ctx);

                var controller = BuildController(ctx);
                FaceTrackingConverter.Run(ctx);
                FaceTrackingInjector.Inject(controller, ctx);
                AvatarScalerInjector.Inject(controller, ctx);
                SaveController(ctx, controller);

                // Only once the copy is built, so a failure above leaves the original showing,
                // and recorded, so Undo brings it back along with removing the copy.
                if (ctx.Target != avatar)
                {
                    Undo.RecordObject(avatar, "AvatarBridge setup");
                    avatar.SetActive(false);
                }

                BridgeFinish.Run(ctx, "SetupReport.md", "Setup report");
                Selection.activeGameObject = ctx.Target;
                // The window resolves report subjects against this; Selection is whatever the
                // user clicked last by the time they read the report.
                report.ConvertedRoot = ctx.Target;

                report.Converted(Category, "Finished",
                    $"\"{ctx.Target.name}\" is set up for ChilloutVR.");
            }
            catch (Exception e)
            {
                report.Error(Category, "Unhandled exception", e.Message);
                Debug.LogException(e);
                WriteFailureReport(ctx, "SetupReport.md", ctx.Target != null ? ctx.Target.name : avatar.name);
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = previousCulture;
            }
            return report;
        }

        // A run that threw never reaches BridgeFinish, and it is the run whose report most needs
        // sending. The markdown alone: the diagnostics would read a half-built avatar.
        internal static void WriteFailureReport(BridgeContext ctx, string reportName, string avatarName)
        {
            if (string.IsNullOrEmpty(ctx.OutputDir))
            {
                return;
            }
            try
            {
                string path = ctx.OutputDir + "/" + reportName;
                File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", path)),
                    ctx.Report.ToMarkdown(avatarName));
                AssetDatabase.ImportAsset(path);
                ctx.Report.SavedReportPath = path;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AvatarBridge] The report could not be written either: {e.Message}");
            }
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
            var cvrAvatar = ctx.Target.GetComponent<CVRAvatar>();
            if (cvrAvatar == null) cvrAvatar = ctx.Target.AddComponent<CVRAvatar>();
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
            // The CCK's own Auto placement first, so every avatar gets one convention. The
            // bounds estimate only covers rigs the Auto chain can't read.
            bool autoView = AvatarFeatureDetect.CckAutoViewPosition(ctx.Target, animator, out var viewAuto);
            var humanoidView = autoView
                ? viewAuto
                : AvatarFeatureDetect.EstimateViewPosition(ctx.Target, animator);

            // A decoy rig (the humanoid map pointing at a hidden stand-in skeleton, with
            // constraints relaying it onto the visible body) puts both markers on the stand-in
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
                    "The humanoid bones are a hidden skeleton relayed onto the visible body, so Auto placement " +
                    $"would land {Vector3.Distance(humanoidView, decoyView):0.##} m from the face. Measured on " +
                    $"the relayed bones: {decoyDetail}. Check both with the CVRAvatar gizmo.");
            }
            else
            {
                ctx.Report.Converted(Category, "Viewpoint",
                    $"Viewpoint at {cvrAvatar.viewPosition.y:0.00} m, " +
                    (autoView
                        ? "from the CCK's own Auto placement (between the eye bones)"
                        : (humanoid ? "estimated from the eye/head bones" : "estimated from the mesh bounds")) +
                    ". Check it in the scene view and nudge it if the first-person camera sits wrong.");
            }

            // --- face mesh -----------------------------------------------------------
            var face = PickFaceMesh(cvrAvatar, ctx.Target, out var visemes);
            if (face != null)
            {
                cvrAvatar.bodyMesh = face;
                ctx.Report.Converted(Category, "Face mesh", face.name);
            }
            else
            {
                ctx.Report.Warning(Category, "No face mesh found",
                    "No skinned mesh with blendshapes: visemes, blink and face tracking are skipped.");
            }

            // --- visemes -------------------------------------------------------------
            var mesh = face != null ? face.sharedMesh : null;
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
            // The CCK's Auto placement (jaw bone, else head offset), and the viseme-measured
            // mouth only when the rig has neither bone.
            if (decoyRig)
            {
                cvrAvatar.voicePosition = decoyVoice;   // reported with the viewpoint above
            }
            else if (AvatarFeatureDetect.CckAutoVoicePosition(ctx.Target, animator, out var voiceAuto))
            {
                cvrAvatar.voicePosition = voiceAuto;
                ctx.Report.Converted(Category, "Voice position",
                    "The CCK's own Auto placement: the jaw bone, or just ahead of the head bone when " +
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
            // Setup builds the menu and the controller fresh from the CCK
            // base. An avatar that already had a menu loses it here, and
            // says so; Undo brings it back.
            int hadEntries = cvrAvatar.avatarSettings != null && cvrAvatar.avatarSettings.settings != null
                ? cvrAvatar.avatarSettings.settings.Count : 0;
            if (hadEntries > 0)
            {
                ctx.Report.Warning(Category, $"Replaced a menu of {hadEntries} entr{(hadEntries == 1 ? "y" : "ies")}",
                    "Setup builds the controller and menu fresh; Undo restores the old ones. Convert an avatar " +
                    "with its own toggles, or run the Toolkit's cards one at a time.");
            }
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
            var face = PickFaceMesh(cvrAvatar, avatar, out var visemes);
            if (face == null)
            {
                report.Warning(Category, "No face mesh found", "No skinned mesh with blendshapes; nothing to wire.");
                return report;
            }
            cvrAvatar.bodyMesh = face;
            report.Converted(Category, "Face mesh", face.name);
            var mesh = face.sharedMesh;
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

        // The mesh the visemes are on, the current face first. Picked by name alone, a "Body"
        // with any shape key replaced a working face and left its visemes naming shapes it lacks.
        static SkinnedMeshRenderer PickFaceMesh(CVRAvatar cvrAvatar, GameObject root, out string[] visemes)
        {
            var current = cvrAvatar.bodyMesh;
            var found = AvatarFeatureDetect.FindFaceMesh(root);
            foreach (var candidate in new[] { current, found })
            {
                visemes = candidate != null ? AvatarFeatureDetect.DetectVisemes(candidate.sharedMesh) : null;
                if (visemes != null)
                {
                    return candidate;
                }
            }
            visemes = null;
            return current != null && current.sharedMesh != null ? current : found;
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

        // Internal, with CckCoreParameters below, so the converter's merger can share them
        // instead of keeping its own copies: setup's copy drifted once and lost the core
        // parameters.
        internal static AnimatorController LoadCckAnimator()
        {
            foreach (var path in CckAnimatorPaths)
            {
                var source = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (source != null)
                {
                    return source;
                }
            }
            return null;
        }

        // The CCK's own controller, or one in a package: every avatar in the project shares it
        // and the next update replaces it. Reading one is fine; a layer written into it reaches
        // every avatar and then vanishes. The Toolkit and YAPS both refuse to write there.
        internal static bool SharedController(AnimatorController controller)
        {
            string path = controller != null ? AssetDatabase.GetAssetPath(controller).Replace('\\', '/') : "";
            return path.StartsWith("Assets/ABI.CCK/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("Assets/CVR.CCK/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase);
        }

        // The fallback when the CCK's controller is missing. Names, types and order match the
        // CCK's own controller: CVR dispatches writes on the declared type, so these must match
        // the real thing exactly.
        internal static AnimatorControllerParameter[] CckCoreParameters() => new[]
        {
            new AnimatorControllerParameter { name = "MovementX", type = AnimatorControllerParameterType.Float },
            new AnimatorControllerParameter { name = "MovementY", type = AnimatorControllerParameterType.Float },
            new AnimatorControllerParameter { name = "Grounded", type = AnimatorControllerParameterType.Bool, defaultBool = true },
            new AnimatorControllerParameter { name = "Emote", type = AnimatorControllerParameterType.Float },
            new AnimatorControllerParameter { name = "CancelEmote", type = AnimatorControllerParameterType.Trigger },
            new AnimatorControllerParameter { name = "GestureLeft", type = AnimatorControllerParameterType.Float },
            new AnimatorControllerParameter { name = "GestureRight", type = AnimatorControllerParameterType.Float },
            new AnimatorControllerParameter { name = "Toggle", type = AnimatorControllerParameterType.Float },
            new AnimatorControllerParameter { name = "Sitting", type = AnimatorControllerParameterType.Bool },
            new AnimatorControllerParameter { name = "Crouching", type = AnimatorControllerParameterType.Bool },
            new AnimatorControllerParameter { name = "Prone", type = AnimatorControllerParameterType.Bool },
            new AnimatorControllerParameter { name = "Flying", type = AnimatorControllerParameterType.Bool },
            new AnimatorControllerParameter { name = "Swimming", type = AnimatorControllerParameterType.Bool }
        };

        static AnimatorController BuildController(BridgeContext ctx)
        {
            var master = new AnimatorController();
            var source = LoadCckAnimator();
            if (source == null)
            {
                ctx.Report.Warning(Category, "CCK AvatarAnimator.controller not found",
                    "Starting from the CCK's core parameters and no layers; the CCK usually regenerates its " +
                    "locomotion layers on upload.");
                master.parameters = CckCoreParameters();
                return master;
            }

            var copier = new AnimatorDeepCopier();
            master.parameters = source.parameters.Select(AnimatorDeepCopier.CloneParameter).ToArray();
            master.layers = source.layers.Select(copier.CloneLayer).ToArray();
            ctx.Report.Converted(Category, "CCK base animator",
                $"Copied {master.layers.Length} layer(s): locomotion, hand poses and emotes stay CVR-native.");
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
            ctx.CvrAvatar.avatarSettings.baseOverrideController = overrides;
            ctx.CvrAvatar.overrides = overrides;

            var animator = ctx.TargetAnimator;
            if (animator != null)
            {
                animator.runtimeAnimatorController = master;
            }
            EditorUtility.SetDirty(ctx.CvrAvatar);
        }

        // ------------------------------------------------------------------ output ----

        // The EditorPrefs key the converter window saves its settings under. Here, not on the
        // window: the Toolkit reads the Output folder from it, and the YAPS package ships the
        // Toolkit without the window.
        internal const string SettingsPrefsKey = "AvatarBridge.Settings";

        // Shared with the converter, which passes the scene object it converts from.
        internal static void PrepareOutputFolder(BridgeContext ctx, GameObject source, string category)
        {
            string folder = CheckedOutputFolder(ctx.Settings.outputFolder);
            if (folder == null)
            {
                ctx.Report.Warning(category, $"Output folder \"{ctx.Settings.outputFolder}\" is not inside Assets",
                    "Using the default \"Assets/AvatarBridgeOutput\" instead.");
                folder = "Assets/AvatarBridgeOutput";
            }
            ctx.OutputDir = CreateOutputDir(folder + "/" + SafeFolderName(source.name), source, ctx.Report, category);
        }

        // The output folder rules, shared with the Toolkit: it finds a conversion's records only
        // by landing on the same folder, and a copy of these rules drifted once already.
        // Names like ".", ".." or all dots would escape or collide with the output folder.
        internal static string SafeFolderName(string name)
        {
            name = SanitizeFileName(name).Trim().Trim('.');
            return string.IsNullOrWhiteSpace(name) ? "Avatar" : name;
        }

        // Null outside Assets: deletes and overwrites must never point outside the project.
        internal static string CheckedOutputFolder(string setting)
        {
            string folder = (setting ?? "").Trim().Replace('\\', '/').TrimEnd('/');
            return folder != "Assets" && !folder.StartsWith("Assets/") || folder.Contains("..") ? null : folder;
        }

        // The folder is named after the avatar, so two avatars sharing a name shared it, and the
        // second conversion deleted the first one's controller and clips. The folder now records
        // which scene object it was made for; another object gets a numbered folder of its own.
        // `chosen` is FindOutputDir's answer from before something else made the folder: asked
        // again, an untagged numbered folder reads as another avatar's.
        internal static string CreateOutputDir(string dir, GameObject source, BridgeReport report, string category,
            string chosen = null)
        {
            string id = SourceId(source);
            chosen = chosen ?? FindOutputDir(dir, source);

            // A folder Unity already knows needs no project-wide refresh, which would import every
            // pending external change mid-conversion, once per avatar in a batch.
            string full = Path.GetFullPath(Path.Combine(Application.dataPath, "..", chosen));
            if (!AssetDatabase.IsValidFolder(chosen) || !Directory.Exists(full))
            {
                Directory.CreateDirectory(full);
                AssetDatabase.Refresh();
            }

            var importer = AssetImporter.GetAtPath(chosen);
            if (id != null && importer != null && importer.userData != id)
            {
                importer.userData = id;
                importer.SaveAndReimport();
            }
            if (chosen != dir)
            {
                report.Approximated(category, $"Saved to \"{chosen}\"",
                    $"\"{dir}\" holds a different avatar with the same name, so this one got its own folder " +
                    "instead of overwriting that one.");
            }
            return chosen;
        }

        // The folder CreateOutputDir picks for this source, read only: nothing is created or
        // tagged, so the Toolkit can find a conversion's folder while it builds its cards.
        internal static string FindOutputDir(string dir, GameObject source)
        {
            string id = SourceId(source);
            // An unrecorded folder is an older version's output, claimed under the plain name
            // only: a numbered one may belong to an avatar whose name really ends in a number.
            bool TakenByAnother(string path) => OwnerOf(path) is string owner
                ? owner != id
                : path != dir && AssetDatabase.IsValidFolder(path);
            string chosen = dir;
            int n = 2;
            while (id != null && TakenByAnother(chosen))
            {
                chosen = $"{dir} {n++}";
            }
            return chosen;
        }

        // Null in an unsaved scene, which has no stable identity: those keep sharing by name.
        static string SourceId(GameObject source)
        {
            var id = GlobalObjectId.GetGlobalObjectIdSlow(source);
            return id.identifierType == 0 || id.assetGUID.Empty() || id.targetObjectId == 0 ? null : id.ToString();
        }

        internal static string OwnerOf(string dir)
        {
            var importer = AssetImporter.GetAtPath(dir);
            return importer != null && !string.IsNullOrEmpty(importer.userData) ? importer.userData : null;
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
