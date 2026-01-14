using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.UI;
using Esri.ArcGISRuntime.UI.Controls;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfMapApp2.Models;

// ★★★ 核心修复：消除 Grid 歧义 ★★★
using Grid = System.Windows.Controls.Grid;

namespace WpfMapApp2
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // 订阅 ViewModel 的导航请求
            if (DataContext is MapViewModel vm)
            {
                vm.RequestNavigation += OnRequestNavigation;

                // ★★★ F1 核心交互：监听视点变化，更新视野统计 ★★★
                //MapView.ViewpointChanged += (s, e) =>
                //{
                    //if (vm.IsF1Active)
                    //{
                    //    var extent = MapView.GetCurrentViewpoint(ViewpointType.BoundingGeometry)?.TargetGeometry as Envelope;
                    //    if (extent != null) vm.UpdateStatisticsFromGraphics(extent);
                    //}
                //};
            }

            // 绑定地图点击事件 (用于气泡弹窗和交互)
            MapView.GeoViewTapped += OnGeoViewTapped;
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

            // 简单模拟报表生成
            var reportService = new AnalysisReportService();
            var report = reportService.GenerateReport(vm.LastCalculationResults);

            txtHighPressureRatio.Text = $"{report.HighPressureAreaRatio:F1}%";
            txtAffectedPop.Text = $"{report.HighPressurePopulation / 10000.0:F1}";

            pnlRankingContainer.Children.Clear();
            var top3 = report.DistrictRankings.Take(3).ToList();

            for (int i = 0; i < top3.Count; i++)
            {
                var item = top3[i];
                // 这里现在会正确使用 System.Windows.Controls.Grid
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

        // F3 图层开关
        private void IDWLayer_Checked(object sender, RoutedEventArgs e) => ToggleLayer("医疗压力_IDW", true);
        private void IDWLayer_Unchecked(object sender, RoutedEventArgs e) => ToggleLayer("医疗压力_IDW", false);
        private void KDELayer_Checked(object sender, RoutedEventArgs e) => ToggleLayer("医疗压力_KDE", true);
        private void KDELayer_Unchecked(object sender, RoutedEventArgs e) => ToggleLayer("医疗压力_KDE", false);

        private void ToggleLayer(string prefix, bool visible)
        {
            if (MapView?.Map?.OperationalLayers == null) return;
            foreach (var layer in MapView.Map.OperationalLayers.Where(l => l.Name.StartsWith(prefix)))
            {
                layer.IsVisible = visible;
            }
        }

        // =========================================================
        // [通用] 地图点击与导航逻辑
        // =========================================================

        private async void OnRequestNavigation(object sender, NavigationEventArgs e)
        {
            if (MapView == null) return;

            // 1. 区域/行政区跳转
            if (e.IsDistrictZoom && e.DistrictEnvelope != null)
            {
                await MapView.SetViewpointGeometryAsync(e.DistrictEnvelope, 50);
                if (!string.IsNullOrEmpty(e.DistrictName) && DataContext is MapViewModel vm)
                {
                    vm.UpdateStatisticsByDistrict(e.DistrictName);
                }
            }
            // 2. 点位跳转
            else if (e.Center != null)
            {
                MapView.DismissCallout();
                double scale = e.Scale > 0 ? e.Scale : 5000;

                // [F4 特殊逻辑] 根据扩散半径动态调整缩放比例
                if (DataContext is MapViewModel vm && vm.CurrentModule == "F4")
                {
                    if (vm.CurrentF4Mode == "Neighborhood")
                    {
                        scale = 5000 + (vm.InitialRadius * 2);
                    }
                    else if (vm.CurrentF4Mode == "Network")
                    {
                        scale = 3000;
                    }
                }

                await MapView.SetViewpointCenterAsync(e.Center, scale);

                if (e.ResultItem != null)
                {
                    var definition = new CalloutDefinition(e.ResultItem.Name, e.ResultItem.DetailInfo);
                    MapView.ShowCalloutAt(e.Center, definition);
                }
            }
        }

        private async void OnGeoViewTapped(object sender, GeoViewInputEventArgs e)
        {
            MapView.DismissCallout();

            if (!(DataContext is MapViewModel vm)) return;

            try
            {
                // 1. 识别图形覆盖层
                var tolerance = 10d;
                var maxResults = 1;
                var identifyResults = await MapView.IdentifyGraphicsOverlaysAsync(e.Position, tolerance, false, maxResults);

                foreach (var result in identifyResults)
                {
                    if (result.Graphics.Count > 0)
                    {
                        var graphic = result.Graphics.First();
                        if (graphic.Attributes.ContainsKey("Name"))
                        {
                            string title = graphic.Attributes["Name"]?.ToString();
                            string detail = graphic.Attributes["DetailInfo"]?.ToString() ?? "";
                            MapView.ShowCalloutAt(e.Location, new CalloutDefinition(title, detail));
                            return;
                        }
                    }
                }

                // 2. 识别业务图层 (F3 医院点 & 压力图)
                if (vm.CurrentModule == "F3")
                {
                    var layerResults = await MapView.IdentifyLayersAsync(e.Position, tolerance, false, maxResults);
                    foreach (var result in layerResults)
                    {
                        if (result.GeoElements.Count > 0)
                        {
                            var geoElement = result.GeoElements.First();
                            if (geoElement.Attributes.ContainsKey("Name"))
                            {
                                string title = geoElement.Attributes["Name"]?.ToString();
                                string detail = "";
                                if (geoElement.Attributes.ContainsKey("LevelLabel"))
                                    detail += $"等级: {geoElement.Attributes["LevelLabel"]}";
                                MapView.ShowCalloutAt(e.Location, new CalloutDefinition(title, detail));
                                return;
                            }
                            else if (geoElement.Attributes.ContainsKey("Pressure"))
                            {
                                double val = Convert.ToDouble(geoElement.Attributes["Pressure"]);
                                string status = val > 75 ? "资源紧缺 (High)" : "供需平衡";
                                MapView.ShowCalloutAt(e.Location, new CalloutDefinition("区域压力监测", $"{status}\n压力指数: {val:F1}"));
                                return;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"点击识别出错: {ex.Message}");
            }
        }
    }
}