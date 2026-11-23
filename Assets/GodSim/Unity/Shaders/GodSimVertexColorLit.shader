// GodSim/VertexColorLit — built-in render pipeline only.
//
// Lambert (wrapped) lighting from the main directional light with shadows, spherical-harmonics
// ambient and fog. Albedo = mesh vertex color * _Color, where _Color is a per-instance property
// (UNITY_INSTANCING_BUFFER) so Graphics.DrawMeshInstanced / RenderMeshInstanced can tint each
// instance through a MaterialPropertyBlock ("_Color" vector array). Includes a ShadowCaster pass.
//
// URP / HDRP: this shader's passes are not rendered by scriptable pipelines. MaterialLibrary then
// falls back to "Universal Render Pipeline/Simple Lit", which IGNORES vertex colors. For correct
// colors under URP create a Shader Graph asset named "GodSimVertexColorLit"
// (Vertex Color node x Color property "_Color" -> Base Color, GPU instancing enabled);
// MaterialLibrary looks it up as "Shader Graphs/GodSimVertexColorLit" before Simple Lit.
//
// Player builds: add this shader to Project Settings > Graphics > Always Included Shaders,
// because it is only referenced through Shader.Find.
Shader "GodSim/VertexColorLit"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _Wrap ("Light Wrap", Range(0, 1)) = 0.25
        _AmbientBoost ("Ambient Boost", Range(0, 2)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200

        Pass
        {
            Name "FORWARD"
            Tags { "LightMode"="ForwardBase" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fwdbase nolightmap nodirlightmap nodynlightmap novertexlight
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            half _Wrap;
            half _AmbientBoost;

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(fixed4, _Color)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                fixed4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;          // must be named "pos" for the shadow macros
                fixed4 color : COLOR0;
                float3 worldNormal : TEXCOORD0;
                half3 ambient : TEXCOORD1;
                SHADOW_COORDS(2)
                UNITY_FOG_COORDS(3)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.color = v.color * UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
                o.ambient = max(half3(0, 0, 0), ShadeSH9(half4(o.worldNormal, 1))) * _AmbientBoost;
                TRANSFER_SHADOW(o)
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.worldNormal);
                half ndl = dot(n, _WorldSpaceLightPos0.xyz);
                half diffuse = saturate((ndl + _Wrap) / (1.0 + _Wrap));
                fixed shadow = SHADOW_ATTENUATION(i);
                half3 light = _LightColor0.rgb * (diffuse * shadow) + i.ambient;
                fixed4 col = fixed4(i.color.rgb * light, 1.0);
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            Cull Back

            CGPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma target 3.0
            #pragma multi_compile_shadowcaster
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            struct appdata_shadow
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f_shadow
            {
                V2F_SHADOW_CASTER;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f_shadow vertShadow (appdata_shadow v)
            {
                v2f_shadow o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f_shadow, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                return o;
            }

            float4 fragShadow (v2f_shadow i) : SV_Target
            {
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }

    FallBack "Legacy Shaders/VertexLit"
}
