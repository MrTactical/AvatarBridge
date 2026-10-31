#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Known-answer test for texture density across per-slot material copies. Squash gives every
    // renderer slot its own copy of a material, and density was keyed by material, so a texture
    // took the most stretched slot's density with that one slot's area: the "across X m2" figure
    // covered one share, and the resize advice shrank. Copies drawing the same maps at the same
    // tiling are one surface again; a different tiling stays apart.
    public static class TextureDensityTest
    {
        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Texture density")]
        public static void Run()
        {
            int fail = 0;
            int Check(string label, bool ok)
            {
                Debug.Log($"[TextureDensityTest] {(ok ? "ok  " : "WRONG")} {label}");
                return ok ? 0 : 1;
            }

            var shader = Shader.Find("Standard");
            var colour = new Texture2D(1024, 1024);
            var bake = new Texture2D(64, 64) { filterMode = FilterMode.Point };
            Material Copy(Vector2 tiling)
            {
                var m = new Material(shader) { mainTexture = colour };
                m.mainTextureScale = tiling;
                return m;
            }
            var use = new AvatarWeight.TextureUse { Texture = colour, Width = 1024, Height = 1024 };
            var bakeUse = new AvatarWeight.TextureUse { Texture = bake, Width = 64, Height = 64, Data = true };
            var textures = new Dictionary<Texture, AvatarWeight.TextureUse> { [colour] = use, [bake] = bakeUse };

            // Two slots of one surface, unevenly unwrapped. One copy also carries a bake, which
            // must not split the surface. Alone they read 324 and 648 texels per metre.
            var a = Copy(Vector2.one);
            var b = Copy(Vector2.one);
            b.SetTexture("_DetailAlbedoMap", bake);
            AvatarWeight.Densities(textures, new Dictionary<Material, AvatarWeight.Area>
            {
                [a] = new AvatarWeight.Area { World = 1, Uv = 0.4 },
                [b] = new AvatarWeight.Area { World = 1, Uv = 0.1 },
            });
            fail += Check($"copies are one surface: density {use.Density:F1}, want 512", Mathf.Abs(use.Density - 512f) < 0.5f);
            fail += Check($"copies are one surface: area {use.WorldArea:F2}, want 2", Mathf.Abs(use.WorldArea - 2f) < 0.001f);
            fail += Check("a data texture still has no density", bakeUse.Density == 0f);

            // The same map tiled twice over is a different surface, and the lower density decides.
            use.Density = 0f;
            use.WorldArea = 0f;
            AvatarWeight.Densities(textures, new Dictionary<Material, AvatarWeight.Area>
            {
                [Copy(Vector2.one)] = new AvatarWeight.Area { World = 1, Uv = 0.1 },
                [Copy(new Vector2(2, 2))] = new AvatarWeight.Area { World = 1, Uv = 0.4 },
            });
            fail += Check($"another tiling stays apart: density {use.Density:F1}, want 323.8", Mathf.Abs(use.Density - 323.8f) < 0.5f);
            fail += Check($"another tiling stays apart: area {use.WorldArea:F2}, want 1", Mathf.Abs(use.WorldArea - 1f) < 0.001f);

            Debug.Log(fail == 0
                ? "[TextureDensityTest] PASS: per-slot copies measure as one surface."
                : $"[TextureDensityTest] FAIL: {fail} case(s) wrong.");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
