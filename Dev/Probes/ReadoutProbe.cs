// The plug readout, built on every plug and hidden until one synced menu
// toggle shows it. Bakes each plug of a converted avatar through the
// toolkit's own door, then checks what a wearer would get: the readout
// slot, the menu row, the layer, and by rendering, that hidden draws
// nothing and the layer's clips really switch it.
//
// Run: -executeMethod AvatarBridge.Regression.ReadoutProbe.Run [-yapsAvatar <prefab path>]
// Writes to the avatar's controller in the test project (the layer), as a
// toolkit Build would.
#if CVR_CCK_EXISTS
using System;
using System.Linq;
using ABI.CCK.Components;
using ABI.CCK.Scripts;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AvatarBridge.Regression
{
    public static class ReadoutProbe
    {
        const string DefaultAvatar = "Assets/AvatarBridgeOutput/Alexa/Alexa (ChilloutVR).prefab";
        static int fail;

        public static void Run()
        {
            fail = 0;
            try
            {
                string path = Arg("-yapsAvatar") ?? DefaultAvatar;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) throw new Exception("no prefab at " + path);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var go = (GameObject) PrefabUtility.InstantiatePrefab(prefab);
                go.transform.position = Vector3.zero;
                var avatar = go.GetComponent<CVRAvatar>();
                var plugs = go.GetComponentsInChildren<YapsPlug>(true);
                Log($"avatar {path}, {plugs.Length} plug(s)");
                // The bake writes the tags into the converted material and the
                // chooser into the controller, both assets: put back at the end.
                var authored = plugs.ToDictionary(p => p, p => new System.Collections.Generic.List<string>(p.answers));
                foreach (var plug in plugs)
                {
                    // Two tags, so the plug's own door has a chooser to build.
                    if (plug.answers.Count < 2) plug.answers = new System.Collections.Generic.List<string> { "mouth", "handleft" };
                    var o = YapsNativeBuilder.BakeAndRefreshMenu(plug);
                    Check(o.Ok, $"bake {plug.name}: {o.Message}");
                    foreach (var n in o.Notes.Where(n => n.Contains("readout"))) Log("  " + n);
                }

                var entry = avatar.avatarSettings?.settings?.FirstOrDefault(e => e != null
                    && e.machineName == YapsDebugOverlayBuilder.Parameter);
                Check(entry != null && entry.type == CVRAdvancedSettingsEntry.SettingsType.Toggle
                      && entry.toggleSettings != null && !entry.toggleSettings.defaultValue
                      && entry.toggleSettings.usedType == ABI.CCK.Scripts.CVRAdvancesAvatarSettingBase.ParameterType.Bool,
                    $"menu row: {(entry == null ? "missing" : entry.name + ", " + entry.type)}");
                Check(avatar.avatarSettings.settings.Count(e => e != null && e.machineName == YapsDebugOverlayBuilder.Parameter) == 1,
                    "one row, however many plugs");
                var controller = YapsOwner.Shipped(avatar);
                Check(controller.layers.Count(l => l.name.StartsWith("YAPS tags ")) == plugs.Length
                      && avatar.avatarSettings.settings.Count(e => e != null && e.name.EndsWith(" answers")) == plugs.Length,
                    "a tag chooser per plug, from the plug's own bake");
                var dead = YapsToggles.DeadBindings(go, controller);
                Check(dead.Count == 0, $"every YAPS curve binds ({dead.Count} dead{(dead.Count > 0 ? ": " + string.Join("; ", dead.Take(5)) : "")})");
                var layers = controller.layers.Where(l => l.name == "YAPS readout").ToList();
                Check(layers.Count == 1, $"layer on {controller.name}: {layers.Count}");
                Check(controller.parameters.Any(p => p.name == YapsDebugOverlayBuilder.Parameter
                                                     && p.type == AnimatorControllerParameterType.Bool), "bool parameter");
                var states = layers.Count == 1 ? layers[0].stateMachine.states.Select(s => s.state).ToList() : null;
                var shown = states?.FirstOrDefault(s => s.name == "Shown")?.motion as AnimationClip;
                var hiddenClip = states?.FirstOrDefault(s => s.name == "Hidden")?.motion as AnimationClip;
                Check(shown != null && hiddenClip != null && layers[0].stateMachine.defaultState?.name == "Hidden",
                    "Hidden by default, Shown on the toggle");

                var cam = new GameObject("probe camera").AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                cam.nearClipPlane = 0.01f;
                var rt = new RenderTexture(256, 256, 24);
                cam.targetTexture = rt;
                foreach (var plug in plugs)
                {
                    var r = plug.readoutRenderer;
                    Check(r != null, $"{plug.name}: readout built");
                    if (r == null) continue;
                    for (var t = r.transform; t != null; t = t.parent) t.gameObject.SetActive(true);
                    if (r is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = true;
                    int slot = Array.FindIndex(r.sharedMaterials, YapsDebugOverlayBuilder.IsReadout);
                    Check(slot >= 0 && r.sharedMaterials[slot].GetFloat("_YAPS_ReadoutOn") == 0f,
                        $"{plug.name}: readout slot {slot}, hidden on the material");
                    string rpath = AnimationUtility.CalculateTransformPath(r.transform, go.transform);
                    Check(shown != null && AnimationUtility.GetCurveBindings(shown).Any(b => b.path == rpath
                            && b.propertyName == YapsToggles.Bound("_YAPS_ReadoutOn")),
                        $"{plug.name}: the clip writes {YapsToggles.Bound("_YAPS_ReadoutOn")} at {rpath}");

                    var at = plug.transform.position;
                    cam.transform.position = at + go.transform.forward * 1.5f + Vector3.up * 0.1f;
                    cam.transform.LookAt(at);
                    var full = r.sharedMaterials;
                    r.sharedMaterials = full.Where((m, i) => i != slot).ToArray();
                    var without = Grab(cam, rt);
                    r.sharedMaterials = full;
                    r.SetPropertyBlock(null);
                    var off = Grab(cam, rt);
                    var block = new MaterialPropertyBlock();
                    r.GetPropertyBlock(block, slot);
                    block.SetFloat("_YAPS_ReadoutOn", 1f);
                    r.SetPropertyBlock(block, slot);
                    var on = Grab(cam, rt);
                    r.SetPropertyBlock(null, slot);
                    Check(Diff(off, without) == 0, $"{plug.name}: hidden draws nothing ({Diff(off, without)} px differ from no slot)");
                    Check(Diff(on, off) > 200, $"{plug.name}: shown draws ({Diff(on, off)} px)");

                    // The layer itself, through a real Animator: the menu's
                    // parameter on, then off. Alone in a scratch controller,
                    // because the avatar's own toggles switch the plug off.
                    var alone = new UnityEditor.Animations.AnimatorController();
                    alone.AddParameter(YapsDebugOverlayBuilder.Parameter, AnimatorControllerParameterType.Bool);
                    alone.AddLayer(new UnityEditor.Animations.AnimatorControllerLayer
                        { name = layers[0].name, stateMachine = layers[0].stateMachine, defaultWeight = 1f });
                    var valid = AnimationUtility.GetAnimatableBindings(r.gameObject, go)
                        .Where(b => b.propertyName.Contains("_YAPS_ReadoutOn") || b.propertyName.Contains("_YAPS_Enabled"))
                        .Select(b => b.propertyName).ToList();
                    Log($"  animatable on {rpath}: {string.Join(", ", valid)}");
                    foreach (var b in AnimationUtility.GetCurveBindings(shown))
                        Log($"  clip binding {b.path} {b.type.Name} {b.propertyName}: type {AnimationUtility.GetEditorCurveValueType(go, b)?.Name ?? "unresolved"}");
                    var animator = go.GetComponent<Animator>();
                    animator.runtimeAnimatorController = alone;
                    animator.Rebind();
                    void Drive(string when)
                    {
                    foreach (bool want in new[] { true, false })
                    {
                        animator.SetBool(YapsDebugOverlayBuilder.Parameter, want);
                        for (int f = 0; f < 5; f++) animator.Update(1f / 60f);
                        // The humanoid pose moved the plug; aim again.
                        cam.transform.position = plug.transform.position + go.transform.forward * 1.5f + Vector3.up * 0.1f;
                        cam.transform.LookAt(plug.transform.position);
                        var mpb = new MaterialPropertyBlock();
                        r.GetPropertyBlock(mpb, slot);
                        var whole = new MaterialPropertyBlock();
                        r.GetPropertyBlock(whole);
                        Log($"  animator {(want ? "on" : "off")}: bool {animator.GetBool(YapsDebugOverlayBuilder.Parameter)}, " +
                            $"active {r.gameObject.activeInHierarchy}, enabled {r.enabled}, slot block {mpb.GetFloat("_YAPS_ReadoutOn")}, " +
                            $"renderer block {whole.GetFloat("_YAPS_ReadoutOn")}, layer weight " +
                            $"{animator.GetLayerWeight(0)}, state {animator.GetCurrentAnimatorStateInfo(0).IsName("Shown")}");
                        var driven = Grab(cam, rt);
                        r.sharedMaterials = full.Where((m, i) => i != slot).ToArray();
                        var bare = Grab(cam, rt);
                        r.sharedMaterials = full;
                        int d = Diff(driven, bare);
                        Check(want ? d > 200 : d == 0,
                            $"{plug.name}{when}: the menu parameter {(want ? "on shows" : "off hides")} it ({d} px from no slot)");
                    }
                    }
                    Drive("");
                    // The editor's owner stand-in writes its own block on
                    // every slot carrying the id, the readout's included.
                    YapsOwnerStandIn.Apply();
                    var standIn = new MaterialPropertyBlock();
                    r.GetPropertyBlock(standIn, slot);
                    Log($"  stand-in: slot block owner {standIn.GetFloat("_YAPS_Owner")}");
                    Drive(", owner stand-in on");
                    // And a socket previewing in reach, which writes the plug's
                    // block every tick.
                    var previewing = new GameObject("__Preview socket").AddComponent<YapsSocket>();
                    previewing.transform.position = plug.transform.position + plug.transform.forward * 0.05f;
                    previewing.preview = true;
                    previewing.PreviewTick();
                    Drive(", socket preview on");
                    previewing.preview = false;
                    previewing.PreviewTick();
                    UnityEngine.Object.DestroyImmediate(previewing.gameObject);
                }
                cam.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(rt);

                // The check catches the spelling that shipped dead, and only that.
                var target = plugs[0].Target;
                string tpath = AnimationUtility.CalculateTransformPath(target.transform, go.transform);
                var planted = new AnimationClip { name = "planted" };
                planted.SetCurve(tpath, target.GetType(), "material[1]._YAPS_Enabled", AnimationCurve.Constant(0f, 1f, 0f));
                planted.SetCurve(tpath, target.GetType(), "material._YAPS_TagInclude.x", AnimationCurve.Constant(0f, 1f, 0f));
                planted.SetCurve(tpath, target.GetType(), "material._YAPS_Enabled", AnimationCurve.Constant(0f, 1f, 0f));
                var holder = new UnityEditor.Animations.AnimatorController();
                holder.AddLayer("planted");
                holder.layers[0].stateMachine.AddState("planted").motion = planted;
                var caught = YapsToggles.DeadBindings(go, holder);
                Check(caught.Count == 1 && caught[0].EndsWith("material[1]._YAPS_Enabled"),
                    $"the check flags material[1] and passes the plain and vector spellings ({string.Join("; ", caught)})");

                // Once the CCK has generated Advanced Settings, what ships and
                // what survives the next generate are two controllers.
                string genDir = "Assets/AvatarBridgeOutput/ReadoutProbe";
                AssetDatabase.DeleteAsset(genDir);
                AssetDatabase.CreateFolder("Assets/AvatarBridgeOutput", "ReadoutProbe");
                AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(controller), genDir + "/generated_aas.controller");
                var generated = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(genDir + "/generated_aas.controller");
                var genOverride = new AnimatorOverrideController(generated);
                AssetDatabase.CreateAsset(genOverride, genDir + "/generated_aas_overrides.overrideController");
                foreach (var c in new[] { controller, generated })
                    for (int i = c.layers.Length - 1; i >= 0; i--)
                        if (c.layers[i].name == "YAPS readout" || c.layers[i].name.StartsWith("YAPS tags ")) c.RemoveLayer(i);
                var keptOverrides = avatar.overrides;
                avatar.overrides = genOverride;
                Check(YapsOwner.Targets(avatar).Count == 2 && YapsOwner.Shipped(avatar) == generated,
                    "two controllers once Advanced Settings is generated, the generated one shipping");
                foreach (var plug in plugs) YapsNativeBuilder.BakeAndRefreshMenu(plug);
                foreach (var c in new[] { generated, controller })
                    Check(c.layers.Count(l => l.name == "YAPS readout") == 1
                          && c.layers.Count(l => l.name.StartsWith("YAPS tags ")) == plugs.Length,
                        $"readout and tag layers built into the {(c == generated ? "uploaded" : "base")} controller");
                avatar.overrides = keptOverrides;
                AssetDatabase.DeleteAsset(genDir);

                foreach (var plug in plugs)
                {
                    plug.answers = authored[plug];
                    YapsNativeBuilder.BakeAndRefreshMenu(plug);
                }
                AssetDatabase.SaveAssets();
                int choosers = controller.layers.Count(l => l.name.StartsWith("YAPS tags "));
                int wanted = plugs.Count(p => YapsTags.Listed(p.answers).Count >= 2);
                Check(choosers == wanted, $"the authored tags put back ({choosers} chooser(s), {wanted} authored)");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                fail++;
            }
            Log(fail == 0 ? "PASS" : $"FAIL: {fail} check(s)");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }

        static Color32[] Grab(Camera cam, RenderTexture rt)
        {
            cam.Render();
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            RenderTexture.active = null;
            var px = tex.GetPixels32();
            UnityEngine.Object.DestroyImmediate(tex);
            return px;
        }

        static int Diff(Color32[] a, Color32[] b)
        {
            int n = 0;
            for (int i = 0; i < a.Length; i++)
                if (Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b) > 6) n++;
            return n;
        }

        static void Check(bool ok, string what)
        {
            if (!ok) fail++;
            Log((ok ? "ok   " : "FAIL ") + what);
        }

        static void Log(string s) => Debug.Log("[ReadoutProbe] " + s);

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
#endif
