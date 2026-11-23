using System;

namespace GodSim.Core
{
    public enum LogLevel { Trace, Debug, Info, Warning, Error }

    /// <summary>Logging abstraction. Unity layer routes to UnityEngine.Debug; headless routes to Console.</summary>
    public interface ISimLogger
    {
        LogLevel MinLevel { get; }
        void Log(LogLevel level, string message);
    }

    public sealed class NullLogger : ISimLogger
    {
        public static readonly NullLogger Instance = new NullLogger();
        public LogLevel MinLevel => LogLevel.Error;
        public void Log(LogLevel level, string message) { }
    }

    public sealed class ConsoleLogger : ISimLogger
    {
        public ConsoleLogger(LogLevel min = LogLevel.Info) { MinLevel = min; }
        public LogLevel MinLevel { get; }
        public void Log(LogLevel level, string message)
        {
            if (level < MinLevel) return;
            Console.WriteLine("[" + level + "] " + message);
        }
    }

    public static class LoggerExtensions
    {
        public static void Info(this ISimLogger log, string msg) { if (log != null && log.MinLevel <= LogLevel.Info) log.Log(LogLevel.Info, msg); }
        public static void Warn(this ISimLogger log, string msg) { if (log != null && log.MinLevel <= LogLevel.Warning) log.Log(LogLevel.Warning, msg); }
        public static void Error(this ISimLogger log, string msg) { if (log != null) log.Log(LogLevel.Error, msg); }
        public static void Debug(this ISimLogger log, string msg) { if (log != null && log.MinLevel <= LogLevel.Debug) log.Log(LogLevel.Debug, msg); }
    }
}
