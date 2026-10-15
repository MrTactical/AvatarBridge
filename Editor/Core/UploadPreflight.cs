// What the CCK's upload panel will refuse, found before anybody opens it. The
// panel hides its Upload button while any of five rules fails, and three of
// them are import settings no conversion touches, so a converted avatar can
// read as finished and still be refused. Each rule here gathers what the CCK
// gathers (CCK_BuildManagerWindow.ContentValidation), so a clean result here
// is a clean panel there.
//
// Plus the one the panel never mentions: the upload saves the avatar as a
// prefab, and a prefab keeps no reference to an object that is not a saved
// asset or part of the avatar. Those arrive in game as nothing.
#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using ABI.CCK.Components;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    public static class UploadPreflight
    {
        const string Category = "Upload";
        const string FixInPanel = "The CCK's Control Panel shows a Fix Import Settings button beside it.";

        public static void Run(BridgeReport report, CVRAvatar avatar)
        {
            if (avatar == null) return;
            var root = avatar.gameObject;
            int before = report.Entries.Count;

            var missing = root.GetComponentsInChildren<Transform>(true)
                .Where(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) > 0)
                .Select(t => t.name).ToList();
            if (missing.Count > 0)
                report.Warning(Category, $"The CCK will not upload this: {BridgeElements.Count(missing.Count, "object has", "objects have")} a missing script",
                    $"{AvatarSurvey.Few(missing)}. A package the avatar uses is not installed. Install it, or remove the " +
                    "scripts with the Control Panel's Remove Missing Scripts once you know what they did.");

            if (root.GetComponent<Animator>() == null)
                report.Warning(Category, "The CCK will not upload this: no Animator on the avatar's root", "Add one and give it the avatar's rig.");

            var textures = Importers<TextureImporter>(Textures(root)).ToList();
            Refuse(report, textures.Where(t => t.importer.maxTextureSize > 8192).Select(t => t.name),
                "texture is", "textures are", "set to a max size above 8192",
                "ChilloutVR loads 8192 at most. Set Max Size to 8192 or lower in the import settings. " + FixInPanel);
            Refuse(report, textures.Where(t => t.importer.mipmapEnabled && !t.importer.streamingMipmaps).Select(t => t.name),
                "texture has", "textures have", "mipmaps without Streaming Mipmaps",
                "ChilloutVR streams mipmaps, so the CCK asks for it on every texture that has them. Tick Streaming " +
                "Mipmaps in the import settings. " + FixInPanel);
            Refuse(report, Importers<AudioImporter>(AudioClips(root)).Where(a => !a.importer.loadInBackground).Select(a => a.name),
                "sound does", "sounds do", "not load in the background",
                "Tick Load In Background in the import settings. " + FixInPanel);

            Unsaved(report, root);
#if AVATARBRIDGE_YAPS
            YapsNativeBuilder.ReportLostShaders(report, root);
#endif

            if (report.Entries.Count == before)
                report.Converted(Category, "Nothing the CCK's upload check refuses",
                    "No missing scripts, an Animator on the root, textures and sounds imported the way it asks, and " +
                    "nothing the upload would drop.");
        }

        static void Refuse(BridgeReport report, IEnumerable<string> names, string one, string many, string what, string fix)
        {
            var list = names.Distinct().ToList();
            if (list.Count > 0)
                report.Warning(Category, $"The CCK will not upload this: {BridgeElements.Count(list.Count, one, many)} {what}",
                    $"{AvatarSurvey.Few(list)}. {fix}");
        }

        // A reference to something that is neither a saved asset nor part of
        // the avatar. The prefab the upload saves keeps none of them.
        static void Unsaved(BridgeReport report, GameObject root)
        {
            var lost = new List<string>();
            var outside = new List<string>();
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;
                var it = new SerializedObject(component).GetIterator();
                while (it.Next(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var value = it.objectReferenceValue;
                    if (value == null || EditorUtility.IsPersistent(value)) continue;
                    var go = value as GameObject ?? (value as Component)?.gameObject;
                    if (go == null) lost.Add($"{value.name} ({value.GetType().Name}) on {component.name}");
                    else if (!go.transform.IsChildOf(root.transform)) outside.Add($"{go.name}, from {component.name}");
                }
            }
            foreach (var controller in Controllers(root))
                lost.AddRange(controller.animationClips.Where(c => c != null && !EditorUtility.IsPersistent(c))
                    .Select(c => $"{c.name} (AnimationClip) in {controller.name}"));

            if (lost.Count > 0)
                report.Warning(Category, $"{BridgeElements.Count(lost.Distinct().Count(), "thing the avatar uses is", "things the avatar uses are")} not saved, so the upload drops them",
                    $"{AvatarSurvey.Few(lost.Distinct())}. Made in memory and never saved as an asset, so the prefab the " +
                    "upload builds holds nothing there. Whatever made them needs to save them.");
            if (outside.Count > 0)
                report.Warning(Category, $"{BridgeElements.Count(outside.Distinct().Count(), "reference points", "references point")} outside the avatar, so the upload drops them",
                    $"{AvatarSurvey.Few(outside.Distinct())}. The upload keeps only the avatar's own hierarchy. Move the " +
                    "object under the avatar, or point the reference at one that is.");
        }

        static IEnumerable<(string name, T importer)> Importers<T>(IEnumerable<Object> assets) where T : AssetImporter
        {
            foreach (var asset in assets.Where(a => a != null).Distinct())
            {
                string path = AssetDatabase.GetAssetPath(asset);
                if (string.IsNullOrWhiteSpace(path)) continue;
                if (AssetImporter.GetAtPath(path) is T importer) yield return (asset.name, importer);
            }
        }

        // The CCK's own gathering: renderer materials, and the materials an
        // animation swaps in on any renderer.
        static IEnumerable<Object> Textures(GameObject root)
        {
            var materials = root.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).ToList();
            foreach (var controller in Controllers(root))
                foreach (var clip in controller.animationClips.Where(c => c != null))
                    foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                        if (binding.type.IsSubclassOf(typeof(Renderer)) && binding.propertyName.StartsWith("m_Material"))
                            materials.AddRange(AnimationUtility.GetObjectReferenceCurve(clip, binding).Select(k => k.value as Material));
            return materials.Where(m => m != null).Distinct()
                .SelectMany(m => m.GetTexturePropertyNames().Select(m.GetTexture));
        }

        static IEnumerable<RuntimeAnimatorController> Controllers(GameObject root)
        {
            var found = root.GetComponentsInChildren<Animator>(true).Select(a => a.runtimeAnimatorController).ToList();
            var avatar = root.GetComponent<CVRAvatar>();
            if (avatar != null && avatar.overrides != null) found.Add(avatar.overrides.runtimeAnimatorController);
            return found.Where(c => c != null).Distinct();
        }

        static IEnumerable<AudioClip> Each(IEnumerable<AudioClip> clips) => clips ?? Enumerable.Empty<AudioClip>();

        static IEnumerable<Object> AudioClips(GameObject root)
        {
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                switch (component)
                {
                    case AudioSource source:
                        yield return source.clip;
                        break;
                    case CVRAudioDriver driver:
                        if (driver.audioSource != null) yield return driver.audioSource.clip;
                        foreach (var clip in Each(driver.audioClips)) yield return clip;
                        break;
                    case CVRParticleSound particles:
                        // The CCK reads the die reference twice and never the spawn one.
                        if (particles.dieAudioSourceReference != null) yield return particles.dieAudioSourceReference.clip;
                        foreach (var clip in Each(particles.spawnSound).Concat(Each(particles.dieSound))) yield return clip;
                        break;
                    case GunController gun:
                        foreach (var clip in Each(gun.reloadSounds).Concat(Each(gun.shootSounds)).Concat(Each(gun.emptyShootSounds))) yield return clip;
                        break;
                }
            }
        }
    }
}
#endif
