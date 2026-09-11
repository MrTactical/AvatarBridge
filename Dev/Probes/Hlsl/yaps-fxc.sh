#!/usr/bin/env bash
# Compiles the YAPS includes the way the patcher emits them, with fxc, in a
# second. Unity is the only other way to find out, and it costs a reconvert
# per attempt and reports line numbers in a generated file nobody can read.
#
# Catches the whole class of error that has cost the most time here: an
# [unroll] the compiler refuses, which turns a local array index dynamic and
# fails hundreds of lines from the loop that caused it.
#
# What it CANNOT tell you: anything about the result. It proves the code
# compiles, never that a plug bends. That still only happens in game.
set -u
YAPS="$(cd "$(dirname "$0")/../../../Editor/Yaps" && pwd -W)"
FXC="$(ls "/c/Program Files (x86)/Windows Kits/10/bin"/*/x64/fxc.exe 2>/dev/null | tail -1)"
[ -z "$FXC" ] && { echo "fxc.exe not found (Windows SDK)"; exit 1; }

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

# Unity's globals, declared rather than included: UnityCG.cginc drags in half
# the engine and none of it is what is being tested.
cat > "$TMP/stubs.cginc" <<'STUB'
#define UNITY_UV_STARTS_AT_TOP 1
float4 _ScreenParams;
float4 _ProjectionParams;
float4 unity_4LightAtten0;
float4 unity_LightColor[8];
float4 unity_4LightPosX0, unity_4LightPosY0, unity_4LightPosZ0;
float4x4 unity_ObjectToWorld, unity_WorldToObject;
float4 _Time;
STUB

emit() {
    {
        echo '#include "stubs.cginc"'
        for f in yaps_props yaps_atlas yaps_resolve yaps_deform yaps_socket; do
            echo "#include \"$f.cginc\""
        done
        cat
    } > "$TMP/$1.hlsl"
}

emit plug <<'BODY'
float4 main(uint id : SV_VertexID) : SV_POSITION
{
    YapsSocket s = YapsResolveSocket(float3(0,0,0), float3(0,0,1), float3(0,1,0), 0.2);
    YapsChain c = YapsResolveChain(float3(0,0,0), float3(0,0,1), 0.2, float3(1e9,1e9,1e9));
    return float4(s.position + s.forward + c.position[0],
                  s.engaged + s.tier + c.arc[1] + c.count);
}
BODY

# The socket writer's side of the protocol, which no plug include reaches.
emit writer <<'BODY'
float4 main(uint id : SV_VertexID) : SV_POSITION
{
    int cx, cy;
    YapsAtlasCellPixels(id, 3, YapsAtlasLayoutNow(), cx, cy);
    float4 o = YapsOwnerEncode(YapsOwnerOf(_YAPS_Owner));
    float kind; int index; bool oneWay;
    YapsFacingDecode(YapsFacingEncode(id % 3, id >> 2), kind, index, oneWay);
    return float4(o.xyz + YapsTagsEncode(id).xyz, YapsOwnerDecode(o) + cx + cy + kind + index
                  + YapsAtlasWidthPx() + YapsAtlasHeightPx());
}
BODY

fail=0
for t in plug writer; do
    # X3556 is a note about integer modulus being slow, and X4008 a division
    # the stubs fold to zero. Neither is a defect in the shader.
    out="$("$FXC" -nologo -T vs_5_0 -E main -I "$YAPS" -I "$TMP" \
           "$TMP/$t.hlsl" -Fo "$TMP/$t.cso" 2>&1 | grep -vE "X3556|X4008|^compilation object save succeeded")"
    if echo "$out" | grep -q "error"; then
        echo "--- $t FAILED"; echo "$out"; fail=1
    else
        echo "--- $t ok"
        [ -n "$out" ] && echo "$out"
    fi
done
exit $fail
