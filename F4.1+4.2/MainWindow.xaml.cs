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

            if (DataContext is MapViewModel vm)
            {
                // 1. 订阅导航请求 (用于 F1 搜索和筛选跳转)
                vm.RequestNavigation += OnRequestNavigation;

                // 2. 监听视角变化 (用于 F1 实时统计视野内医院数量)
                MapView.ViewpointChanged += (s, e) =>  // MapView是实例，不是类型
                {
                    if (vm.IsF1Active) // 性能优化：只有 F1 模式才计算
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

        private async void OnRequestNavigation(object sender, NavigationEventArgs e)
        {
            if (MapView == null) return;

            if (e.IsDistrictZoom && e.DistrictEnvelope != null)
            {
                await MapView.SetViewpointAsync(new Viewpoint(e.DistrictEnvelope));
            }
            else if (e.ResultItem != null)
            {
                MapView.DismissCallout();

                // 对于F4模式，使用固定比例尺5000
                double scale = 5000;

                // 如果是扩散模式的搜索结果，使用一个能显示缓冲区的比例尺
                if (DataContext is MapViewModel vm && vm.CurrentModule == "F4")
                {
                    // 根据初始半径计算合适的比例尺
                    double radiusInMeters = vm.InitialRadius;
                    // 近似计算：每米对应约0.00001度
                    double bufferWidthDegrees = radiusInMeters * 0.00001 * 2; // 直径
                                                                              // 计算合适的比例尺（确保能看到整个缓冲区）
                    scale = 5000 + (radiusInMeters / 100); // 线性调整
                }

                await MapView.SetViewpointCenterAsync(e.Center, scale);

                // 只在F1和F4模式下显示气泡
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