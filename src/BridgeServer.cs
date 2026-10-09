// N.E.K.O-KSP-bridge — HTTP 服务
//
// 用最原始的 TcpListener 手写 HTTP，理由和 SFS-Agent 一样：
// KSP 的 Unity Mono 对 HttpListener 支持不稳（很多平台被裁掉了），
// 而我们的需求极简 —— 只读、GET、JSON。
//
// 线程模型：
//   这是**只读**模组，所有数据在 Capture() 时已经拍成字符串快照，
//   所以 HTTP 线程可以完全独立地应答，**不需要**回主线程。
//   （一旦将来加控制接口，就必须改成入队 + 主线程执行 —— 在 SFS-Agent 上
//    这个坑踩过：SpawnBlueprint 在后台线程调用会直接崩游戏。）

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace NekoKspBridge
{
    public static class BridgeServer
    {
        private static TcpListener listener;
        private static Thread acceptThread;
        private static volatile bool running;
        private static int port;

        public static bool Running
        {
            get { return running; }
        }

        public static int Port
        {
            get { return port; }
        }

        public static void Start(int p)
        {
            if (running)
            {
                return;
            }
            port = p;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, p);
                listener.Start();
                running = true;

                acceptThread = new Thread(AcceptLoop);
                acceptThread.IsBackground = true;
                acceptThread.Name = "NekoKspBridge";
                acceptThread.Start();
            }
            catch (Exception ex)
            {
                running = false;
                BridgeLog.Error("监听端口 " + p + " 失败: " + ex.Message);
            }
        }

        public static void Stop()
        {
            running = false;
            try
            {
                if (listener != null)
                {
                    listener.Stop();
                }
            }
            catch
            {
            }
            listener = null;
        }

        /// <summary>主线程每帧调用。只读模组暂时不需要做什么，留着占位。</summary>
        public static void Pump()
        {
        }

        private static void AcceptLoop()
        {
            while (running)
            {
                TcpClient client = null;
                try
                {
                    client = listener.AcceptTcpClient();
                }
                catch
                {
                    if (!running)
                    {
                        return;
                    }
                    Thread.Sleep(50);
                    continue;
                }

                TcpClient c = client;
                ThreadPool.QueueUserWorkItem(delegate { Handle(c); });
            }
        }

        private static void Handle(object state)
        {
            TcpClient client = state as TcpClient;
            if (client == null)
            {
                return;
            }
            try
            {
                client.ReceiveTimeout = 5000;
                client.SendTimeout = 5000;

                NetworkStream ns = client.GetStream();

                // 只读第一行就够：方法 + 路径
                string requestLine = ReadLine(ns, 4096);
                if (requestLine == null)
                {
                    return;
                }

                // 把请求头读完（不读干净会让客户端重置连接）
                int guard = 0;
                while (guard++ < 200)
                {
                    string h = ReadLine(ns, 8192);
                    if (h == null || h.Length == 0)
                    {
                        break;
                    }
                }

                string method = "GET";
                string path = "/";
                string query = "";

                int sp1 = requestLine.IndexOf(' ');
                if (sp1 > 0)
                {
                    method = requestLine.Substring(0, sp1);
                    int sp2 = requestLine.IndexOf(' ', sp1 + 1);
                    string target = sp2 > 0
                        ? requestLine.Substring(sp1 + 1, sp2 - sp1 - 1)
                        : requestLine.Substring(sp1 + 1);

                    int q = target.IndexOf('?');
                    if (q >= 0)
                    {
                        path = target.Substring(0, q);
                        query = target.Substring(q + 1);
                    }
                    else
                    {
                        path = target;
                    }
                }

                string body = Route(method.ToUpperInvariant(), path, query);
                SendJson(ns, 200, body);
            }
            catch (Exception ex)
            {
                BridgeLog.Error("请求处理异常: " + ex.Message);
            }
            finally
            {
                try
                {
                    client.Close();
                }
                catch
                {
                }
            }
        }

        // -- 路由 -------------------------------------------------------------

        private static string Route(string method, string path, string query)
        {
            if (method != "GET")
            {
                return "{\"ok\":false,\"error\":\"只支持 GET（这个模组是只读的）\"}";
            }

            switch (path)
            {
                case "/":
                case "/ping":
                    return Ping();

                case "/state":
                    return BridgeTelemetry.StateJson();

                case "/vessel":
                    return BridgeTelemetry.VesselJson();

                case "/orbit":
                    return BridgeTelemetry.OrbitJson();

                case "/resources":
                    return BridgeTelemetry.ResourcesJson();

                case "/crew":
                    return BridgeTelemetry.CrewJson();

                case "/target":
                    return BridgeTelemetry.TargetJson();

                case "/vessels":
                    return BridgeTelemetry.VesselsJson();

                case "/bodies":
                    return BridgeTelemetry.BodiesJson();

                case "/scene":
                    return BridgeTelemetry.SceneJson();

                case "/config":
                    return ConfigJson();

                case "/log":
                    return BridgeLog.RecentJson(QueryInt(query, "n", 100));

                case "/favicon.ico":
                    return "{}";

                default:
                    return "{\"ok\":false,\"error\":\"没有这个接口：" + BridgeJson.Esc(path)
                        + "\",\"endpoints\":[\"/ping\",\"/state\",\"/vessel\",\"/orbit\","
                        + "\"/resources\",\"/crew\",\"/target\",\"/vessels\",\"/bodies\","
                        + "\"/scene\",\"/config\",\"/log\"]}";
            }
        }

        private static string Ping()
        {
            StringBuilder sb = new StringBuilder(256);
            sb.Append("{\"ok\":true");
            sb.Append(",\"mod\":\"").Append(BridgeJson.Esc(KspBridgeAddon.ModName)).Append("\"");
            sb.Append(",\"version\":\"").Append(BridgeJson.Esc(KspBridgeAddon.Version)).Append("\"");
            sb.Append(",\"mode\":\"read-only\"");
            sb.Append(",\"port\":").Append(port);
            sb.Append(",\"game\":\"Kerbal Space Program\"");
            sb.Append("}");
            return sb.ToString();
        }

        private static string ConfigJson()
        {
            StringBuilder sb = new StringBuilder(256);
            sb.Append("{\"ok\":true");
            sb.Append(",\"port\":").Append(BridgeConfig.Port);
            sb.Append(",\"poll_interval_ms\":").Append(BridgeJson.Num(BridgeConfig.PollIntervalMs));
            sb.Append(",\"log_to_file\":").Append(BridgeJson.Bool(BridgeConfig.LogToFile));
            sb.Append(",\"lang\":\"").Append(BridgeJson.Esc(BridgeConfig.Lang)).Append("\"");
            sb.Append(",\"note\":\"改配置需要重启 KSP\"");
            sb.Append("}");
            return sb.ToString();
        }

        // -- 工具 -------------------------------------------------------------

        private static int QueryInt(string query, string key, int fallback)
        {
            if (string.IsNullOrEmpty(query))
            {
                return fallback;
            }
            string[] parts = query.Split('&');
            for (int i = 0; i < parts.Length; i++)
            {
                int eq = parts[i].IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }
                if (parts[i].Substring(0, eq) == key)
                {
                    int v;
                    if (int.TryParse(parts[i].Substring(eq + 1), out v))
                    {
                        return v;
                    }
                }
            }
            return fallback;
        }

        /// <summary>逐字节读一行（\r\n 结尾）。返回 null 表示连接断了。</summary>
        private static string ReadLine(NetworkStream ns, int max)
        {
            StringBuilder sb = new StringBuilder(128);
            byte[] one = new byte[1];
            while (sb.Length < max)
            {
                int n;
                try
                {
                    n = ns.Read(one, 0, 1);
                }
                catch
                {
                    return null;
                }
                if (n <= 0)
                {
                    return sb.Length > 0 ? sb.ToString() : null;
                }
                char c = (char)one[0];
                if (c == '\n')
                {
                    break;
                }
                if (c != '\r')
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static void SendJson(NetworkStream ns, int status, string body)
        {
            byte[] payload = Encoding.UTF8.GetBytes(body);
            StringBuilder head = new StringBuilder(256);
            head.Append("HTTP/1.1 ").Append(status).Append(" OK\r\n");
            head.Append("Content-Type: application/json; charset=utf-8\r\n");
            head.Append("Content-Length: ").Append(payload.Length).Append("\r\n");
            head.Append("Access-Control-Allow-Origin: *\r\n");
            head.Append("Cache-Control: no-store\r\n");
            head.Append("Connection: close\r\n");
            head.Append("\r\n");

            byte[] headBytes = Encoding.ASCII.GetBytes(head.ToString());
            ns.Write(headBytes, 0, headBytes.Length);
            ns.Write(payload, 0, payload.Length);
            ns.Flush();
        }
    }
}
