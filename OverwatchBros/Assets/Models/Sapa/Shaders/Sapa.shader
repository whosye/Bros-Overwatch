Shader "BrosOverwatch/Sapa"
{
    Properties
    {
        _Style("OWM style", Float) = 0
        _Cutoff("Alpha cutoff", Float) = 0.01
        _DebugSurface("Debug albedo", Float) = 0
        [MainTexture] _BaseMap("Main texture", 2D) = "white" {}
        [MainColor] _BaseColor("Main color", Color) = (1,1,1,1)
        [HideInInspector] _SrcBlend("Source blend", Float) = 1
        [HideInInspector] _DstBlend("Destination blend", Float) = 0
        [HideInInspector] _ZWrite("Depth write", Float) = 1
        [HideInInspector] _Cull("Cull", Float) = 0
                _OW_AO("AO", 2D) = "white" {}
        _OW_AOValue("AO default", Vector) = (1,1,1,1)
        _OW_AOMode("AO mode", Float) = 0
        _OW_AOUV("AO UV", Float) = 0
        _OW_Alpha("Alpha", 2D) = "white" {}
        _OW_AlphaValue("Alpha default", Vector) = (1,1,1,1)
        _OW_AlphaMode("Alpha mode", Float) = 0
        _OW_AlphaUV("Alpha UV", Float) = 0
        _OW_Alpha_B("Alpha B", 2D) = "white" {}
        _OW_Alpha_BValue("Alpha B default", Vector) = (1,1,1,1)
        _OW_Alpha_BMode("Alpha B mode", Float) = 0
        _OW_Alpha_BUV("Alpha B UV", Float) = 0
        _OW_Alpha_Divisor("Alpha Divisor", 2D) = "white" {}
        _OW_Alpha_DivisorValue("Alpha Divisor default", Vector) = (1,1,1,1)
        _OW_Alpha_DivisorMode("Alpha Divisor mode", Float) = 0
        _OW_Alpha_DivisorUV("Alpha Divisor UV", Float) = 0
        _OW_Blend("Blend", 2D) = "white" {}
        _OW_BlendValue("Blend default", Vector) = (1,1,1,1)
        _OW_BlendMode("Blend mode", Float) = 0
        _OW_BlendUV("Blend UV", Float) = 0
        _OW_Blend_Factor("Blend Factor", 2D) = "white" {}
        _OW_Blend_FactorValue("Blend Factor default", Vector) = (1,1,1,1)
        _OW_Blend_FactorMode("Blend Factor mode", Float) = 0
        _OW_Blend_FactorUV("Blend Factor UV", Float) = 0
        _OW_Cloth_Mask("Cloth Mask", 2D) = "white" {}
        _OW_Cloth_MaskValue("Cloth Mask default", Vector) = (1,1,1,1)
        _OW_Cloth_MaskMode("Cloth Mask mode", Float) = 0
        _OW_Cloth_MaskUV("Cloth Mask UV", Float) = 0
        _OW_Color("Color", 2D) = "white" {}
        _OW_ColorValue("Color default", Vector) = (1,1,1,1)
        _OW_ColorMode("Color mode", Float) = 0
        _OW_ColorUV("Color UV", Float) = 0
        _OW_Color_B("Color B", 2D) = "white" {}
        _OW_Color_BValue("Color B default", Vector) = (1,1,1,1)
        _OW_Color_BMode("Color B mode", Float) = 0
        _OW_Color_BUV("Color B UV", Float) = 0
        _OW_Color_C("Color C", 2D) = "white" {}
        _OW_Color_CValue("Color C default", Vector) = (1,1,1,1)
        _OW_Color_CMode("Color C mode", Float) = 0
        _OW_Color_CUV("Color C UV", Float) = 0
        _OW_Dirt("Dirt", 2D) = "white" {}
        _OW_DirtValue("Dirt default", Vector) = (1,1,1,1)
        _OW_DirtMode("Dirt mode", Float) = 0
        _OW_DirtUV("Dirt UV", Float) = 0
        _OW_Dirt_Color("Dirt Color", 2D) = "white" {}
        _OW_Dirt_ColorValue("Dirt Color default", Vector) = (1,1,1,1)
        _OW_Dirt_ColorMode("Dirt Color mode", Float) = 0
        _OW_Dirt_ColorUV("Dirt Color UV", Float) = 0
        _OW_Dirt_Factor("Dirt Factor", 2D) = "white" {}
        _OW_Dirt_FactorValue("Dirt Factor default", Vector) = (1,1,1,1)
        _OW_Dirt_FactorMode("Dirt Factor mode", Float) = 0
        _OW_Dirt_FactorUV("Dirt Factor UV", Float) = 0
        _OW_Dirt_Power("Dirt Power", 2D) = "white" {}
        _OW_Dirt_PowerValue("Dirt Power default", Vector) = (1,1,1,1)
        _OW_Dirt_PowerMode("Dirt Power mode", Float) = 0
        _OW_Dirt_PowerUV("Dirt Power UV", Float) = 0
        _OW_Dirt_Strength("Dirt Strength", 2D) = "white" {}
        _OW_Dirt_StrengthValue("Dirt Strength default", Vector) = (1,1,1,1)
        _OW_Dirt_StrengthMode("Dirt Strength mode", Float) = 0
        _OW_Dirt_StrengthUV("Dirt Strength UV", Float) = 0
        _OW_Emission("Emission", 2D) = "white" {}
        _OW_EmissionValue("Emission default", Vector) = (1,1,1,1)
        _OW_EmissionMode("Emission mode", Float) = 0
        _OW_EmissionUV("Emission UV", Float) = 0
        _OW_Emission_B("Emission B", 2D) = "white" {}
        _OW_Emission_BValue("Emission B default", Vector) = (1,1,1,1)
        _OW_Emission_BMode("Emission B mode", Float) = 0
        _OW_Emission_BUV("Emission B UV", Float) = 0
        _OW_Emission_Color("Emission Color", 2D) = "white" {}
        _OW_Emission_ColorValue("Emission Color default", Vector) = (1,1,1,1)
        _OW_Emission_ColorMode("Emission Color mode", Float) = 0
        _OW_Emission_ColorUV("Emission Color UV", Float) = 0
        _OW_Emission_Power("Emission Power", 2D) = "white" {}
        _OW_Emission_PowerValue("Emission Power default", Vector) = (1,1,1,1)
        _OW_Emission_PowerMode("Emission Power mode", Float) = 0
        _OW_Emission_PowerUV("Emission Power UV", Float) = 0
        _OW_Emission_Strength("Emission Strength", 2D) = "white" {}
        _OW_Emission_StrengthValue("Emission Strength default", Vector) = (1,1,1,1)
        _OW_Emission_StrengthMode("Emission Strength mode", Float) = 0
        _OW_Emission_StrengthUV("Emission Strength UV", Float) = 0
        _OW_Emission_Tint("Emission Tint", 2D) = "white" {}
        _OW_Emission_TintValue("Emission Tint default", Vector) = (1,1,1,1)
        _OW_Emission_TintMode("Emission Tint mode", Float) = 0
        _OW_Emission_TintUV("Emission Tint UV", Float) = 0
        _OW_Emission_Tint_Amount("Emission Tint Amount", 2D) = "white" {}
        _OW_Emission_Tint_AmountValue("Emission Tint Amount default", Vector) = (1,1,1,1)
        _OW_Emission_Tint_AmountMode("Emission Tint Amount mode", Float) = 0
        _OW_Emission_Tint_AmountUV("Emission Tint Amount UV", Float) = 0
        _OW_Material("Material", 2D) = "white" {}
        _OW_MaterialValue("Material default", Vector) = (1,1,1,1)
        _OW_MaterialMode("Material mode", Float) = 0
        _OW_MaterialUV("Material UV", Float) = 0
        _OW_Normal("Normal", 2D) = "white" {}
        _OW_NormalValue("Normal default", Vector) = (1,1,1,1)
        _OW_NormalMode("Normal mode", Float) = 0
        _OW_NormalUV("Normal UV", Float) = 0
        _OW_Normal_B("Normal B", 2D) = "white" {}
        _OW_Normal_BValue("Normal B default", Vector) = (1,1,1,1)
        _OW_Normal_BMode("Normal B mode", Float) = 0
        _OW_Normal_BUV("Normal B UV", Float) = 0
        _OW_Normal_B_Strength("Normal B Strength", 2D) = "white" {}
        _OW_Normal_B_StrengthValue("Normal B Strength default", Vector) = (1,1,1,1)
        _OW_Normal_B_StrengthMode("Normal B Strength mode", Float) = 0
        _OW_Normal_B_StrengthUV("Normal B Strength UV", Float) = 0
        _OW_Normal_Strength("Normal Strength", 2D) = "white" {}
        _OW_Normal_StrengthValue("Normal Strength default", Vector) = (1,1,1,1)
        _OW_Normal_StrengthMode("Normal Strength mode", Float) = 0
        _OW_Normal_StrengthUV("Normal Strength UV", Float) = 0
        _OW_Opacity("Opacity", 2D) = "white" {}
        _OW_OpacityValue("Opacity default", Vector) = (1,1,1,1)
        _OW_OpacityMode("Opacity mode", Float) = 0
        _OW_OpacityUV("Opacity UV", Float) = 0
        _OW_PBR("PBR", 2D) = "white" {}
        _OW_PBRValue("PBR default", Vector) = (1,1,1,1)
        _OW_PBRMode("PBR mode", Float) = 0
        _OW_PBRUV("PBR UV", Float) = 0
        _OW_PBR_B("PBR B", 2D) = "white" {}
        _OW_PBR_BValue("PBR B default", Vector) = (1,1,1,1)
        _OW_PBR_BMode("PBR B mode", Float) = 0
        _OW_PBR_BUV("PBR B UV", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "SapaSurface.hlsl"
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float2 uv2 : TEXCOORD2;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 tangentWS : TEXCOORD2;
                float2 uv0 : TEXCOORD3;
                float2 uv1 : TEXCOORD4;
                float2 uv2 : TEXCOORD5;
                float4 color : COLOR;
                float fog : TEXCOORD6;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes IN)
            {
                Varyings OUT=(Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN,OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                VertexPositionInputs p=GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs n=GetVertexNormalInputs(IN.normalOS,IN.tangentOS);
                OUT.positionCS=p.positionCS;
                OUT.positionWS=p.positionWS;
                OUT.normalWS=n.normalWS;
                OUT.tangentWS=float4(n.tangentWS,IN.tangentOS.w*GetOddNegativeScale());
                OUT.uv0=IN.uv0; OUT.uv1=IN.uv1; OUT.uv2=IN.uv2; OUT.color=IN.color;
                OUT.fog=ComputeFogFactor(p.positionCS.z);
                return OUT;
            }
            half4 Frag(Varyings IN, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                SapaInput sampleInput;
                sampleInput.uv0=IN.uv0; sampleInput.uv1=IN.uv1; sampleInput.uv2=IN.uv2;
                sampleInput.color=IN.color;
                SapaSurface source=EvaluateSapa(sampleInput);
                clip(source.alpha-_Cutoff);
                if (_DebugSurface > 0.5) return half4(source.color,source.alpha);
                half3 normal=NormalizeNormalPerPixel(IN.normalWS)*(frontFace?1.0:-1.0);
                half3 tangent=SafeNormalize(IN.tangentWS.xyz);
                half3 bitangent=cross(normal,tangent)*IN.tangentWS.w;
                InputData input=(InputData)0;
                input.positionWS=IN.positionWS;
                input.normalWS=NormalizeNormalPerPixel(TransformTangentToWorld(source.normal,half3x3(tangent,bitangent,normal)));
                input.viewDirectionWS=GetWorldSpaceNormalizeViewDir(IN.positionWS);
                input.shadowCoord=TransformWorldToShadowCoord(IN.positionWS);
                input.fogCoord=IN.fog;
                input.vertexLighting=VertexLighting(IN.positionWS,input.normalWS);
                input.bakedGI=SampleSH(input.normalWS);
                input.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(IN.positionCS);
                input.shadowMask=half4(1,1,1,1);
                SurfaceData surface=(SurfaceData)0;
                surface.albedo=max(source.color,0.0);
                surface.metallic=saturate(source.metallic);
                surface.specular=half3(.04,.04,.04);
                surface.smoothness=1.0-saturate(source.roughness);
                surface.normalTS=source.normal;
                surface.emission=max(source.emission,0.0);
                surface.occlusion=1;
                surface.alpha=saturate(source.alpha);
                half4 color=UniversalFragmentPBR(input,surface);
                color.rgb=MixFog(color.rgb,input.fogCoord);
                color.a=surface.alpha;
                return color;
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
