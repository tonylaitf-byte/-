Shader "Puppet/PBR Texture Surface"
{
    Properties
    {
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _MainTex ("Albedo", 2D) = "white" {}
        _NormalMap ("Normal Map", 2D) = "bump" {}
        _RoughnessMap ("Roughness", 2D) = "white" {}
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Smoothness ("Smoothness Multiplier", Range(0, 1)) = 1
        _BumpScale ("Normal Strength", Range(0, 2)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 350

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _NormalMap;
        sampler2D _RoughnessMap;
        fixed4 _Color;
        half _Metallic;
        half _Smoothness;
        half _BumpScale;

        struct Input
        {
            float2 uv_MainTex;
        };

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 albedo = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            half roughness = tex2D(_RoughnessMap, IN.uv_MainTex).r;
            o.Albedo = albedo.rgb;
            o.Normal = UnpackScaleNormal(tex2D(_NormalMap, IN.uv_MainTex), _BumpScale);
            o.Metallic = _Metallic;
            o.Smoothness = saturate((1.0h - roughness) * _Smoothness);
            o.Occlusion = 1.0h;
            o.Alpha = albedo.a;
        }
        ENDCG
    }
    FallBack "Standard"
}
