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

            // 监听 ViewModel 中的事件
            if (DataContext is MapViewModel vm)
            {
                // 1. 订阅导航请求 (用于搜索跳转、封控区定位)
                vm.RequestNavigation += OnRequestNavigation;

                // 2. 监听视角变化 (仅在 F1 模式下统计视野内资源)
                MapView.ViewpointChanged += (s, e) =>
                {
                    if (vm.IsF1Active)
                    {
                        var extent = MapView.GetCurrentViewpoint(ViewpointType.BoundingGeometry)?.TargetGeometry as Envelope;
                        if (extent != null)
                        {
                            vm.UpdateStatisticsFromGraphics(extent);
                        }
                    }
                };
            }

            // 点击地图隐藏气泡
            MapView.GeoViewTapped += (s, e) => MapView.DismissCallout();
        }

        // 处理来自 ViewModel 的导航请求
        private async void OnRequestNavigation(object sender, NavigationEventArgs e)
        {
            if (MapView == null) return;

            // 1. 区域/面状几何体缩放 (如: 封控圈生成后、行政区跳转)
            if (e.IsDistrictZoom && e.DistrictEnvelope != null)
            {
                // 留出 50px 边距，确保展示完整
                await MapView.SetViewpointGeometryAsync(e.DistrictEnvelope, 50);
            }
            // 2. 点位缩放 (如: 小区搜索定位)
            else if (e.ResultItem != null)
            {
                MapView.DismissCallout();

                // 默认比例尺
                double scale = 5000;

                // 如果是 F4 模式，根据扩散半径动态调整比例尺，确保能看到圆
                if (DataContext is MapViewModel vm && vm.CurrentModule == "F4")
                {
                    double radiusInMeters = vm.InitialRadius;
                    // 简单的动态比例尺计算
                    scale = 5000 + (radiusInMeters / 100);
                }

                await MapView.SetViewpointCenterAsync(e.Center, scale);

                // 显示气泡 (F1 和 F4 模式下)
                if (DataContext is MapViewModel vm2)
                {
                    if (vm2.CurrentModule == "F1" || vm2.CurrentModule == "F4")
                    {
                        var definition = new CalloutDefinition(e.ResultItem.Name, e.ResultItem.DetailInfo);
                        MapView.ShowCalloutAt(e.Center, definition);
                    }
                }
            }
        }
    }
}