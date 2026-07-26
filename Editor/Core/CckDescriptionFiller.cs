#if CVR_CCK_EXISTS
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AvatarBridge
{
    /// <summary>
    /// Types the generated description into the CCK Content Manager's Description box.
    ///
    /// There is no supported route to this. ChilloutVR keeps no description on the avatar —
    /// `CVRAssetInfo` has no such field — and the Content Manager holds the value in Unity's
    /// `SessionState`, which `BuilderTab.SelectContent` wipes through `ClearFields()` every time
    /// the chosen content changes. Writing that key at conversion time loses the race against the
    /// user's own next click, so it is not attempted.
    ///
    /// What does work is the UI itself. The panel is a UI Toolkit window and the box is a
    /// `TextField` named `input-description` (CCK `ContentBuilder2.uxml`). Assigning its `value`
    /// fires the CCK's own `RegisterValueChangedCallback`, which sets `State.Description` exactly
    /// as typing would — so the value travels the CCK's normal path to upload with nothing here
    /// reaching into its internals.
    ///
    /// The costs are real and worth naming, since they decide the design:
    ///
    ///   * It depends on an element name inside someone else's UXML. A CCK release that renames
    ///     it breaks this, and the break is invisible unless it is reported — hence a result the
    ///     caller must show, and never a silent best-effort during conversion.
    ///   * It only works while the panel is open on the Builder tab. That makes it a button the
    ///     user presses when they are looking at the box, not a step in the conversion.
    ///   * It must never overwrite what someone has written. An empty box is an invitation; a
    ///     full one is a decision.
    /// </summary>
    public static class CckDescriptionFiller
    {
        public enum Result
        {
            /// <summary>Text is in the box.</summary>
            Filled,
            /// <summary>The box already had something in it — left alone, deliberately.</summary>
            AlreadyWritten,
            /// <summary>The CCK Control Panel isn't open.</summary>
            PanelClosed,
            /// <summary>Panel open, but no description field on screen — wrong tab, or renamed.</summary>
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

        /// <summary>A sentence for the user, naming the next move rather than just the outcome.</summary>
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
                           "have renamed the field — \"Copy description\" still works, and that's worth " +
                           "reporting.";
            }
        }

        static bool AnyPanelOpen()
        {
            return Resources.FindObjectsOfTypeAll<EditorWindow>()
                .Any(w => w != null && w.GetType().Name == PanelTypeName);
        }

        /// <summary>
        /// Found by type NAME rather than by referencing the type, so this compiles and runs
        /// whatever assembly the CCK ships its editor UI in, and simply finds nothing if a future
        /// CCK drops the window entirely.
        /// </summary>
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
