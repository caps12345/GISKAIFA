using System;
using System.Collections.Generic;
using System.Linq;
using WpfMapApp2.Models;

namespace WpfMapApp2
{
    public class AnalysisReportService
    {
        // 统计结果模型
        public class DistrictStat
        {
            public string DistrictName { get; set; }
            public double AvgPressure { get; set; }
            public int PopulationAffected { get; set; }
            public string Status => AvgPressure > 70 ? "紧缺" : (AvgPressure > 40 ? "平衡" : "充足");
        }

        public class GlobalReport
        {
            public double HighPressureAreaRatio { get; set; } // 高压面积占比
            public int HighPressurePopulation { get; set; }   // 高压区人口
            public List<DistrictStat> DistrictRankings { get; set; } // 各区排名
        }

        // ==========================================
        // 核心方法：生成全域分析报告
        // ==========================================
        public GlobalReport GenerateReport(List<PressureAnalysisService.CommunityPressureResult> results)
        {
            if (results == null || results.Count == 0) return new GlobalReport();

            var report = new GlobalReport();

            // 1. 全局统计
            // 定义"高压力"阈值为 > 70
            var highPressureItems = results.Where(r => r.PressureIndex > 70).ToList();

            report.HighPressurePopulation = highPressureItems.Sum(r => r.CommunityInfo.FinalPopulation ?? 0);
            report.HighPressureAreaRatio = (double)highPressureItems.Count / results.Count * 100.0;

            // 2. 分行政区统计
            // 假设 Community 模型中有 District 字段 (如 "鼓楼区", "建邺区")
            var grouped = results.GroupBy(r => r.CommunityInfo.District ?? "未知区域");

            report.DistrictRankings = new List<DistrictStat>();

            foreach (var group in grouped)
            {
                double avgP = group.Average(r => r.PressureIndex);
                int totalPop = group.Sum(r => r.CommunityInfo.FinalPopulation ?? 0);

                report.DistrictRankings.Add(new DistrictStat
                {
                    DistrictName = group.Key,
                    AvgPressure = avgP,
                    PopulationAffected = totalPop
                });
            }

            // 按压力从大到小排序 (最缺医的排前面)
            report.DistrictRankings = report.DistrictRankings.OrderByDescending(d => d.AvgPressure).ToList();

            return report;
        }
    }
}