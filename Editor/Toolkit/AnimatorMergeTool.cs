// Merges animator controllers: every layer and parameter of the sources
// into the target, deep-copied, with clashes named. Nothing in a source
// is edited.
#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge
{
    public static class AnimatorMergeTool
    {
        const string Category = "Animator";

        // Into a copy of the target at `savePath`, or into the target
        // itself when `savePath` is null.
        public static AnimatorController Merge(AnimatorController target, IList<AnimatorController> sources,
            string savePath, BridgeReport report)
        {
            if (target == null) { report.Error(Category, "No target controller"); return null; }
            var into = target;
            if (!string.IsNullOrEmpty(savePath))
            {
                string targetPath = AssetDatabase.GetAssetPath(target);
                if (string.IsNullOrEmpty(targetPath) || !AssetDatabase.CopyAsset(targetPath, savePath))
                {
                    report.Error(Category, "Could not copy the target", $"\"{targetPath}\" to \"{savePath}\"");
                    return null;
                }
                into = AssetDatabase.LoadAssetAtPath<AnimatorController>(savePath);
            }

            var names = new HashSet<string>(into.layers.Select(l => l.name));
            var types = into.parameters.ToDictionary(p => p.name, p => p.type);
            int layersAdded = 0, parametersAdded = 0;
            var clashes = new List<string>();
            var renamed = new List<string>();

            foreach (var source in sources)
            {
                if (source == null || source == target) continue;
                foreach (var p in source.parameters)
                {
                    if (types.TryGetValue(p.name, out var have))
                    {
                        if (have != p.type) clashes.Add($"{p.name} ({have} in the target, {p.type} in {source.name})");
                        continue;
                    }
                    into.AddParameter(AnimatorDeepCopier.CloneParameter(p));
                    types[p.name] = p.type;
                    parametersAdded++;
                }

                var copier = new AnimatorDeepCopier();
                var layers = into.layers.ToList();
                foreach (var layer in source.layers)
                {
                    var clone = copier.CloneLayer(layer);
                    if (names.Contains(clone.name))
                    {
                        string fresh = clone.name + " (" + source.name + ")";
                        renamed.Add($"{clone.name} → {fresh}");
                        clone.name = fresh;
                    }
                    names.Add(clone.name);
                    layers.Add(clone);
                    into.layers = layers.ToArray();
                    AnimatorAssetSaver.EmbedLayer(clone, into);
                    layersAdded++;
                }
            }

            EditorUtility.SetDirty(into);
            AssetDatabase.SaveAssets();
            report.Converted(Category, $"Merged {layersAdded} layer(s) and {parametersAdded} parameter(s) into \"{into.name}\"",
                (renamed.Count > 0 ? "Layers renamed to keep names unique: " + string.Join(", ", renamed) + ". " : "") +
                "Layers keep their order after the target's own; masks and clips are shared, not copied.");
            if (clashes.Count > 0)
            {
                report.Warning(Category, $"{clashes.Count} parameter(s) exist in both with different types",
                    string.Join(", ", clashes) + ". The target's type was kept; transitions from the source that " +
                    "compare the other way will need retyping.");
            }
            return into;
        }
    }
}
#endif
