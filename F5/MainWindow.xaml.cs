using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.UI;
using System;
using System.Windows;

namespace WpfMapApp2
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // 初始化 ViewModel 并赋值给 DataContext
            var vm = new MapViewModel();
            this.DataContext = vm;

            // 订阅跳转事件
            vm.RequestNavigation += (s, e) =>
            {
                Dispatcher.Invoke(async () =>
                {
                    if (e.IsDistrictZoom && e.DistrictEnvelope != null)
                    {
                        await MapView.SetViewpointGeometryAsync(e.DistrictEnvelope, 20);
                    }
                    else if (e.Center != null)
                    {
                        await MapView.SetViewpointCenterAsync(e.Center, e.Scale);
                    }
                });
            };
        }
    }
}