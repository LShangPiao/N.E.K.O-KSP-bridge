// N.E.K.O-KSP-bridge — JSON 工具
//
// 手写 JSON 的理由：KSP 里的 Mono 对某些序列化库不友好，
// 而且我们要的输出结构很固定，手写反而更可控、更省事。

using System;
using System.Globalization;
using System.Text;

namespace NekoKspBridge
{
    public static class BridgeJson
    {
        /// <summary>转义成 JSON 字符串内容（不含首尾引号）。</summary>
        public static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return "";
            }
            StringBuilder sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32)
                        {
                            sb.Append(' ');
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            return sb.ToString();
        }

        /// <summary>数字：NaN / Infinity 输出 null，避免产生非法 JSON。</summary>
        public static string Num(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v))
            {
                return "null";
            }
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        public static string Num(float v)
        {
            return Num((double)v);
        }

        public static string Bool(bool b)
        {
            return b ? "true" : "false";
        }

        public static string Str(string s)
        {
            return "\"" + Esc(s) + "\"";
        }
    }
}
