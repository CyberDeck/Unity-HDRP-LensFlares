// Shader based on the Aerobox Flare shader from https://github.com/modanhan/Unity-Lens-Flare-2019
// Original Shader Copyright(c) 2019 Anthony Han
// Modified (HDRP) Shader Variant Copyright(c) 2020 H. Gregor Molter

Shader "Hidden/Shader/LensFlares/RadialWarpAndGhosts"
{
    HLSLINCLUDE

    #pragma target 4.5
    #pragma only_renderers d3d11 ps4 xboxone vulkan metal switch

    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

    struct Attributes
    {
        uint vertexID : SV_VertexID;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float2 texcoord   : TEXCOORD0;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    Varyings Vert(Attributes input)
    {
        Varyings output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
        output.texcoord = GetFullScreenTriangleTexCoord(input.vertexID);
        return output;
    }

    // Properties of the Flare Shader
    TEXTURE2D_X(_SourceTexture);
    TEXTURE2D(_InputTexture);
    TEXTURE2D(_AddTexture);
    TEXTURE2D(_ChromaticAberration_Spectrum);

    float4 _InputTexture_TexelSize;

    float _Intensity;
    float _RadialWarpLength; // 0.5f
    float _RadialWarpIntensity; // 0.0025f
    float _GhostIntensity; // 0.005f
    float _AddMultiplier; // 4f
    float _Delta; // 0.5f

    // Initial Prefilter for the Flare shader (just copy it)
    float4 FragPrefilter(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        uint2 ss = input.texcoord * _ScreenSize.xy;
        float3 c = LOAD_TEXTURE2D_X(_SourceTexture, ss).rgb;
        return float4(c, 1);
    }

    static const float gaussian[7] = {
            0.00598,	0.060626,	0.241843,	0.383103,	0.241843,	0.060626,	0.00598
    };

    // Horizontal Blur 
    float4 FragHBlur(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = input.texcoord;
        float2 step = float2(_InputTexture_TexelSize.x, 0);
        float3 color;
        for (int idx = -3; idx <= 3; idx++) {
            float3 tColor = SAMPLE_TEXTURE2D(_InputTexture, s_linear_clamp_sampler, uv + idx * step).rgb;
            color += tColor * gaussian[idx + 3];
        }
        return float4(color, 1);
    }

    // Vertical Blur 
    float4 FragVBlur(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = input.texcoord;
        float2 step = float2(0, _InputTexture_TexelSize.y);
        float3 color;
        for (int idx = -3; idx <= 3; idx++) {
            float3 tColor = SAMPLE_TEXTURE2D(_InputTexture, s_linear_clamp_sampler, uv + idx * step).rgb;
            color += tColor * gaussian[idx + 3];
        }
        return float4(color, 1);
    }

    // Radial warp
    float4 FragRadialWarp(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = input.texcoord;
        float2 ghostVec = uv - 0.5;
        float2 haloVec = normalize(ghostVec) * -_RadialWarpLength;
        float3 color = max(SAMPLE_TEXTURE2D(_InputTexture, s_linear_clamp_sampler, uv + haloVec).rgb - 0.0, 0)
            * length(ghostVec) * _RadialWarpIntensity;
        return float4(color, 1);
    }

    static const float ghosts[9] = {
        0.625,	0.390625,	0.24414,	0.15258,    -0.625,	-0.390625,	-0.24414,	-0.15258,   -0.09536,
    };

    // Ghosts
    float4 FragGhost(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = input.texcoord;
        float2 ghost_uv = uv - 0.5;
        float3 color;
        
        for (int i = 0; i < 9; i++) {
            float t_p = ghosts[i];
            color += SAMPLE_TEXTURE2D(_InputTexture, s_linear_clamp_sampler, ghost_uv * t_p + 0.5).rgb * (t_p * t_p);
        }
        return float4(color * _GhostIntensity, 1);
    }


    // Chromatic Aberration from two combined textures 
    float4 FragChromaticAberration(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 texcoord = input.texcoord.xy * 2 - 1;
        float2 diff_texels = normalize(-texcoord) * pow(dot(texcoord, texcoord), 0.25) * 36;
        float2 diff_sampler = diff_texels * _InputTexture_TexelSize.xy;
        float2 pos = input.texcoord.xy;
        int samples = clamp(int(length(diff_texels)), 3, 18);
        float inv_samples = 1.0 / samples;
        float2 delta = diff_sampler * inv_samples;
        pos -= delta * samples * 0.5;
        float3 sum = float3(0,0,0), filterSum=float3(0, 0, 0);
        float2 t = float2(0.5 * inv_samples, 0);
        for (int i = 0; i < samples; i++)
        {
            float3 s1 = SAMPLE_TEXTURE2D_LOD(_InputTexture, s_linear_clamp_sampler, pos, 0).rgb;
            float3 s2 = SAMPLE_TEXTURE2D_LOD(_AddTexture, s_linear_clamp_sampler, pos, 0).rgb;
            float3 s = s1 + s2 * _AddMultiplier;
            float3 filter = SAMPLE_TEXTURE2D_LOD(_ChromaticAberration_Spectrum, s_linear_clamp_sampler, t, 0).rgb;
            sum += s * filter;
            t.x += inv_samples;
            filterSum += filter;
            pos += delta;
        }
        return float4(sum / filterSum, 1);
    }

    // Box Up Sampling 
    float4 FragBox(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = input.texcoord;
        float4 offset = _InputTexture_TexelSize.xyxy * float4(-_Delta, -_Delta, _Delta, _Delta);
        float3 color = (SAMPLE_TEXTURE2D(_InputTexture, s_linear_clamp_sampler, uv + offset.xy).rgb
        + SAMPLE_TEXTURE2D(_InputTexture, s_linear_clamp_sampler, uv + offset.zy).rgb
        + SAMPLE_TEXTURE2D(_InputTexture, s_linear_clamp_sampler, uv + offset.xw).rgb
        + SAMPLE_TEXTURE2D(_InputTexture, s_linear_clamp_sampler, uv + offset.zw).rgb) * 0.25;
        return float4(color, 1);
    }

    // Final composition
    float4 FragComposition(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = input.texcoord;
        uint2 ss = uv * _ScreenSize.xy;
        
        float3 original = LOAD_TEXTURE2D_X(_SourceTexture, ss).rgb;
        float3 flare = SAMPLE_TEXTURE2D(_InputTexture, s_linear_clamp_sampler, uv).rgb;

        return float4(original+flare*_AddMultiplier, 1);
    }
    ENDHLSL

    SubShader
    {
        ZWrite Off
        ZTest Always
        Blend Off
        Cull Off


        Pass // 0 - Prefilter
        {
            Name "Radial Warp and Ghosts - Prefilter"
            HLSLPROGRAM
                #pragma fragment FragPrefilter
                #pragma vertex Vert
            ENDHLSL
        }

        Pass // 1 - Horizontal Blur
        {
            Name "Radial Warp and Ghosts - HBlur"
            HLSLPROGRAM
                #pragma fragment FragHBlur
                #pragma vertex Vert
            ENDHLSL
        }

        Pass // 2 - Veritcal Blur
        {
            Name "Radial Warp and Ghosts - VBlur"
            HLSLPROGRAM
                #pragma fragment FragVBlur
                #pragma vertex Vert
            ENDHLSL
        }

        Pass // 3 - Radial Warp
        {
            Name "Radial Warp and Ghosts - Radial Warp"
            HLSLPROGRAM
                #pragma fragment FragRadialWarp
                #pragma vertex Vert
            ENDHLSL
        }

        Pass // 4 - Ghost
        {
            Name "Radial Warp and Ghosts - Ghost"
            HLSLPROGRAM
                #pragma fragment FragGhost
                #pragma vertex Vert
            ENDHLSL
        }

        Pass // 5 - Chromatic Aberration
        {
            Name "Radial Warp and Ghosts - Chromatic Aberration"
            HLSLPROGRAM
                #pragma fragment FragChromaticAberration
                #pragma vertex Vert
            ENDHLSL
        }

        Pass // 6 - Box
        {
            Name "Radial Warp and Ghosts - Box"
            HLSLPROGRAM
                #pragma fragment FragBox
                #pragma vertex Vert
            ENDHLSL
        }
            
        Pass // 7 - Composition
        {
            Name "Radial Warp and Ghosts - Composition"
            HLSLPROGRAM
                #pragma fragment FragComposition
                #pragma vertex Vert
            ENDHLSL
        }
    }
    Fallback Off
}
