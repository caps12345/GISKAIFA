using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.UI;
using System.Windows;

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

                // 监听视角变化
                MapView.ViewpointChanged += (s, e) =>
                {
                    // 获取当前视野范围
                    var extent = MapView.GetCurrentViewpoint(ViewpointType.BoundingGeometry)?.TargetGeometry as Envelope;

                    // 只有当范围有效且地图停止漫游时更新（可选）
                    if (extent != null)
                    {
                        vm.UpdateStatisticsFromGraphics(extent);
                    }
                };
            }

            // 点击地图任何位置时，隐藏当前显示的气泡弹窗
            MapView.GeoViewTapped += (s, e) => MapView.DismissCallout();
        }

        private async void OnRequestNavigation(object sender, NavigationEventArgs e)
        {
            if (MapView == null) return;

            if (e.IsDistrictZoom && e.DistrictEnvelope != null)
            {
                // 如果是行政区缩放，直接缩放到该矩形范围，并留出 20% 的页边距
                await MapView.SetViewpointAsync(new Viewpoint(e.DistrictEnvelope), TimeSpan.FromSeconds(1.5));
            }
            else if (e.ResultItem != null)
            {
                // 原有的点定位逻辑
                MapView.DismissCallout();
                await MapView.SetViewpointCenterAsync(e.Center, e.Scale);

                var definition = new CalloutDefinition(e.ResultItem.Name, e.ResultItem.DetailInfo);
                MapView.ShowCalloutAt(e.Center, definition);
            }
        }
    }
}