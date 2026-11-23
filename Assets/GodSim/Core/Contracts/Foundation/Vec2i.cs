using System;

namespace GodSim.Core
{
    /// <summary>Immutable integer grid coordinate (tile space). X = column, Y = row.</summary>
    [Serializable]
    public readonly struct Vec2i : IEquatable<Vec2i>
    {
        public readonly int X;
        public readonly int Y;

        public Vec2i(int x, int y) { X = x; Y = y; }

        public static readonly Vec2i Zero = new Vec2i(0, 0);
        public static readonly Vec2i One = new Vec2i(1, 1);
        public static readonly Vec2i Invalid = new Vec2i(int.MinValue, int.MinValue);

        /// <summary>N, E, S, W.</summary>
        public static readonly Vec2i[] Dirs4 = { new Vec2i(0, 1), new Vec2i(1, 0), new Vec2i(0, -1), new Vec2i(-1, 0) };

        /// <summary>N, NE, E, SE, S, SW, W, NW.</summary>
        public static readonly Vec2i[] Dirs8 =
        {
            new Vec2i(0, 1), new Vec2i(1, 1), new Vec2i(1, 0), new Vec2i(1, -1),
            new Vec2i(0, -1), new Vec2i(-1, -1), new Vec2i(-1, 0), new Vec2i(-1, 1)
        };

        public bool IsValid => X != int.MinValue;

        public int ManhattanTo(Vec2i o) => Math.Abs(X - o.X) + Math.Abs(Y - o.Y);
        public int ChebyshevTo(Vec2i o) => Math.Max(Math.Abs(X - o.X), Math.Abs(Y - o.Y));
        public int DistanceSqTo(Vec2i o) { int dx = X - o.X, dy = Y - o.Y; return dx * dx + dy * dy; }
        public float DistanceTo(Vec2i o) => (float)Math.Sqrt(DistanceSqTo(o));

        /// <summary>Moves one step toward the target (8-directional).</summary>
        public Vec2i StepToward(Vec2i target) => new Vec2i(X + Math.Sign(target.X - X), Y + Math.Sign(target.Y - Y));

        public static Vec2i operator +(Vec2i a, Vec2i b) => new Vec2i(a.X + b.X, a.Y + b.Y);
        public static Vec2i operator -(Vec2i a, Vec2i b) => new Vec2i(a.X - b.X, a.Y - b.Y);
        public static Vec2i operator *(Vec2i a, int s) => new Vec2i(a.X * s, a.Y * s);
        public static bool operator ==(Vec2i a, Vec2i b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(Vec2i a, Vec2i b) => !(a == b);

        public bool Equals(Vec2i other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is Vec2i v && Equals(v);
        public override int GetHashCode() => Hashing.Coord(X, Y);
        public override string ToString() => "(" + X + "," + Y + ")";
    }
}
