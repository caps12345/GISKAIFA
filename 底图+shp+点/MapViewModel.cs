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

                // 3. 加载 Level 为 1 的医院点 (新增)
                await AddHospitalPointsAsync();

                await AddHighLevelHospitalsAsync();
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

        private async Task AddHospitalPointsAsync()
        {
            try
            {
                // 1. 创建图形覆盖层并添加到 MapView (需要通过 Map 对象管理或通过属性暴露)
                // 注意：点数据通常放在 GraphicsOverlay 中，以便于频繁更新和样式设置
                GraphicsOverlay hospitalOverlay = new GraphicsOverlay { Id = "HospitalOverlay" };

                // 为了能在 MVVM 中操作 GraphicsOverlay，建议在 ViewModel 中维护一个集合或直接操作
                // 这里演示直接创建要素并添加的逻辑

                using (var db = new NanjingContext())
                {
                    // 2. 查询 Level 为 1 的医院数据
                    var level1Hospitals = await db.Hospitals
                        .Where(h => h.Level == 1 && h.WgsLongitude != null && h.WgsLatitude != null)
                        .ToListAsync();

                    if (level1Hospitals.Count == 0)
                    {
                        StatusMessage = "未找到等级为 1 的医院数据。";
                        return;
                    }

                    // 3. 定义符号 (蓝色实心圆点)
                    SimpleMarkerSymbol hospitalSymbol = new SimpleMarkerSymbol(
                        SimpleMarkerSymbolStyle.Circle,
                        System.Drawing.Color.Blue,
                        10);

                    // 4. 将医院转换为地图图形
                    foreach (var hospital in level1Hospitals)
                    {
                        // 使用 WGS84 坐标创建点
                        MapPoint point = new MapPoint(hospital.WgsLongitude.Value, hospital.WgsLatitude.Value, SpatialReferences.Wgs84);

                        // 创建图形并附加属性（方便点击查询）
                        Graphic graphic = new Graphic(point, hospitalSymbol);
                        graphic.Attributes["Name"] = hospital.Name;
                        graphic.Attributes["Address"] = hospital.Address;

                        hospitalOverlay.Graphics.Add(graphic);
                    }
                }

                // 5. 将覆盖层信息更新到 UI
                // 由于 ArcGIS Map 对象不直接持有 GraphicsOverlays，通常需要在 View 层绑定
                // 简单做法：在 ViewModel 增加一个 GraphicsOverlayCollection 属性
                HospitalOverlays.Add(hospitalOverlay);

                StatusMessage = $"成功加载一级医院";
            }
            catch (Exception ex)
            {
                StatusMessage = $"加载医院数据失败: {ex.Message}";
            }
        }

        // 补充属性定义
        private GraphicsOverlayCollection _graphicsOverlays = new GraphicsOverlayCollection();
        public GraphicsOverlayCollection HospitalOverlays
        {
            get => _graphicsOverlays;
            set { _graphicsOverlays = value; OnPropertyChanged(); }
        }

        private GraphicsOverlay _highLevelHospitalOverlay;

        private async Task AddHighLevelHospitalsAsync()
        {
            try
            {
                // 1. 创建新的图形覆盖层 
                _highLevelHospitalOverlay = new GraphicsOverlay
                {
                    Id = "Level23Hospitals",
                    // 2. 设置最小显示比例尺 (街道级别)
                    // 这里的数值代表分母，数值越小代表缩放得越近 (越详细)
                    // 50000 左右通常是城市/街道级别的分界点
                    MinScale = 50000
                };

                using (var db = new NanjingContext()) //
                {
                    // 3. 从数据库查询 Level 为 2 或 3 的点
                    var hospitals = await db.Hospitals
                        .Where(h => (h.Level == 2 || h.Level == 3) && h.WgsLongitude != null && h.WgsLatitude != null)
                        .ToListAsync();

                    // 4. 定义符号 (例如绿色圆点) 
                    SimpleMarkerSymbol symbol = new SimpleMarkerSymbol(
                        SimpleMarkerSymbolStyle.Circle,
                        System.Drawing.Color.Green,
                        8);

                    foreach (var hospital in hospitals)
                    {
                        // 创建地理点 
                        MapPoint point = new MapPoint(hospital.WgsLongitude.Value, hospital.WgsLatitude.Value, SpatialReferences.Wgs84);

                        // 创建图形并添加 
                        Graphic graphic = new Graphic(point, symbol);
                        graphic.Attributes["Name"] = hospital.Name;

                        _highLevelHospitalOverlay.Graphics.Add(graphic);
                    }
                }

                // 5. 将新图层添加到集合中 
                HospitalOverlays.Add(_highLevelHospitalOverlay);
            }
            catch (Exception ex)
            {
                 StatusMessage = $"加载高级别医院失败: {ex.Message}";
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