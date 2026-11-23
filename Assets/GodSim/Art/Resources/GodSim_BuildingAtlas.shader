Shader "GodSim/BuildingAtlas"
{
    Properties
    {
        _MainTex ("Building atlas", 2D) = "white" {}
        _EmissionMap ("Window and forge light", 2D) = "black" {}
        _AgeRowOffset ("Age row offset (Unity UV)", Float) = 0
        _Color ("Instance tint", Color) = (1,1,1,1)
        _UseVertexTint ("Use mesh vertex tint", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf Lambert addshadow vertex:vert
        #pragma multi_compile_instancing
        #include "UnityCG.cginc"

        sampler2D _MainTex;
        sampler2D _EmissionMap;
        UNITY_INSTANCING_BUFFER_START(Buildings)
            UNITY_DEFINE_INSTANCED_PROP(float, _AgeRowOffset)
            UNITY_DEFINE_INSTANCED_PROP(fixed4, _Color)
            UNITY_DEFINE_INSTANCED_PROP(float, _UseVertexTint)
        UNITY_INSTANCING_BUFFER_END(Buildings)

        struct Input
        {
            float2 atlasUV;
            fixed3 tint;
        };

        void vert(inout appdata_full v, out Input data)
        {
            UNITY_INITIALIZE_OUTPUT(Input, data);
            UNITY_SETUP_INSTANCE_ID(v);
            data.atlasUV = v.texcoord.xy;
            data.atlasUV.y += UNITY_ACCESS_INSTANCED_PROP(Buildings, _AgeRowOffset);
            data.tint = lerp(fixed3(1,1,1), v.color.rgb,
                UNITY_ACCESS_INSTANCED_PROP(Buildings, _UseVertexTint))
                * UNITY_ACCESS_INSTANCED_PROP(Buildings, _Color).rgb;
        }

        void surf(Input data, inout SurfaceOutput output)
        {
            output.Albedo = tex2D(_MainTex, data.atlasUV).rgb * data.tint;
            output.Emission = tex2D(_EmissionMap, data.atlasUV).rgb;
            output.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
