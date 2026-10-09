// N.E.K.O-KSP-bridge — 遥测采集
//
// 每 Capture() 一次就把当前状态拍成 JSON 字符串缓存起来，
// HTTP 线程直接读缓存 —— 这样 HTTP 线程完全不碰 Unity 对象。
//
// 全部在主线程调用（由 Mod.Update 驱动）。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NekoKspBridge
{
    public static class BridgeTelemetry
    {
        private static readonly object Gate = new object();

        // 缓存的快照（都在主线程写，HTTP 线程读）
        private static string stateJson = "{\"ok\":false,\"error\":\"尚未采集到数据\"}";
        private static string vesselJson = "{\"ok\":false,\"error\":\"尚未采集到数据\"}";
        private static string orbitJson = "{\"ok\":false,\"error\":\"尚未采集到数据\"}";
        private static string resourcesJson = "{\"ok\":true,\"resources\":[]}";
        private static string crewJson = "{\"ok\":true,\"crew\":[]}";
        private static string targetJson = "{\"ok\":true,\"has_target\":false}";
        private static string vesselsJson = "{\"ok\":true,\"vessels\":[]}";
        private static string bodiesJson = "{\"ok\":true,\"bodies\":[]}";
        private static string sceneJson = "{\"ok\":true,\"scene\":\"UNKNOWN\"}";

        public static string StateJson() { lock (Gate) { return stateJson; } }
        public static string VesselJson() { lock (Gate) { return vesselJson; } }
        public static string OrbitJson() { lock (Gate) { return orbitJson; } }
        public static string ResourcesJson() { lock (Gate) { return resourcesJson; } }
        public static string CrewJson() { lock (Gate) { return crewJson; } }
        public static string TargetJson() { lock (Gate) { return targetJson; } }
        public static string VesselsJson() { lock (Gate) { return vesselsJson; } }
        public static string BodiesJson() { lock (Gate) { return bodiesJson; } }
        public static string SceneJson() { lock (Gate) { return sceneJson; } }

        // -- 采集（主线程）------------------------------------------------------

        public static void Capture()
        {
            try
            {
                string scene = SceneName();
                string v = BuildVessel();
                string o = BuildOrbit();
                string r = BuildResources();
                string c = BuildCrew();
                string t = BuildTarget();
                string vs = BuildVessels();
                string bs = BuildBodies();

                StringBuilder sb = new StringBuilder(4096);
                sb.Append("{\"ok\":true");
                sb.Append(",\"scene\":\"").Append(BridgeJson.Esc(scene)).Append("\"");
                sb.Append(",\"vessel\":").Append(v);
                sb.Append(",\"orbit\":").Append(o);
                sb.Append(",\"resources\":").Append(r);
                sb.Append(",\"crew\":").Append(c);
                sb.Append(",\"target\":").Append(t);
                sb.Append("}");

                lock (Gate)
                {
                    sceneJson = "{\"ok\":true,\"scene\":\"" + BridgeJson.Esc(scene) + "\"}";
                    vesselJson = v;
                    orbitJson = o;
                    resourcesJson = r;
                    crewJson = c;
                    targetJson = t;
                    vesselsJson = vs;
                    bodiesJson = bs;
                    stateJson = sb.ToString();
                }
            }
            catch (Exception ex)
            {
                string err = "{\"ok\":false,\"error\":\"" + BridgeJson.Esc(ex.Message) + "\"}";
                lock (Gate)
                {
                    stateJson = err;
                    vesselJson = err;
                }
            }
        }

        private static string SceneName()
        {
            try
            {
                return HighLogic.LoadedScene.ToString();
            }
            catch
            {
                return "UNKNOWN";
            }
        }

        private static Vessel Active()
        {
            try
            {
                if (!HighLogic.LoadedSceneIsFlight)
                {
                    return null;
                }
                Vessel v = FlightGlobals.ActiveVessel;
                if (v == null || v.state == Vessel.State.DEAD)
                {
                    return null;
                }
                return v;
            }
            catch
            {
                return null;
            }
        }

        // -- 飞船 -------------------------------------------------------------

        private static string BuildVessel()
        {
            Vessel v = Active();
            StringBuilder sb = new StringBuilder(1024);

            if (v == null)
            {
                if (HighLogic.LoadedSceneIsEditor)
                {
                    sb.Append("{\"ok\":true,\"exists\":false,\"context\":\"editor\"");
                    sb.Append(",\"note\":\"当前在建造场景，没有活动飞船。\"}");
                    return sb.ToString();
                }
                string scene = SceneName();
                sb.Append("{\"ok\":true,\"exists\":false");
                if (scene == "MAINMENU")
                {
                    sb.Append(",\"note\":\"当前在主菜单，还没有进入任何存档。\"");
                }
                else if (scene == "SPACECENTER" || scene == "TRACKINGSTATION")
                {
                    sb.Append(",\"note\":\"当前在航天中心 / 追踪站，没有正在飞行的飞船。\"");
                    sb.Append(",\"hint\":\"要读飞行遥测需要进入飞行场景（选一艘飞船点 Fly）。\"");
                }
                else
                {
                    sb.Append(",\"note\":\"当前没有活动飞船（不在飞行场景，或飞船已毁）。\"");
                }
                sb.Append("}");
                return sb.ToString();
            }

            sb.Append("{\"ok\":true,\"exists\":true");
            sb.Append(",\"name\":\"").Append(BridgeJson.Esc(v.vesselName)).Append("\"");
            sb.Append(",\"situation\":\"").Append(BridgeJson.Esc(SafeSituation(v))).Append("\"");
            sb.Append(",\"is_active\":").Append(BridgeJson.Bool(v.isActiveVessel));
            sb.Append(",\"is_loaded\":").Append(BridgeJson.Bool(v.loaded));
            sb.Append(",\"landed\":").Append(BridgeJson.Bool(v.Landed));
            sb.Append(",\"splashed\":").Append(BridgeJson.Bool(v.Splashed));
            sb.Append(",\"landed_at\":\"").Append(BridgeJson.Esc(v.landedAt)).Append("\"");
            sb.Append(",\"mission_time\":").Append(BridgeJson.Num(v.missionTime));
            sb.Append(",\"mass\":").Append(BridgeJson.Num(v.GetTotalMass()));

            try
            {
                sb.Append(",\"crew_count\":").Append(v.GetCrewCount());
                sb.Append(",\"crew_capacity\":").Append(v.GetCrewCapacity());
            }
            catch
            {
            }

            // 位置
            sb.Append(",\"altitude\":").Append(BridgeJson.Num(v.altitude));
            sb.Append(",\"height_from_terrain\":").Append(BridgeJson.Num(v.heightFromTerrain));
            sb.Append(",\"height_from_surface\":").Append(BridgeJson.Num(v.heightFromSurface));
            sb.Append(",\"latitude\":").Append(BridgeJson.Num(v.latitude));
            sb.Append(",\"longitude\":").Append(BridgeJson.Num(v.longitude));

            // 速度
            sb.Append(",\"speed_surface\":").Append(BridgeJson.Num(v.srfSpeed));
            sb.Append(",\"speed_orbital\":").Append(BridgeJson.Num(v.obt_speed));
            sb.Append(",\"speed_vertical\":").Append(BridgeJson.Num(v.verticalSpeed));
            sb.Append(",\"speed_horizontal\":").Append(BridgeJson.Num(v.horizontalSrfSpeed));

            // 姿态
            try
            {
                sb.Append(",\"heading\":").Append(BridgeJson.Num(FlightGlobals.ship_heading));
            }
            catch
            {
            }
            sb.Append(",\"pitch\":").Append(BridgeJson.Num(PitchOf(v)));

            // 所在地
            sb.Append(",\"body\":\"").Append(BridgeJson.Esc(BodyName(v.mainBody))).Append("\"");
            sb.Append("}");

            return sb.ToString();
        }

        private static string SafeSituation(Vessel v)
        {
            try
            {
                return v.situation.ToString();
            }
            catch
            {
                return "UNKNOWN";
            }
        }

        /// <summary>
        /// 俯仰角：90 = 正对天顶，0 = 水平，负 = 朝下。
        /// 用「飞船的 up（沿火箭纵轴）」与「当地天顶方向」的夹角换算。
        /// </summary>
        private static double PitchOf(Vessel v)
        {
            try
            {
                Vector3d radial = (v.CoM - v.mainBody.position).normalized;
                double angle = Vector3d.Angle(v.transform.up, radial);
                return 90.0 - angle;
            }
            catch
            {
                return double.NaN;
            }
        }

        private static string BodyName(CelestialBody b)
        {
            try
            {
                return b == null ? "" : b.bodyName;
            }
            catch
            {
                return "";
            }
        }

        // -- 轨道 -------------------------------------------------------------

        private static string BuildOrbit()
        {
            Vessel v = Active();
            if (v == null || v.orbit == null)
            {
                return "{\"ok\":true,\"exists\":false}";
            }

            Orbit o = v.orbit;
            StringBuilder sb = new StringBuilder(1024);
            sb.Append("{\"ok\":true,\"exists\":true");
            sb.Append(",\"body\":\"").Append(BridgeJson.Esc(BodyName(o.referenceBody))).Append("\"");

            // 高度（相对地表）
            sb.Append(",\"apoapsis\":").Append(BridgeJson.Num(o.ApA));
            sb.Append(",\"periapsis\":").Append(BridgeJson.Num(o.PeA));
            // 半径（相对星球中心）
            sb.Append(",\"apoapsis_radius\":").Append(BridgeJson.Num(o.ApR));
            sb.Append(",\"periapsis_radius\":").Append(BridgeJson.Num(o.PeR));

            sb.Append(",\"semi_major_axis\":").Append(BridgeJson.Num(o.semiMajorAxis));
            sb.Append(",\"eccentricity\":").Append(BridgeJson.Num(o.eccentricity));
            sb.Append(",\"inclination\":").Append(BridgeJson.Num(o.inclination));
            sb.Append(",\"period\":").Append(BridgeJson.Num(o.period));
            sb.Append(",\"time_to_apoapsis\":").Append(BridgeJson.Num(o.timeToAp));
            sb.Append(",\"time_to_periapsis\":").Append(BridgeJson.Num(o.timeToPe));
            sb.Append(",\"true_anomaly\":").Append(BridgeJson.Num(o.trueAnomaly));
            sb.Append(",\"mean_anomaly\":").Append(BridgeJson.Num(o.meanAnomaly));
            sb.Append(",\"lan\":").Append(BridgeJson.Num(o.LAN));
            sb.Append(",\"argument_of_periapsis\":").Append(BridgeJson.Num(o.argumentOfPeriapsis));
            sb.Append(",\"orbital_speed\":").Append(BridgeJson.Num(o.orbitalSpeed));

            // 大气层信息，帮助判断「算不算入轨」
            try
            {
                CelestialBody b = o.referenceBody;
                if (b != null)
                {
                    sb.Append(",\"body_radius\":").Append(BridgeJson.Num(b.Radius));
                    sb.Append(",\"has_atmosphere\":").Append(BridgeJson.Bool(b.atmosphere));
                    if (b.atmosphere)
                    {
                        sb.Append(",\"atmosphere_depth\":").Append(BridgeJson.Num(b.atmosphereDepth));
                    }
                    sb.Append(",\"sphere_of_influence\":").Append(BridgeJson.Num(b.sphereOfInfluence));
                    sb.Append(",\"gravity_asl\":").Append(BridgeJson.Num(b.GeeASL));

                    if (b.atmosphere)
                    {
                        sb.Append(",\"is_stable_orbit\":")
                          .Append(BridgeJson.Bool(o.PeA > b.atmosphereDepth));
                    }
                    else
                    {
                        sb.Append(",\"is_stable_orbit\":")
                          .Append(BridgeJson.Bool(o.PeA > 0));
                    }
                }
            }
            catch
            {
            }

            sb.Append("}");
            return sb.ToString();
        }

        // -- 资源 -------------------------------------------------------------

        private static string BuildResources()
        {
            Vessel v = Active();
            if (v == null)
            {
                return "{\"ok\":true,\"resources\":[]}";
            }

            // 按资源名汇总
            Dictionary<string, double> amounts = new Dictionary<string, double>();
            Dictionary<string, double> maxes = new Dictionary<string, double>();

            try
            {
                List<Part> parts = v.parts;
                if (parts != null)
                {
                    for (int i = 0; i < parts.Count; i++)
                    {
                        Part p = parts[i];
                        if (p == null || p.Resources == null)
                        {
                            continue;
                        }
                        foreach (PartResource res in p.Resources)
                        {
                            if (res == null || string.IsNullOrEmpty(res.resourceName))
                            {
                                continue;
                            }
                            string key = res.resourceName;
                            double cur;
                            double mx;
                            amounts.TryGetValue(key, out cur);
                            maxes.TryGetValue(key, out mx);
                            amounts[key] = cur + res.amount;
                            maxes[key] = mx + res.maxAmount;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return "{\"ok\":false,\"error\":\"" + BridgeJson.Esc(ex.Message) + "\"}";
            }

            List<string> keys = new List<string>(amounts.Keys);
            keys.Sort(StringComparer.OrdinalIgnoreCase);

            StringBuilder sb = new StringBuilder(1024);
            sb.Append("{\"ok\":true,\"count\":").Append(keys.Count).Append(",\"resources\":[");
            for (int i = 0; i < keys.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(",");
                }
                string k = keys[i];
                double amt = amounts[k];
                double mx = maxes[k];
                sb.Append("{\"name\":\"").Append(BridgeJson.Esc(k)).Append("\"");
                sb.Append(",\"amount\":").Append(BridgeJson.Num(amt));
                sb.Append(",\"max\":").Append(BridgeJson.Num(mx));
                sb.Append(",\"percent\":").Append(BridgeJson.Num(mx > 0 ? amt / mx : 0));
                sb.Append("}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        // -- 乘员 -------------------------------------------------------------

        private static string BuildCrew()
        {
            Vessel v = Active();
            if (v == null)
            {
                return "{\"ok\":true,\"count\":0,\"crew\":[]}";
            }

            StringBuilder sb = new StringBuilder(512);
            int n = 0;
            sb.Append("{\"ok\":true,\"crew\":[");
            try
            {
                List<ProtoCrewMember> list = v.GetVesselCrew();
                if (list != null)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        ProtoCrewMember m = list[i];
                        if (m == null)
                        {
                            continue;
                        }
                        if (n > 0)
                        {
                            sb.Append(",");
                        }
                        sb.Append("{\"name\":\"").Append(BridgeJson.Esc(m.name)).Append("\"");
                        try
                        {
                            sb.Append(",\"trait\":\"").Append(BridgeJson.Esc(m.trait)).Append("\"");
                            sb.Append(",\"type\":\"").Append(BridgeJson.Esc(m.type.ToString())).Append("\"");
                            sb.Append(",\"experience\":").Append(BridgeJson.Num(m.experienceLevel));
                            sb.Append(",\"is_veteran\":").Append(BridgeJson.Bool(m.veteran));
                        }
                        catch
                        {
                        }
                        sb.Append("}");
                        n++;
                    }
                }
            }
            catch (Exception ex)
            {
                return "{\"ok\":false,\"error\":\"" + BridgeJson.Esc(ex.Message) + "\"}";
            }
            sb.Append("],\"count\":").Append(n).Append("}");
            return sb.ToString();
        }

        // -- 目标 -------------------------------------------------------------

        private static string BuildTarget()
        {
            Vessel v = Active();
            if (v == null)
            {
                return "{\"ok\":true,\"has_target\":false}";
            }

            StringBuilder sb = new StringBuilder(512);
            try
            {
                ITargetable t = FlightGlobals.fetch == null ? null : FlightGlobals.fetch.VesselTarget;
                if (t == null)
                {
                    return "{\"ok\":true,\"has_target\":false}";
                }

                sb.Append("{\"ok\":true,\"has_target\":true");
                sb.Append(",\"name\":\"").Append(BridgeJson.Esc(t.GetName())).Append("\"");

                Vessel tv = t as Vessel;
                if (tv != null)
                {
                    sb.Append(",\"kind\":\"vessel\"");
                    sb.Append(",\"distance\":").Append(BridgeJson.Num(
                        Vector3d.Distance(v.CoM, tv.CoM)));
                    // Orbit 没有公开的 velocity 字段；
                    // 速度向量在 Vessel 上：obt_velocity / srf_velocity
                    Vector3d rel = tv.obt_velocity - v.obt_velocity;
                    sb.Append(",\"relative_speed\":").Append(BridgeJson.Num(rel.magnitude));
                    sb.Append(",\"target_altitude\":").Append(BridgeJson.Num(tv.altitude));
                    sb.Append(",\"target_situation\":\"").Append(BridgeJson.Esc(SafeSituation(tv))).Append("\"");
                }
                else
                {
                    CelestialBody tb = t as CelestialBody;
                    if (tb != null)
                    {
                        sb.Append(",\"kind\":\"body\"");
                        sb.Append(",\"distance\":").Append(BridgeJson.Num(
                            Vector3d.Distance(v.CoM, tb.position)));
                        sb.Append(",\"radius\":").Append(BridgeJson.Num(tb.Radius));
                        sb.Append(",\"sphere_of_influence\":").Append(BridgeJson.Num(tb.sphereOfInfluence));
                    }
                    else
                    {
                        sb.Append(",\"kind\":\"other\"");
                        sb.Append(",\"distance\":").Append(BridgeJson.Num(
                            Vector3d.Distance(v.CoM, t.GetTransform().position)));
                    }
                }
                sb.Append("}");
            }
            catch (Exception ex)
            {
                return "{\"ok\":false,\"error\":\"" + BridgeJson.Esc(ex.Message) + "\"}";
            }
            return sb.ToString();
        }

        // -- 其它飞船 ---------------------------------------------------------

        private static string BuildVessels()
        {
            StringBuilder sb = new StringBuilder(2048);

            // 加载阶段 FlightGlobals 还没初始化，直接取会 NRE。
            // （主菜单下星球馆已加载，天体/飞船列表是可用的，不要在这里排除）
            if (HighLogic.LoadedScene == GameScenes.LOADING || FlightGlobals.fetch == null)
            {
                return "{\"ok\":true,\"count\":0,\"vessels\":[]"
                    + ",\"note\":\"游戏仍在加载中，飞船列表暂不可用。\"}";
            }

            int n = 0;
            sb.Append("{\"ok\":true,\"vessels\":[");
            try
            {
                List<Vessel> all = FlightGlobals.Vessels;
                if (all != null)
                {
                    for (int i = 0; i < all.Count; i++)
                    {
                        Vessel v = all[i];
                        if (v == null)
                        {
                            continue;
                        }
                        try
                        {
                        if (n > 0)
                        {
                            sb.Append(",");
                        }
                        sb.Append("{\"name\":\"").Append(BridgeJson.Esc(v.vesselName)).Append("\"");
                        sb.Append(",\"situation\":\"").Append(BridgeJson.Esc(SafeSituation(v))).Append("\"");
                        sb.Append(",\"type\":\"").Append(BridgeJson.Esc(v.vesselType.ToString())).Append("\"");
                        sb.Append(",\"is_active\":").Append(BridgeJson.Bool(v.isActiveVessel));
                        sb.Append(",\"is_loaded\":").Append(BridgeJson.Bool(v.loaded));
                        sb.Append(",\"body\":\"").Append(BridgeJson.Esc(BodyName(v.mainBody))).Append("\"");
                        sb.Append(",\"altitude\":").Append(BridgeJson.Num(v.altitude));
                        sb.Append(",\"mass\":").Append(BridgeJson.Num(v.GetTotalMass()));
                        sb.Append("}");
                        n++;
                        }
                        catch
                        {
                            // 单个飞船取不到就跳过，不要让整个接口挂掉
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return "{\"ok\":false,\"error\":\"" + BridgeJson.Esc(ex.Message) + "\"}";
            }
            sb.Append("],\"count\":").Append(n).Append("}");
            return sb.ToString();
        }

        // -- 天体 -------------------------------------------------------------

        private static string BuildBodies()
        {
            StringBuilder sb = new StringBuilder(4096);

            if (HighLogic.LoadedScene == GameScenes.LOADING || FlightGlobals.fetch == null)
            {
                return "{\"ok\":true,\"count\":0,\"bodies\":[]"
                    + ",\"note\":\"游戏仍在加载中，天体数据暂不可用。\"}";
            }

            int n = 0;
            sb.Append("{\"ok\":true,\"bodies\":[");
            try
            {
                List<CelestialBody> all = FlightGlobals.Bodies;
                if (all != null)
                {
                    for (int i = 0; i < all.Count; i++)
                    {
                        CelestialBody b = all[i];
                        if (b == null)
                        {
                            continue;
                        }
                        if (n > 0)
                        {
                            sb.Append(",");
                        }
                        try
                        {
                        sb.Append("{\"name\":\"").Append(BridgeJson.Esc(b.bodyName)).Append("\"");
                        sb.Append(",\"display_name\":\"").Append(BridgeJson.Esc(b.displayName)).Append("\"");
                        sb.Append(",\"radius\":").Append(BridgeJson.Num(b.Radius));
                        sb.Append(",\"gravity_asl\":").Append(BridgeJson.Num(b.GeeASL));
                        sb.Append(",\"mass\":").Append(BridgeJson.Num(b.Mass));
                        sb.Append(",\"has_atmosphere\":").Append(BridgeJson.Bool(b.atmosphere));
                        if (b.atmosphere)
                        {
                            sb.Append(",\"atmosphere_depth\":").Append(BridgeJson.Num(b.atmosphereDepth));
                        }
                        sb.Append(",\"sphere_of_influence\":").Append(BridgeJson.Num(b.sphereOfInfluence));
                        sb.Append(",\"rotation_period\":").Append(BridgeJson.Num(b.rotationPeriod));
                        sb.Append("}");
                        n++;
                        }
                        catch
                        {
                            // 单个天体取不到就跳过
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return "{\"ok\":false,\"error\":\"" + BridgeJson.Esc(ex.Message) + "\"}";
            }
            sb.Append("],\"count\":").Append(n).Append("}");
            return sb.ToString();
        }
    }
}
