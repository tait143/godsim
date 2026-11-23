using GodSim.Core;
using UnityEngine;

namespace GodSim.Unity
{
    /// <summary>
    /// Routes simulation log output to the Unity console. Every line is prefixed with "[GodSim]".
    /// Warnings go to <see cref="Debug.LogWarning(object)"/>, errors to <see cref="Debug.LogError(object)"/>,
    /// everything else to <see cref="Debug.Log(object)"/>.
    /// </summary>
    public sealed class UnityLogSink : ISimLogger
    {
        public const string Prefix = "[GodSim] ";

        public UnityLogSink(LogLevel minLevel = LogLevel.Info) { MinLevel = minLevel; }

        /// <summary>Messages below this level are dropped.</summary>
        public LogLevel MinLevel { get; set; }

        public void Log(LogLevel level, string message)
        {
            if (level < MinLevel) return;
            switch (level)
            {
                case LogLevel.Error:
                    Debug.LogError(Prefix + message);
                    break;
                case LogLevel.Warning:
                    Debug.LogWarning(Prefix + message);
                    break;
                case LogLevel.Info:
                    Debug.Log(Prefix + message);
                    break;
                default:
                    Debug.Log(Prefix + "(" + level + ") " + message);
                    break;
            }
        }
    }
}
