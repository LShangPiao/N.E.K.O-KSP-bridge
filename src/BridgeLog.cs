// N.E.K.O-KSP-bridge — 日志
//
// 写到模组目录下的 neko-ksp-bridge.log，同时转发到 KSP 自己的日志
// （KSP.log 和 Player.log），方便两边对照。

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NekoKspBridge
{
    public static class BridgeLog
    {
        private static readonly object Gate = new object();
        private static readonly List<string> Recent = new List<string>();
        private const int RecentMax = 400;

        private static string filePath;
        private static bool fileTried;

        /// <summary>普通信息。</summary>
        public static void Write(string msg)
        {
            Add("[信息] " + msg);
        }

        /// <summary>警告。</summary>
        public static void Warn(string msg)
        {
            Add("[警告] " + msg);
        }

        /// <summary>错误。</summary>
        public static void Error(string msg)
        {
            Add("[错误] " + msg);
        }

        private static void Add(string line)
        {
            string stamped = DateTime.Now.ToString("HH:mm:ss") + " " + line;

            try
            {
                UnityEngine.Debug.Log("[" + KspBridgeAddon.ModName + "] " + line);
            }
            catch
            {
            }

            lock (Gate)
            {
                Recent.Add(stamped);
                if (Recent.Count > RecentMax)
                {
                    Recent.RemoveAt(0);
                }
            }

            WriteFile(stamped);
        }

        private static void WriteFile(string stamped)
        {
            if (!BridgeConfig.LogToFile)
            {
                return;
            }
            try
            {
                if (filePath == null && !fileTried)
                {
                    fileTried = true;
                    string dir = BridgeConfig.Dir();
                    if (!string.IsNullOrEmpty(dir))
                    {
                        filePath = Path.Combine(dir, "neko-ksp-bridge.log");
                    }
                }
                if (string.IsNullOrEmpty(filePath))
                {
                    return;
                }
                // 日志超过 2MB 就截断，免得无限增长
                try
                {
                    FileInfo fi = new FileInfo(filePath);
                    if (fi.Exists && fi.Length > 2 * 1024 * 1024)
                    {
                        File.Delete(filePath);
                    }
                }
                catch
                {
                }
                File.AppendAllText(filePath, stamped + Environment.NewLine, new UTF8Encoding(false));
            }
            catch
            {
            }
        }

        /// <summary>最近的日志（给 /log 用）。</summary>
        public static string RecentJson(int count)
        {
            lock (Gate)
            {
                int n = count <= 0 || count > Recent.Count ? Recent.Count : count;
                int start = Recent.Count - n;
                StringBuilder sb = new StringBuilder(2048);
                sb.Append("{\"ok\":true,\"count\":").Append(n).Append(",\"lines\":[");
                for (int i = 0; i < n; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(",");
                    }
                    sb.Append("\"").Append(BridgeJson.Esc(Recent[start + i])).Append("\"");
                }
                sb.Append("]}");
                return sb.ToString();
            }
        }
    }
}
