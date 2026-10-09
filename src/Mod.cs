// N.E.K.O-KSP-bridge — 主入口
//
// 把 KSP 的飞行遥测通过 HTTP 暴露出去，供外部程序（例如 N.E.K.O. 的猫娘）读取。
//
// 设计原则
//   1. **只读** —— 这个版本不做任何控制，只报数据。玩家永远握着操纵杆。
//   2. **公用** —— 不依赖 N.E.K.O.，任何程序都能读。插件只是其中一个消费者。
//   3. **不打扰** —— 不弹窗、不改 UI、不接管输入。
//
// 线程模型（重要）
//   Unity 的 API 只能在主线程调用，所以：
//     主线程  Update()  -> BridgeTelemetry.Capture() 把快照存成字符串
//     HTTP 线程          -> 直接读那份字符串，不碰任何 Unity 对象

using System;
using System.IO;
using System.Text;

namespace NekoKspBridge
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class KspBridgeAddon : UnityEngine.MonoBehaviour
    {
        public const string ModName = "N.E.K.O-KSP-bridge";
        public const string Version = "0.1.0";

        private static KspBridgeAddon instance;
        private float nextCapture;
        private int sceneIndex = -1;

        public static KspBridgeAddon Instance
        {
            get { return instance; }
        }

        private void Awake()
        {
            try
            {
                instance = this;
                UnityEngine.Object.DontDestroyOnLoad(this.gameObject);

                BridgeConfig.Load();
                BridgeLog.Write("= " + ModName + " v" + Version + " =");
                BridgeLog.Write("配置: " + BridgeConfig.Describe());

                BridgeServer.Start(BridgeConfig.Port);
                BridgeLog.Write("HTTP 服务已启动: http://127.0.0.1:" + BridgeConfig.Port + "/");
            }
            catch (Exception ex)
            {
                BridgeLog.Error("启动失败: " + ex);
            }
        }

        private void Update()
        {
            try
            {
                // 场景变化记一笔，方便排查
                int now = (int)HighLogic.LoadedScene;
                if (now != sceneIndex)
                {
                    sceneIndex = now;
                    BridgeLog.Write("场景切换 -> " + HighLogic.LoadedScene);
                }

                // 遥测按间隔采集：这些量变化不快，没必要每帧算
                if (UnityEngine.Time.realtimeSinceStartup >= nextCapture)
                {
                    nextCapture = UnityEngine.Time.realtimeSinceStartup
                        + (float)Math.Max(0.05, BridgeConfig.PollIntervalMs / 1000.0);
                    BridgeTelemetry.Capture();
                }

                BridgeServer.Pump();
            }
            catch (Exception ex)
            {
                BridgeLog.Error("Update 异常: " + ex.Message);
            }
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }
    }

    /// <summary>
    /// 配置文件。放在模组目录下的 neko-ksp-bridge.ini，没有就自动生成。
    /// </summary>
    public static class BridgeConfig
    {
        public static int Port = 21579;
        public static double PollIntervalMs = 200;
        public static bool LogToFile = true;
        public static string Lang = "zh";

        private static string iniPath;

        public static string Dir()
        {
            try
            {
                // DLL 在 GameData/N.E.K.O-KSP-bridge/ 下
                return Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
            }
            catch
            {
                return null;
            }
        }

        public static void Load()
        {
            try
            {
                string dir = Dir();
                if (string.IsNullOrEmpty(dir))
                {
                    return;
                }
                iniPath = Path.Combine(dir, "neko-ksp-bridge.ini");

                if (!File.Exists(iniPath))
                {
                    WriteDefault(iniPath);
                    return;
                }

                string[] lines = File.ReadAllLines(iniPath, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line[0] == '#' || line[0] == ';')
                    {
                        continue;
                    }
                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                    {
                        continue;
                    }
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string val = line.Substring(eq + 1).Trim();

                    if (key == "port")
                    {
                        int p;
                        if (int.TryParse(val, out p) && p > 0 && p < 65536)
                        {
                            Port = p;
                        }
                    }
                    else if (key == "poll_interval_ms")
                    {
                        double d;
                        if (double.TryParse(val, out d) && d >= 50 && d <= 60000)
                        {
                            PollIntervalMs = d;
                        }
                    }
                    else if (key == "log_to_file")
                    {
                        LogToFile = val == "1" || val.ToLowerInvariant() == "true";
                    }
                    else if (key == "lang")
                    {
                        Lang = val;
                    }
                }
            }
            catch (Exception ex)
            {
                BridgeLog.Error("读配置失败: " + ex.Message);
            }
        }

        private static void WriteDefault(string path)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# N.E.K.O-KSP-bridge 配置");
                sb.AppendLine("# 改完需要重启 KSP 才生效");
                sb.AppendLine();
                sb.AppendLine("# HTTP 端口（只监听 127.0.0.1，不对局域网开放）");
                sb.AppendLine("port=21579");
                sb.AppendLine();
                sb.AppendLine("# 遥测采集间隔（毫秒），范围 50-60000");
                sb.AppendLine("# 调小更实时但更耗 CPU；调大更省。默认 200");
                sb.AppendLine("poll_interval_ms=200");
                sb.AppendLine();
                sb.AppendLine("# 是否写日志文件（neko-ksp-bridge.log）");
                sb.AppendLine("log_to_file=true");
                sb.AppendLine();
                sb.AppendLine("lang=zh");
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            catch
            {
            }
        }

        public static string Describe()
        {
            return "port=" + Port + " poll=" + PollIntervalMs + "ms log=" + LogToFile;
        }
    }
}
