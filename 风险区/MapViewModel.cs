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
using WpfMapApp2.Models;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace WpfMapApp2
{
    public class MapViewModel : INotifyPropertyChanged
    {
        private Map _map;
        private string _statusMessage = "系统初始化中...";
        private GraphicsOverlayCollection _graphicsOverlays;

        public MapViewModel()
        {
            // 初始化 GraphicsOverlays 集合
            _graphicsOverlays = new GraphicsOverlayCollection();

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
                string token = "59b3588d6e5a398398aaa7705d35325f"; // 使用B项目中的Token

                // 1.1 矢量底图 (vec_w) - Web墨卡托投影
                string vecUrl = @"http://t{subDomain}.tianditu.gov.cn/vec_w/wmts?SERVICE=WMTS&REQUEST=GetTile&VERSION=1.0.0&LAYER=vec&STYLE=default&TILEMATRIXSET=w&FORMAT=tiles&TILEMATRIX={level}&TILEROW={row}&TILECOL={col}&tk=" + token;

                WebTiledLayer baseLayer = new WebTiledLayer(vecUrl, new List<string> { "0", "1", "2", "3", "4", "5", "6", "7" })
                { Name = "天地图底图" };

                // 1.2 文字注记 (cva_w)
                string cvaUrl = @"http://t{subDomain}.tianditu.gov.cn/cva_w/wmts?SERVICE=WMTS&REQUEST=GetTile&VERSION=1.0.0&LAYER=cva&STYLE=default&TILEMATRIXSET=w&FORMAT=tiles&TILEMATRIX={level}&TILEROW={row}&TILECOL={col}&tk=" + token;

                WebTiledLayer labelLayer = new WebTiledLayer(cvaUrl, new List<string> { "0", "1", "2", "3", "4", "5", "6", "7" })
                { Name = "天地图注记" };

                // 1.3 组合到底图中 - 关键：使用Web墨卡托坐标系
                Basemap myBasemap = new Basemap(baseLayer);
                myBasemap.BaseLayers.Add(labelLayer); // 叠加注记层

                // 创建地图对象 - 使用Web墨卡托坐标系
                _map = new Map(SpatialReferences.WebMercator)
                {
                    Basemap = myBasemap,
                    // 南京初始视角
                    InitialViewpoint = new Viewpoint(32.058, 118.793, 100000)
                };

                // 触发界面更新
                Map = _map;
                StatusMessage = "天地图加载成功，正在读取南京数据...";

                // =========================================================
                // 2. 加载南京 Shapefile 并自动缩放
                // =========================================================
                await AddNanjingLayerAsync();

                // 3. 加载医院点数据（推迟到地图加载完成后）
                StatusMessage = "地图加载完成，医院数据将在后台加载";
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
            // 请检查此路径是否正确
            string shpPath = @"D:\GIS_DATA\Data\南京区县.shp";

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

                // 2. 设置样式 - 改为蓝色无填充边框
                var outlineColor = System.Drawing.Color.FromArgb(255, 33, 150, 243); // 蓝色 #2196F3
                SimpleLineSymbol outline = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, outlineColor, 2);

                // 创建无填充符号（Null填充，只有边框）
                var fillColor = System.Drawing.Color.Transparent; // 完全透明
                SimpleFillSymbol fill = new SimpleFillSymbol(SimpleFillSymbolStyle.Null, fillColor, outline);

                shpLayer.Renderer = new SimpleRenderer(fill);

                // 3. 添加到地图
                _map.OperationalLayers.Add(shpLayer);

                // 4. 等待加载并获取范围
                await shpLayer.LoadAsync();

                if (shpLayer.FullExtent != null)
                {
                    // 设置地图的初始视点为图层范围 (MVVM模式下的缩放方式)
                    _map.InitialViewpoint = new Viewpoint(shpLayer.FullExtent);

                    StatusMessage = "南京行政边界加载完成（蓝色无填充边框）";

                    // 5. 加载医院点数据
                    await LoadHospitalPointsAsync();
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"加载图层失败: {ex.Message}";
            }
        }

        private async Task LoadHospitalPointsAsync()
        {
            try
            {
                StatusMessage = "正在加载医院数据...";

                // 清空现有的 GraphicsOverlays
                _graphicsOverlays.Clear();

                // 创建一级医院覆盖层
                GraphicsOverlay hospitalOverlay1 = new GraphicsOverlay { Id = "Level1Hospitals" };
                await AddHospitalsByLevelAsync(hospitalOverlay1, 1, System.Drawing.Color.Blue, 10);
                if (hospitalOverlay1.Graphics.Count > 0)
                {
                    _graphicsOverlays.Add(hospitalOverlay1);
                }

                // 创建二三级医院覆盖层
                GraphicsOverlay hospitalOverlay23 = new GraphicsOverlay
                {
                    Id = "Level23Hospitals",
                    MinScale = 50000
                };
                await AddHospitalsByLevelAsync(hospitalOverlay23, new List<int> { 2, 3 }, System.Drawing.Color.Green, 8);
                if (hospitalOverlay23.Graphics.Count > 0)
                {
                    _graphicsOverlays.Add(hospitalOverlay23);
                }

                StatusMessage = $"医院数据加载完成：一级医院 {hospitalOverlay1.Graphics.Count} 个，二三级医院 {hospitalOverlay23.Graphics.Count} 个";
                OnPropertyChanged(nameof(HospitalOverlays)); // 通知UI更新
            }
            catch (Exception ex)
            {
                StatusMessage = $"加载医院数据失败: {ex.Message}";
            }
        }

        private async Task AddHospitalsByLevelAsync(GraphicsOverlay overlay, int level, System.Drawing.Color color, int size)
        {
            await AddHospitalsByLevelAsync(overlay, new List<int> { level }, color, size);
        }

        private async Task AddHospitalsByLevelAsync(GraphicsOverlay overlay, List<int> levels, System.Drawing.Color color, int size)
        {
            try
            {
                using (var db = new NanjingContext())
                {
                    // 查询指定等级的医院数据
                    var hospitals = await db.Hospitals
                        .Where(h => levels.Contains(h.Level.Value) && h.WgsLongitude != null && h.WgsLatitude != null)
                        .ToListAsync();

                    if (hospitals.Count == 0)
                        return;

                    // 定义符号
                    SimpleMarkerSymbol hospitalSymbol = new SimpleMarkerSymbol(
                        SimpleMarkerSymbolStyle.Circle,
                        color,
                        size);

                    // 将医院转换为地图图形
                    foreach (var hospital in hospitals)
                    {
                        // 使用 WGS84 坐标创建点
                        MapPoint point = new MapPoint(hospital.WgsLongitude.Value, hospital.WgsLatitude.Value, SpatialReferences.Wgs84);

                        // 转换为Web墨卡托坐标系
                        var pointWebMercator = (MapPoint)GeometryEngine.Project(point, SpatialReferences.WebMercator);

                        // 创建图形并附加属性
                        Graphic graphic = new Graphic(pointWebMercator, hospitalSymbol);
                        graphic.Attributes["Name"] = hospital.Name;
                        graphic.Attributes["Address"] = hospital.Address;
                        graphic.Attributes["Level"] = hospital.Level;

                        overlay.Graphics.Add(graphic);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加载医院数据失败: {ex.Message}");
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

        public GraphicsOverlayCollection HospitalOverlays
        {
            get => _graphicsOverlays;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
