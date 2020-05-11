// Shader based on the Kino Streak shader from https://github.com/keijiro/Kino
// Original Shader by Keijiro Takahashi
// Modified Shader Variant Copyright(c) 2020 H. Gregor Molter


Shader "Hidden/Shader/LensFlares/Anamorphic"
{
    HLSLINCLUDE

    //#define __DEBUG_SHADER
    #pragma target 4.5
    #pragma only_renderers d3d11 ps4 xboxone vulkan metal switch

    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

    // Helper to create a 2d rotation matrix
    float2x2 RotateMatrix(float angle) {
        float sin, cos;
        sincos(radians(angle), sin, cos); // compute the sin and cosine
        float2x2 rotMat = float2x2(cos, -sin, sin, cos);
        const float2x2 halfMat = float2x2(float2(0.5, 0.5), float2(0.5, 0.5));
        const float2x2 doubleMat = float2x2(float2(2, 2), float2(2, 2));
        const float2x2 oneMat = float2x2(float2(1, 1), float2(1, 1));
        rotMat *= halfMat;
        rotMat += halfMat;
        rotMat *= doubleMat;
        rotMat -= oneMat;
        return rotMat;
    }

    // Properties of the Anamorphic Shader
    TEXTURE2D_X(_SourceTexture);
    TEXTURE2D(_InputTexture);
    TEXTURE2D(_OtherTexture);

    float4 _InputTexture_TexelSize;

    float _Intensity;
    float _Threshold; // Threshold to draw the anamorphic streaks
    float4 _Color; // Color of the streak
    float _Angle;
    // if _Angle = 0 it is assumed that the base texture sizes are equal the screen sizes. 
    // If a rotated anamorphic streak shall be shown, it is necessary that the internal textures are bigger (to cope with the rotation).
    // By doing so, we have to adjust (scale) the prefilter transformation (copy of the screen pixels to the internal textures) and at the end at the final composition.
    float4 _AngleTextureScale;
    float _Stretch;
    float _Fade;

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

#ifdef __DEBUG_SHADER

    // Some helpers to draw lines and rectangles for debugging purpose
    float drawLine(float2 uv, float2 p1, float2 p2) {
        const float Thickness = 0.002;
        float a = abs(distance(p1, uv));
        float b = abs(distance(p2, uv));
        float c = abs(distance(p1, p2));

        if (a >= c || b >= c) return 0.0;

        float p = (a + b + c) * 0.5;

        // median to (p1, p2) vector
        float h = 2 / c * sqrt(p * (p - a) * (p - b) * (p - c));

        return lerp(1.0, 0.0, smoothstep(0.5 * Thickness, 1.5 * Thickness, h));
    }

    float drawRect(float2 uv, float4 dim) {
        return max(max(max(
            drawLine(uv, dim.xy, dim.zy),
            drawLine(uv, dim.zy, dim.zw)),
            drawLine(uv, dim.zw, dim.xw)),
            drawLine(uv, dim.xw, dim.xy));
    }
#endif // __DEBUG_SHADER

    // Initial Prefilter for the Anamorphic shader
    float4 FragPrefilter(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2x2 rotMat = RotateMatrix(-_Angle);
        float2 uv = input.texcoord;

#ifndef __DEBUG_SHADER
        // Normal Shader

        // Scale and rotate UV 
        uv = (mul(uv - float2(0.5, 0.5), rotMat) * _AngleTextureScale.xy) + float2(0.5, 0.5);

        // Load the textures (start to comment from here for test pattern to be viewed in the frame debugger)
        uint2 ss = uv * _ScreenSize.xy - float2(0, 0.5);
        float3 c0 = LOAD_TEXTURE2D_X(_SourceTexture, ss).rgb;
        float3 c1 = LOAD_TEXTURE2D_X(_SourceTexture, ss+uint2(0,1)).rgb;
        float3 c = (c0 + c1) / 2;

        float br = max(c.r, max(c.g, c.b));
        c *= max(0, br - _Threshold) / max(br, 1e-5);
        return float4(c, 1);
#else // Debug Shader
        float2 scaled_uv = ((uv - float2(0.5, 0.5)) * _AngleTextureScale.xy) + float2(0.5, 0.5);
        float2 rot_scaled_uv = (mul(uv - float2(0.5, 0.5), rotMat) * _AngleTextureScale.xy) + float2(0.5, 0.5);
        float2 rot_uv = (mul(uv - float2(0.5, 0.5), rotMat)) + float2(0.5, 0.5);

        const float3 red = float3(1, 0, 0);
        const float3 green = float3(0, 1, 0);
        const float3 blue = float3(0, 0, 1);
        const float3 white = float3(1, 1, 1);
        const float3 yellow = float3(1, 1, 0);
        const float4 scaled_rect = float4(0.5 - (0.5 / _AngleTextureScale.x), 0.5 - (0.5 / _AngleTextureScale.y), 0.5 + (0.5 / _AngleTextureScale.x), 0.5 + (0.5 / _AngleTextureScale.y));

        // Little white cross in the middle
        float3 c0 = white * max(drawLine(uv, float2(0.475, 0.5), float2(0.525, 0.5)), drawLine(uv, float2(0.5, 0.475), float2(0.5, 0.525)));
        // Red Box showing outer area
        float3 c1 = red * max(drawRect(uv, float4(0, 0, 1, 1)), drawLine(uv, float2(0, 0.5), float2(1, 0.5)));
        // Box with the scaled ared (eg. normalized)
        float3 c2 = red * drawRect(uv, scaled_rect);
        // Box with target texture size
        float3 c6 = yellow * drawRect(uv, float4(0.5 - (_AngleTextureScale.x / 2), 0.5 - (_AngleTextureScale.y / 2), 0.5 + (_AngleTextureScale.x / 2), 0.5 + (_AngleTextureScale.y / 2)));

        // Box scaled and rotated
        float3 c3 = blue * drawRect(rot_uv, scaled_rect);
        float3 c4 = yellow * drawRect(rot_scaled_uv, float4(0, 0, 1, 1));

        // Filled box with blue shade scaled and rotated
        float3 c5 = float3(0, 0, 0);
        if (saturate(rot_scaled_uv.x) == rot_scaled_uv.x && saturate(rot_scaled_uv.y) == rot_scaled_uv.y) {
            c5 += float3(0, lerp(0, 1, rot_scaled_uv.x), lerp(0, 1, rot_scaled_uv.y));
        }

        return float4(max(max(max(max(max(max(c0, c1), c2), c3), c4), c5), c6), 1);
#endif
    }

    // Downsample
    float4 FragDownsample(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

        float2 uv = input.texcoord;
        const float dx = _InputTexture_TexelSize.x;

        float u0 = uv.x - dx * 5;
        float u1 = uv.x - dx * 3;
        float u2 = uv.x - dx * 1;
        float u3 = uv.x + dx * 1;
        float u4 = uv.x + dx * 3;
        float u5 = uv.x + dx * 5;

        half3 c0 = SAMPLE_TEXTURE2D(_InputTexture, s_linear_clamp_sampler, float2(u0, uv.y)).rgb;
        half3 c1 = SAMPLE_TEXTURE2D(_InputTexture, s_linear_clamp_sampler, float2(u1, uv.y)).rgb;
        half3 c2 = SAMPLE_TEXTURE2D(_InputTexture, s_linear_clamp_sampler, float2(u2, uv.y)).rgb;
        half3 c3 = SAMPLE_TEXTURE2D(_InputTexture, s_linear_clamp_sampler, float2(u3, uv.y)).rgb;
        half3 c4 = SAMPLE_TEXTURE2D(_InputTexture, s_linear_clamp_sampler, float2(u4, uv.y)).rgb;
        half3 c5 = SAMPLE_TEXTURE2D(_InputTexture, s_linear_clamp_sampler, float2(u5, uv.y)).rgb;

        return half4((c0 + c1 * 2 + c2 * 3 + c3 * 3 + c4 * 2 + c5) / 12, 1);
    }

    // Upsample
    float4 FragUpsample(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

        float2 uv = input.texcoord;
        const float dx = _InputTexture_TexelSize.x * 1.5;

        float u0 = uv.x - dx;
        float u1 = uv.x;
        float u2 = uv.x + dx;

        float3 c0 = SAMPLE_TEXTURE2D(_InputTexture, s_linear_clamp_sampler, float2(u0, uv.y)).rgb;
        float3 c1 = SAMPLE_TEXTURE2D(_InputTexture, s_linear_clamp_sampler, float2(u1, uv.y)).rgb;
        float3 c2 = SAMPLE_TEXTURE2D(_InputTexture, s_linear_clamp_sampler, float2(u2, uv.y)).rgb;
        float3 c3 = SAMPLE_TEXTURE2D(_OtherTexture,  s_linear_clamp_sampler, uv).rgb;
        return float4(lerp(c3, c0 / 4 + c1 / 2 + c2 / 4, _Stretch), 1);
    }

    // Blend with old anamorphic streaks to fade out smoothly
    float4 FragFade(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = input.texcoord;
        float3 c0 = SAMPLE_TEXTURE2D(_OtherTexture, s_linear_clamp_sampler, uv).rgb;
        float3 c1 = SAMPLE_TEXTURE2D(_InputTexture, s_linear_clamp_sampler, uv).rgb;
        return float4(lerp(max(c0,c1), c1, unity_DeltaTime.x/_Fade).rgb, 1);
    }


    // Final composition
    float4 FragComposition(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = input.texcoord;
        uint2 ss = uv * _ScreenSize.xy;
        float2x2 rotMat = RotateMatrix(_Angle);

        // Apply inverse scale and rotation
        uv = (mul(uv - float2(0.5, 0.5), rotMat) / _AngleTextureScale.xy) + float2(0.5, 0.5);

#ifndef __DEBUG_SHADER
        // Normal Shader
        float dx = _InputTexture_TexelSize.x * 1.5;

        float3 c0 = SAMPLE_TEXTURE2D(_InputTexture, s_linear_clamp_sampler, float2(uv.x + dx, uv.y)).rgb;
        float3 c1 = SAMPLE_TEXTURE2D(_InputTexture, s_linear_clamp_sampler, uv).rgb;
        float3 c2 = SAMPLE_TEXTURE2D(_InputTexture, s_linear_clamp_sampler, float2(uv.x + dx, uv.y)).rgb;
        float3 c3 = LOAD_TEXTURE2D_X(_SourceTexture, ss).rgb;
        float3 cf = (c0 / 4 + c1 / 2 + c2 / 4) * _Color.rgb * _Intensity * 5;

        return float4(cf + c3, 1);
#else // Debug Shader Variant
        float3 c1 = SAMPLE_TEXTURE2D(_InputTexture, s_linear_clamp_sampler, uv).rgb;
        return float4(c1, 1);
#endif
    }

    // Fill Black
    float4 FragFillBlack(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        return float4(0, 0, 0, 1);
    }

    // Fill Copy
    float4 FragCopy(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = input.texcoord;
        return SAMPLE_TEXTURE2D(_InputTexture, s_linear_clamp_sampler, uv);
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
            Name "Anamorphic - Prefilter"
            HLSLPROGRAM
                #pragma fragment FragPrefilter
                #pragma vertex Vert
            ENDHLSL
        }
            
        Pass // 1 - Downsample
        {
            Name "Anamorphic - Downsample"
            HLSLPROGRAM
                #pragma fragment FragDownsample
                #pragma vertex Vert
            ENDHLSL
        }

        Pass // 2 - UpSample
        {
            Name "Anamorphic - Upsample"
            HLSLPROGRAM
                #pragma fragment FragUpsample
                #pragma vertex Vert
            ENDHLSL
        }

        Pass // 3 - Fade
        {
            Name "Anamorphic - Fade"
            HLSLPROGRAM
                #pragma fragment FragFade
                #pragma vertex Vert
            ENDHLSL
        }

        Pass // 4 - Composition
        {
            Name "Anamorphic - Composition"
            HLSLPROGRAM
                #pragma fragment FragComposition
                #pragma vertex Vert
            ENDHLSL
        }

        Pass // 5 - Fill with black 
        {
            Name "Anamorphic - Fill Black"
            HLSLPROGRAM
                #pragma fragment FragFillBlack 
                #pragma vertex Vert
            ENDHLSL
        }

        Pass // 6 - Copy
        {
            Name "Anamorphic - Copy"
            HLSLPROGRAM
                #pragma fragment FragCopy
                #pragma vertex Vert
            ENDHLSL
        }
    }
    Fallback Off
}
