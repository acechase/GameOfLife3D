// GameOfLife3D — ground reference grid
//
// A dim procedural grid on the XZ plane beneath the volume. Its whole job is
// parallax: without something in frame that holds still, orbiting a centred
// subject against a featureless background looks like the world spinning
// rather than the camera moving. Near grid lines sweeping past faster than far
// ones is what tells the eye which one is happening.
//
// Lines are computed from world position (no texture), antialiased with screen
// derivatives, and faded radially so the plane has no visible edge. Kept well
// under 1.0 in HDR so bloom never picks it up — it must stay background.
Shader "GameOfLife3D/Ground"
{
    Properties { }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Grid"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _LineColor;
            float3 _GridCenter;     // world position the lines are laid out around
            float  _MinorSpacing;   // metres between fine lines
            float  _MajorEvery;     // a heavier line every N minor ones
            float  _LineWidth;      // half-width in pixels
            float  _FadeRadius;     // metres from centre where the grid vanishes
            float  _Opacity;

            struct Attributes
            {
                float3 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                return OUT;
            }

            // Coverage of the nearest grid line, 0..1. Dividing the distance to
            // the line by its screen-space derivative keeps the line a constant
            // pixel width at any distance or angle, which is what stops a grid
            // from turning into moire noise as it recedes.
            // (Single assignment, one return — Metal warns about early returns
            // inside branches.)
            float GridCoverage(float2 p, float spacing, float widthPx)
            {
                float2 c = p / max(spacing, 1e-6);
                float2 d = abs(frac(c - 0.5) - 0.5) / max(fwidth(c), 1e-6);
                float nearest = min(d.x, d.y);
                return 1.0 - saturate(nearest / max(widthPx, 1e-6));
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 p = IN.positionWS.xz - _GridCenter.xz;

                float minor = GridCoverage(p, _MinorSpacing, _LineWidth);
                float major = GridCoverage(p, _MinorSpacing * _MajorEvery, _LineWidth * 1.7);
                float lines = max(minor * 0.4, major);

                // Squared radial falloff: no hard rim, and the plane reads as
                // fog-bounded rather than as a floating rectangle.
                float fade = 1.0 - saturate(length(p) / max(_FadeRadius, 1e-4));
                fade *= fade;

                return half4(_LineColor.rgb, lines * fade * _Opacity);
            }
            ENDHLSL
        }
    }
}
