using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.UI;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using Windows.System.Profile;
using WpfMapApp2.Models;
using static WpfMapApp2.MapViewModel;

namespace WpfMapApp2
{
    public class NavigationEventArgs : EventArgs
    {
        public MapPoint Center { get; set; }
        public double Scale { get; set; }
        public SearchResultItem ResultItem { get; set; }
        public bool IsDistrictZoom { get; set; } = false; // 是否为行政区缩放
        public Envelope DistrictEnvelope { get; set; }  // 行政区边界
    }

    public class SearchResultItem
    {
        public string Name { get; set; }
        public string Type { get; set; } // "医院" 或 "小区"
        public double Lat { get; set; }
        public double Lon { get; set; }
        public object RawData { get; set; }// 存储原始 Hospital 或 Community 对象
        public string DetailInfo { get; set; }
    }

    public class RelayCommand : System.Windows.Input.ICommand
    {
        private readonly Action _execute;
        public RelayCommand(Action execute) => _execute = execute;
        public bool CanExecute(object parameter) => true;
        public void Execute(object parameter) => _execute();
        public event EventHandler CanExecuteChanged;
    }

    public class MapViewModel : INotifyPropertyChanged
    {
        private Map _map;
        private string _statusMessage = "系统初始化中...";
        public System.Windows.Input.ICommand SearchCommand { get; }

        public MapViewModel()
        {
            // 修改初始化命令的逻辑
            SearchCommand = new RelayCommand(async () =>
            {
                // 1. 点击按钮时，先清空当前选中的结果项，避免逻辑干扰
                SelectedResult = null;

                // 2. 强制执行搜索
                await PerformSearchAsync(SearchText);

                // 3. 如果搜索出了结果，确保下拉列表能显示出来
                OnPropertyChanged(nameof(IsShowingResults));
            });

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
               
                shpLayer.Renderer = new SimpleRenderer(outline);

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
                        System.Drawing.Color.Red,
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
                        graphic.Attributes["Level"] = hospital.Level;

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
                        graphic.Attributes["Level"] = hospital.Level;

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
       
        private string _searchText;
        private List<SearchResultItem> _searchResults;
        private SearchResultItem _selectedResult;

        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value;
                OnPropertyChanged();
                // 如果你希望手动输入时也实时联想，保留下面这行；
                // 如果只想靠按钮搜索，可以注释掉下面这行。
                _ = PerformSearchAsync(value);
            }
        }

