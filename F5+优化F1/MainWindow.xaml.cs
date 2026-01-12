using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.UI;
using System;
using System.Windows;
using WpfMapApp2.Models;

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

                // 确保高亮层最后添加，或者手动管理顺序
                if (!MapView.GraphicsOverlays.Contains(vm.HighlightOverlay))
                {
                    MapView.GraphicsOverlays.Add(vm.HighlightOverlay);
                }
            }

            // 点击地图隐藏气泡
            MapView.GeoViewTapped += async (s, e) =>
            {
                if (DataContext is MapViewModel vm)
                {
                    vm.ClearHighlight();
                    vm.UpdateStatisticsByDistrict("南京市"); // 恢复全城统计
                }

                // 1. 先关闭已有弹窗
                MapView.DismissCallout();

                try
                {
                    // 2. 识别所有图层中的图形 (容差设为 10，图片符号大一点更好点中)
                    var results = await MapView.IdentifyGraphicsOverlaysAsync(e.Position, 10, false);

                    // 3. 从识别结果中提取第一个有效的 Graphic
                    var selectedGraphic = results
                        .SelectMany(r => r.Graphics)
                        .FirstOrDefault(g => g.Attributes.ContainsKey("Name"));

                    if (selectedGraphic != null)
                    {
                        string title = selectedGraphic.Attributes["Name"].ToString();
                        string detail = selectedGraphic.Attributes["DetailInfo"].ToString();

                        // 4. 显示弹窗
                        CalloutDefinition definition = new CalloutDefinition(title, detail);
                        MapView.ShowCalloutAt((MapPoint)selectedGraphic.Geometry, definition);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"点击识别出错: {ex.Message}");
                }
            };
        }

        private async void OnRequestNavigation(object sender, NavigationEventArgs e)
        {
            if (MapView == null || DataContext is not MapViewModel vm) return;

            // 情况 A：处理行政区统计跳转
            if (e.IsDistrictZoom && e.DistrictEnvelope != null)
            {
                await MapView.SetViewpointAsync(new Viewpoint(e.DistrictEnvelope));
                vm.UpdateStatisticsByDistrict(e.DistrictName);
                MapView.DismissCallout(); // 切换区域时关闭气泡
            }
            // 情况 B：处理搜索结果跳转
            else if (e.Center != null)
            {
                // 1. 缩放到指定点
                await MapView.SetViewpointCenterAsync(e.Center, e.Scale > 0 ? e.Scale : 5000);

                // 2. 如果有搜索结果项，手动弹出气泡 [新增逻辑]
                if (e.ResultItem != null)
                {
                    CalloutDefinition definition = new CalloutDefinition(
                        e.ResultItem.Name,
                        e.ResultItem.DetailInfo
                    );
                    MapView.ShowCalloutAt(e.Center, definition);
                }
            }
        }
    }
}