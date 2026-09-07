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
// This fills them in. Put it on any object, list one transform per player, and
// the ownership test runs for real in edit mode and in Play.
//
// The array is bound at 255 because Unity locks a shader array's size at first
// bind and the shader declares it at the client's capacity. Binding a short
// array once makes every later bind silently wrong.
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace AvatarBridge.Regression
{
    [ExecuteAlways]
    [AddComponentMenu("")]
    public class YapsFakePlayers : MonoBehaviour
    {
        const int Capacity = 255;

        [Tooltip("One transform per player, standing in for that player's hip. " +
                 "The wearer belongs in this list too: without a hip of their own " +
                 "the wearer's sockets read as somebody else's.")]
        public List<Transform> hips = new List<Transform>();

        static readonly Vector4[] buffer = new Vector4[Capacity];
        static readonly int HipsId = Shader.PropertyToID("_CVR_PlayerHipPositions");
        static readonly int ParamsId = Shader.PropertyToID("CVRGlobalParams1");

        void OnEnable() { Publish(); }
        void Update() { Publish(); }

        void OnDisable()
        {
            // Back to what the editor looks like without this component, so a
            // disabled probe does not leave a scene half faked.
            for (int i = 0; i < Capacity; i++) { buffer[i] = Vector4.zero; }
            Shader.SetGlobalVectorArray(HipsId, buffer);
            Shader.SetGlobalVector(ParamsId, Vector4.zero);
        }

        void Publish()
        {
            int count = 0;
            for (int i = 0; i < hips.Count && count < Capacity; i++)
            {
                var hip = hips[i];
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
    }
}
#endif
