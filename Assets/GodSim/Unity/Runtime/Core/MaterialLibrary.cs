using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GodSim.Unity
{
    /// <summary>
    /// Shared, lazily created runtime materials. Main thread only. Materials are cached and re-created
    /// automatically if Unity destroyed them (scene unload, domain-reload-free play mode).
    /// <para>
    /// Shader chain for the vertex-color materials: "GodSim/VertexColorLit" (built-in pipeline) ->
    /// "Universal Render Pipeline/Simple Lit" -> "Standard". When a scriptable render pipeline (URP) is
    /// active the built-in GodSim shaders are skipped because their ForwardBase passes do not render there,
    /// and a Shader Graph asset named "GodSimVertexColorLit" ("Shader Graphs/GodSimVertexColorLit") is tried
    /// first instead. URP Simple Lit ignores vertex colors, so URP projects should author that graph
    /// (Vertex Color node x a _Color property into Base Color, GPU instancing enabled).
    /// </para>
    /// <para>
    /// Shader.Find only finds shaders that are included in a player build: add the GodSim shaders to
    /// Project Settings > Graphics > Always Included Shaders (or reference them from a material in Resources).
    /// </para>
    /// </summary>
    public static class MaterialLibrary
    {
        public const string VertexColorShaderName = "GodSim/VertexColorLit";
        public const string WaterShaderName = "GodSim/Water";
        /// <summary>Optional URP Shader Graph asset "GodSimVertexColorLit" (Vertex Color x instanced _Color into Base Color).</summary>
        public const string UrpVertexColorGraphName = "Shader Graphs/GodSimVertexColorLit";
        /// <summary>Optional URP Shader Graph asset "GodSimWater".</summary>
        public const string UrpWaterGraphName = "Shader Graphs/GodSimWater";
        public const string UrpSimpleLit = "Universal Render Pipeline/Simple Lit";
        public const string UrpUnlit = "Universal Render Pipeline/Unlit";
        public const string BuiltinStandard = "Standard";

        private static readonly Color DefaultWaterColor = new Color(0.16f, 0.45f, 0.66f, 0.72f);

        private static Material _vertexColorOpaque;
        private static Material _vertexColorInstanced;
        private static Material _water;
        private static readonly Dictionary<uint, Material> _unlit = new Dictionary<uint, Material>();

        /// <summary>Opaque lit material using mesh vertex colors (terrain, merged building meshes).</summary>
        public static Material VertexColorOpaque
        {
            get
            {
                if (_vertexColorOpaque == null)
                    _vertexColorOpaque = Create("GodSim VertexColor Opaque", FindVertexColorShader(), Color.white, false);
                return _vertexColorOpaque;
            }
        }

        /// <summary>
        /// Same shader as <see cref="VertexColorOpaque"/> but a separate instance intended for
        /// Graphics.DrawMeshInstanced / RenderMeshInstanced with a per-instance "_Color" (MaterialPropertyBlock).
        /// </summary>
        public static Material VertexColorInstanced
        {
            get
            {
                if (_vertexColorInstanced == null)
                    _vertexColorInstanced = Create("GodSim VertexColor Instanced", FindVertexColorShader(), Color.white, false);
                return _vertexColorInstanced;
            }
        }

        /// <summary>
        /// Transparent animated water: "GodSim/Water" (built-in pipeline) or "Shader Graphs/GodSimWater" (SRP),
        /// otherwise a transparent Simple Lit / Standard material tinted blue.
        /// </summary>
        public static Material Water
        {
            get
            {
                if (_water == null)
                {
                    Shader custom = FindSupported(BuiltinPipelineActive ? WaterShaderName : UrpWaterGraphName);
                    if (custom != null)
                        _water = Create("GodSim Water", custom, Color.white, false);
                    else
                        _water = Create("GodSim Water (fallback)", FindFirst(UrpSimpleLit, BuiltinStandard), DefaultWaterColor, true);
                }
                return _water;
            }
        }

        /// <summary>Cached unlit material of a flat color (selection markers, overlays, debug). Alpha &lt; 1 renders transparent.</summary>
        public static Material Unlit(Color c)
        {
            Color32 c32 = c;
            uint key = ((uint)c32.r << 24) | ((uint)c32.g << 16) | ((uint)c32.b << 8) | c32.a;
            Material m;
            if (_unlit.TryGetValue(key, out m) && m != null) return m;
            bool transparent = c32.a < 255;
            Shader shader = BuiltinPipelineActive
                ? FindFirst(transparent ? "Sprites/Default" : "Unlit/Color", "Unlit/Color", BuiltinStandard)
                : FindFirst(UrpUnlit, UrpSimpleLit, BuiltinStandard);
            m = Create("GodSim Unlit " + key.ToString("X8"), shader, c, transparent);
            _unlit[key] = m;
            return m;
        }

        /// <summary>True when no scriptable render pipeline asset is active (built-in renderer).</summary>
        public static bool BuiltinPipelineActive => GraphicsSettings.currentRenderPipeline == null;

        private static Shader FindVertexColorShader()
        {
            // The bundled .shader only has built-in-pipeline passes; under URP look for a Shader Graph instead.
            return BuiltinPipelineActive
                ? FindFirst(VertexColorShaderName, UrpSimpleLit, BuiltinStandard)
                : FindFirst(UrpVertexColorGraphName, UrpSimpleLit, BuiltinStandard);
        }

        private static Shader FindSupported(string name)
        {
            Shader s = Shader.Find(name);
            return s != null && s.isSupported ? s : null;
        }

        private static Shader FindFirst(string a, string b, string c = null)
        {
            Shader s = FindSupported(a);
            if (s == null && b != null) s = FindSupported(b);
            if (s == null && c != null) s = FindSupported(c);
            if (s == null) s = Shader.Find("Hidden/InternalErrorShader"); // always present: never pass null to new Material
            return s;
        }

        private static Material Create(string name, Shader shader, Color color, bool transparent)
        {
            var m = new Material(shader)
            {
                name = name,
                enableInstancing = true
            };
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (transparent) MakeTransparent(m);
            return m;
        }

        /// <summary>Best-effort switch of a Standard / URP Lit-family material to alpha blending.</summary>
        private static void MakeTransparent(Material m)
        {
            // URP (Lit / Simple Lit / Unlit)
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
            // Built-in Standard
            if (m.HasProperty("_Mode")) m.SetFloat("_Mode", 3f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
        }
    }
}
