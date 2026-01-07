using System;
using System.Linq;
using System.Windows;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.UI;

namespace WpfMapApp2
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private async void MapView_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MapView?.Map != null)
                {
                    // 设置初始视点到江苏省范围
                    var jiangsuExtent = new Envelope(116.0, 30.0, 122.0, 35.0, SpatialReferences.Wgs84);
                    await MapView.SetViewpointAsync(new Viewpoint(jiangsuExtent));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"地图初始化失败: {ex.Message}", "错误");
            }
        }
    }
}