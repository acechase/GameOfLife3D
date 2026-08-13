// GameOfLife3D — instanced cell shader (URP, unlit + HDR emission for bloom)
//
// Drawn via Graphics.RenderMeshIndirect: each instance reads its cell from
// _LiveCells (cellIndex, age), decodes the 3D grid position, and colors itself
// by age along a bioluminescent gradient. Newborn cells "pop" in, scaling up
// over the interval between simulation steps (_StepPhase).
Shader "GameOfLife3D/Cell"
{
    Properties { }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            StructuredBuffer<uint2> _LiveCells; // (cellIndex, age)

            float4x4 _ObjectToWorld;
            float4 _GridDims;       // x,y,z = grid size in cells
            float  _CellSizeLocal;  // local meters per cell
            float  _CubeScale;      // 0..1 cube fill of its cell (gap between cubes)
            float4 _ColorYoung;     // HDR
            float4 _ColorMid;       // HDR
            float4 _ColorOld;       // HDR
            float  _AgeMidpoint;    // age (generations) at the mid color
            float  _StepPhase;      // 0..1 progress toward the next sim step

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                uint   instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 color      : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                uint2 cellData = _LiveCells[IN.instanceID];
                uint index = cellData.x;
                float age = (float)cellData.y;

                uint sx = (uint)_GridDims.x;
                uint sy = (uint)_GridDims.y;
                uint layer = sx * sy;
                uint3 cell;
                cell.z = index / layer;
                uint rem = index - cell.z * layer;
                cell.y = rem / sx;
                cell.x = rem - cell.y * sx;

                float3 centerLocal = ((float3)cell + 0.5 - _GridDims.xyz * 0.5) * _CellSizeLocal;

                // Newborns scale in over the inter-step interval.
                float birth = age <= 1.0 ? lerp(0.15, 1.0, smoothstep(0.0, 1.0, _StepPhase)) : 1.0;
                float s = _CellSizeLocal * _CubeScale * birth;

                float3 localPos = centerLocal + IN.positionOS * s;
                float3 worldPos = mul(_ObjectToWorld, float4(localPos, 1.0)).xyz;
                OUT.positionCS = TransformWorldToHClip(worldPos);

                // Age gradient: young -> mid -> old.
                float t = saturate((age - 1.0) / (2.0 * _AgeMidpoint));
                float3 col = t < 0.5
                    ? lerp(_ColorYoung.rgb, _ColorMid.rgb, t * 2.0)
                    : lerp(_ColorMid.rgb, _ColorOld.rgb, t * 2.0 - 1.0);

                // Newborn flash.
                col *= age <= 1.0 ? 1.6 : 1.0;

                // Cheap directional shading so cubes read as 3D forms.
                float3 worldNormal = normalize(mul((float3x3)_ObjectToWorld, IN.normalOS));
                float3 keyDir = normalize(float3(0.4, 0.8, 0.3));
                float shade = 0.55 + 0.45 * saturate(dot(worldNormal, keyDir));
                OUT.color = col * shade;

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return half4(IN.color, 1.0);
            }
            ENDHLSL
        }
    }
}
