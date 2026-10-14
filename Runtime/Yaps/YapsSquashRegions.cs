// Squash regions painted on a body mesh: authoring data the toolkit and the converter bake
// from in place of finding the parts by bone name. ChilloutVR strips it at upload.
// No SDK dependency.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace AvatarBridge.Yaps
{
    [AddComponentMenu("YAPS/YAPS Squash Regions")]
    [DisallowMultipleComponent]
    public class YapsSquashRegions : MonoBehaviour
    {
        [Serializable]
        public class Pair
        {
            public string name = "Pair";
            // Per vertex of the body mesh, 0 to 255. A is the side toward the avatar's left
            // (its -x), B the other. Bytes, so a body's worth stays small in the scene file.
            public byte[] a = new byte[0], b = new byte[0];
        }

        [Tooltip("The skin the regions are painted on. Clothing over it follows by nearest surface.")]
        public SkinnedMeshRenderer body;

        // Two at most: the shader has room for four parts, a pair each.
        public List<Pair> pairs = new List<Pair>();

        // What the weights were painted against. A mesh that changed since is refused rather
        // than read with every weight on the wrong vertex.
        [HideInInspector] public int vertexCount;
        [HideInInspector] public int fingerprint;
    }
}
