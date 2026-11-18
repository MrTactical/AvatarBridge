#if CVR_CCK_EXISTS
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AvatarBridge
{
    // Types the description into the CCK Content Manager's box. No supported route: CVRAssetInfo
    // has no field, and the SessionState key is wiped by ClearFields() on every content change.
    // Setting the `input-description` TextField (ContentBuilder2.uxml) fires the CCK's own
    // change callback, as typing would. Depends on that element name, so the caller must show
    // the result; works only with the Builder tab open; never overwrites a filled box.
    public static class CckDescriptionFiller
    {
        public enum Result
        {
            Filled,
            AlreadyWritten,
            PanelClosed,
            FieldMissing
        }

        const string FieldName = "input-description";
        const string PanelTypeName = "CCKControlPanel";

        public static Result Fill(string description, bool overwrite = false)
        {
            var field = FindDescriptionField();
            if (field == null)
            {
                return AnyPanelOpen() ? Result.FieldMissing : Result.PanelClosed;
            }
            if (!overwrite && !string.IsNullOrWhiteSpace(field.value))
            {
                return Result.AlreadyWritten;
            }

            // Assigning `value` (rather than SetValueWithoutNotify) is the whole trick: it raises
            // the change event the CCK is listening for, so its own state updates and the text is
            // carried to upload by its normal path.
            field.value = description ?? string.Empty;
            return Result.Filled;
        }

        public static string Explain(Result result)
        {
            switch (result)
            {
                case Result.Filled:
                    return "Description filled in. Check it reads how you want before uploading.";
                case Result.AlreadyWritten:
                    return "The Description box already has text in it, so nothing was changed. " +
                           "Clear it and press again, or use \"Copy description\" and paste where you want it.";
                case Result.PanelClosed:
                    return "The CCK Control Panel isn't open. Open it, pick this avatar under the " +
                           "Builder tab, then press this again.";
                default:
                    return "Couldn't find the Description box. Make sure the CCK Control Panel is on " +
                           "the Builder tab with this avatar selected. If it is, this CCK version may " +
                           "have renamed the field: the description can still be pasted in by hand, and " +
                           "that's worth reporting.";
            }
        }

        static bool AnyPanelOpen()
        {
            return Resources.FindObjectsOfTypeAll<EditorWindow>()
                .Any(w => w != null && w.GetType().Name == PanelTypeName);
        }

        static TextField FindDescriptionField()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (window == null || window.GetType().Name != PanelTypeName)
                {
                    continue;
                }
                var root = window.rootVisualElement;
                var field = root?.Q<TextField>(FieldName);
                if (field != null)
                {
                    return field;
                }
            }
            return null;
        }
    }
}
#endif
