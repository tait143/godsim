// Self-test fixture for scripts/check-unity-compat.sh: valid C# 9 that must NOT be flagged.
// Never use System.Random, DateTime.Now, Guid.NewGuid(), Task.Run or UnityEngine in Core.
/* using UnityEngine;
   namespace Not.FileScoped;
   public record Nope(int X); */
using System;
using System.Collections.Generic;

namespace GodSim.Core.Fixtures
{
    /// <summary>Uses <c>ctx.Rng</c>, never System.Random or Environment.TickCount.</summary>
    public sealed class Lookalikes
    {
        private readonly List<int> _records = new List<int>();
        private bool required;
        public bool Required;
        public int TaskCount;
        public int Init { get; set; }
        public string Quote = @"He said ""hi""";
        public string Verbatim = @"multi
line with System.Random and DateTime.Now; global using X;
and ""quoted"" Guid.NewGuid() end""";
        public string Url = "http://example.com/UnityEngine // not a comment";
        public string Esc = "escaped \" Guid.NewGuid() \\";
        public string Interp => $"{Init} UnityEngine async Task<int> System.Text.Json";
        public string VerbInterp => $@"{Init} ""DateTime.UtcNow""";
        public char QuoteChar = '"';
        public char Apostrophe = '\'';

        public int Sum(IReadOnlyList<int> record)
        {
            int init = 0;
            int asyncCount = 0;
            bool flag = !!required;
            foreach (var r in record) { init += r; }
            if (!!flag) init++;
            _records.Add(init + asyncCount + TaskCount);
            return init;
        }

#if UNITY_EDITOR
        public string EditorOnly = "UNITY_EDITOR define, not an engine reference";
#endif

        public override int GetHashCode() { return Init.GetHashCode(); }

        public override bool Equals(object obj) { return obj is Lookalikes other && other.Init == Init; }

        public void Records()
        {
            var record = 1;
            record = record + 1;
            Init = record;
        }
    }
}
