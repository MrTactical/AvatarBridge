// Wires a scene for the own-body atlas test, and says what it found.
//
// The rule being tested lives in the SHIPPED resolver, so the spike rig is no
// use here: YapsSpikePlug.shader reimplements the reader rather than including
// yaps_resolve.cginc, and testing a copy proves nothing about the original.
// The instrument has to be a real patched plug on a real avatar.
//
// The hips themselves come from YapsFakePlayers, which needs nothing in the
// scene. This reads back the three things that decide whether the test runs at
// all: two bodies, the fake hips switched on, and the two material flags. Each
// of them fails by rejecting nothing, which reads exactly like a pass.
//
//   Tools/YAPS/Own-body test: wire fake players
#if CVR_CCK_EXISTS && UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ABI.CCK.Components;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    public static class OwnBodyScene
    {
        [MenuItem("Tools/YAPS/Own-body test: wire fake players")]
        static void Wire()
        {
            var avatars = Object.FindObjectsOfType<CVRAvatar>(true);
            if (avatars.Length == 0)
            {
                Debug.Log("OWN-BODY: no CVRAvatar in the scene. The test needs two bodies: " +
                          "an avatar carrying a patched plug, and a second body carrying a socket.");
                return;
            }

            int hips = avatars.Count(a => YapsFakePlayers.HipOf(a) != null);

            var report = new StringBuilder();
            report.AppendLine($"OWN-BODY: {hips} hip(s) available, one per avatar.");
            report.AppendLine(YapsFakePlayers.On
                ? "  Fake players are ON, so the ownership test runs."
                : "  Fake players are OFF. Turn them on: Tools/YAPS/Fake CVR player hips. " +
                  "Until then the hips are zero, YapsSameBodyAt claims nothing, and NO socket " +
                  "is rejected: the test would pass without running.");
            if (avatars.Length < 2)
            {
                report.AppendLine("  ONLY ONE BODY. The ownership test needs a second: duplicate the " +
                                  "avatar and move it a couple of metres away, then run this again. " +
                                  "With one body every socket in reach is the wearer's own.");
            }

            foreach (var avatar in avatars)
            {
                report.AppendLine($"  {avatar.name}");
                var hip = YapsFakePlayers.HipOf(avatar);
                report.AppendLine($"    hip: {(hip != null ? hip.name : "NONE")}");
                var plugs = PatchedMaterials(avatar.gameObject);
                if (plugs.Count == 0)
                {
                    report.AppendLine("    no patched plug material found; this body can only be a socket carrier");
                    continue;
                }
                foreach (var m in plugs)
                {
                    // Both flags have to be right or the test measures nothing.
                    // SelfTag under zero switches ownership off entirely, and
                    // the atlas off sends the plug back to lights and contacts.
                    float self = m.GetFloat("_YAPS_SelfTag");
                    float atlas = m.HasProperty("_YAPS_UseAtlas") ? m.GetFloat("_YAPS_UseAtlas") : -1f;
                    report.AppendLine($"    {m.name}: SelfTag {self:0.##}" +
                        (self >= 0 ? " (ownership asked)" : " (OWNERSHIP OFF, nothing will be rejected)") +
                        $", atlas {atlas:0.##}" + (atlas > 0.5f ? "" : " (ATLAS OFF, the rule never runs)"));
                }
            }

            report.AppendLine();
            report.AppendLine("Then, in Play mode:");
            report.AppendLine("  1. Set the plug material's View to \"Resolved by\". Full length is the atlas,");
            report.AppendLine("     three quarters a marker light, a half the contact channel, a quarter nobody.");
            report.AppendLine("  2. Far apart: the plug should NOT take its own socket, and should find the");
            report.AppendLine("     other body's. \"Atlas taps\" reads two thirds for a socket rejected as");
            report.AppendLine("     out of reach or on your own body.");
            report.AppendLine("  3. Bring the plug's own socket inside engagement range. It should now be");
            report.AppendLine("     taken, and \"Resolved by\" should read full.");
            report.AppendLine("  4. Both at once, which is the whole question: own socket near, other body's");
            report.AppendLine("     further off, and whether the chain still holds the second.");

            Debug.Log(report.ToString());
        }

        static List<Material> PatchedMaterials(GameObject root)
        {
            var found = new List<Material>();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var m in renderer.sharedMaterials)
                {
                    if (m == null || found.Contains(m)) { continue; }
                    if (m.HasProperty("_YAPS_SelfTag")) { found.Add(m); }
                }
            }
            return found;
        }
    }
}
#endif
