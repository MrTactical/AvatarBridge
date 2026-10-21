#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Every converted plug was labelled from its root, which the conversion always names
    // "YAPS Plug", so an avatar with two or four plugs got that many menu rows all reading
    // "YAPS Plug plug own sockets": working rows nobody could tell apart. The label climbs
    // past the conversion's and VRCFury's wrappers to the author's own object.
    public static class PlugLabelTest
    {
        [MenuItem("Tools/AvatarBridge Dev/Test: plug labels")]
        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[PlugLabelTest] {(ok ? "ok  " : "WRONG")} {label}");
                return ok ? 0 : 1;
            }

            var root = new GameObject("PlugLabelTest");
            try
            {
                Transform Path(string path)
                {
                    var at = root.transform;
                    foreach (var name in path.Split('/'))
                    {
                        var next = at.Find(name);
                        if (next == null) { next = new GameObject(name).transform; next.SetParent(at, false); }
                        at = next;
                    }
                    return at;
                }
                string Label(string path)
                {
                    var t = Path(path);
                    var plug = t.gameObject.AddComponent<YapsPlug>();
                    plug.rootBone = t;
                    return YapsToggles.LabelFor(plug);
                }

                string left = Label("Armature/Hips/Base/Left Shaft/SPS Plug/YAPS Plug");
                string right = Label("Armature/Hips/Base/Right Shaft/SPS Plug/YAPS Plug");
                fail += Check($"two converted plugs read apart (\"{left}\", \"{right}\")",
                    left == "Left Shaft plug" && right == "Right Shaft plug");
                string ear = Label("Armature/Hips/Head/[VF500] Ear_L/Original Object/Ear_SPS_L/YAPS Plug");
                fail += Check($"the author's object under VRCFury's wrapper (\"{ear}\")", ear == "Ear_SPS_L plug");
                string wrapped = Label("Armature/Hips/[VF796] Tail/Original Object/BakedSpsPlug");
                fail += Check($"a Fury id stripped from the object it lands on (\"{wrapped}\")", wrapped == "Tail plug");
                string native = Label("Armature/Hips/Shaft");
                fail += Check($"a native plug keeps its bone (\"{native}\")", native == "Shaft plug");

                // One name on two plugs, a prop in each hand. Both exist before either
                // is labelled, since each label reads the other.
                Label("Armature/Hips/Hand.L/[VF11] Prop L/Original Object/Prop/YAPS Plug");
                string propR = Label("Armature/Hips/Hand.R/[VF12] Prop R/Original Object/Prop/YAPS Plug");
                string propL = YapsToggles.LabelFor(root.transform
                    .Find("Armature/Hips/Hand.L/[VF11] Prop L/Original Object/Prop/YAPS Plug").GetComponent<YapsPlug>());
                fail += Check($"two plugs of one name read apart (\"{propL}\", \"{propR}\")",
                    propL == "Prop L plug" && propR == "Prop R plug");

                // A menu row writes the whole renderer, so plugs on one mesh share it,
                // and every build renamed it to whichever plug went last.
                var mesh = Path("Variants").gameObject.AddComponent<MeshRenderer>();
                YapsPlug OnMesh(string path)
                {
                    var t = Path(path);
                    var plug = t.gameObject.AddComponent<YapsPlug>();
                    plug.rootBone = t;
                    plug.renderer = mesh;
                    return plug;
                }
                var first = OnMesh("Armature/Hips/Base/First/YAPS Plug");
                var second = OnMesh("Armature/Hips/Base/Second/YAPS Plug");
                string row1 = YapsToggles.MenuLabelFor(first), row2 = YapsToggles.MenuLabelFor(second);
                fail += Check($"plugs on one mesh share one row naming both (\"{row1}\", \"{row2}\")",
                    row1 == "First and Second plugs" && row2 == row1);
                string own = YapsToggles.LabelFor(first);
                fail += Check($"each still has its own name in the windows (\"{own}\")", own == "First plug");
                string alone = YapsToggles.MenuLabelFor(root.transform.Find("Armature/Hips/Shaft").GetComponent<YapsPlug>());
                fail += Check($"a plug alone on its mesh keeps its label as the row (\"{alone}\")", alone == "Shaft plug");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            Debug.Log($"[PlugLabelTest] {(fail == 0 ? "PASS" : fail + " WRONG")}");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
