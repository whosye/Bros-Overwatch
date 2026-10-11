Shader "BrosOverwatch/BulletHole"
{
    Properties
    {
        _Opacity ("Opacity", Range(0, 1)) = 1
        _Style ("Style", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Offset -1, -1
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Opacity;
                float _Style;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = (input.uv - 0.5) * 2;
                if (_Style > 0.5)
                {
                    float taper = saturate(1 - p.x * p.x);
                    float jagged = p.y + 0.05 * sin(p.x * 23) * taper;
                    float slit = 1 - smoothstep(0.07 * taper, 0.17 * taper + 0.01, abs(jagged));
                    float edge = 1 - smoothstep(0.24 * taper, 0.65 * taper + 0.01, abs(jagged));
                    float endFade = 1 - smoothstep(0.75, 0.98, abs(p.x));
                    float alpha = max(slit * 0.95, edge * 0.55) * endFade * _Opacity;
                    clip(alpha - 0.003);
                    return half4(lerp(half3(0.27, 0.23, 0.18), half3(0.035, 0.03, 0.025), slit), alpha);
                }
                float angle = atan2(p.y, p.x);
                float roughness = 1 + 0.08 * sin(angle * 7) + 0.05 * sin(angle * 13);
                float radius = length(p) * roughness;
                float center = 1 - smoothstep(0.22, 0.38, radius);
                float rim = (1 - smoothstep(0.45, 0.7, radius)) * 0.55;
                float alpha = max(center * 0.95, rim) * _Opacity;
                clip(alpha - 0.003);
                return half4(lerp(half3(0.20, 0.17, 0.13), half3(0.025, 0.022, 0.02), center), alpha);
            }
            ENDHLSL
        }
    }
}
