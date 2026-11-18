// Publishes fake CVR player hips, so own-body logic can be tested here.
//
// The client's player-hip globals are zero in the editor, so YapsSameBodyAt
// claims nothing and own-body paths act as a lone prop.
// No component: a MonoBehaviour in the editor assembly cannot be added
// (AddComponent returns null), and globals need no scene object.
// One hip per CVRAvatar, the wearer included: without its own hip the wearer
// reads as somebody else and the test passes without firing.
//
//   Tools/Avatar Bridge/Dev/Atlas/Fake CVR player hips
#if CVR_CCK_EXISTS && UNITY_EDITOR
using ABI.CCK.Components;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    [InitializeOnLoad]
    public static class YapsFakePlayers
    {
        const string MenuPath = "Tools/Avatar Bridge/Dev/Atlas/Fake CVR player hips";
        const string Pref = "AvatarBridge.FakePlayers";

        // Unity locks a shader array's size at first bind; a short one breaks every later bind.
        const int Capacity = 255;

        static readonly Vector4[] buffer = new Vector4[Capacity];
        static readonly int HipsId = Shader.PropertyToID("_CVR_PlayerHipPositions");
        static readonly int ParamsId = Shader.PropertyToID("CVRGlobalParams1");

        static CVRAvatar[] avatars = new CVRAvatar[0];

        public static bool On
        {
            get { return EditorPrefs.GetBool(Pref, false); }
        }

        static YapsFakePlayers()
        {
            EditorApplication.delayCall += () => Apply(On);
        }

        [MenuItem(MenuPath)]
        static void Toggle() { Apply(!On); }

        [MenuItem(MenuPath, true)]
        static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, On);
            return true;
        }

        static void Apply(bool on)
        {
            EditorPrefs.SetBool(Pref, on);
            Menu.SetChecked(MenuPath, on);

            EditorApplication.update -= Publish;
            EditorApplication.hierarchyChanged -= Rescan;
            if (!on)
            {
                Clear();
                return;
            }
            Rescan();
            EditorApplication.update += Publish;
            EditorApplication.hierarchyChanged += Rescan;
            Debug.Log($"FAKE PLAYERS on: {Count()} hip(s). " +
                      "Own-body rejection now runs in the editor.");
        }

        // Cached per hierarchy change; transforms are read fresh each publish.
        static void Rescan()
        {
            avatars = Object.FindObjectsOfType<CVRAvatar>(true);
        }

        static int Count()
        {
            int n = 0;
            foreach (var avatar in avatars) { if (HipOf(avatar) != null) { n++; } }
            return n;
        }

        static void Publish()
        {
            int count = 0;
            foreach (var avatar in avatars)
            {
                if (count >= Capacity) { break; }
                var hip = HipOf(avatar);
                if (hip == null) { continue; }
                var p = hip.position;
                // A hip at the world origin reads as absent: the resolver skips
                // any entry whose square length is under 1e-6.
                buffer[count++] = new Vector4(p.x, p.y, p.z, 1f);
            }
            for (int i = count; i < Capacity; i++) { buffer[i] = Vector4.zero; }

            Shader.SetGlobalVectorArray(HipsId, buffer);
            Shader.SetGlobalVector(ParamsId, new Vector4(0f, count, 0f, 0f));
        }

        static void Clear()
        {
            for (int i = 0; i < Capacity; i++) { buffer[i] = Vector4.zero; }
            Shader.SetGlobalVectorArray(HipsId, buffer);
            Shader.SetGlobalVector(ParamsId, Vector4.zero);
        }

        public static Transform HipOf(CVRAvatar avatar)
        {
            if (avatar == null) { return null; }
            var animator = avatar.GetComponent<Animator>();
            if (animator != null && animator.isHuman)
            {
                var hip = animator.GetBoneTransform(HumanBodyBones.Hips);
                if (hip != null) { return hip; }
            }
            // No humanoid rig: the root beats an absent hip, which claims nothing.
            return avatar.transform;
        }
    }
}
#endif
