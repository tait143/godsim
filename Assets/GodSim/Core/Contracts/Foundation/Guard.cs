using System;

namespace GodSim.Core
{
    /// <summary>Argument validation helpers. Throw early on programmer errors; never let bad data enter WorldState.</summary>
    public static class Guard
    {
        public static T NotNull<T>(T value, string name) where T : class
        {
            if (value == null) throw new ArgumentNullException(name);
            return value;
        }

        public static int InRange(int value, int minInclusive, int maxInclusive, string name)
        {
            if (value < minInclusive || value > maxInclusive)
                throw new ArgumentOutOfRangeException(name, value, "Expected [" + minInclusive + ", " + maxInclusive + "]");
            return value;
        }

        public static float Finite(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentException(name + " must be finite.", name);
            return value;
        }

        public static void That(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }

    /// <summary>Thrown when untrusted data (save file, mod data, command) fails validation.</summary>
    public sealed class SimValidationException : Exception
    {
        public SimValidationException(string message) : base(message) { }
        public SimValidationException(string message, Exception inner) : base(message, inner) { }
    }
}
