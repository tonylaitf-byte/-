Shader "Puppet/Moonlit Architecture PBR"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.2, 0.2, 0.2, 1)
        _AccentColor ("Accent Color", Color) = (0.1, 0.1, 0.1, 1)
        _Mode ("Material Mode", Float) = 0
        _Tiling ("Detail Scale", Range(0.05, 8)) = 1
        _Smoothness ("Smoothness", Range(0, 1)) = 0.35
        _Metallic ("Metallic", Range(0, 1)) = 0
        _EmissionStrength ("Emission Strength", Range(0, 4)) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 350

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0

        fixed4 _BaseColor;
        fixed4 _AccentColor;
        half _Mode;
        half _Tiling;
        half _Smoothness;
        half _Metallic;
        half _EmissionStrength;

        struct Input { float3 worldPos; };

        float Hash21(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }

        float Noise(float2 p)
        {
            float2 i = floor(p);
            float2 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(
                lerp(Hash21(i), Hash21(i + float2(1, 0)), f.x),
                lerp(Hash21(i + float2(0, 1)), Hash21(i + float2(1, 1)), f.x),
                f.y
            );
        }

        float Fbm(float2 p)
        {
            float n = 0;
            n += Noise(p) * 0.55;
            n += Noise(p * 2.13) * 0.27;
            n += Noise(p * 4.37) * 0.12;
            return n;
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float3 world = IN.worldPos;
            float2 p = world.xz * _Tiling;
            float variation = Fbm(p * 1.8) - 0.5;
            float seams = 0;
            float wet = 0;
            float metal = _Metallic;
            float smoothness = _Smoothness;
            fixed3 albedo = _BaseColor.rgb;
            fixed3 emission = 0;

            // 0 = aged timber: long grain and narrow plank gaps.
            if (_Mode < 0.5)
            {
                float grain = sin((world.y + world.z * 0.32) * _Tiling * 34.0 + variation * 9.0);
                float board = abs(frac(world.y * _Tiling * 1.35) - 0.5);
                seams = smoothstep(0.455, 0.49, board);
                albedo = lerp(_BaseColor.rgb * (0.72 + grain * 0.14), _AccentColor.rgb, seams);
                smoothness = lerp(_Smoothness, 0.16, seams);
            }
            // 1 = roof tiles: staggered rows with darker recessed joints.
            else if (_Mode < 1.5)
            {
                float2 tile = p * float2(1.15, 2.05);
                tile.x += fmod(floor(tile.y), 2.0) * 0.5;
                float2 cell = frac(tile);
                float edge = max(abs(cell.x - 0.5) * 1.7, abs(cell.y - 0.5) * 2.0);
                seams = smoothstep(0.71, 0.86, edge);
                float ridge = sin(cell.x * 6.28318) * 0.12;
                albedo = lerp(_BaseColor.rgb * (0.84 + ridge + variation * 0.16), _AccentColor.rgb, seams);
                smoothness = lerp(_Smoothness, 0.12, seams);
            }
            // 2 = plaster/stone: weathering, dampness and shallow masonry joints.
            else if (_Mode < 2.5)
            {
                float2 brick = world.xy * _Tiling * float2(0.75, 1.15);
                brick.x += fmod(floor(brick.y), 2.0) * 0.5;
                float2 cell = frac(brick);
                seams = max(smoothstep(0.44, 0.49, abs(cell.x - 0.5)), smoothstep(0.43, 0.49, abs(cell.y - 0.5)));
                float damp = smoothstep(0.56, 0.9, Fbm(p * 0.7));
                albedo = lerp(_BaseColor.rgb * (0.82 + variation * 0.24), _AccentColor.rgb, seams);
                albedo *= 1.0 - damp * 0.23;
                smoothness = lerp(_Smoothness, _Smoothness * 0.35, damp);
            }
            // 3 = aged gilt bronze: high metal response with worn dark patches.
            else if (_Mode < 3.5)
            {
                float patina = smoothstep(0.62, 0.9, Fbm(p * 1.45));
                albedo = lerp(_BaseColor.rgb * (0.82 + variation * 0.25), _AccentColor.rgb, patina);
                metal = 0.88;
                smoothness = lerp(_Smoothness, 0.25, patina);
            }
            // 4 = foliage: broken moonlit hue and low surface reflection.
            else if (_Mode < 4.5)
            {
                float leaf = Fbm(p * 5.0);
                albedo = lerp(_AccentColor.rgb, _BaseColor.rgb, leaf);
                smoothness = _Smoothness * 0.45;
            }
            // 5 = lantern paper: fine ribs and restrained warm emission.
            else
            {
                float ribs = pow(abs(sin(world.y * _Tiling * 22.0)), 18.0);
                albedo = lerp(_BaseColor.rgb * (0.85 + variation * 0.14), _AccentColor.rgb, ribs);
                emission = albedo * _EmissionStrength;
                smoothness = _Smoothness;
            }

            o.Albedo = saturate(albedo);
            o.Metallic = metal;
            o.Smoothness = saturate(smoothness);
            o.Occlusion = 0.78 + (1.0 - seams) * 0.22;
            o.Emission = emission;
            o.Normal = normalize(float3(variation * 0.28, seams * 0.24, 1.0));
        }
        ENDCG
    }
    FallBack "Standard"
}
