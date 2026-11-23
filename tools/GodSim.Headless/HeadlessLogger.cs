using System;
using GodSim.Core;

namespace GodSim.Headless
{
    /// <summary>Console logger: warnings/errors to stderr, the rest to stdout; output is capped to avoid floods from faulting systems.</summary>
    public sealed class HeadlessLogger : ISimLogger
    {
        public const int MaxLines = 200;
        private int _lines;
        private bool _suppressedNoticeShown;

        public HeadlessLogger(LogLevel minLevel) { MinLevel = minLevel; }

        public LogLevel MinLevel { get; }
        public int ErrorCount { get; private set; }
        public int WarningCount { get; private set; }

        public void Log(LogLevel level, string message)
        {
            if (level == LogLevel.Error) ErrorCount++;
            else if (level == LogLevel.Warning) WarningCount++;
            if (level < MinLevel) return;
            if (_lines >= MaxLines)
            {
                if (!_suppressedNoticeShown)
                {
                    _suppressedNoticeShown = true;
                    Console.Error.WriteLine("[log] more than " + MaxLines + " log lines; further simulation log output suppressed.");
                }
                return;
            }
            _lines++;
            string line = "[" + level + "] " + (message ?? "");
            if (level >= LogLevel.Warning) Console.Error.WriteLine(line); else Console.WriteLine(line);
        }
    }
}
