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

namespace WpfMapApp2
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        // ==========================================
        // [F4] 执行分层诊断分析 (点击事件)
        // ==========================================
        private async void BtnAnalyze_Click(object sender, RoutedEventArgs e)
        {
            var vm = this.DataContext as MapViewModel;
            if (vm == null) return;

            // 1. 获取分析参数
            bool isHighEnd = rbModeB.IsChecked == true;
            string diseaseTag = (cbDiseaseType.SelectedItem as ComboBoxItem)?.Tag.ToString() ?? "General";

            // 2. 获取当前复选框状态 (决定分析后哪些图层可见)
            bool showIdw = chkIDW.IsChecked == true;
            bool showKde = chkKDE.IsChecked == true;

            // 3. 构建配置
            var options = new PressureAnalysisService.AnalysisOptions
            {
                OnlyHighEnd = isHighEnd,
                DiseaseType = diseaseTag
            };

            // 4. 调用 ViewModel 进行刷新
            await vm.RefreshPressureLayer(options, showIdw, showKde);
        }

        // --- 医院图层开关 ---
        private void HospitalLayer_Checked(object sender, RoutedEventArgs e) => ToggleHospitalLayers(true);
        private void HospitalLayer_Unchecked(object sender, RoutedEventArgs e) => ToggleHospitalLayers(false);
        private void ToggleHospitalLayers(bool visible)
        {
            if (MapView.Map != null)
            {
                SetLayerVisibility("医疗设施分布_重点", visible);
                SetLayerVisibility("医疗设施分布_基础", visible);
            }
        }

        // --- 压力图层开关 ---
        private void IDWLayer_Checked(object sender, RoutedEventArgs e) => SetLayerVisibility("医疗压力_IDW", true);
        private void IDWLayer_Unchecked(object sender, RoutedEventArgs e) => SetLayerVisibility("医疗压力_IDW", false);
        private void KDELayer_Checked(object sender, RoutedEventArgs e) => SetLayerVisibility("医疗压力_KDE", true);
        private void KDELayer_Unchecked(object sender, RoutedEventArgs e) => SetLayerVisibility("医疗压力_KDE", false);

        private void SetLayerVisibility(string layerName, bool isVisible)
        {
            if (MapView.Map?.OperationalLayers == null) return;
            var layer = MapView.Map.OperationalLayers.FirstOrDefault(l => l.Name == layerName);
            if (layer != null) layer.IsVisible = isVisible;
        }

        // --- 地图点击识别 ---
        private async void MapView_GeoViewTapped(object sender, GeoViewInputEventArgs e)
        {
            try
            {
                MapView.DismissCallout();
                var identifyResults = await MapView.IdentifyLayersAsync(e.Position, 12, false);

                // 优先查医院
                var hospitalResult = identifyResults.FirstOrDefault(r => r.LayerContent.Name.StartsWith("医疗设施分布"));
                if (hospitalResult != null && hospitalResult.LayerContent.IsVisible && hospitalResult.GeoElements.Count > 0)
                {
                    ShowHospitalCallout(hospitalResult.GeoElements.First() as Feature, e.Location);
                    return;
                }

                // 查压力图
                var pressureResult = identifyResults.FirstOrDefault(r => r.LayerContent.Name.StartsWith("医疗压力_") && r.LayerContent.IsVisible);
                if (pressureResult != null && pressureResult.GeoElements.Count > 0)
                {
                    string algoName = pressureResult.LayerContent.Name.Contains("KDE") ? "KDE核密度" : "IDW插值";
                    ShowPressureCallout(pressureResult.GeoElements.First() as Feature, e.Location, algoName);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
            }
        }

        private void ShowHospitalCallout(Feature feature, MapPoint location)
        {
            if (feature == null) return;
            var attr = feature.Attributes;
            var stackPanel = new StackPanel { Margin = new Thickness(10) };
            stackPanel.Children.Add(new TextBlock { Text = attr["Name"]?.ToString(), FontWeight = FontWeights.Bold, FontSize = 14, Foreground = new SolidColorBrush(Colors.Navy) });
            stackPanel.Children.Add(new TextBlock { Text = $"等级: {attr["LevelLabel"]} | 评分: {attr["Score"]}", FontSize = 12, Foreground = new SolidColorBrush(Colors.DarkRed) });
            MapView.ShowCalloutAt(location, stackPanel);
        }

        private void ShowPressureCallout(Feature feature, MapPoint location, string algoName)
        {
            if (feature == null) return;
            double pressure = Convert.ToDouble(feature.Attributes["Pressure"] ?? 0);
            string statusText = pressure > 70 ? "资源紧缺 (高压)" : (pressure > 40 ? "供需平衡" : "资源充足");
            Brush color = pressure > 70 ? Brushes.Red : (pressure > 40 ? Brushes.DarkGoldenrod : Brushes.Green);

            var stackPanel = new StackPanel { Margin = new Thickness(10) };
            stackPanel.Children.Add(new TextBlock { Text = $"区域监测 ({algoName})", FontWeight = FontWeights.Bold, FontSize = 13 });
            stackPanel.Children.Add(new TextBlock { Text = statusText, FontWeight = FontWeights.Bold, Foreground = color });
            stackPanel.Children.Add(new TextBlock { Text = $"压力指数: {pressure:F1}", Foreground = Brushes.Gray });
            MapView.ShowCalloutAt(location, stackPanel);
        }
    }
}