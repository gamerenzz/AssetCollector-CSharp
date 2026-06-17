using System;
using System.Collections.Generic;

namespace AssetCollector
{
    // 【模块化】实时安全调试日志引擎
    public static class DebugLogger
    {
        private static readonly object logLock = new object();
        public static readonly List<string> Logs = new List<string>();
        public static Action<string> OnLogAdded;

        public static void Log(string level, string message, Exception ex = null)
        {
            string time = DateTime.Now.ToString("HH:mm:ss");
            string line = $"[{time}] [{level}] {message}";
            if (ex != null)
            {
                line += $"\n   [异常详情]: {ex.Message}\n   [调用位置]: {ex.StackTrace}";
            }

            lock (logLock)
            {
                Logs.Add(line);
                if (Logs.Count > 200) Logs.RemoveAt(0); // 仅保留最新200条
            }

            OnLogAdded?.Invoke(line);
        }
    }
}
