using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.UI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;

namespace WpfMapApp2
{
    public class MapViewModel : INotifyPropertyChanged
    {
        private Map _map;
        private string _statusMessage = "系统初始化中...";

        public MapViewModel()
        {
            // 在构造函数中启动异步初始化
            InitializeMap();
        }

        private async void InitializeMap()
        {
            try
            {
                // =========================================================
                // 1. 加载天地图底图 (矢量底图 + 文字注记)
                // =========================================================
                string token = "96cd361c8473c7c2d2c96bd05c598a2c"; // 使用你提供的Token

                // 1.1 矢量底图 (vec_w)
                string vecUrl = @"http://t{subDomain}.tianditu.gov.cn/vec_w/wmts?SERVICE=WMTS&REQUEST=GetTile&VERSION=1.0.0&LAYER=vec&STYLE=default&TILEMATRIXSET=w&FORMAT=tiles&TILEMATRIX={level}&TILEROW={row}&TILECOL={col}&tk=" + token;

                WebTiledLayer baseLayer = new WebTiledLayer(vecUrl, new List<string> { "0", "1", "2", "3", "4", "5", "6", "7" })
                { Name = "天地图底图" };

                // 1.2 文字注记 (cva_w) - 没有这个地图就没有地名
                string cvaUrl = @"http://t{subDomain}.tianditu.gov.cn/cva_w/wmts?SERVICE=WMTS&REQUEST=GetTile&VERSION=1.0.0&LAYER=cva&STYLE=default&TILEMATRIXSET=w&FORMAT=tiles&TILEMATRIX={level}&TILEROW={row}&TILECOL={col}&tk=" + token;

                WebTiledLayer labelLayer = new WebTiledLayer(cvaUrl, new List<string> { "0", "1", "2", "3", "4", "5", "6", "7" })
                { Name = "天地图注记" };

                // 1.3 组合到底图中
                Basemap myBasemap = new Basemap(baseLayer);
                myBasemap.BaseLayers.Add(labelLayer); // 叠加注记层

                // 创建地图对象
                _map = new Map(myBasemap);

                // 设置一个临时的全省视角
                _map.InitialViewpoint = new Viewpoint(new Envelope(116.0, 30.0, 122.0, 35.0, SpatialReferences.Wgs84));

                // 触发界面更新
                Map = _map;
                StatusMessage = "天地图加载成功，正在读取南京数据...";

                // =========================================================
                // 2. 加载南京 Shapefile 并自动缩放
                // =========================================================
                await AddNanjingLayerAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"初始化错误: {ex.Message}";
                // 出错时回退到默认底图
                Map = new Map(BasemapStyle.ArcGISStreets);
            }
        }

        private async Task AddNanjingLayerAsync()
        {
            // ★★★ TODO: 请检查此路径是否正确 ★★★
            string shpPath = @"D:\GIS_DATA\南京区县.shp";

            if (!File.Exists(shpPath))
            {
                StatusMessage = "错误：找不到 Shapefile 文件，请检查路径！";
                return;
            }

            try
            {
                // 1. 读取数据
                ShapefileFeatureTable shpTable = new ShapefileFeatureTable(shpPath);
                FeatureLayer shpLayer = new FeatureLayer(shpTable);

                // 2. 设置样式 (红色边框 + 半透明红底)
                // 使用 System.Drawing.Color 避免命名冲突
                var outlineColor = System.Drawing.Color.Red;
                SimpleLineSymbol outline = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, outlineColor, 2);

                var fillColor = System.Drawing.Color.FromArgb(50, 255, 0, 0); // Alpha=50 (半透明)
                SimpleFillSymbol fill = new SimpleFillSymbol(SimpleFillSymbolStyle.Solid, fillColor, outline);

                shpLayer.Renderer = new SimpleRenderer(fill);

                // 3. 添加到地图
                _map.OperationalLayers.Add(shpLayer);

                // 4. 等待加载并获取范围
                await shpLayer.LoadAsync();

                if (shpLayer.FullExtent != null)
                {
                    // 设置地图的初始视点为图层范围 (MVVM模式下的缩放方式)
                    _map.InitialViewpoint = new Viewpoint(shpLayer.FullExtent);

                    StatusMessage = "南京行政边界加载完成";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"加载图层失败: {ex.Message}";
            }
        }

        // --- MVVM 属性实现 ---

        public Map Map
        {
            get => _map;
            set { _map = value; OnPropertyChanged(); }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}