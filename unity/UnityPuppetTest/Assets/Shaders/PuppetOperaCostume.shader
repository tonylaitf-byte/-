Shader "Puppet/Opera Costume PBR"
{
    Properties
    {
        _RobeColor ("Robe Color", Color) = (0.035, 0.10, 0.24, 1)
        _TrimColor ("Trim Color", Color) = (0.06, 0.14, 0.31, 1)
        _GoldColor ("Gold Embroidery", Color) = (0.82, 0.44, 0.075, 1)
        _SkinColor ("Face Color", Color) = (0.72, 0.42, 0.28, 1)
        _MinY ("Mesh Bottom", Float) = -1
        _MaxY ("Mesh Top", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0

        fixed4 _RobeColor;
        fixed4 _TrimColor;
        fixed4 _GoldColor;
        fixed4 _SkinColor;
        float _MinY;
        float _MaxY;

        struct Input { float3 worldPos; };

        float Hash21(float2 p)
        {
            p = frac(p * float2(127.1, 311.7));
            p += dot(p, p + 19.19);
            return frac(p.x * p.y);
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            // Object-space mapping keeps the costume details attached while the
            // skeleton moves the puppet during live camera control.
            float3 local = mul(unity_WorldToObject, float4(IN.worldPos, 1)).xyz;
            float h = saturate((local.y - _MinY) / max(_MaxY - _MinY, 0.001));
            float weave = Hash21(floor(local.xz * 20.0 + local.y * 3.0)) - 0.5;
            // Keep the palette deliberately restrained. The FBX shares one
            // material across the robe, hair and ornaments, so height-based
            // colour bands produce visible seams while the rig is animated.
            float brocade = step(0.965, Hash21(floor(local.xy * 5.0)));
            fixed3 robe = _RobeColor.rgb * (0.91 + weave * 0.12);
            fixed3 color = lerp(robe, _TrimColor.rgb, 0.09);
            color = lerp(color, _GoldColor.rgb, brocade * 0.45);

            o.Albedo = saturate(color);
            o.Metallic = brocade * 0.62;
            o.Smoothness = lerp(0.32, 0.58, brocade);
            o.Occlusion = 0.9;
            o.Normal = normalize(float3(weave * 0.18, brocade * 0.13, 1));
        }
        ENDCG
    }
    FallBack "Standard"
}
