using System;
using System.Collections.Generic;
using System.Linq;
using WpfMapApp2.Models;

namespace WpfMapApp2
{
    public class PressureAnalysisService
    {
        // ==========================================
        // 诊断配置参数类
        // ==========================================
        public class AnalysisOptions
        {
            // 模式 A/B: 是否仅包含三级高端医院
            public bool OnlyHighEnd { get; set; } = false;

            // 疾病类型权重: General(综合), Critical(重症), Infectious(传染)
            public string DiseaseType { get; set; } = "General";
        }

        public class CommunityPressureResult
        {
            public Community CommunityInfo { get; set; }
            public double Accessibility { get; set; }
            public double PressureIndex { get; set; }
        }

        // 内部辅助类
        private class HospitalSupplyInfo
        {
            public Hospital Hospital { get; set; }
            public double CapacityScore { get; set; } // 服务能力 Sj
            public double SearchRadius { get; set; }  // 搜索半径 d0
            public double FrictionBeta { get; set; }  // 衰减系数 β
            public double SupplyToDemandRatio { get; set; } // 供需比 Rj
        }

        // ==========================================
        // [核心逻辑] 计算压力指数
        // ==========================================
        public List<CommunityPressureResult> CalculatePressure(List<Hospital> hospitals, List<Community> communities, AnalysisOptions options)
        {
            var processedHospitals = new List<HospitalSupplyInfo>();

            // 1. 供给侧建模 (基于 options 筛选和加权)
            foreach (var h in hospitals)
            {
                if (h.Longitude == null || h.Latitude == null) continue;

                int level = h.Level ?? 3;

                // [Mode B 筛选] 如果开启"仅高端"，剔除非三级医院
                if (options.OnlyHighEnd && level != 1)
                {
                    continue;
                }

                // 读取基础数据
                double docs = h.DoctorCount ?? 10;
                double beds = h.BedCount ?? 20;
                double score = (double)(h.Score ?? 60);
                string name = h.Name ?? "";

                var info = new HospitalSupplyInfo { Hospital = h };

                // [疾病类型权重] 调整 CapacityScore
                if (options.DiseaseType == "Critical")
                {
                    info.CapacityScore = (docs * 0.8) + (beds * 0.2) + (score * 1.5);
                }
                else if (options.DiseaseType == "Infectious")
                {
                    info.CapacityScore = (docs * 0.3) + (beds * 0.7) + (score * 1.0);
                }
                else // General
                {
                    info.CapacityScore = (docs * 0.5) + (beds * 0.3) + (score * 1.0);
                }

                // [搜寻半径与阻抗逻辑] - 核心优化部分
                // 判断是否为顶级名院 (模拟逻辑：鼓楼、人医、总院等)
                bool isTopTier = score > 95 || name.Contains("鼓楼") || name.Contains("人民") || name.Contains("总");

                if (options.OnlyHighEnd)
                {
                    // =========================================================
                    // 场景 B：高端/疑难重症模式 (High-End Optimization)
                    // 逻辑：模拟"全域就诊"，大幅扩大半径，降低距离阻抗
                    // =========================================================

                    // 半径：覆盖全城及近郊 (50km)
                    info.SearchRadius = 50.0;

                    // 阻抗：
                    // TopTier (0.6): 极低阻抗，患者愿意跨城就医
                    // Normal (0.9): 低阻抗，主要覆盖半个城市
                    info.FrictionBeta = isTopTier ? 0.6 : 0.9;

                    // 能力放大：名院在疑难重症上的实际承载力远超普通三甲
                    if (isTopTier) info.CapacityScore *= 2.0;
                }
                else
                {
                    // =========================================================
                    // 场景 A：日常综合模式 (General Mode)
                    // 逻辑：日常就医，距离敏感
                    // =========================================================
                    if (level == 1) // 三级
                    {
                        info.SearchRadius = isTopTier ? 20.0 : 12.0;
                        info.FrictionBeta = 1.5;
                        if (isTopTier) info.CapacityScore *= 1.2;
                    }
                    else if (level == 2) // 二级
                    {
                        info.SearchRadius = 8.0;
                        info.FrictionBeta = 1.8;
                    }
                    else // 社区
                    {
                        info.SearchRadius = 3.0; // 微循环
                        info.FrictionBeta = 2.5; // 高阻抗
                    }
                }

                processedHospitals.Add(info);
            }

            // 2. 计算供需比 Rj (2SFCA Step 1)
            foreach (var hInfo in processedHospitals)
            {
                double totalDemand = 0;
                foreach (var comm in communities)
                {
                    if (comm.Longitude == null || comm.Latitude == null) continue;
                    double dist = GetDistance(hInfo.Hospital.Latitude.Value, hInfo.Hospital.Longitude.Value, comm.Latitude.Value, comm.Longitude.Value);

                    if (dist <= hInfo.SearchRadius)
                    {
                        double friction = Math.Pow(Math.Max(dist, 0.5), -hInfo.FrictionBeta);
                        double pop = (double)(comm.FinalPopulation ?? 1000);
                        totalDemand += pop * friction;
                    }
                }
                hInfo.SupplyToDemandRatio = totalDemand > 0 ? hInfo.CapacityScore / totalDemand : 0;
            }

            // 3. 计算可达性 Ai (2SFCA Step 2)
            var results = new List<CommunityPressureResult>();
            foreach (var comm in communities)
            {
                if (comm.Longitude == null || comm.Latitude == null) continue;
                double accessibility = 0;

                foreach (var hInfo in processedHospitals)
                {
                    double dist = GetDistance(hInfo.Hospital.Latitude.Value, hInfo.Hospital.Longitude.Value, comm.Latitude.Value, comm.Longitude.Value);
                    if (dist <= hInfo.SearchRadius)
                    {
                        double friction = Math.Pow(Math.Max(dist, 0.5), -hInfo.FrictionBeta);
                        accessibility += hInfo.SupplyToDemandRatio * friction;
                    }
                }

                results.Add(new CommunityPressureResult
                {
                    CommunityInfo = comm,
                    Accessibility = accessibility
                });
            }

            // 4. 压力值转换与动态归一化
            foreach (var item in results)
            {
                if (item.Accessibility > 1e-9) // 避免除以0
                    item.PressureIndex = 1.0 / item.Accessibility;
                else
                    item.PressureIndex = 10000.0; // 盲区高压
            }

            // 归一化 (0-100)
            if (results.Count > 0)
            {
                var sorted = results.Select(r => r.PressureIndex).OrderBy(p => p).ToList();
                // 动态分位数处理：根据数据分布自适应
                double minP = sorted[(int)(sorted.Count * 0.05)];
                double maxP = sorted[(int)(sorted.Count * 0.95)];

                if (maxP <= minP) maxP = minP + 1;

                foreach (var item in results)
                {
                    if (item.PressureIndex > maxP) item.PressureIndex = 100;
                    else if (item.PressureIndex < minP) item.PressureIndex = 0;
                    else item.PressureIndex = 100 * (item.PressureIndex - minP) / (maxP - minP);
                }
            }

            return results;
        }

        // 距离计算辅助方法
        private double GetDistance(double lat1, double lon1, double lat2, double lon2)
        {
            var R = 6371d;
            var dLat = (lat2 - lat1) * (Math.PI / 180d);
            var dLon = (lon2 - lon1) * (Math.PI / 180d);
            var a = Math.Sin(dLat / 2d) * Math.Sin(dLat / 2d) +
                    Math.Cos(lat1 * (Math.PI / 180d)) * Math.Cos(lat2 * (Math.PI / 180d)) *
                    Math.Sin(dLon / 2d) * Math.Sin(dLon / 2d);
            var c = 2d * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1d - a));
            return R * c;
        }
    }
}