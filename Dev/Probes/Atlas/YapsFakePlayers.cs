// Publishes fake CVR player hips, so own-body logic can be tested here.
//
// The resolver tells a wearer's own socket from somebody else's by asking
// ChilloutVR for every player's hip: nearest player to the plug, nearest to
// the socket, same person or not. Those globals are written by the client and
// are ZERO in the editor, so YapsSameBodyAt takes its "claim nothing" exit and
// no socket is ever rejected. Every own-body path therefore behaves in the
// editor the way it behaves for a lone prop, and the only way to see the real
// behaviour has been to upload.
//
// No component. The first version of this was a MonoBehaviour and could not be
// added to a GameObject at all: it lives in the editor assembly, AddComponent
// gave back nothing, and the null landed a line later. Shader globals are
// global, so nothing has to sit in the scene to hold them.
//
// One hip per CVRAvatar in the scene, the wearer's own included. A wearer with
// no hip of their own reads as somebody else and the ownership test never
// fires, which is the failure that looks like a pass.
//
//   Tools/YAPS/Fake CVR player hips
#if CVR_CCK_EXISTS && UNITY_EDITOR
using ABI.CCK.Components;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    [InitializeOnLoad]
    public static class YapsFakePlayers
    {
        const string MenuPath = "Tools/YAPS/Fake CVR player hips";
        const string Pref = "AvatarBridge.FakePlayers";

        // The array is bound at 255 because Unity locks a shader array's size
        // at first bind and the shader declares it at the client's capacity.
        // Binding a short array once makes every later bind silently wrong.
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

        // Scanning the scene per frame is not free and the answer only changes
        // when the hierarchy does, so the list is cached and the transforms are
        // read fresh each publish.
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
            // A body with no humanoid rig still needs a position, and the root
            // is nearer the truth than nothing: an absent hip reads as absent
            // and the ownership test claims nothing.
            return avatar.transform;
        }
    }
}
#endif
