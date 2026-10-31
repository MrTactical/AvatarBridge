#if CVR_CCK_EXISTS
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AvatarBridge.Regression
{
    // A greyed button has to say why it is greyed, or it reads as broken. The button smoke found
    // the Toolkit's Merge animators "clear" greyed with nothing to hover. Every other greyed
    // button with no tooltip is listed, for a look, but only clear is checked.
    //
    // Run: -executeMethod AvatarBridge.Regression.GreyedButtonTooltipTest.Run -quit
    public static class GreyedButtonTooltipTest
    {
        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Greyed button tooltips")]
        public static void Run()
        {
            var root = new VisualElement();
            new ToolkitPanel().Mount(root);
            var silent = root.Query<Button>().ToList().Where(b => !b.enabledSelf && string.IsNullOrEmpty(b.tooltip)).ToList();
            foreach (var b in silent) Debug.Log($"[GreyedButtonTooltipTest] greyed, no tooltip: {b.text}");
            var clear = root.Query<Button>().ToList().FirstOrDefault(b => b.text == "clear");
            bool ok = clear != null && !clear.enabledSelf && !string.IsNullOrEmpty(clear.tooltip);
            Debug.Log($"[GreyedButtonTooltipTest] {(ok ? "ok   " : "WRONG")} Merge animators clear, greyed with no source, says why"
                + (clear == null ? " (not found)" : ""));
            Debug.Log(ok ? "[GreyedButtonTooltipTest] PASS" : "[GreyedButtonTooltipTest] FAIL");
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
#endif
