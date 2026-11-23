// GodSim/Water — built-in render pipeline only.
//
// Transparent water with vertex-animated waves (driven by _Time), analytic wave normals,
// half-Lambert + Blinn specular from the main directional light, SH ambient, a fresnel rim tint
// and fog. Vertex color convention: RGB tints the water (white = no tint) and ALPHA is
// "shallowness" (1 = shallow/coast -> _ShallowColor, 0 = open ocean -> _DeepColor). Meshes without
// vertex colors get Unity's default white and render with the shallow color.
// The water renderer should set ShadowCastingMode.Off (this shader has no ShadowCaster pass).
//
// URP / HDRP: not rendered by scriptable pipelines. MaterialLibrary looks for a Shader Graph named
// "GodSimWater" ("Shader Graphs/GodSimWater") and otherwise uses a transparent
// "Universal Render Pipeline/Simple Lit" material (no waves).
//
// Player builds: add this shader to Project Settings > Graphics > Always Included Shaders.
Shader "GodSim/Water"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _ShallowColor ("Shallow Color", Color) = (0.30, 0.68, 0.80, 0.62)
        _DeepColor ("Deep Color", Color) = (0.06, 0.24, 0.45, 0.86)
        _FresnelColor ("Fresnel Tint", Color) = (0.80, 0.92, 1.00, 1)
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 3
        _WaveAmplitude ("Wave Amplitude", Range(0, 0.5)) = 0.05
        _WaveFrequency ("Wave Frequency", Range(0.05, 4)) = 0.8
        _WaveSpeed ("Wave Speed", Range(0, 5)) = 1.2
        _Specular ("Specular Strength", Range(0, 2)) = 0.6
        _Gloss ("Gloss", Range(4, 256)) = 80
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 200

        Pass
        {
            Name "FORWARD"
            Tags { "LightMode"="ForwardBase" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            fixed4 _Color;
            fixed4 _ShallowColor;
            fixed4 _DeepColor;
            fixed4 _FresnelColor;
            half _FresnelPower;
            float _WaveAmplitude;
            float _WaveFrequency;
            float _WaveSpeed;
            half _Specular;
            half _Gloss;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                fixed4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR0;
                float3 worldPos : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                UNITY_FOG_COORDS(2)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Sum of two travelling sine waves; returns height and writes the XZ gradient.
            float WaveHeight (float2 p, float t, out float2 grad)
            {
                float f = _WaveFrequency;
                float a1 = p.x * f + t;
                float a2 = (p.x * 0.6 + p.y * 0.8) * f * 1.7 + t * 1.3;
                float a3 = p.y * f * 0.9 - t * 0.7;
                float h = sin(a1) * cos(a3) + 0.5 * sin(a2);
                grad.x = f * (cos(a1) * cos(a3) + 0.5 * 1.7 * 0.6 * cos(a2));
                grad.y = f * (-0.9 * sin(a1) * sin(a3) + 0.5 * 1.7 * 0.8 * cos(a2));
                h *= _WaveAmplitude;
                grad *= _WaveAmplitude;
                return h;
            }

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                float2 grad;
                float t = _Time.y * _WaveSpeed;
                wp.y += WaveHeight(wp.xz, t, grad);

                o.worldPos = wp;
                o.worldNormal = normalize(float3(-grad.x, 1.0, -grad.y));
                o.pos = UnityWorldToClipPos(wp);
                fixed shallow = saturate(v.color.a);
                fixed4 baseCol = lerp(_DeepColor, _ShallowColor, shallow);
                o.color = fixed4(baseCol.rgb * v.color.rgb, baseCol.a) * _Color;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.worldNormal);
                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - i.worldPos);
                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);

                half ndl = dot(n, lightDir) * 0.5 + 0.5;               // half-Lambert keeps water soft
                half3 ambient = max(half3(0, 0, 0), ShadeSH9(half4(n, 1)));
                half3 h = normalize(lightDir + viewDir);
                half spec = pow(saturate(dot(n, h)), _Gloss) * _Specular;
                half fresnel = pow(1.0 - saturate(dot(n, viewDir)), _FresnelPower);

                fixed4 col = i.color;
                col.rgb = col.rgb * (_LightColor0.rgb * ndl + ambient);
                col.rgb = lerp(col.rgb, _FresnelColor.rgb, fresnel * _FresnelColor.a);
                col.rgb += _LightColor0.rgb * spec;
                col.a = saturate(lerp(col.a, 1.0, fresnel * 0.5) + spec);
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }

    FallBack Off
}
