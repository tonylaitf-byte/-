Shader "Puppet/Refined Surface Preview"
{
    Properties
    {
        _Color ("Surface Color", Color) = (.1,.2,.4,1)
        _MainTex ("Original Albedo", 2D) = "white" {}
        _Metallic ("Metallic", Range(0,1)) = 0
        _Glossiness ("Smoothness", Range(0,1)) = .35
        _Profile ("Cloth Metal Skin Hair Lacquer Steel", Float) = 0
        _DetailScale ("UV Detail Scale", Float) = 90
        _DetailStrength ("Micro Detail", Range(0,1)) = .3
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0
        sampler2D _MainTex;
        fixed4 _Color;
        half _Metallic, _Glossiness, _Profile, _DetailScale, _DetailStrength;
        struct Input { float2 uv_MainTex; float3 viewDir; };
        float Hash(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            // UV detail stays attached to the original skinned surface. Fade it
            // before it reaches subpixel frequency to avoid crawling on stage.
            float2 uv = IN.uv_MainTex * _DetailScale;
            float fade = 1 - smoothstep(.2, .9, max(fwidth(uv.x), fwidth(uv.y)));
            float detail = fade * _DetailStrength;
            float noise = Hash(floor(uv * 2)) - .5;
            float2 weave = sin(uv * 6.2831853);
            half3 baseColor = tex2D(_MainTex, IN.uv_MainTex).rgb * _Color.rgb;
            half smoothness = _Glossiness;
            half3 normal = half3(0,0,1);
            half3 emission = 0;
            if (_Profile < .5)
            {
                baseColor *= 1 + (weave.x * weave.y * .045 + noise * .025) * detail;
                normal.xy = weave * .08 * detail;
                smoothness += weave.x * weave.y * .05 * detail;
                half rim = pow(1 - saturate(dot(normalize(IN.viewDir), normal)), 4);
                emission = baseColor * rim * .035;
            }
            else if (_Profile < 1.5)
            {
                baseColor *= 1 + noise * .07 * detail;
                smoothness += noise * .12 * detail;
                normal.xy = half2(weave.x, noise) * .018 * detail;
            }
            else if (_Profile < 2.5)
            {
                baseColor *= 1 + noise * .035 * detail;
                normal.xy = half2(noise, weave.y) * .018 * detail;
                smoothness += noise * .045 * detail;
            }
            else if (_Profile < 3.5)
            {
                baseColor *= 1 + weave.x * .07 * detail;
                normal.x = weave.x * .07 * detail;
            }
            else if (_Profile > 4.5)
            {
                smoothness += noise * .08 * detail;
                normal.x = weave.x * .012 * detail;
            }
            o.Albedo = saturate(baseColor);
            o.Metallic = _Metallic;
            o.Smoothness = saturate(smoothness);
            o.Normal = normalize(normal);
            o.Occlusion = 1;
            o.Emission = emission;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Standard"
}