        public List<SearchResultItem> SearchResults
        {
            get => _searchResults;
            set { _searchResults = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsShowingResults)); }
        }

        public bool IsShowingResults => SearchResults?.Any() == true && !string.IsNullOrWhiteSpace(SearchText);

        public SearchResultItem SelectedResult
        {
            get => _selectedResult;
            set
            {
                _selectedResult = value;
                OnPropertyChanged();
                if (value != null) ZoomToLocation(value);
            }
        }

        private async Task PerformSearchAsync(string query)
        {
            // 如果输入为空，清空结果并返回
            if (string.IsNullOrWhiteSpace(query))
            {
                SearchResults = null;
                return;
            }

            using (var db = new NanjingContext())
            {
                // 模糊查询医院
                var hospitals = await db.Hospitals
                    .Where(h => h.Name.Contains(query) && h.WgsLongitude != null)
                    .Select(h => new SearchResultItem
                    {
                        Name = h.Name,
                        Type = "医疗机构",
                        Lat = h.WgsLatitude.Value,
                        Lon = h.WgsLongitude.Value,
                        DetailInfo = $"地址：{h.Address}\n类型：{h.LevelLabel}\n电话：{h.Phone}"
                    }).Take(5).ToListAsync();

                // 模糊查询小区
                var communities = await db.Communities
                    .Where(c => c.Name.Contains(query) && c.WgsLongitude != null)
                    .Select(c => new SearchResultItem
                    {
                        Name = c.Name,
                        Type = "住宅小区",
                        Lat = c.WgsLatitude.Value,
                        Lon = c.WgsLongitude.Value,
                        DetailInfo = $"所属街道：{c.Street}\n类型：{c.Type}"
                    }).Take(5).ToListAsync();

                // 合并结果
                var allResults = hospitals.Concat(communities).ToList();

                // 核心：更新结果列表
                SearchResults = allResults;

                // 如果点击按钮后搜索到了唯一结果，或者想让用户选，
                // 必须确保 IsShowingResults 属性感知到变化
                OnPropertyChanged(nameof(IsShowingResults));
            }
        }

        public event EventHandler<NavigationEventArgs> RequestNavigation;

        private void ZoomToLocation(SearchResultItem item)
        {
            if (item == null) return;

            // 1. 触发导航和弹窗事件（保持不变）
            MapPoint targetPoint = new MapPoint(item.Lon, item.Lat, SpatialReferences.Wgs84);
            RequestNavigation?.Invoke(this, new NavigationEventArgs
            {
                Center = targetPoint,
                Scale = 5000,
                ResultItem = item
            });

            StatusMessage = $"已定位至：{item.Name}";

            // --- 核心修改部分 ---

            // 2. 隐藏搜索结果列表（通过清空结果集实现）
            SearchResults = null;

            // 3. 将搜索框文字设置为当前点击的名称，而不是清空它
            _searchText = item.Name;

            // 4. 通知界面更新输入框文字
            OnPropertyChanged(nameof(SearchText));
        }

        // 行政区范围字典 (WGS84 坐标: MinLon, MinLat, MaxLon, MaxLat)
        private readonly Dictionary<string, Viewpoint> _districtViewpoints = new Dictionary<string, Viewpoint>
        {
            { "全南京市", new Viewpoint(new Envelope(118.3, 31.2, 119.2, 32.6, SpatialReferences.Wgs84)) },
            { "玄武区", new Viewpoint(new Envelope(118.78, 32.03, 118.92, 32.12, SpatialReferences.Wgs84)) },
            { "秦淮区", new Viewpoint(new Envelope(118.75, 31.98, 118.88, 32.05, SpatialReferences.Wgs84)) },
            { "建邺区", new Viewpoint(new Envelope(118.67, 31.95, 118.78, 32.05, SpatialReferences.Wgs84)) },
            { "鼓楼区", new Viewpoint(new Envelope(118.70, 32.03, 118.80, 32.12, SpatialReferences.Wgs84)) },
            { "浦口区", new Viewpoint(new Envelope(118.30, 31.90, 118.75, 32.25, SpatialReferences.Wgs84)) },
            { "栖霞区", new Viewpoint(new Envelope(118.78, 32.08, 119.25, 32.25, SpatialReferences.Wgs84)) },
            { "雨花台区", new Viewpoint(new Envelope(118.65, 31.90, 118.85, 32.02, SpatialReferences.Wgs84)) },
            { "江宁区", new Viewpoint(new Envelope(118.55, 31.60, 119.15, 32.10, SpatialReferences.Wgs84)) },
            { "六合区", new Viewpoint(new Envelope(118.60, 32.15, 119.10, 32.60, SpatialReferences.Wgs84)) },
            { "溧水区", new Viewpoint(new Envelope(118.85, 31.35, 119.25, 31.85, SpatialReferences.Wgs84)) },
            { "高淳区", new Viewpoint(new Envelope(118.75, 31.15, 119.15, 31.55, SpatialReferences.Wgs84)) }
        };

        public List<string> Districts => _districtViewpoints.Keys.ToList();

        private string _selectedDistrict;
        public string SelectedDistrict
        {
            get => _selectedDistrict;
            set
            {
                _selectedDistrict = value;
                OnPropertyChanged();
                if (!string.IsNullOrEmpty(value))
                {
                    ZoomToDistrict(value);
                }
            }
        }

        private void ZoomToDistrict(string districtName)
        {
            if (_districtViewpoints.TryGetValue(districtName, out var vp))
            {
                // 触发一个专门的行政区跳转事件，或者复用之前的 RequestNavigation
                // 这里我们复用之前的逻辑，只是不传 ResultItem 即可
                RequestNavigation?.Invoke(this, new NavigationEventArgs
                {
                    // 对于矩形范围，我们取其中心点
                    Center = vp.TargetGeometry.Extent.GetCenter(),
                    // 比例尺设为 0，因为我们将直接使用扩展范围（Envelope）
                    Scale = 100000,
                    IsDistrictZoom = true,
                    DistrictEnvelope = vp.TargetGeometry.Extent
                });
                StatusMessage = $"已切换至：{districtName}";
            }
        }

        //  添加统计数据属性
        private int _level1Count;
        public int Level1Count { get => _level1Count; set { _level1Count = value; OnPropertyChanged(); } }

        private int _level2Count;
        public int Level2Count { get => _level2Count; set { _level2Count = value; OnPropertyChanged(); } }

        // 核心：基于地图现有图标进行统计
        public void UpdateStatisticsFromGraphics(Envelope currentExtent)
        {
            if (currentExtent == null || HospitalOverlays == null) return;

            int l1 = 0;
            int l2 = 0;

            // 1. 确保 Extent 坐标系与 Graphics 一致 (统一转为 WGS84 比较安全)
            Envelope wgsExtent = currentExtent;
            if (currentExtent.SpatialReference.Wkid != 4326)
            {
                wgsExtent = GeometryEngine.Project(currentExtent, SpatialReferences.Wgs84) as Envelope;
            }

            if (wgsExtent == null) return;

            // 2. 遍历所有图层中的图形
            foreach (var overlay in HospitalOverlays)
            {
                foreach (var graphic in overlay.Graphics)
                {
                    // 检查图形是否在当前视野范围内
                    if (GeometryEngine.Contains(wgsExtent, graphic.Geometry))
                    {
                        // 根据你在 InitializeMap 中设置的属性来识别等级
                        // 假设你在创建 Graphic 时设置了 graphic.Attributes["Level"] = hospital.Level;
                        if (graphic.Attributes.ContainsKey("Level"))
                        {
                            var level = Convert.ToInt32(graphic.Attributes["Level"]);
                            if (level == 1) l1++;
                            else if (level == 2) l2++;
                        }
                    }
                }
            }

            Level1Count = l1;
            Level2Count = l2;
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