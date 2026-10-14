// The add-on's sections in the Avatar Tester's Interaction lab. The tester ships in the public
// package and this file must build without it, so it is reached by name.
#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ABI.CCK.Components;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace AvatarBridge
{
    static class YapsTesterSections
    {
        [InitializeOnLoadMethod]
        static void Register()
        {
            // No type is an add-on installed alone, which users do. A type without its Add is a
            // rename, and says so rather than dropping both sections quietly.
            var sections = typeof(YapsTesterSections).Assembly.GetType("AvatarBridge.TesterSections");
            if (sections == null) return;
            var add = sections.GetMethod("Add", BindingFlags.Public | BindingFlags.Static);
            if (add == null)
            {
                Debug.LogError("[YAPS] The Avatar Tester has no TesterSections.Add, so its squash and plug sections are missing.");
                return;
            }
            add.Invoke(null, new object[] { "Squash", (Func<GameObject, bool, Action, VisualElement>) Squash, (Func<GameObject, int>) SquashPrint });
            add.Invoke(null, new object[] { "Plugs and sockets", (Func<GameObject, bool, Action, VisualElement>) Sockets, (Func<GameObject, int>) SocketPrint });
        }

        static YapsSquashSettings SquashOf(GameObject avatar) => avatar != null ? YapsSquash.SettingsOf(avatar.transform) : null;

        static VisualElement Squash(GameObject avatar, bool live, Action refresh)
        {
            var settings = SquashOf(avatar);
            if (settings == null) return null;
            bool on = YapsSquashPreview.On == settings;
            var fold = new BridgeElements.Card("Squash", on ? "previewing" : null, true).Nested().Remember("Tester.Squash");
            YapsSquashPreview.Controls(fold.Body, settings, refresh).Add(BridgeElements.Btn("Tune it", () => Selection.activeObject = settings,
                "Selects the YAPS Squash component, where the knobs and Build are."));
            return fold;
        }

        static int SquashPrint(GameObject avatar)
        {
            var settings = SquashOf(avatar);
            if (settings == null) return 0;
            return (YapsSquash.Has(avatar.transform) ? 2 : 1) + (YapsSquashPreview.On == settings ? 4 : 0);
        }

        // On the avatar, or on nothing that is one (props and test objects). The preview's own
        // socket and plug have their own row.
        static List<T> InScope<T>(GameObject avatar) where T : Component =>
            Object.FindObjectsOfType<T>(true).Where(c => c != null
                && c.name != YapsPreview.SocketName && c.name != YapsPreview.PlugName
                && (avatar == null || c.transform.IsChildOf(avatar.transform) || c.GetComponentInParent<CVRAvatar>(true) == null)).ToList();

        static VisualElement Sockets(GameObject avatar, bool live, Action refresh)
        {
            var sockets = InScope<YapsSocket>(avatar);
            var plugs = InScope<YapsPlug>(avatar);
            bool testSocket = YapsPreview.TestSocketInScene, testPlug = YapsPreview.TestPlugInScene;
            if (sockets.Count == 0 && plugs.Count == 0 && !testSocket && !testPlug) return null;

            var fold = new BridgeElements.Card("Plugs and sockets", $"{BridgeElements.Count(sockets.Count, "socket", "sockets")} · {BridgeElements.Count(plugs.Count, "plug", "plugs")}", true)
                .Nested().Remember("Tester.Plugs and sockets");
            fold.Body.Add(BridgeElements.Hint(live
                ? "Bends the baked plugs toward a socket, in Play mode too, where a plug a toggle brings in can be seen. It stops when Play ends."
                : "Bends the baked plugs toward a socket. A test plug or socket tries one end on its own.",
                "Contacts are the game's, so a socket's shapes that go through one do not move here."));

            bool canDrop = YapsPreview.CanDropTestPlug;
            foreach (var s in sockets)
            {
                var socket = s;
                var preview = BridgeElements.Btn(socket.preview ? "Stop" : "Preview", () =>
                {
                    YapsPreview.Set(socket, !socket.preview);
                    refresh();
                }, socket.preview ? "Previewing. Click again to stop." : "Bends the baked plugs near it toward this socket.");
                preview.EnableInClassList("ab-on", socket.preview);
                var drop = BridgeElements.Btn("Drop a test plug", () =>
                {
                    YapsPreview.Set(socket, true, spawnPlugIfNone: false);
                    YapsPreview.DropTestPlug(socket);
                    refresh();
                }, canDrop ? "A test plug in front of it, even with a baked one near."
                    : "In Play mode the test plug comes from the plug prop prefab; create it under Tools ▸ YAPS ▸ Setup ▸ Test it.");
                drop.SetEnabled(canDrop);
                fold.Body.Add(Line(socket.name, preview, drop));
            }
            foreach (var p in plugs)
            {
                var plug = p;
                bool baked = YapsPreview.PlugFrame(plug, out _, out _, out _, out _);
                var buttons = new List<VisualElement>();
                foreach (var kind in new[] { YapsSocket.SocketKind.Hole, YapsSocket.SocketKind.Ring })
                {
                    var k = kind;
                    var b = BridgeElements.Btn(k == YapsSocket.SocketKind.Hole ? "Into a hole" : "Through a ring", () =>
                    {
                        YapsPreview.DropTestSocket(plug, k);
                        refresh();
                    }, !baked ? "Bake first" : k == YapsSocket.SocketKind.Hole
                        ? "A test hole ahead: it closes around the shaft and stops it."
                        : "A test ring ahead: the shaft slides through it.");
                    b.SetEnabled(baked);
                    buttons.Add(b);
                }
                fold.Body.Add(Line(plug.name, buttons.ToArray()));
            }
            var tidy = new List<VisualElement>();
            if (testSocket) tidy.Add(BridgeElements.Btn("Remove the test socket", () => { YapsPreview.RemoveTestSocket(); refresh(); }));
            if (testPlug) tidy.Add(BridgeElements.Btn("Take the test plug away", () => { YapsPreview.RemoveTestPlug(); refresh(); }));
            if (tidy.Count > 0) fold.Body.Add(BridgeElements.ButtonRow(tidy.ToArray()));
            return fold;
        }

        static VisualElement Line(string name, params VisualElement[] buttons)
        {
            var label = new Label(name) { tooltip = name };
            label.AddToClassList("ab-grow");
            return BridgeElements.Row(new VisualElement[] { label }.Concat(buttons).ToArray());
        }

        static int SocketPrint(GameObject avatar)
        {
            unchecked
            {
                int hash = (YapsPreview.TestSocketInScene ? 1 : 0) + (YapsPreview.TestPlugInScene ? 2 : 0);
                foreach (var s in Object.FindObjectsOfType<YapsSocket>(true)) hash = hash * 31 + s.GetInstanceID() * (s.preview ? 2 : 1);
                foreach (var p in Object.FindObjectsOfType<YapsPlug>(true)) hash = hash * 31 + p.GetInstanceID();
                return hash;
            }
        }
    }
}
#endif
