// Generated from the original OWM Blender node graphs. Do not hand-edit.
struct SapaInput { float2 uv0, uv1, uv2; float4 color; };
struct SapaSurface { float3 color, normal, emission; float metallic, roughness, alpha; };
float SapaDivide(float a,float b) { return b == 0.0 ? 0.0 : a/b; }
float3 SapaNormal(float3 color,float strength) { float3 n=color*2.0-1.0; n.xy*=strength; return SafeNormalize(n); }
SapaSurface SapaEmpty() { SapaSurface s=(SapaSurface)0; s.normal=float3(0,0,1); s.alpha=1; s.roughness=1; return s; }
SapaSurface SapaMix(SapaSurface a,SapaSurface b,float f) { SapaSurface s; s.color=lerp(a.color,b.color,f); s.normal=SafeNormalize(lerp(a.normal,b.normal,f)); s.emission=lerp(a.emission,b.emission,f); s.metallic=lerp(a.metallic,b.metallic,f); s.roughness=lerp(a.roughness,b.roughness,f); s.alpha=lerp(a.alpha,b.alpha,f); return s; }

TEXTURE2D(_OW_AO);
TEXTURE2D(_OW_Alpha);
TEXTURE2D(_OW_Alpha_B);
TEXTURE2D(_OW_Alpha_Divisor);
TEXTURE2D(_OW_Blend);
TEXTURE2D(_OW_Blend_Factor);
TEXTURE2D(_OW_Cloth_Mask);
TEXTURE2D(_OW_Color);
TEXTURE2D(_OW_Color_B);
TEXTURE2D(_OW_Color_C);
TEXTURE2D(_OW_Dirt);
TEXTURE2D(_OW_Dirt_Color);
TEXTURE2D(_OW_Dirt_Factor);
TEXTURE2D(_OW_Dirt_Power);
TEXTURE2D(_OW_Dirt_Strength);
TEXTURE2D(_OW_Emission);
TEXTURE2D(_OW_Emission_B);
TEXTURE2D(_OW_Emission_Color);
TEXTURE2D(_OW_Emission_Power);
TEXTURE2D(_OW_Emission_Strength);
TEXTURE2D(_OW_Emission_Tint);
TEXTURE2D(_OW_Emission_Tint_Amount);
TEXTURE2D(_OW_Material);
TEXTURE2D(_OW_Normal);
TEXTURE2D(_OW_Normal_B);
TEXTURE2D(_OW_Normal_B_Strength);
TEXTURE2D(_OW_Normal_Strength);
TEXTURE2D(_OW_Opacity);
TEXTURE2D(_OW_PBR);
TEXTURE2D(_OW_PBR_B);
CBUFFER_START(UnityPerMaterial)
float _Style, _Cutoff, _DebugSurface;
float4 _BaseMap_ST, _BaseColor;
float4 _OW_AOValue;
float _OW_AOMode;
float _OW_AOUV;
float4 _OW_AlphaValue;
float _OW_AlphaMode;
float _OW_AlphaUV;
float4 _OW_Alpha_BValue;
float _OW_Alpha_BMode;
float _OW_Alpha_BUV;
float4 _OW_Alpha_DivisorValue;
float _OW_Alpha_DivisorMode;
float _OW_Alpha_DivisorUV;
float4 _OW_BlendValue;
float _OW_BlendMode;
float _OW_BlendUV;
float4 _OW_Blend_FactorValue;
float _OW_Blend_FactorMode;
float _OW_Blend_FactorUV;
float4 _OW_Cloth_MaskValue;
float _OW_Cloth_MaskMode;
float _OW_Cloth_MaskUV;
float4 _OW_ColorValue;
float _OW_ColorMode;
float _OW_ColorUV;
float4 _OW_Color_BValue;
float _OW_Color_BMode;
float _OW_Color_BUV;
float4 _OW_Color_CValue;
float _OW_Color_CMode;
float _OW_Color_CUV;
float4 _OW_DirtValue;
float _OW_DirtMode;
float _OW_DirtUV;
float4 _OW_Dirt_ColorValue;
float _OW_Dirt_ColorMode;
float _OW_Dirt_ColorUV;
float4 _OW_Dirt_FactorValue;
float _OW_Dirt_FactorMode;
float _OW_Dirt_FactorUV;
float4 _OW_Dirt_PowerValue;
float _OW_Dirt_PowerMode;
float _OW_Dirt_PowerUV;
float4 _OW_Dirt_StrengthValue;
float _OW_Dirt_StrengthMode;
float _OW_Dirt_StrengthUV;
float4 _OW_EmissionValue;
float _OW_EmissionMode;
float _OW_EmissionUV;
float4 _OW_Emission_BValue;
float _OW_Emission_BMode;
float _OW_Emission_BUV;
float4 _OW_Emission_ColorValue;
float _OW_Emission_ColorMode;
float _OW_Emission_ColorUV;
float4 _OW_Emission_PowerValue;
float _OW_Emission_PowerMode;
float _OW_Emission_PowerUV;
float4 _OW_Emission_StrengthValue;
float _OW_Emission_StrengthMode;
float _OW_Emission_StrengthUV;
float4 _OW_Emission_TintValue;
float _OW_Emission_TintMode;
float _OW_Emission_TintUV;
float4 _OW_Emission_Tint_AmountValue;
float _OW_Emission_Tint_AmountMode;
float _OW_Emission_Tint_AmountUV;
float4 _OW_MaterialValue;
float _OW_MaterialMode;
float _OW_MaterialUV;
float4 _OW_NormalValue;
float _OW_NormalMode;
float _OW_NormalUV;
float4 _OW_Normal_BValue;
float _OW_Normal_BMode;
float _OW_Normal_BUV;
float4 _OW_Normal_B_StrengthValue;
float _OW_Normal_B_StrengthMode;
float _OW_Normal_B_StrengthUV;
float4 _OW_Normal_StrengthValue;
float _OW_Normal_StrengthMode;
float _OW_Normal_StrengthUV;
float4 _OW_OpacityValue;
float _OW_OpacityMode;
float _OW_OpacityUV;
float4 _OW_PBRValue;
float _OW_PBRMode;
float _OW_PBRUV;
float4 _OW_PBR_BValue;
float _OW_PBR_BMode;
float _OW_PBR_BUV;
CBUFFER_END
float4 Sample_AO(SapaInput IN)
{
    if (_OW_AOMode < 0.5) return _OW_AOValue;
    float2 uv = _OW_AOUV < 0.5 ? IN.uv0 : (_OW_AOUV < 1.5 ? IN.uv1 : IN.uv2);
    float4 value = SAMPLE_TEXTURE2D(_OW_AO,sampler_LinearRepeat,uv);
    return _OW_AOMode > 1.5 ? value.aaaa : value;
}
float4 Sample_Alpha(SapaInput IN)
{
    if (_OW_AlphaMode < 0.5) return _OW_AlphaValue;
    float2 uv = _OW_AlphaUV < 0.5 ? IN.uv0 : (_OW_AlphaUV < 1.5 ? IN.uv1 : IN.uv2);
    float4 value = SAMPLE_TEXTURE2D(_OW_Alpha,sampler_LinearRepeat,uv);
    return _OW_AlphaMode > 1.5 ? value.aaaa : value;
}
float4 Sample_Alpha_B(SapaInput IN)
{
    if (_OW_Alpha_BMode < 0.5) return _OW_Alpha_BValue;
    float2 uv = _OW_Alpha_BUV < 0.5 ? IN.uv0 : (_OW_Alpha_BUV < 1.5 ? IN.uv1 : IN.uv2);
    float4 value = SAMPLE_TEXTURE2D(_OW_Alpha_B,sampler_LinearRepeat,uv);
    return _OW_Alpha_BMode > 1.5 ? value.aaaa : value;
}
float4 Sample_Alpha_Divisor(SapaInput IN)
{
    if (_OW_Alpha_DivisorMode < 0.5) return _OW_Alpha_DivisorValue;
    float2 uv = _OW_Alpha_DivisorUV < 0.5 ? IN.uv0 : (_OW_Alpha_DivisorUV < 1.5 ? IN.uv1 : IN.uv2);
    float4 value = SAMPLE_TEXTURE2D(_OW_Alpha_Divisor,sampler_LinearRepeat,uv);
    return _OW_Alpha_DivisorMode > 1.5 ? value.aaaa : value;
}
float4 Sample_Blend(SapaInput IN)
{
    if (_OW_BlendMode < 0.5) return _OW_BlendValue;
    float2 uv = _OW_BlendUV < 0.5 ? IN.uv0 : (_OW_BlendUV < 1.5 ? IN.uv1 : IN.uv2);
    float4 value = SAMPLE_TEXTURE2D(_OW_Blend,sampler_LinearRepeat,uv);
    return _OW_BlendMode > 1.5 ? value.aaaa : value;
}
float4 Sample_Blend_Factor(SapaInput IN)
{
    if (_OW_Blend_FactorMode < 0.5) return _OW_Blend_FactorValue;
    float2 uv = _OW_Blend_FactorUV < 0.5 ? IN.uv0 : (_OW_Blend_FactorUV < 1.5 ? IN.uv1 : IN.uv2);
    float4 value = SAMPLE_TEXTURE2D(_OW_Blend_Factor,sampler_LinearRepeat,uv);
    return _OW_Blend_FactorMode > 1.5 ? value.aaaa : value;
}
float4 Sample_Cloth_Mask(SapaInput IN)
{
    if (_OW_Cloth_MaskMode < 0.5) return _OW_Cloth_MaskValue;
    float2 uv = _OW_Cloth_MaskUV < 0.5 ? IN.uv0 : (_OW_Cloth_MaskUV < 1.5 ? IN.uv1 : IN.uv2);
    float4 value = SAMPLE_TEXTURE2D(_OW_Cloth_Mask,sampler_LinearRepeat,uv);
    return _OW_Cloth_MaskMode > 1.5 ? value.aaaa : value;
}
float4 Sample_Color(SapaInput IN)
{
    if (_OW_ColorMode < 0.5) return _OW_ColorValue;
    float2 uv = _OW_ColorUV < 0.5 ? IN.uv0 : (_OW_ColorUV < 1.5 ? IN.uv1 : IN.uv2);
    float4 value = SAMPLE_TEXTURE2D(_OW_Color,sampler_LinearRepeat,uv);
    return _OW_ColorMode > 1.5 ? value.aaaa : value;
}
float4 Sample_Color_B(SapaInput IN)
{
    if (_OW_Color_BMode < 0.5) return _OW_Color_BValue;
    float2 uv = _OW_Color_BUV < 0.5 ? IN.uv0 : (_OW_Color_BUV < 1.5 ? IN.uv1 : IN.uv2);
    float4 value = SAMPLE_TEXTURE2D(_OW_Color_B,sampler_LinearRepeat,uv);
    return _OW_Color_BMode > 1.5 ? value.aaaa : value;
}
float4 Sample_Color_C(SapaInput IN)
{
    if (_OW_Color_CMode < 0.5) return _OW_Color_CValue;
    float2 uv = _OW_Color_CUV < 0.5 ? IN.uv0 : (_OW_Color_CUV < 1.5 ? IN.uv1 : IN.uv2);
    float4 value = SAMPLE_TEXTURE2D(_OW_Color_C,sampler_LinearRepeat,uv);
    return _OW_Color_CMode > 1.5 ? value.aaaa : value;
}
float4 Sample_Dirt(SapaInput IN)
{
    if (_OW_DirtMode < 0.5) return _OW_DirtValue;
    float2 uv = _OW_DirtUV < 0.5 ? IN.uv0 : (_OW_DirtUV < 1.5 ? IN.uv1 : IN.uv2);
    float4 value = SAMPLE_TEXTURE2D(_OW_Dirt,sampler_LinearRepeat,uv);
    return _OW_DirtMode > 1.5 ? value.aaaa : value;
}
float4 Sample_Dirt_Color(SapaInput IN)
{
    if (_OW_Dirt_ColorMode < 0.5) return _OW_Dirt_ColorValue;
    float2 uv = _OW_Dirt_ColorUV < 0.5 ? IN.uv0 : (_OW_Dirt_ColorUV < 1.5 ? IN.uv1 : IN.uv2);
    float4 value = SAMPLE_TEXTURE2D(_OW_Dirt_Color,sampler_LinearRepeat,uv);
    return _OW_Dirt_ColorMode > 1.5 ? value.aaaa : value;
}
float4 Sample_Dirt_Factor(SapaInput IN)
{
    if (_OW_Dirt_FactorMode < 0.5) return _OW_Dirt_FactorValue;
    float2 uv = _OW_Dirt_FactorUV < 0.5 ? IN.uv0 : (_OW_Dirt_FactorUV < 1.5 ? IN.uv1 : IN.uv2);
    float4 value = SAMPLE_TEXTURE2D(_OW_Dirt_Factor,sampler_LinearRepeat,uv);
    return _OW_Dirt_FactorMode > 1.5 ? value.aaaa : value;
}
float4 Sample_Dirt_Power(SapaInput IN)
{
    if (_OW_Dirt_PowerMode < 0.5) return _OW_Dirt_PowerValue;
    float2 uv = _OW_Dirt_PowerUV < 0.5 ? IN.uv0 : (_OW_Dirt_PowerUV < 1.5 ? IN.uv1 : IN.uv2);
    float4 value = SAMPLE_TEXTURE2D(_OW_Dirt_Power,sampler_LinearRepeat,uv);
    return _OW_Dirt_PowerMode > 1.5 ? value.aaaa : value;
}
float4 Sample_Dirt_Strength(SapaInput IN)
{
    if (_OW_Dirt_StrengthMode < 0.5) return _OW_Dirt_StrengthValue;
    float2 uv = _OW_Dirt_StrengthUV < 0.5 ? IN.uv0 : (_OW_Dirt_StrengthUV < 1.5 ? IN.uv1 : IN.uv2);
    float4 value = SAMPLE_TEXTURE2D(_OW_Dirt_Strength,sampler_LinearRepeat,uv);
    return _OW_Dirt_StrengthMode > 1.5 ? value.aaaa : value;
}
float4 Sample_Emission(SapaInput IN)
{
    if (_OW_EmissionMode < 0.5) return _OW_EmissionValue;
    float2 uv = _OW_EmissionUV < 0.5 ? IN.uv0 : (_OW_EmissionUV < 1.5 ? IN.uv1 : IN.uv2);
    float4 value = SAMPLE_TEXTURE2D(_OW_Emission,sampler_LinearRepeat,uv);
    return _OW_EmissionMode > 1.5 ? value.aaaa : value;
}
float4 Sample_Emission_B(SapaInput IN)
{
    if (_OW_Emission_BMode < 0.5) return _OW_Emission_BValue;
    float2 uv = _OW_Emission_BUV < 0.5 ? IN.uv0 : (_OW_Emission_BUV < 1.5 ? IN.uv1 : IN.uv2);
    float4 value = SAMPLE_TEXTURE2D(_OW_Emission_B,sampler_LinearRepeat,uv);
    return _OW_Emission_BMode > 1.5 ? value.aaaa : value;
}
float4 Sample_Emission_Color(SapaInput IN)
{
    if (_OW_Emission_ColorMode < 0.5) return _OW_Emission_ColorValue;
    float2 uv = _OW_Emission_ColorUV < 0.5 ? IN.uv0 : (_OW_Emission_ColorUV < 1.5 ? IN.uv1 : IN.uv2);
    float4 value = SAMPLE_TEXTURE2D(_OW_Emission_Color,sampler_LinearRepeat,uv);
    return _OW_Emission_ColorMode > 1.5 ? value.aaaa : value;
}
float4 Sample_Emission_Power(SapaInput IN)
{
    if (_OW_Emission_PowerMode < 0.5) return _OW_Emission_PowerValue;
    float2 uv = _OW_Emission_PowerUV < 0.5 ? IN.uv0 : (_OW_Emission_PowerUV < 1.5 ? IN.uv1 : IN.uv2);
    float4 value = SAMPLE_TEXTURE2D(_OW_Emission_Power,sampler_LinearRepeat,uv);
    return _OW_Emission_PowerMode > 1.5 ? value.aaaa : value;
}
float4 Sample_Emission_Strength(SapaInput IN)
{
    if (_OW_Emission_StrengthMode < 0.5) return _OW_Emission_StrengthValue;
    float2 uv = _OW_Emission_StrengthUV < 0.5 ? IN.uv0 : (_OW_Emission_StrengthUV < 1.5 ? IN.uv1 : IN.uv2);
    float4 value = SAMPLE_TEXTURE2D(_OW_Emission_Strength,sampler_LinearRepeat,uv);
    return _OW_Emission_StrengthMode > 1.5 ? value.aaaa : value;
}
float4 Sample_Emission_Tint(SapaInput IN)
{
    if (_OW_Emission_TintMode < 0.5) return _OW_Emission_TintValue;
    float2 uv = _OW_Emission_TintUV < 0.5 ? IN.uv0 : (_OW_Emission_TintUV < 1.5 ? IN.uv1 : IN.uv2);
    float4 value = SAMPLE_TEXTURE2D(_OW_Emission_Tint,sampler_LinearRepeat,uv);
    return _OW_Emission_TintMode > 1.5 ? value.aaaa : value;
}
float4 Sample_Emission_Tint_Amount(SapaInput IN)
{
    if (_OW_Emission_Tint_AmountMode < 0.5) return _OW_Emission_Tint_AmountValue;
    float2 uv = _OW_Emission_Tint_AmountUV < 0.5 ? IN.uv0 : (_OW_Emission_Tint_AmountUV < 1.5 ? IN.uv1 : IN.uv2);
    float4 value = SAMPLE_TEXTURE2D(_OW_Emission_Tint_Amount,sampler_LinearRepeat,uv);
    return _OW_Emission_Tint_AmountMode > 1.5 ? value.aaaa : value;
}
float4 Sample_Material(SapaInput IN)
{
    if (_OW_MaterialMode < 0.5) return _OW_MaterialValue;
    float2 uv = _OW_MaterialUV < 0.5 ? IN.uv0 : (_OW_MaterialUV < 1.5 ? IN.uv1 : IN.uv2);
    float4 value = SAMPLE_TEXTURE2D(_OW_Material,sampler_LinearRepeat,uv);
    return _OW_MaterialMode > 1.5 ? value.aaaa : value;
}
float4 Sample_Normal(SapaInput IN)
{
    if (_OW_NormalMode < 0.5) return _OW_NormalValue;
    float2 uv = _OW_NormalUV < 0.5 ? IN.uv0 : (_OW_NormalUV < 1.5 ? IN.uv1 : IN.uv2);
    float4 value = SAMPLE_TEXTURE2D(_OW_Normal,sampler_LinearRepeat,uv);
    return _OW_NormalMode > 1.5 ? value.aaaa : value;
}
float4 Sample_Normal_B(SapaInput IN)
{
    if (_OW_Normal_BMode < 0.5) return _OW_Normal_BValue;
    float2 uv = _OW_Normal_BUV < 0.5 ? IN.uv0 : (_OW_Normal_BUV < 1.5 ? IN.uv1 : IN.uv2);
    float4 value = SAMPLE_TEXTURE2D(_OW_Normal_B,sampler_LinearRepeat,uv);
    return _OW_Normal_BMode > 1.5 ? value.aaaa : value;
}
float4 Sample_Normal_B_Strength(SapaInput IN)
{
    if (_OW_Normal_B_StrengthMode < 0.5) return _OW_Normal_B_StrengthValue;
    float2 uv = _OW_Normal_B_StrengthUV < 0.5 ? IN.uv0 : (_OW_Normal_B_StrengthUV < 1.5 ? IN.uv1 : IN.uv2);
    float4 value = SAMPLE_TEXTURE2D(_OW_Normal_B_Strength,sampler_LinearRepeat,uv);
    return _OW_Normal_B_StrengthMode > 1.5 ? value.aaaa : value;
}
float4 Sample_Normal_Strength(SapaInput IN)
{
    if (_OW_Normal_StrengthMode < 0.5) return _OW_Normal_StrengthValue;
    float2 uv = _OW_Normal_StrengthUV < 0.5 ? IN.uv0 : (_OW_Normal_StrengthUV < 1.5 ? IN.uv1 : IN.uv2);
    float4 value = SAMPLE_TEXTURE2D(_OW_Normal_Strength,sampler_LinearRepeat,uv);
    return _OW_Normal_StrengthMode > 1.5 ? value.aaaa : value;
}
float4 Sample_Opacity(SapaInput IN)
{
    if (_OW_OpacityMode < 0.5) return _OW_OpacityValue;
    float2 uv = _OW_OpacityUV < 0.5 ? IN.uv0 : (_OW_OpacityUV < 1.5 ? IN.uv1 : IN.uv2);
    float4 value = SAMPLE_TEXTURE2D(_OW_Opacity,sampler_LinearRepeat,uv);
    return _OW_OpacityMode > 1.5 ? value.aaaa : value;
}
float4 Sample_PBR(SapaInput IN)
{
    if (_OW_PBRMode < 0.5) return _OW_PBRValue;
    float2 uv = _OW_PBRUV < 0.5 ? IN.uv0 : (_OW_PBRUV < 1.5 ? IN.uv1 : IN.uv2);
    float4 value = SAMPLE_TEXTURE2D(_OW_PBR,sampler_LinearRepeat,uv);
    return _OW_PBRMode > 1.5 ? value.aaaa : value;
}
float4 Sample_PBR_B(SapaInput IN)
{
    if (_OW_PBR_BMode < 0.5) return _OW_PBR_BValue;
    float2 uv = _OW_PBR_BUV < 0.5 ? IN.uv0 : (_OW_PBR_BUV < 1.5 ? IN.uv1 : IN.uv2);
    float4 value = SAMPLE_TEXTURE2D(_OW_PBR_B,sampler_LinearRepeat,uv);
    return _OW_PBR_BMode > 1.5 ? value.aaaa : value;
}
// OWM: Basic
SapaSurface SapaStyle0(SapaInput IN)
{
    float4 v1 = lerp(Sample_Color(IN),(Sample_Color(IN)*Sample_AO(IN)),saturate(1.0));
    float v2 = ((Sample_PBR(IN)).xyz).x;
    float v3 = (v2>0.524999976?1.0:0.0);
    float v4 = (v3*v2);
    float v5 = (v4-0.5);
    float v6 = (v5*2.0);
    float v7 = ((float4(0.0,0.0,0.0,1.0)).xyz).x;
    float v8 = (v7*v2);
    float v9 = (v8*0.5);
    float v10 = saturate((v6-v9));
    float v11 = ((Sample_PBR(IN)).xyz).y;
    float v12 = (1.0-v11);
    float v13 = pow(max(0.0,dot((Sample_Emission(IN)).xyz,float3(0.2126,0.7152,0.0722))),1.0);
    float v14 = (v13*dot((Sample_Emission_Strength(IN)).xyz,float3(0.2126,0.7152,0.0722)));
    float v15 = ((Sample_Normal(IN)).xyz).x;
    float v16 = ((Sample_Normal(IN)).xyz).y;
    float3 v17 = float3(v15,v16,v15);
    float3 v18 = (v17-float3(0.5,0.5,0.5));
    float v19 = ((Sample_Normal(IN)).xyz).z;
    float v20 = (v19*31.875);
    float v21 = (v20+1.0);
    float v22 = SapaDivide(1.0,v21);
    float3 v23 = (v18*float3(v22,v22,v22));
    float3 v24 = (v23+float3(0.5,0.5,0.5));
    float3 v25 = (v24*float3(2.0,2.0,0.0));
    float3 v26 = (v25+float3(-1.0,-1.0,1.0));
    float v27 = dot(v26,v26);
    float v28 = SapaDivide(2.0,v27);
    float3 v29 = (float3(v28,v28,v28)*v26);
    float v30 = (v29).x;
    float v31 = (v29).y;
    float v32 = (v28-1.0);
    float3 v33 = float3(v30,v31,v32);
    float3 v34 = SafeNormalize(v33);
    float3 v35 = (v34*float3(0.5,0.5,0.5));
    float3 v36 = (v35+float3(0.5,0.5,0.5));
    float3 v37 = SapaNormal(v36,dot((Sample_Normal_Strength(IN)).xyz,float3(0.2126,0.7152,0.0722)));
    SapaSurface s0 = SapaEmpty();
    s0.color = (v1).xyz;
    s0.metallic = v10;
    s0.roughness = v12;
    s0.alpha = dot((Sample_Alpha(IN)).xyz,float3(0.2126,0.7152,0.0722));
    s0.emission = ((v1).xyz*v14);
    s0.normal = v37;
    return s0;
}
// OWM: Blend
SapaSurface SapaStyle1(SapaInput IN)
{
    float4 v1 = lerp(Sample_Color_B(IN),(Sample_Color_B(IN)*Sample_Color(IN)),saturate(0.5));
    float4 v2 = lerp(v1,(v1+v1),saturate(0.5));
    float4 v3 = lerp(Sample_Normal(IN),Sample_Normal_B(IN),saturate(dot((Sample_Normal_B_Strength(IN)).xyz,float3(0.2126,0.7152,0.0722))));
    float4 v4 = lerp(v2,(v2*Sample_AO(IN)),saturate(1.0));
    float v5 = ((Sample_PBR(IN)).xyz).x;
    float v6 = (v5>0.524999976?1.0:0.0);
    float v7 = (v6*v5);
    float v8 = (v7-0.5);
    float v9 = (v8*2.0);
    float v10 = ((float4(0.0,0.0,0.0,1.0)).xyz).x;
    float v11 = (v10*v5);
    float v12 = (v11*0.5);
    float v13 = saturate((v9-v12));
    float v14 = ((Sample_PBR(IN)).xyz).y;
    float v15 = (1.0-v14);
    float v16 = pow(max(0.0,0.0),1.0);
    float v17 = (v16*16.0);
    float v18 = ((v3).xyz).x;
    float v19 = ((v3).xyz).y;
    float3 v20 = float3(v18,v19,v18);
    float3 v21 = (v20-float3(0.5,0.5,0.5));
    float v22 = ((v3).xyz).z;
    float v23 = (v22*31.875);
    float v24 = (v23+1.0);
    float v25 = SapaDivide(1.0,v24);
    float3 v26 = (v21*float3(v25,v25,v25));
    float3 v27 = (v26+float3(0.5,0.5,0.5));
    float3 v28 = (v27*float3(2.0,2.0,0.0));
    float3 v29 = (v28+float3(-1.0,-1.0,1.0));
    float v30 = dot(v29,v29);
    float v31 = SapaDivide(2.0,v30);
    float3 v32 = (float3(v31,v31,v31)*v29);
    float v33 = (v32).x;
    float v34 = (v32).y;
    float v35 = (v31-1.0);
    float3 v36 = float3(v33,v34,v35);
    float3 v37 = SafeNormalize(v36);
    float3 v38 = (v37*float3(0.5,0.5,0.5));
    float3 v39 = (v38+float3(0.5,0.5,0.5));
    float3 v40 = SapaNormal(v39,1.0);
    SapaSurface s3 = SapaEmpty();
    s3.color = (v4).xyz;
    s3.metallic = v13;
    s3.roughness = v15;
    s3.alpha = 1.0;
    s3.emission = ((v4).xyz*v17);
    s3.normal = v40;
    return s3;
}
// OWM: Blend 1 A
SapaSurface SapaStyle2(SapaInput IN)
{
    float v1 = ((IN.color).xyz).x;
    float v2 = (1.0-v1);
    float v3 = (v2*-1.0);
    float v4 = (0.0*-1.0);
    float v5 = (v4+dot((Sample_Blend(IN)).xyz,float3(0.2126,0.7152,0.0722)));
    float v6 = (v3+v5);
    float v7 = ((IN.color).xyz).y;
    float v8 = SapaDivide(v6,v7);
    float v9 = saturate((v1+v8));
    float3 v10 = ((Sample_Color(IN)).xyz*(Sample_Color_C(IN)).xyz);
    float3 v11 = (v10+v10);
    float3 v12 = (v11*float3(-1.0,-1.0,-1.0));
    float3 v13 = ((Sample_Color_B(IN)).xyz*(Sample_Blend_Factor(IN)).xyz);
    float3 v14 = (v12+v13);
    float3 v15 = (float3(v9,v9,v9)*v14+v11);
    float3 v16 = ((Sample_PBR(IN)).xyz*float3(-1.0,-1.0,-1.0));
    float3 v17 = (v16+(Sample_PBR_B(IN)).xyz);
    float3 v18 = (float3(v9,v9,v9)*v17+(Sample_PBR(IN)).xyz);
    float4 v19 = lerp(Sample_Normal(IN),Sample_Normal_B(IN),saturate(v8));
    float4 v20 = lerp(float4(v15,1.0),(float4(v15,1.0)*float4(0.5,0.5,0.5,1.0)),saturate(1.0));
    float v21 = (v18).x;
    float v22 = (v21>0.524999976?1.0:0.0);
    float v23 = (v22*v21);
    float v24 = (v23-0.5);
    float v25 = (v24*2.0);
    float v26 = ((float4(0.0,0.0,0.0,1.0)).xyz).x;
    float v27 = (v26*v21);
    float v28 = (v27*0.5);
    float v29 = saturate((v25-v28));
    float v30 = (v18).y;
    float v31 = (1.0-v30);
    float v32 = pow(max(0.0,0.0),1.0);
    float v33 = (v32*1.0);
    float v34 = ((v19).xyz).x;
    float v35 = ((v19).xyz).y;
    float3 v36 = float3(v34,v35,v34);
    float3 v37 = (v36-float3(0.5,0.5,0.5));
    float v38 = ((v19).xyz).z;
    float v39 = (v38*31.875);
    float v40 = (v39+1.0);
    float v41 = SapaDivide(1.0,v40);
    float3 v42 = (v37*float3(v41,v41,v41));
    float3 v43 = (v42+float3(0.5,0.5,0.5));
    float3 v44 = (v43*float3(2.0,2.0,0.0));
    float3 v45 = (v44+float3(-1.0,-1.0,1.0));
    float v46 = dot(v45,v45);
    float v47 = SapaDivide(2.0,v46);
    float3 v48 = (float3(v47,v47,v47)*v45);
    float v49 = (v48).x;
    float v50 = (v48).y;
    float v51 = (v47-1.0);
    float3 v52 = float3(v49,v50,v51);
    float3 v53 = SafeNormalize(v52);
    float3 v54 = (v53*float3(0.5,0.5,0.5));
    float3 v55 = (v54+float3(0.5,0.5,0.5));
    float3 v56 = SapaNormal(v55,1.0);
    SapaSurface s19 = SapaEmpty();
    s19.color = (v20).xyz;
    s19.metallic = v29;
    s19.roughness = v31;
    s19.alpha = 1.0;
    s19.emission = ((v20).xyz*v33);
    s19.normal = v56;
    return s19;
}
// OWM: Blend 2 A
SapaSurface SapaStyle3(SapaInput IN)
{
    float v1 = ((IN.color).xyz).x;
    float v2 = (1.0-v1);
    float v3 = (v2*-1.0);
    float v4 = (0.0*-1.0);
    float v5 = (v4+dot((Sample_Blend(IN)).xyz,float3(0.2126,0.7152,0.0722)));
    float v6 = (v3+v5);
    float v7 = ((IN.color).xyz).y;
    float v8 = SapaDivide(v6,v7);
    float v9 = saturate((v1+v8));
    float3 v10 = ((Sample_Color(IN)).xyz*float3(-1.0,-1.0,-1.0));
    float3 v11 = ((Sample_Color_B(IN)).xyz*(Sample_Blend_Factor(IN)).xyz);
    float3 v12 = (v10+v11);
    float3 v13 = (float3(v9,v9,v9)*v12+(Sample_Color(IN)).xyz);
    float3 v14 = ((Sample_Color_C(IN)).xyz+(Sample_Color_C(IN)).xyz);
    float3 v15 = (v13*v14);
    float3 v16 = ((Sample_PBR(IN)).xyz*float3(-1.0,-1.0,-1.0));
    float3 v17 = (v16+(Sample_PBR_B(IN)).xyz);
    float3 v18 = (float3(v9,v9,v9)*v17+(Sample_PBR(IN)).xyz);
    float4 v19 = lerp(Sample_Normal(IN),Sample_Normal_B(IN),saturate(v8));
    float4 v20 = lerp(float4(v15,1.0),(float4(v15,1.0)*float4(1.0,1.0,1.0,1.0)),saturate(1.0));
    float v21 = (v18).x;
    float v22 = (v21>0.524999976?1.0:0.0);
    float v23 = (v22*v21);
    float v24 = (v23-0.5);
    float v25 = (v24*2.0);
    float v26 = ((float4(0.0,0.0,0.0,1.0)).xyz).x;
    float v27 = (v26*v21);
    float v28 = (v27*0.5);
    float v29 = saturate((v25-v28));
    float v30 = (v18).y;
    float v31 = (1.0-v30);
    float v32 = pow(max(0.0,0.0),1.0);
    float v33 = (v32*16.0);
    float v34 = ((v19).xyz).x;
    float v35 = ((v19).xyz).y;
    float3 v36 = float3(v34,v35,v34);
    float3 v37 = (v36-float3(0.5,0.5,0.5));
    float v38 = ((v19).xyz).z;
    float v39 = (v38*31.875);
    float v40 = (v39+1.0);
    float v41 = SapaDivide(1.0,v40);
    float3 v42 = (v37*float3(v41,v41,v41));
    float3 v43 = (v42+float3(0.5,0.5,0.5));
    float3 v44 = (v43*float3(2.0,2.0,0.0));
    float3 v45 = (v44+float3(-1.0,-1.0,1.0));
    float v46 = dot(v45,v45);
    float v47 = SapaDivide(2.0,v46);
    float3 v48 = (float3(v47,v47,v47)*v45);
    float v49 = (v48).x;
    float v50 = (v48).y;
    float v51 = (v47-1.0);
    float3 v52 = float3(v49,v50,v51);
    float3 v53 = SafeNormalize(v52);
    float3 v54 = (v53*float3(0.5,0.5,0.5));
    float3 v55 = (v54+float3(0.5,0.5,0.5));
    float3 v56 = SapaNormal(v55,1.0);
    SapaSurface s19 = SapaEmpty();
    s19.color = (v20).xyz;
    s19.metallic = v29;
    s19.roughness = v31;
    s19.alpha = 1.0;
    s19.emission = ((v20).xyz*v33);
    s19.normal = v56;
    return s19;
}
// OWM: Blend 2 B
SapaSurface SapaStyle4(SapaInput IN)
{
    float v1 = ((IN.color).xyz).x;
    float v2 = (1.0-v1);
    float v3 = (v2*-1.0);
    float v4 = (0.0*-1.0);
    float v5 = (v4+dot((Sample_Blend(IN)).xyz,float3(0.2126,0.7152,0.0722)));
    float v6 = (v3+v5);
    float v7 = ((IN.color).xyz).y;
    float v8 = SapaDivide(v6,v7);
    float v9 = saturate((v1+v8));
    float3 v10 = ((Sample_Color(IN)).xyz*float3(-1.0,-1.0,-1.0));
    float3 v11 = ((Sample_Color_B(IN)).xyz*(Sample_Blend_Factor(IN)).xyz);
    float3 v12 = (v10+v11);
    float3 v13 = (float3(v9,v9,v9)*v12+(Sample_Color(IN)).xyz);
    float3 v14 = ((Sample_PBR(IN)).xyz*float3(-1.0,-1.0,-1.0));
    float3 v15 = (v14+(Sample_PBR_B(IN)).xyz);
    float3 v16 = (float3(v9,v9,v9)*v15+(Sample_PBR(IN)).xyz);
    float4 v17 = lerp(Sample_Normal(IN),Sample_Normal_B(IN),saturate(v8));
    float4 v18 = lerp(float4(v13,1.0),(float4(v13,1.0)*float4(1.0,1.0,1.0,1.0)),saturate(1.0));
    float v19 = (v16).x;
    float v20 = (v19>0.524999976?1.0:0.0);
    float v21 = (v20*v19);
    float v22 = (v21-0.5);
    float v23 = (v22*2.0);
    float v24 = ((float4(0.0,0.0,0.0,1.0)).xyz).x;
    float v25 = (v24*v19);
    float v26 = (v25*0.5);
    float v27 = saturate((v23-v26));
    float v28 = (v16).y;
    float v29 = (1.0-v28);
    float v30 = pow(max(0.0,0.0),1.0);
    float v31 = (v30*16.0);
    float v32 = ((v17).xyz).x;
    float v33 = ((v17).xyz).y;
    float3 v34 = float3(v32,v33,v32);
    float3 v35 = (v34-float3(0.5,0.5,0.5));
    float v36 = ((v17).xyz).z;
    float v37 = (v36*31.875);
    float v38 = (v37+1.0);
    float v39 = SapaDivide(1.0,v38);
    float3 v40 = (v35*float3(v39,v39,v39));
    float3 v41 = (v40+float3(0.5,0.5,0.5));
    float3 v42 = (v41*float3(2.0,2.0,0.0));
    float3 v43 = (v42+float3(-1.0,-1.0,1.0));
    float v44 = dot(v43,v43);
    float v45 = SapaDivide(2.0,v44);
    float3 v46 = (float3(v45,v45,v45)*v43);
    float v47 = (v46).x;
    float v48 = (v46).y;
    float v49 = (v45-1.0);
    float3 v50 = float3(v47,v48,v49);
    float3 v51 = SafeNormalize(v50);
    float3 v52 = (v51*float3(0.5,0.5,0.5));
    float3 v53 = (v52+float3(0.5,0.5,0.5));
    float3 v54 = SapaNormal(v53,1.0);
    SapaSurface s17 = SapaEmpty();
    s17.color = (v18).xyz;
    s17.metallic = v27;
    s17.roughness = v29;
    s17.alpha = 1.0;
    s17.emission = ((v18).xyz*v31);
    s17.normal = v54;
    return s17;
}
// OWM: Cloth
SapaSurface SapaStyle5(SapaInput IN)
{
    float4 v1 = lerp(Sample_Color(IN),(Sample_Color(IN)*Sample_AO(IN)),saturate(1.0));
    float v2 = ((Sample_PBR(IN)).xyz).x;
    float v3 = (v2>0.524999976?1.0:0.0);
    float v4 = (v3*v2);
    float v5 = (v4-0.5);
    float v6 = (v5*2.0);
    float v7 = ((Sample_Cloth_Mask(IN)).xyz).x;
    float v8 = (v7*v2);
    float v9 = (v8*0.5);
    float v10 = saturate((v6-v9));
    float v11 = ((Sample_PBR(IN)).xyz).y;
    float v12 = (1.0-v11);
    float v13 = (dot((Sample_Emission(IN)).xyz,float3(0.2126,0.7152,0.0722))*dot((Sample_Emission_Strength(IN)).xyz,float3(0.2126,0.7152,0.0722)));
    float4 v14 = saturate(lerp(v1,(v1*float4(v13,v13,v13,v13)),saturate(1.0)));
    float v15 = ((Sample_Normal(IN)).xyz).x;
    float v16 = ((Sample_Normal(IN)).xyz).y;
    float3 v17 = float3(v15,v16,v15);
    float3 v18 = (v17-float3(0.5,0.5,0.5));
    float v19 = ((Sample_Normal(IN)).xyz).z;
    float v20 = (v19*31.875);
    float v21 = (v20+1.0);
    float v22 = SapaDivide(1.0,v21);
    float3 v23 = (v18*float3(v22,v22,v22));
    float3 v24 = (v23+float3(0.5,0.5,0.5));
    float3 v25 = (v24*float3(2.0,2.0,0.0));
    float3 v26 = (v25+float3(-1.0,-1.0,1.0));
    float v27 = dot(v26,v26);
    float v28 = SapaDivide(2.0,v27);
    float3 v29 = (float3(v28,v28,v28)*v26);
    float v30 = (v29).x;
    float v31 = (v29).y;
    float v32 = (v28-1.0);
    float3 v33 = float3(v30,v31,v32);
    float3 v34 = SafeNormalize(v33);
    float3 v35 = (v34*float3(0.5,0.5,0.5));
    float3 v36 = (v35+float3(0.5,0.5,0.5));
    float3 v37 = SapaNormal(v36,dot((Sample_Normal_Strength(IN)).xyz,float3(0.2126,0.7152,0.0722)));
    SapaSurface s0 = SapaEmpty();
    s0.color = (v1).xyz;
    s0.metallic = v10;
    s0.roughness = v12;
    s0.alpha = dot((Sample_Alpha(IN)).xyz,float3(0.2126,0.7152,0.0722));
    s0.emission = ((v14).xyz*v13);
    s0.normal = v37;
    return s0;
}
// OWM: Complex
SapaSurface SapaStyle6(SapaInput IN)
{
    float4 v1 = lerp(Sample_Color(IN),(Sample_Color(IN)*Sample_AO(IN)),saturate(1.0));
    float v2 = ((Sample_PBR(IN)).xyz).x;
    float v3 = (v2>0.524999976?1.0:0.0);
    float v4 = (v3*v2);
    float v5 = (v4-0.5);
    float v6 = (v5*2.0);
    float v7 = ((Sample_Material(IN)).xyz).x;
    float v8 = (v7*v2);
    float v9 = (v8*0.5);
    float v10 = saturate((v6-v9));
    float v11 = ((Sample_PBR(IN)).xyz).y;
    float v12 = (1.0-v11);
    float v13 = saturate((dot((Sample_Alpha(IN)).xyz,float3(0.2126,0.7152,0.0722))+dot((Sample_Alpha_B(IN)).xyz,float3(0.2126,0.7152,0.0722))));
    float4 v14 = saturate(lerp(v1,Sample_Emission_Tint(IN),saturate(dot((Sample_Emission_Tint_Amount(IN)).xyz,float3(0.2126,0.7152,0.0722)))));
    float v15 = (dot((Sample_Emission(IN)).xyz,float3(0.2126,0.7152,0.0722))+dot((Sample_Emission_B(IN)).xyz,float3(0.2126,0.7152,0.0722)));
    float v16 = pow(max(0.0,v15),dot((Sample_Emission_Power(IN)).xyz,float3(0.2126,0.7152,0.0722)));
    float v17 = (v16*dot((Sample_Emission_Strength(IN)).xyz,float3(0.2126,0.7152,0.0722)));
    float v18 = ((Sample_Normal(IN)).xyz).x;
    float v19 = ((Sample_Normal(IN)).xyz).y;
    float3 v20 = float3(v18,v19,v18);
    float3 v21 = (v20-float3(0.5,0.5,0.5));
    float v22 = ((Sample_Normal(IN)).xyz).z;
    float v23 = (v22*31.875);
    float v24 = (v23+1.0);
    float v25 = SapaDivide(1.0,v24);
    float3 v26 = (v21*float3(v25,v25,v25));
    float3 v27 = (v26+float3(0.5,0.5,0.5));
    float3 v28 = (v27*float3(2.0,2.0,0.0));
    float3 v29 = (v28+float3(-1.0,-1.0,1.0));
    float v30 = dot(v29,v29);
    float v31 = SapaDivide(2.0,v30);
    float3 v32 = (float3(v31,v31,v31)*v29);
    float v33 = (v32).x;
    float v34 = (v32).y;
    float v35 = (v31-1.0);
    float3 v36 = float3(v33,v34,v35);
    float3 v37 = SafeNormalize(v36);
    float3 v38 = (v37*float3(0.5,0.5,0.5));
    float3 v39 = (v38+float3(0.5,0.5,0.5));
    float3 v40 = SapaNormal(v39,dot((Sample_Normal_Strength(IN)).xyz,float3(0.2126,0.7152,0.0722)));
    SapaSurface s0 = SapaEmpty();
    s0.color = (v1).xyz;
    s0.metallic = v10;
    s0.roughness = v12;
    s0.alpha = v13;
    s0.emission = ((v14).xyz*v17);
    s0.normal = v40;
    return s0;
}
// OWM: Decal
SapaSurface SapaStyle7(SapaInput IN)
{
    float v1 = ((Sample_Alpha(IN)).xyz).x;
    float v2 = (1.0-dot((Sample_Opacity(IN)).xyz,float3(0.2126,0.7152,0.0722)));
    float v3 = max(v1,v2);
    float4 v4 = lerp(Sample_Color(IN),(Sample_Color(IN)*Sample_AO(IN)),saturate(1.0));
    float v5 = ((Sample_PBR(IN)).xyz).x;
    float v6 = (v5>0.524999976?1.0:0.0);
    float v7 = (v6*v5);
    float v8 = (v7-0.5);
    float v9 = (v8*2.0);
    float v10 = ((float4(0.0,0.0,0.0,1.0)).xyz).x;
    float v11 = (v10*v5);
    float v12 = (v11*0.5);
    float v13 = saturate((v9-v12));
    float v14 = ((Sample_PBR(IN)).xyz).y;
    float v15 = (1.0-v14);
    float v16 = pow(max(0.0,dot((Sample_Emission(IN)).xyz,float3(0.2126,0.7152,0.0722))),1.0);
    float v17 = (v16*dot((Sample_Emission_Strength(IN)).xyz,float3(0.2126,0.7152,0.0722)));
    float v18 = ((Sample_Normal(IN)).xyz).x;
    float v19 = ((Sample_Normal(IN)).xyz).y;
    float3 v20 = float3(v18,v19,v18);
    float3 v21 = (v20-float3(0.5,0.5,0.5));
    float v22 = ((Sample_Normal(IN)).xyz).z;
    float v23 = (v22*31.875);
    float v24 = (v23+1.0);
    float v25 = SapaDivide(1.0,v24);
    float3 v26 = (v21*float3(v25,v25,v25));
    float3 v27 = (v26+float3(0.5,0.5,0.5));
    float3 v28 = (v27*float3(2.0,2.0,0.0));
    float3 v29 = (v28+float3(-1.0,-1.0,1.0));
    float v30 = dot(v29,v29);
    float v31 = SapaDivide(2.0,v30);
    float3 v32 = (float3(v31,v31,v31)*v29);
    float v33 = (v32).x;
    float v34 = (v32).y;
    float v35 = (v31-1.0);
    float3 v36 = float3(v33,v34,v35);
    float3 v37 = SafeNormalize(v36);
    float3 v38 = (v37*float3(0.5,0.5,0.5));
    float3 v39 = (v38+float3(0.5,0.5,0.5));
    float3 v40 = SapaNormal(v39,dot((Sample_Normal_Strength(IN)).xyz,float3(0.2126,0.7152,0.0722)));
    SapaSurface s3 = SapaEmpty();
    s3.color = (v4).xyz;
    s3.metallic = v13;
    s3.roughness = v15;
    s3.alpha = v3;
    s3.emission = ((v4).xyz*v17);
    s3.normal = v40;
    SapaSurface s47 = SapaEmpty(); s47.alpha = 0.0;
    SapaSurface s48 = SapaMix(s3,s47,saturate(0.0));
    return s48;
}
// OWM: Glass
SapaSurface SapaStyle8(SapaInput IN)
{
    float v1 = SapaDivide(dot((Sample_Alpha(IN)).xyz,float3(0.2126,0.7152,0.0722)),dot((Sample_Alpha_Divisor(IN)).xyz,float3(0.2126,0.7152,0.0722)));
    float v2 = pow(max(0.0,dot((Sample_Dirt(IN)).xyz,float3(0.2126,0.7152,0.0722))),dot((Sample_Dirt_Power(IN)).xyz,float3(0.2126,0.7152,0.0722)));
    float v3 = (v2*dot((Sample_Dirt_Strength(IN)).xyz,float3(0.2126,0.7152,0.0722)));
    float v4 = (v3*dot((Sample_Dirt_Factor(IN)).xyz,float3(0.2126,0.7152,0.0722)));
    float v5 = saturate((v1+v4));
    SapaSurface s5 = SapaEmpty(); s5.alpha = 0.0;
    float4 v6 = lerp(Sample_Color(IN),Sample_Dirt_Color(IN),saturate(v4));
    float v7 = ((Sample_PBR(IN)).xyz).x;
    float v8 = (v7>0.524999976?1.0:0.0);
    float v9 = (v8*v7);
    float v10 = (v9-0.5);
    float v11 = (v10*2.0);
    float v12 = ((Sample_Material(IN)).xyz).x;
    float v13 = (v12*v7);
    float v14 = (v13*0.5);
    float v15 = saturate((v11-v14));
    float v16 = ((Sample_PBR(IN)).xyz).y;
    float v17 = (1.0-v16);
    float v18 = ((Sample_Normal(IN)).xyz).x;
    float v19 = ((Sample_Normal(IN)).xyz).y;
    float3 v20 = float3(v18,v19,v18);
    float3 v21 = (v20-float3(0.5,0.5,0.5));
    float v22 = ((Sample_Normal(IN)).xyz).z;
    float v23 = (v22*31.875);
    float v24 = (v23+1.0);
    float v25 = SapaDivide(1.0,v24);
    float3 v26 = (v21*float3(v25,v25,v25));
    float3 v27 = (v26+float3(0.5,0.5,0.5));
    float3 v28 = (v27*float3(2.0,2.0,0.0));
    float3 v29 = (v28+float3(-1.0,-1.0,1.0));
    float v30 = dot(v29,v29);
    float v31 = SapaDivide(2.0,v30);
    float3 v32 = (float3(v31,v31,v31)*v29);
    float v33 = (v32).x;
    float v34 = (v32).y;
    float v35 = (v31-1.0);
    float3 v36 = float3(v33,v34,v35);
    float3 v37 = SafeNormalize(v36);
    float3 v38 = (v37*float3(0.5,0.5,0.5));
    float3 v39 = (v38+float3(0.5,0.5,0.5));
    float3 v40 = SapaNormal(v39,dot((Sample_Normal_Strength(IN)).xyz,float3(0.2126,0.7152,0.0722)));
    SapaSurface s6 = SapaEmpty();
    s6.color = (v6).xyz;
    s6.metallic = v15;
    s6.roughness = v17;
    s6.alpha = 1.0;
    s6.emission = ((Sample_Emission_Color(IN)).xyz*dot((Sample_Emission_Strength(IN)).xyz,float3(0.2126,0.7152,0.0722)));
    s6.normal = v40;
    SapaSurface s48 = SapaMix(s5,s6,saturate(v5));
    return s48;
}
SapaSurface EvaluateSapa(SapaInput IN) {
    if (_Style < 0.5) return SapaStyle0(IN);
    if (_Style < 1.5) return SapaStyle1(IN);
    if (_Style < 2.5) return SapaStyle2(IN);
    if (_Style < 3.5) return SapaStyle3(IN);
    if (_Style < 4.5) return SapaStyle4(IN);
    if (_Style < 5.5) return SapaStyle5(IN);
    if (_Style < 6.5) return SapaStyle6(IN);
    if (_Style < 7.5) return SapaStyle7(IN);
    return SapaStyle8(IN);
}