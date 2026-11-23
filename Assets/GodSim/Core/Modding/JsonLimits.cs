namespace GodSim.Core.Modding
{
    /// <summary>
    /// Hard resource limits for <see cref="SafeJson"/>. Every limit is enforced while parsing, so a hostile
    /// document is rejected as soon as it crosses one, in time linear in the input consumed so far.
    /// Values above the absolute ceilings below are clamped down to them.
    /// </summary>
    public sealed class JsonLimits
    {
        /// <summary>Absolute ceiling on MaxDepth regardless of configuration (keeps recursion stack-safe).</summary>
        public const int DepthCeiling = 128;
        /// <summary>Absolute ceiling on MaxInputBytes (16 MB).</summary>
        public const int InputCeiling = 16 * 1024 * 1024;

        /// <summary>Maximum input size: bytes for UTF-8 input, UTF-16 code units for string input. Default 2 MB.</summary>
        public int MaxInputBytes = 2 * 1024 * 1024;
        /// <summary>Maximum container nesting (the root object/array is depth 1). Default 32.</summary>
        public int MaxDepth = 32;
        /// <summary>Maximum decoded length of any string or key, in UTF-16 code units. Default 4096.</summary>
        public int MaxStringLength = 4096;
        /// <summary>Maximum items in one array. Default 10,000.</summary>
        public int MaxArrayItems = 10000;
        /// <summary>Maximum members in one object. Default 1,000.</summary>
        public int MaxObjectKeys = 1000;
        /// <summary>Maximum values in the whole document. Default 200,000.</summary>
        public int MaxTotalNodes = 200000;
        /// <summary>Maximum characters in one number literal. Default 64.</summary>
        public int MaxNumberLength = 64;

        /// <summary>A fresh instance with the default limits (a new object each call, so it cannot be mutated globally).</summary>
        public static JsonLimits Default => new JsonLimits();

        /// <summary>Copy with every value forced into a sane range (non-positive values fall back to defaults).</summary>
        public JsonLimits Normalized()
        {
            var d = new JsonLimits();
            return new JsonLimits
            {
                MaxInputBytes = Pick(MaxInputBytes, d.MaxInputBytes, InputCeiling),
                MaxDepth = Pick(MaxDepth, d.MaxDepth, DepthCeiling),
                MaxStringLength = Pick(MaxStringLength, d.MaxStringLength, InputCeiling),
                MaxArrayItems = Pick(MaxArrayItems, d.MaxArrayItems, InputCeiling),
                MaxObjectKeys = Pick(MaxObjectKeys, d.MaxObjectKeys, 100000),
                MaxTotalNodes = Pick(MaxTotalNodes, d.MaxTotalNodes, InputCeiling),
                MaxNumberLength = Pick(MaxNumberLength, d.MaxNumberLength, 1024),
            };
        }

        private static int Pick(int v, int fallback, int ceiling) => v <= 0 ? fallback : (v > ceiling ? ceiling : v);
    }
}
