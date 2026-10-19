Shader "Puppet/Procedural Wet Courtyard Stone"
{
    Properties
    {
        _StoneColor ("Stone Color", Color) = (0.16, 0.19, 0.22, 1)
        _MortarColor ("Mortar Color", Color) = (0.035, 0.045, 0.06, 1)
        _Tiling ("Stone Tiling", Range(0.1, 4)) = 0.65
        _Wetness ("Wetness", Range(0, 1)) = 0.62
        _Smoothness ("Base Smoothness", Range(0, 1)) = 0.58
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0

        fixed4 _StoneColor;
        fixed4 _MortarColor;
        half _Tiling;
        half _Wetness;
        half _Smoothness;

        struct Input
        {
            float3 worldPos;
        };

        float Hash21(float2 p)
        {
            p = frac(p * float2(123.34, 345.45));
            p += dot(p, p + 34.345);
            return frac(p.x * p.y);
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float2 p = IN.worldPos.xz * _Tiling;
            float row = floor(p.y);
            p.x += fmod(row, 2.0) * 0.5;

            float2 cell = floor(p);
            float2 local = frac(p) - 0.5;
            float stoneRandom = Hash21(cell);
            float grain = Hash21(floor(IN.worldPos.xz * 9.0));
            float edge = max(abs(local.x), abs(local.y));
            float mortar = smoothstep(0.43, 0.49, edge);

            float shade = (stoneRandom - 0.5) * 0.18 + (grain - 0.5) * 0.09;
            fixed3 stone = _StoneColor.rgb * (1.0 + shade);
            float puddle = smoothstep(0.72, 0.96, Hash21(floor(IN.worldPos.xz * 0.33)));

            o.Albedo = lerp(stone, _MortarColor.rgb, mortar);
            o.Metallic = 0.03;
            o.Smoothness = lerp(_Smoothness + puddle * _Wetness * 0.32, 0.16, mortar);
            o.Occlusion = lerp(1.0, 0.72, mortar);
            o.Normal = normalize(float3(
                (Hash21(cell + float2(1, 0)) - Hash21(cell + float2(-1, 0))) * 0.35,
                1.0,
                (Hash21(cell + float2(0, 1)) - Hash21(cell + float2(0, -1))) * 0.35
            ));
        }
        ENDCG
    }
    FallBack "Standard"
}
