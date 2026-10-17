// Runs the scratch YapsPortalExit once per key, for PortalDirectoryProbe.
// Dev only. Beside the scratch yaps_atlas.cginc, hence the bare include.
//
// Pass 0 writes four pixels of summary, then four a query, row by row from
// the top of a target the atlas's size, so the layout is the atlas's own:
//   0  exit position as decoded, hit
//   1  exit facing as decoded, the key's tag
//   2  home A and home B, column and row
//   3  owner and number decoded where a tag answered, which home, or -1
// Everything but the hit comes from the real encoders, so the probe can
// predict what the draw order left readable without re-deriving the hash.
// Pass 1 puts one sentinel in the bottom right corner of the atlas, so the
// probe can tell which way a readback runs.
Shader "Hidden/YapsPortalDirectoryReader"
{
    SubShader
    {
        ZTest Always ZWrite Off Cull Off
        CGINCLUDE
        #pragma target 5.0
        #include "UnityCG.cginc"
        #include "yaps_atlas.cginc"

        // Two a query: (owner, number, 0, 0), then (entrance, 0).
        StructuredBuffer<float4> _Queries;

        struct v2c { float4 pos : SV_POSITION; nointerpolation float4 value : TEXCOORD0; };

        float4 PixelAt(uint i)
        {
            uint w = (uint) _ScreenParams.x;
            return float4(YapsAtlasToClip(float2(i % w, i / w) + 0.5), UNITY_NEAR_CLIP_VALUE, 1);
        }

        float4 Summary(uint k)
        {
            return k == 0 ? float4(YapsAtlasFits() ? 1 : 0, YapsAtlasWidthPx(), YapsAtlasHeightPx(), 7777)
                 : k == 1 ? float4(YapsDirPerRow(), YAPS_ATLAS_DIR_ROWS, YAPS_ATLAS_DIR_PX, YAPS_ATLAS_DIR_CELL)
                 : 0;
        }

        float4 Query(uint q, uint k)
        {
            float4 a = _Queries[2 * q];
            float3 at = _Queries[2 * q + 1].xyz, fwd = 0;
            int owner = (int) a.x, n = (int) a.y;
            bool hit = YapsPortalExit(owner << 4 | n, at, fwd);

            uint pxA, rowA, pxB, rowB;
            YapsDirHome(owner, n, 0, pxA, rowA);
            YapsDirHome(owner, n, 1, pxB, rowB);
            float tag = YapsDirTag(owner, n);
            // The lookup's own order, A then B: where a tag answered is where
            // the owner and number were compared, hit or not.
            bool onA = YapsDirTagged(YAPS_ATLAS_LOAD(pxA, YapsAtlasRow(rowA)), tag);
            bool onB = !onA && YapsDirTagged(YAPS_ATLAS_LOAD(pxB, YapsAtlasRow(rowB)), tag);
            uint px = onA ? pxA : pxB;
            int py = YapsAtlasRow(onA ? rowA : rowB);
            float gotOwner = YapsOwnerDecode(YAPS_ATLAS_LOAD(px + 3, py));
            float gotN = round(saturate(YAPS_ATLAS_LOAD(px + 1, py).a) * YAPS_ATLAS_OWNERMAX) - 1;
            bool tagged = onA || onB;
            return k == 0 ? float4(at, hit ? 1 : 0)
                 : k == 1 ? float4(fwd, tag)
                 : k == 2 ? float4(pxA, rowA, pxB, rowB)
                 : float4(tagged ? gotOwner : -1, tagged ? gotN : -1, onA ? 0 : onB ? 1 : -1, 0);
        }

        v2c vertQuery(uint id : SV_VertexID)
        {
            v2c o;
            o.pos = PixelAt(id);
            if (id < 4) o.value = Summary(id);
            else o.value = Query(id / 4 - 1, id % 4);
            return o;
        }

        v2c vertSentinel(uint id : SV_VertexID)
        {
            v2c o;
            o.pos = float4(YapsAtlasToClip(_ScreenParams.xy - 0.5), UNITY_NEAR_CLIP_VALUE, 1);
            o.value = float4(1, 0, 1, 1);
            return o;
        }

        float4 frag(v2c i) : SV_Target { return i.value; }
        ENDCG

        Pass
        {
            CGPROGRAM
            #pragma vertex vertQuery
            #pragma fragment frag
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vertSentinel
            #pragma fragment frag
            ENDCG
        }
    }
}
