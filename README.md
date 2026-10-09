# N.E.K.O-KSP-bridge

把《坎巴拉太空计划 Kerbal Space Program》的**飞行遥测**通过 HTTP 暴露出来，
让外部程序（例如 N.E.K.O. 的猫娘）能读到飞船在干什么。

**这个版本是只读的** —— 它只报数据，不做任何控制。玩家永远握着操纵杆。

---

## 为什么需要它

KSP 没有对外开放的查询接口。想知道「现在高度多少、油还剩多少、轨道什么样」，
只能写个 mod 进游戏里读，再想办法送出来。

这个 mod 就是那个「送出来」的部分：一个只监听 `127.0.0.1` 的 HTTP 服务。

它**不依赖 N.E.K.O.** —— 任何程序都能读，浏览器直接打开也行。

---

## 安装

1. 把 `NekoKspBridge.dll` 放进

   ```
   <KSP>/GameData/N.E.K.O-KSP-bridge/
   ```

2. 启动游戏。首次运行会自动生成配置文件

   ```
   <KSP>/GameData/N.E.K.O-KSP-bridge/neko-ksp-bridge.ini
   ```

3. 浏览器打开 <http://127.0.0.1:21579/ping> 确认活着。

---

## 接口

所有接口都是 `GET`，返回 JSON。默认端口 **21579**。

| 接口 | 说明 |
| --- | --- |
| `/ping` | 存活检查 + 版本 |
| `/state` | 全部数据（飞船 + 轨道 + 资源 + 乘员 + 目标） |
| `/vessel` | 飞船基础信息：名称、状态、高度、速度、姿态 |
| `/orbit` | 轨道根数：远点/近点/离心率/倾角/周期 |
| `/resources` | 资源汇总：燃料、氧化剂、电、单组元…… |
| `/crew` | 乘员名单 |
| `/target` | 当前锁定的目标（飞船或天体） |
| `/vessels` | 存档里所有飞船 |
| `/bodies` | 星系里所有天体 |
| `/scene` | 当前场景（主菜单 / 航天中心 / 飞行……） |
| `/config` | 当前配置 |
| `/log?n=100` | 最近的模组日志 |

### 快速看一眼

```
curl http://127.0.0.1:21579/orbit
```

```json
{
  "ok": true, "exists": true, "body": "Kerbin",
  "apoapsis": 152340.5, "periapsis": 78500.2,
  "eccentricity": 0.0531, "inclination": 28.61,
  "period": 2412.7, "time_to_apoapsis": 1203.4,
  "is_stable_orbit": true, "atmosphere_depth": 70000
}
```

---

## 配置

`neko-ksp-bridge.ini`（改完要**重启游戏**）：

```ini
# HTTP 端口（只监听 127.0.0.1，不对局域网开放）
port=21579

# 遥测采集间隔（毫秒），范围 50-60000
# 调小更实时但更耗 CPU；调大更省
poll_interval_ms=200

# 是否写日志文件
log_to_file=true

lang=zh
```

---

## 设计说明

### 线程模型

Unity 的 API 只能在主线程调用，所以：

```
主线程  Update()  ->  BridgeTelemetry.Capture()  把状态拍成 JSON 字符串
HTTP 线程          ->  直接读那份缓存，完全不碰 Unity 对象
```

因为是只读，HTTP 线程可以独立应答，不需要回主线程。

> ⚠️ 将来如果要加**控制**接口，必须改成「入队 + 主线程执行」。
> 在姊妹项目 SFS-Agent 上踩过这个坑：在后台线程调用会创建 Unity Mesh 的
> 操作，游戏会直接崩溃。

### 为什么手写 HTTP

用的 `TcpListener`，没用 `HttpListener`。
KSP 的 Unity Mono 对 `HttpListener` 支持不稳（很多平台被裁掉），
而这里的 HTTP 需求极简：只读、GET、JSON。

### 为什么手动拼 JSON

KSP 的 Mono 环境对某些序列化库不友好，而且输出结构固定，手写反而更可控。
所有数字经过 `BridgeJson.Num()`，`NaN` / `Infinity` 一律输出 `null`，
保证永远是合法 JSON。

---

## 兼容性

- 目标游戏：**Kerbal Space Program 1.12.x**（Unity 2019.4.18）
- 编译目标：.NET Framework 4.0，**C# 5 语法**
  （本机只有 `v4.0.30319\csc.exe`，没有 Roslyn）
- 不依赖 Harmony（纯 KSPAddon，不需要打补丁）
- 已实测可用

---

## 编译

```
pwsh -File build.ps1
```

脚本会：
1. 从 KSP 安装目录复制所需程序集到 `refs/`
2. 用 `csc.exe` 编译 `src/*.cs` 到 `dist/NekoKspBridge.dll`
3. 部署到 `<KSP>/GameData/N.E.K.O-KSP-bridge/`

> 注意：游戏运行时 DLL 被占用，部署会失败。**先关游戏再编译**。

---

## 已知限制

- **只读** —— 不能控制飞船，这个版本有意如此
- 未做 `Delta-V` 计算（KSP 不直接提供，需要遍历零件自己算）
- 大气层内没有做气动/热分析
- 不读建造场景（VAB/SPH）里的设计数据

---

## 姊妹项目

- [SFS-Agent](https://github.com/LShangPiao/SFS-Agent) —— 同样的思路，用于《航天模拟器》
- 配套的 N.E.K.O. 插件：**和猫娘一起坐火箭**

---

GPL-3.0 · 星河拓航Studio (Galaxy Exploration Studio)
