using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.UI;
using Esri.ArcGISRuntime.UI.Controls;
using WpfMapApp2.Models;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace WpfMapApp2
{
    public partial class MainWindow : Window
    {
        // 图形叠加层
        private GraphicsOverlay _diffusionLayer;
        private GraphicsOverlay _communityLayer;

        // 扩散状态
        private MapPoint _startPoint;
        private int _currentDay = 0;

        // 当前激活的模块
        private string _currentModule = "RiskSimulation";

        // 小区数据列表
        private List<Community> _allCommunities = new List<Community>();

        public MainWindow()
        {
            InitializeComponent();

            // 先设置 DataContext
            this.DataContext = new MapViewModel();

            // 初始化 GraphicsOverlays（在设置 DataContext 后）
            MyMapView.GraphicsOverlays = new GraphicsOverlayCollection();

            InitializeGraphicsLayers();
            LoadCommunitiesFromDatabase();

            // 默认显示风险模拟模块
            ActivateRiskSimulationModule();
        }
        // 模块按钮点击事件
        private void ModuleButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null) return;

            // 重置所有按钮样式
            ResetModuleButtonsStyle();

            // 设置当前按钮为激活状态
            button.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(255, 25, 118, 210));
            button.BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(255, 13, 71, 161));
            button.BorderThickness = new Thickness(0, 0, 0, 2);

            // 根据按钮切换模块
            switch (button.Name)
            {
                case "BtnResourceSearch":
                    SwitchToModule("ResourceSearch");
                    break;
                case "BtnCoverageAnalysis":
                    SwitchToModule("CoverageAnalysis");
                    break;
                case "BtnPressureMonitor":
                    SwitchToModule("PressureMonitor");
                    break;
                case "BtnRiskSimulation":
                    SwitchToModule("RiskSimulation");
                    break;
                case "BtnSmartLocation":
                    SwitchToModule("SmartLocation");
                    break;
            }
        }

        // 重置所有模块按钮样式
        private void ResetModuleButtonsStyle()
        {
            BtnResourceSearch.ClearValue(Button.BackgroundProperty);
            BtnResourceSearch.ClearValue(Button.BorderBrushProperty);
            BtnResourceSearch.BorderThickness = new Thickness(0);

            BtnCoverageAnalysis.ClearValue(Button.BackgroundProperty);
            BtnCoverageAnalysis.ClearValue(Button.BorderBrushProperty);
            BtnCoverageAnalysis.BorderThickness = new Thickness(0);

            BtnPressureMonitor.ClearValue(Button.BackgroundProperty);
            BtnPressureMonitor.ClearValue(Button.BorderBrushProperty);
            BtnPressureMonitor.BorderThickness = new Thickness(0);

            BtnRiskSimulation.ClearValue(Button.BackgroundProperty);
            BtnRiskSimulation.ClearValue(Button.BorderBrushProperty);
            BtnRiskSimulation.BorderThickness = new Thickness(0);

            BtnSmartLocation.ClearValue(Button.BackgroundProperty);
            BtnSmartLocation.ClearValue(Button.BorderBrushProperty);
            BtnSmartLocation.BorderThickness = new Thickness(0);
        }

        // 切换模块
        private void SwitchToModule(string moduleName)
        {
            _currentModule = moduleName;

            // 隐藏所有工具栏
            RiskSimulationTools.Visibility = Visibility.Collapsed;

            // 隐藏所有控制面板
            DiffusionControlPanel.Visibility = Visibility.Collapsed;

            // 清空扩散图形
            if (_diffusionLayer != null)
                _diffusionLayer.Graphics.Clear();

            // 根据模块显示对应的工具栏
            switch (moduleName)
            {
                case "RiskSimulation":
                    ActivateRiskSimulationModule();
                    break;
                // 其他模块暂不实现
                default:
                    ShowModuleNotImplemented();
                    break;
            }
        }

        // 激活风险模拟模块
        private void ActivateRiskSimulationModule()
        {
            RiskSimulationTools.Visibility = Visibility.Visible;

            // 默认显示邻域扩散按钮为激活状态
            BtnNeighborhoodDiffusion.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(255, 187, 222, 251));
            BtnNeighborhoodDiffusion.BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(255, 100, 181, 246));

            // 显示提示信息
            StatusText.Text = "请在地图上点击选择扩散起点 或 搜索小区定位";
        }

        // 显示模块未实现提示
        private void ShowModuleNotImplemented()
        {
            MessageBox.Show($"'{((Button)FindName($"Btn{_currentModule}"))?.Content}' 模块正在开发中...",
                          "功能预告",
                          MessageBoxButton.OK,
                          MessageBoxImage.Information);

            // 切换回风险模拟模块
            ActivateRiskSimulationModule();
        }

        // 风险模拟工具栏按钮点击事件
        private void RiskSimulationButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null) return;

            // 重置风险模拟按钮样式
            BtnNeighborhoodDiffusion.ClearValue(Button.BackgroundProperty);
            BtnNeighborhoodDiffusion.ClearValue(Button.BorderBrushProperty);
            BtnNetworkDiffusion.ClearValue(Button.BackgroundProperty);
            BtnNetworkDiffusion.ClearValue(Button.BorderBrushProperty);
            BtnLockdownZone.ClearValue(Button.BackgroundProperty);
            BtnLockdownZone.ClearValue(Button.BorderBrushProperty);

            // 设置当前按钮为激活状态
            button.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(255, 187, 222, 251));
            button.BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(255, 100, 181, 246));

            // 根据按钮显示对应面板
            switch (button.Name)
            {
                case "BtnNeighborhoodDiffusion":
                    DiffusionControlPanel.Visibility = Visibility.Visible;
                    break;
                case "BtnNetworkDiffusion":
                    MessageBox.Show("网络扩散功能正在开发中...", "功能预告", MessageBoxButton.OK, MessageBoxImage.Information);
                    break;
                case "BtnLockdownZone":
                    MessageBox.Show("封控圈生成功能正在开发中...", "功能预告", MessageBoxButton.OK, MessageBoxImage.Information);
                    break;
            }
        }

        // 重置模拟按钮点击事件
        private void ResetSimulation_Click(object sender, RoutedEventArgs e)
        {
            // 清空扩散图形
            if (_diffusionLayer != null)
                _diffusionLayer.Graphics.Clear();

            // 重置状态
            _startPoint = null;
            _currentDay = 0;
            DaySlider.Value = 0;
            CommunitySearchBox.Text = "";

            // 更新显示
            StatusText.Text = "模拟已重置，请在地图上点击选择新的扩散起点 或 搜索小区定位";
        }

        // 初始化图形层
        private void InitializeGraphicsLayers()
        {
            // 扩散图形层（缓冲区）
            _diffusionLayer = new GraphicsOverlay();
            MyMapView.GraphicsOverlays.Add(_diffusionLayer);

            // 小区点图层
            _communityLayer = new GraphicsOverlay();
            MyMapView.GraphicsOverlays.Add(_communityLayer);
        }

        // 从数据库加载小区点
        private async void LoadCommunitiesFromDatabase()
        {
            try
            {
                using (var db = new NanjingContext())
                {
                    // 查询有坐标的小区数据
                    _allCommunities = await db.Communities
                        .Where(c => c.WgsLongitude != null && c.WgsLatitude != null)
                        .ToListAsync();

                    if (_allCommunities.Count == 0)
                    {
                        StatusText.Text = "未找到小区数据。";
                        return;
                    }

                    // 定义小区点符号（蓝色实心圆点）
                    var communitySymbol = new SimpleMarkerSymbol(
                        SimpleMarkerSymbolStyle.Circle,
                        System.Drawing.Color.FromArgb(255, 33, 150, 243), // 蓝色
                        6); // 减小点的大小

                    foreach (var community in _allCommunities)
                    {
                        // 1. 先创建WGS84坐标点
                        var pointWgs84 = new MapPoint(community.WgsLongitude.Value, community.WgsLatitude.Value, SpatialReferences.Wgs84);

                        // 2. 转换为Web墨卡托坐标系（与天地图一致）
                        var pointWebMercator = (MapPoint)GeometryEngine.Project(pointWgs84, SpatialReferences.WebMercator);

                        // 创建图形并添加属性
                        var graphic = new Graphic(pointWebMercator, communitySymbol);
                        graphic.Attributes["Id"] = community.Id;
                        graphic.Attributes["Name"] = community.Name;
                        graphic.Attributes["District"] = community.District;
                        graphic.Attributes["Street"] = community.Street;
                        graphic.Attributes["Population"] = community.FinalPopulation;

                        _communityLayer.Graphics.Add(graphic);
                    }

                    StatusText.Text = $"已加载{_allCommunities.Count}个小区点数据";
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"加载小区数据失败: {ex.Message}";
            }
        }

        // 搜索小区按钮点击事件
        private async void BtnSearchCommunity_Click(object sender, RoutedEventArgs e)
        {
            await SearchAndLocateCommunity();
        }

        // 搜索小区文本框回车事件
        private async void CommunitySearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await SearchAndLocateCommunity();
            }
        }

        // 搜索并定位小区
        private async Task SearchAndLocateCommunity()
        {
            string searchText = CommunitySearchBox.Text.Trim();
            if (string.IsNullOrEmpty(searchText))
            {
                MessageBox.Show("请输入小区名称", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // 在内存中搜索小区（已加载到内存中）
                var community = _allCommunities
                    .Where(c => c.Name != null && c.Name.Contains(searchText))
                    .FirstOrDefault();

                if (community == null)
                {
                    MessageBox.Show($"未找到包含'{searchText}'的小区", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 1. 创建WGS84坐标点
                var pointWgs84 = new MapPoint(community.WgsLongitude.Value, community.WgsLatitude.Value, SpatialReferences.Wgs84);

                // 2. 转换为Web墨卡托坐标系
                var pointWebMercator = (MapPoint)GeometryEngine.Project(pointWgs84, SpatialReferences.WebMercator);

                // 设置为扩散起点
                _startPoint = pointWebMercator;

                // 清空之前的扩散图形
                _diffusionLayer.Graphics.Clear();

                // 添加起点标记
                var startSymbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Cross,
                    System.Drawing.Color.FromArgb(255, 244, 67, 54), 20); // 红色
                _diffusionLayer.Graphics.Add(new Graphic(_startPoint, startSymbol));

                // 重置到Day 0
                DaySlider.Value = 0;
                UpdateDiffusion(0);

                // 缩放到小区位置（500米范围）
                var viewpoint = new Viewpoint(_startPoint, 500);
                await MyMapView.SetViewpointAsync(viewpoint, TimeSpan.FromSeconds(1));

                // 更新状态信息
                StatusText.Text = $"已定位小区: {community.Name} ({pointWgs84.Y:F4}°, {pointWgs84.X:F4}°)";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"搜索小区失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 地图点击事件
        private void MyMapView_GeoViewTapped(object sender, Esri.ArcGISRuntime.UI.Controls.GeoViewInputEventArgs e)
        {
            // 只有在风险模拟模块才处理点击事件
            if (_currentModule != "RiskSimulation") return;

            // 只有在邻域扩散面板显示时才处理
            if (DiffusionControlPanel.Visibility != Visibility.Visible) return;

            // 显示控制面板（如果还没显示）
            DiffusionControlPanel.Visibility = Visibility.Visible;

            // 设置扩散起点（已经是Web墨卡托坐标系）
            _startPoint = (MapPoint)e.Location;

            // 清空之前的扩散图形
            _diffusionLayer.Graphics.Clear();

            // 添加起点标记
            var startSymbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Cross,
                System.Drawing.Color.FromArgb(255, 244, 67, 54), 20); // 红色
            _diffusionLayer.Graphics.Add(new Graphic(_startPoint, startSymbol));

            // 重置到Day 0
            DaySlider.Value = 0;
            UpdateDiffusion(0);

            // 转换为WGS84显示（用户更熟悉经纬度）
            var pointWgs84 = (MapPoint)GeometryEngine.Project(_startPoint, SpatialReferences.Wgs84);
            StatusText.Text = $"起点坐标: ({pointWgs84.Y:F4}°, {pointWgs84.X:F4}°)";
        }

        // 滑块值改变事件
        private void DaySlider_ValueChanged(object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (_startPoint == null) return;

            _currentDay = (int)DaySlider.Value;
            UpdateDiffusion(_currentDay);
        }

        // 更新扩散显示
        private void UpdateDiffusion(int day)
        {
            // 获取参数
            if (!double.TryParse(InitRadiusBox.Text, out double initRadius))
                initRadius = 500;
            if (!double.TryParse(DailyIncrBox.Text, out double dailyIncr))
                dailyIncr = 200;

            // 计算当前半径（米）
            double radiusMeters = initRadius + (day * dailyIncr);

            // 更新显示
            DayText.Text = day.ToString();
            RadiusText.Text = radiusMeters.ToString();

            // 清除之前的缓冲区（保留起点）
            var buffers = _diffusionLayer.Graphics.Where(g =>
                g.Attributes.ContainsKey("Buffer")).ToList();
            foreach (var g in buffers) _diffusionLayer.Graphics.Remove(g);

            // 在Web墨卡托坐标系下创建缓冲区
            Geometry buffer = CreateBufferInWebMercator(_startPoint, radiusMeters);

            // 创建缓冲区符号（颜色随天数加深）
            byte alpha = (byte)(150 - (day * 15));
            var color = System.Drawing.Color.FromArgb(alpha, 244, 67, 54); // 红色
            var bufferSymbol = new SimpleFillSymbol(SimpleFillSymbolStyle.Solid,
                color, new SimpleLineSymbol(SimpleLineSymbolStyle.Solid,
                System.Drawing.Color.FromArgb(255, 183, 28, 28), 2)); // 深红色边框

            var bufferGraphic = new Graphic(buffer, bufferSymbol);
            bufferGraphic.Attributes["Buffer"] = true;
            bufferGraphic.Attributes["Day"] = day;
            bufferGraphic.Attributes["Radius"] = radiusMeters;
            _diffusionLayer.Graphics.Add(bufferGraphic);

            // 统计受影响的小区点
            int affectedCount = CountAffectedPoints(buffer);
            StatusText.Text = $"第{day}天: 扩散半径{radiusMeters}米，影响{affectedCount}个居民小区";
        }

        // 在Web墨卡托坐标系下创建缓冲区（按米计算）
        private Geometry CreateBufferInWebMercator(MapPoint center, double radiusMeters)
        {
            // 在Web墨卡托坐标系下，可以直接按米计算缓冲区
            // 因为Web墨卡托是平面坐标系，单位是米
            return GeometryEngine.Buffer(center, radiusMeters);
        }

        // 统计缓冲区内的点
        private int CountAffectedPoints(Geometry buffer)
        {
            int count = 0;

            foreach (var graphic in _communityLayer.Graphics)
            {
                var point = graphic.Geometry as MapPoint;
                if (point != null && GeometryEngine.Intersects(point, buffer))
                {
                    count++;
                }
            }

            return count;
        }
    }
}