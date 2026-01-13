using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.UI;
using Esri.ArcGISRuntime.UI.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfMapApp2.Models;
using Grid = System.Windows.Controls.Grid;
namespace WpfMapApp2
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            MapView.SelectionProperties.Color = System.Drawing.Color.Yellow;

            if (DataContext is MapViewModel vm)
            {
                vm.RequestNavigation += OnRequestNavigation;
                // 确保高亮层最后添加
                if (!MapView.GraphicsOverlays.Contains(vm.HighlightOverlay))
                {
                    MapView.GraphicsOverlays.Add(vm.HighlightOverlay);
                }
            }

            // 统一的点击处理 (F1/F2/F3/F4/F5 通用)
            MapView.GeoViewTapped += MapView_GeoViewTapped;
        }

        // =========================================================
        // [F3 核心交互] 按钮与界面逻辑
        // =========================================================
        private async void BtnAnalyze_Click(object sender, RoutedEventArgs e)
        {
            var vm = this.DataContext as MapViewModel;
            if (vm == null) return;

            bool isHighEnd = rbModeB.IsChecked == true;
            string diseaseTag = (cbDiseaseType.SelectedItem as ComboBoxItem)?.Tag.ToString() ?? "General";
            bool showIdw = chkIDW.IsChecked == true;
            bool showKde = chkKDE.IsChecked == true;

            var options = new PressureAnalysisService.AnalysisOptions
            {
                OnlyHighEnd = isHighEnd,
                DiseaseType = diseaseTag
            };

            await vm.RefreshPressureLayer(options, showIdw, showKde);
            GenerateReportUI(vm);
        }

        private void BtnReport_Click(object sender, RoutedEventArgs e)
        {
            GenerateReportUI(this.DataContext as MapViewModel);
        }

        private void GenerateReportUI(MapViewModel vm)
        {
            if (vm == null || vm.LastCalculationResults == null)
            {
                if (ReportPanel.Visibility == Visibility.Visible) MessageBox.Show("请先执行分析！");
                return;
            }

            var reportService = new AnalysisReportService();
            var report = reportService.GenerateReport(vm.LastCalculationResults);

            txtHighPressureRatio.Text = $"{report.HighPressureAreaRatio:F1}%";
            txtAffectedPop.Text = $"{report.HighPressurePopulation / 10000.0:F1}";

            pnlRankingContainer.Children.Clear();
            var top3 = report.DistrictRankings.Take(3).ToList();

            for (int i = 0; i < top3.Count; i++)
            {
                var item = top3[i];
                Grid row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                row.Children.Add(new TextBlock { Text = (i + 1).ToString(), FontWeight = FontWeights.Bold, Foreground = Brushes.Gray });

                var pb = new ProgressBar { Value = item.AvgPressure, Maximum = 100, Height = 4, Background = Brushes.Transparent, Foreground = i == 0 ? Brushes.Red : Brushes.Orange, BorderThickness = new Thickness(0) };
                var namePanel = new StackPanel();
                namePanel.Children.Add(new TextBlock { Text = item.DistrictName, FontSize = 11 });
                namePanel.Children.Add(pb);
                Grid.SetColumn(namePanel, 1);
                row.Children.Add(namePanel);

                var score = new TextBlock { Text = item.AvgPressure.ToString("F0"), FontWeight = FontWeights.Bold, Foreground = Brushes.DarkRed };
                Grid.SetColumn(score, 2);
                row.Children.Add(score);

                pnlRankingContainer.Children.Add(row);
            }
            ReportPanel.Visibility = Visibility.Visible;
        }

        // 图层开关
        private void HospitalLayer_Checked(object sender, RoutedEventArgs e) => ToggleLayer("医疗设施分布", true);
        private void HospitalLayer_Unchecked(object sender, RoutedEventArgs e) => ToggleLayer("医疗设施分布", false);
        private void IDWLayer_Checked(object sender, RoutedEventArgs e) => ToggleLayer("医疗压力_IDW", true);
        private void IDWLayer_Unchecked(object sender, RoutedEventArgs e) => ToggleLayer("医疗压力_IDW", false);
        private void KDELayer_Checked(object sender, RoutedEventArgs e) => ToggleLayer("医疗压力_KDE", true);
        private void KDELayer_Unchecked(object sender, RoutedEventArgs e) => ToggleLayer("医疗压力_KDE", false);

        private void ToggleLayer(string prefix, bool visible)
        {
            // [关键修复] 增加 MapView 的空值检查
            // 使用 ?. 操作符：如果 MapView 是 null，直接停止，防止崩溃
            if (MapView?.Map?.OperationalLayers == null) return;

            foreach (var layer in MapView.Map.OperationalLayers.Where(l => l.Name.StartsWith(prefix)))
            {
                layer.IsVisible = visible;
            }
        }

        // =========================================================
        // [通用] 地图点击与导航逻辑
        // =========================================================
        private async void MapView_GeoViewTapped(object sender, GeoViewInputEventArgs e)
        {
            if (DataContext is MapViewModel vm)
            {
                vm.ClearHighlight();
                vm.UpdateStatisticsByDistrict("南京市");
            }
            MapView.DismissCallout();

            try
            {
                // 1. 识别 ArcGIS Graphics (针对 F1/F2/F4/F5 的图形)
                var graphicResults = await MapView.IdentifyGraphicsOverlaysAsync(e.Position, 10, false);
                var selectedGraphic = graphicResults.SelectMany(r => r.Graphics).FirstOrDefault(g => g.Attributes.ContainsKey("Name"));

                if (selectedGraphic != null)
                {
                    ShowCallout(selectedGraphic.Attributes["Name"].ToString(), selectedGraphic.Attributes["DetailInfo"]?.ToString(), e.Location);
                    return;
                }

                // 2. 识别 Operational Layers (针对 F3 的医院点和热力图)
                var layerResults = await MapView.IdentifyLayersAsync(e.Position, 10, false);

                // 2a. 识别医院
                var hospRes = layerResults.FirstOrDefault(r => r.LayerContent.Name.StartsWith("医疗设施分布"));
                if (hospRes != null && hospRes.GeoElements.Count > 0)
                {
                    var f = hospRes.GeoElements.First();
                    ShowCallout(f.Attributes["Name"]?.ToString(), $"等级: {f.Attributes["LevelLabel"]} | 评分: {f.Attributes["Score"]}", e.Location);
                    return;
                }

                // 2b. 识别压力图
                var pressRes = layerResults.FirstOrDefault(r => r.LayerContent.Name.StartsWith("医疗压力_"));
                if (pressRes != null && pressRes.GeoElements.Count > 0)
                {
                    var f = pressRes.GeoElements.First();
                    double val = Convert.ToDouble(f.Attributes["Pressure"] ?? 0);
                    string status = val > 75 ? "资源紧缺 (High)" : "供需平衡";
                    ShowCallout("区域压力监测", $"{status}\n压力指数: {val:F1}", e.Location);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"点击识别出错: {ex.Message}");
            }
        }

        private void ShowCallout(string title, string detail, MapPoint location)
        {
            var def = new CalloutDefinition(title, detail);
            MapView.ShowCalloutAt(location, def);
        }

        private async void OnRequestNavigation(object sender, NavigationEventArgs e)
        {
            if (MapView == null || DataContext is not MapViewModel vm) return;

            if (e.IsDistrictZoom && e.DistrictEnvelope != null)
            {
                await MapView.SetViewpointAsync(new Viewpoint(e.DistrictEnvelope));
                vm.UpdateStatisticsByDistrict(e.DistrictName);
                MapView.DismissCallout();
            }
            else if (e.Center != null)
            {
                await MapView.SetViewpointCenterAsync(e.Center, e.Scale > 0 ? e.Scale : 5000);
                if (e.ResultItem != null)
                {
                    ShowCallout(e.ResultItem.Name, e.ResultItem.DetailInfo, e.Center);
                }
            }
        }
    }
}