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
                MapView.ViewpointChanged += (s, e) =>
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
                await MapView.SetViewpointCenterAsync(e.Center, e.Scale);
                var definition = new CalloutDefinition(e.ResultItem.Name, e.ResultItem.DetailInfo);
                MapView.ShowCalloutAt(e.Center, definition);
            }
        }
    }
}