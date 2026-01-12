using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.UI;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json; // ★★★ 修复 CS0246: 必须安装并引用 Newtonsoft.Json ★★★
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using WpfMapApp2.Models;

namespace WpfMapApp2
{
    // --- 辅助类 ---
    public class NavigationEventArgs : EventArgs
    {
        public MapPoint Center { get; set; }
        public double Scale { get; set; }
        public SearchResultItem ResultItem { get; set; }
        public bool IsDistrictZoom { get; set; } = false;
        public Envelope DistrictEnvelope { get; set; }
    }

    public class SearchResultItem
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public double Lat { get; set; }
        public double Lon { get; set; }
        public string DetailInfo { get; set; }
    }

    public class DistrictStat
    {
        public string Name { get; set; }
        public double CoverageRate { get; set; }
        public string RateText => $"{CoverageRate:F1}%";
        public double BarWidth => CoverageRate * 2.0;
        public string BarColor => CoverageRate < 50 ? "#FF5252" : (CoverageRate < 80 ? "#FFC107" : "#4CAF50");
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        public RelayCommand(Action<object> execute) => _execute = execute;
        public RelayCommand(Action execute) : this(o => execute()) { }
        public bool CanExecute(object parameter) => true;
        public void Execute(object parameter) => _execute(parameter);
        public event EventHandler CanExecuteChanged;
    }

    // --- 高德API数据模型 ---
    public class AmapRouteResponse
    {
        public string status { get; set; }
        public AmapRouteResult route { get; set; }
    }
    public class AmapRouteResult { public List<AmapPath> paths { get; set; } }
    public class AmapPath
    {
        public string distance { get; set; } // 总距离
        public string duration { get; set; } // 总耗时
        public List<AmapStep> steps { get; set; }
    }
    public class AmapStep
    {
        public string polyline { get; set; }
        public string road { get; set; }
        public string duration { get; set; }
        public string distance { get; set; }
    }

    // 智能路径点 (带速度)
    public class SmartRoutePoint
    {
        public MapPoint Point { get; set; }
        public double Speed { get; set; } // m/s
        public string RoadName { get; set; }
    }

    // 路径结果
    public class RouteResultInfo
    {
        public Polyline Geometry { get; set; }
        public List<SmartRoutePoint> SmartPoints { get; set; }
        public double TotalDistance { get; set; }
        public double TotalDuration { get; set; }
    }

    public enum RescueState { Idle, ToPatient, OnScene, ToHospital }

    // --- 坐标转换工具 (GCJ02 -> WGS84) ---
    public static class CoordTransform
    {
        private const double pi = 3.1415926535897932384626;
        private const double a = 6378245.0;
        private const double ee = 0.00669342162296594323;

        public static MapPoint GCJ02ToWGS84(double lng, double lat)
        {
            if (OutOfChina(lng, lat)) return new MapPoint(lng, lat, SpatialReferences.Wgs84);
            double dlat = TransformLat(lng - 105.0, lat - 35.0);
            double dlng = TransformLng(lng - 105.0, lat - 35.0);
            double radlat = lat / 180.0 * pi;
            double magic = Math.Sin(radlat);
            magic = 1 - ee * magic * magic;
            double sqrtmagic = Math.Sqrt(magic);
            dlat = (dlat * 180.0) / ((a * (1 - ee)) / (magic * sqrtmagic) * pi);
            dlng = (dlng * 180.0) / (a / sqrtmagic * Math.Cos(radlat) * pi);
            double mglat = lat + dlat;
            double mglng = lng + dlng;
            return new MapPoint(lng * 2 - mglng, lat * 2 - mglat, SpatialReferences.Wgs84);
        }
        private static bool OutOfChina(double lng, double lat) => (lng < 72.004 || lng > 137.8347 || lat < 0.8293 || lat > 55.8271);
        private static double TransformLat(double x, double y)
        {
            double ret = -100.0 + 2.0 * x + 3.0 * y + 0.2 * y * y + 0.1 * x * y + 0.2 * Math.Sqrt(Math.Abs(x));
            ret += (20.0 * Math.Sin(6.0 * x * pi) + 20.0 * Math.Sin(2.0 * x * pi)) * 2.0 / 3.0;
            ret += (20.0 * Math.Sin(y * pi) + 40.0 * Math.Sin(y / 3.0 * pi)) * 2.0 / 3.0;
            ret += (160.0 * Math.Sin(y / 12.0 * pi) + 320 * Math.Sin(y * pi / 30.0)) * 2.0 / 3.0;
            return ret;
        }
        private static double TransformLng(double x, double y)
        {
            double ret = 300.0 + x + 2.0 * y + 0.1 * x * x + 0.1 * x * y + 0.1 * Math.Sqrt(Math.Abs(x));
            ret += (20.0 * Math.Sin(6.0 * x * pi) + 20.0 * Math.Sin(2.0 * x * pi)) * 2.0 / 3.0;
            ret += (20.0 * Math.Sin(x * pi) + 40.0 * Math.Sin(x / 3.0 * pi)) * 2.0 / 3.0;
            ret += (150.0 * Math.Sin(x / 12.0 * pi) + 300.0 * Math.Sin(x / 30.0 * pi)) * 2.0 / 3.0;
            return ret;
        }
    }

    // ================= ViewModel =================
    public class MapViewModel : INotifyPropertyChanged
    {
        // 1. 配置
        private const string ShpPath = @"D:\GIS_DATA\Data\南京区县.shp";
        private const string MmpkPath = @"D:\GIS_DATA\Data\nanjing.mmpk";
        private const string AmapKey = "d858a21cb4bfc1ffb236ed80f34bdc57";

        // 2. 核心属性
        private Map _map;
        private string _statusMessage = "系统初始化...";

        public GraphicsOverlayCollection GraphicsOverlays { get; set; } = new GraphicsOverlayCollection();
        private GraphicsOverlay _blindSpotOverlay = new GraphicsOverlay { Id = "BlindSpot" };
        private GraphicsOverlay _level1Overlay = new GraphicsOverlay { Id = "Level1Hospitals" };
        private GraphicsOverlay _highLevelOverlay = new GraphicsOverlay { Id = "HighLevelHospitals", MinScale = 50000 };

        // F4/F5 专用图层
        private GraphicsOverlay _f4CommunityOverlay = new GraphicsOverlay { Id = "F4Communities", MinScale = 50000 };
        private GraphicsOverlay _f4Level1HospitalOverlay = new GraphicsOverlay { Id = "F4Level1Hospitals" };
        private GraphicsOverlay _diffusionOverlay = new GraphicsOverlay { Id = "DiffusionOverlay" };
        private GraphicsOverlay _networkDiffusionOverlay = new GraphicsOverlay { Id = "NetworkDiffusion" };
        private GraphicsOverlay _lockdownOverlay = new GraphicsOverlay { Id = "LockdownLayer" };
        private GraphicsOverlay _rescueOverlay = new GraphicsOverlay { Id = "RescueOverlay" };

        public Map Map { get => _map; set { _map = value; OnPropertyChanged(); } }
        public string StatusMessage { get => _statusMessage; set { _statusMessage = value; OnPropertyChanged(); } }

        // --- 3. 界面显隐控制 ---
        private Visibility _leftPanelVisibility = Visibility.Collapsed;
        public Visibility LeftPanelVisibility { get => _leftPanelVisibility; set { _leftPanelVisibility = value; OnPropertyChanged(); } }

        private Visibility _isF1ConfigVisible = Visibility.Collapsed;
        public Visibility IsF1ConfigVisible { get => _isF1ConfigVisible; set { _isF1ConfigVisible = value; OnPropertyChanged(); } }

        private Visibility _isF2ConfigVisible = Visibility.Collapsed;
        public Visibility IsF2ConfigVisible { get => _isF2ConfigVisible; set { _isF2ConfigVisible = value; OnPropertyChanged(); } }

        private Visibility _f1StatsVisibility = Visibility.Collapsed;
        public Visibility F1StatsVisibility { get => _f1StatsVisibility; set { _f1StatsVisibility = value; OnPropertyChanged(); } }

        private Visibility _statsPanelVisibility = Visibility.Collapsed;
        public Visibility StatsPanelVisibility { get => _statsPanelVisibility; set { _statsPanelVisibility = value; OnPropertyChanged(); } }

        private Visibility _isF4ConfigVisible = Visibility.Collapsed;
        public Visibility IsF4ConfigVisible { get => _isF4ConfigVisible; set { _isF4ConfigVisible = value; OnPropertyChanged(); } }

        private string _currentF4Mode = "Neighborhood";
        public string CurrentF4Mode
        {
            get => _currentF4Mode;
            set { _currentF4Mode = value; OnPropertyChanged(); UpdateF4ModeVisibility(); }
        }

        private Visibility _isNeighborhoodModeVisible = Visibility.Visible;
        public Visibility IsNeighborhoodModeVisible { get => _isNeighborhoodModeVisible; set { _isNeighborhoodModeVisible = value; OnPropertyChanged(); } }

        private Visibility _isNetworkModeVisible = Visibility.Collapsed;
        public Visibility IsNetworkModeVisible { get => _isNetworkModeVisible; set { _isNetworkModeVisible = value; OnPropertyChanged(); } }

        // F5 仪表盘
        private string _rescueDashboardText = "等待任务...";
        private string _rescueDashboardColor = "#333";
        private Visibility _isRescueDashboardVisible = Visibility.Collapsed;
        public string RescueDashboardText { get => _rescueDashboardText; set { _rescueDashboardText = value; OnPropertyChanged(); } }
        public string RescueDashboardColor { get => _rescueDashboardColor; set { _rescueDashboardColor = value; OnPropertyChanged(); } }
        public Visibility IsRescueDashboardVisible { get => _isRescueDashboardVisible; set { _isRescueDashboardVisible = value; OnPropertyChanged(); } }

        // --- 4. F1 功能属性 ---
        public bool IsF1Active { get; set; } = false;
        private string _currentModule = "F1";
        public string CurrentModule { get => _currentModule; set { _currentModule = value; OnPropertyChanged(); } }

        private string _searchText;
        private List<SearchResultItem> _searchResults;
        private SearchResultItem _selectedResult;
        private string _selectedDistrict;
        private int _level1Count;
        private int _level2Count;

        public string SearchText
        {
            get => _searchText;
            set { _searchText = value; OnPropertyChanged(); if (!string.IsNullOrWhiteSpace(value)) _ = PerformSearchAsync(value); }
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
            set { _selectedResult = value; OnPropertyChanged(); if (value != null) ZoomToLocation(value); }
        }
        public int Level1Count { get => _level1Count; set { _level1Count = value; OnPropertyChanged(); } }
        public int Level2Count { get => _level2Count; set { _level2Count = value; OnPropertyChanged(); } }

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
        public string SelectedDistrict
        {
            get => _selectedDistrict;
            set { _selectedDistrict = value; OnPropertyChanged(); if (!string.IsNullOrEmpty(value)) ZoomToDistrict(value); }
        }

        // --- 5. F2 功能属性 ---
        public ObservableCollection<DistrictStat> DistrictStats { get; set; } = new ObservableCollection<DistrictStat>();
        private FeatureLayer _layerWalkL1, _layerDriveL1, _layerWalkAll, _layerDriveAll;
        private bool _isAnalysisStarted = false;
        private int _selectedModeIndex = 0;
        private int _selectedHospitalTypeIndex = 0;

        private Visibility _legendWalkL1 = Visibility.Collapsed;
        private Visibility _legendDriveL1 = Visibility.Collapsed;
        private Visibility _legendWalkAll = Visibility.Collapsed;
        private Visibility _legendDriveAll = Visibility.Collapsed;
        public Visibility LegendWalkL1 { get => _legendWalkL1; set { _legendWalkL1 = value; OnPropertyChanged(); } }
        public Visibility LegendDriveL1 { get => _legendDriveL1; set { _legendDriveL1 = value; OnPropertyChanged(); } }
        public Visibility LegendWalkAll { get => _legendWalkAll; set { _legendWalkAll = value; OnPropertyChanged(); } }
        public Visibility LegendDriveAll { get => _legendDriveAll; set { _legendDriveAll = value; OnPropertyChanged(); } }

        public int SelectedModeIndex { get => _selectedModeIndex; set { _selectedModeIndex = value; OnPropertyChanged(); CheckF2Update(); } }
        public int SelectedHospitalTypeIndex { get => _selectedHospitalTypeIndex; set { _selectedHospitalTypeIndex = value; OnPropertyChanged(); CheckF2Update(); } }

        // --- 6. F4 功能属性 (优化版) ---
        private bool _isDiffusionActive = false;
        public bool IsDiffusionActive { get => _isDiffusionActive; set { _isDiffusionActive = value; OnPropertyChanged(); } }

        private string _f4SearchText;
        public string F4SearchText { get => _f4SearchText; set { _f4SearchText = value; OnPropertyChanged(); } }

        private List<SearchResultItem> _f4SearchResults;
        public List<SearchResultItem> F4SearchResults { get => _f4SearchResults; set { _f4SearchResults = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsShowingF4Results)); } }
        public bool IsShowingF4Results => F4SearchResults?.Any() == true && !string.IsNullOrWhiteSpace(F4SearchText);

        private SearchResultItem _selectedF4Community;
        public SearchResultItem SelectedF4Community
        {
            get => _selectedF4Community;
            set { _selectedF4Community = value; OnPropertyChanged(); if (value != null) HandleF4CommunitySelection(value); }
        }

        private int _currentDay = 0;
        public int CurrentDay { get => _currentDay; set { if (_currentDay != value) { _currentDay = value; OnPropertyChanged(); UpdateDiffusionBuffer(); } } }

        private double _initialRadius = 200;
        public double InitialRadius { get => _initialRadius; set { if (_initialRadius != value) { _initialRadius = value; OnPropertyChanged(); UpdateDiffusionBuffer(); } } }

        private double _dailyIncrement = 200;
        public double DailyIncrement { get => _dailyIncrement; set { if (_dailyIncrement != value) { _dailyIncrement = value; OnPropertyChanged(); UpdateDiffusionBuffer(); } } }

        private string _diffusionStatus = "请先在上方搜索并选择小区";
        public string DiffusionStatus { get => _diffusionStatus; set { _diffusionStatus = value; OnPropertyChanged(); } }

        private string _nearestHospitalInfo = "等待选择小区...";
        public string NearestHospitalInfo { get => _nearestHospitalInfo; set { _nearestHospitalInfo = value; OnPropertyChanged(); } }

        private double _corridorWidth = 100;
        public double CorridorWidth { get => _corridorWidth; set { _corridorWidth = value; OnPropertyChanged(); } }

        // F4/F5 内部变量
        private MapPoint _f4StartPoint = null;
        private MapPoint _nearestHospitalPoint = null;
        private Geometry _lastNeighborhoodBuffer;
        private Geometry _lastNetworkCorridor;

        // F5 急救变量
        private System.Timers.Timer _rescueTimer;
        private List<SmartRoutePoint> _rescueSmartPoints;
        private List<SmartRoutePoint> _tempLeg2SmartPoints;
        private int _rescuePathIndex = 0;
        private Graphic _ambulanceGraphic;
        private Graphic _patientGraphic;
        private Polyline _pathLeg2;
        private RescueState _currentRescueState = RescueState.Idle;

        // --- 7. 命令 ---
        public ICommand SearchCommand { get; }
        public ICommand LoadAnalysisCommand { get; }
        public ICommand CalcBlindSpotCommand { get; }
        public ICommand CalcEquityCommand { get; }
        public ICommand SwitchModuleCommand { get; }
        public event EventHandler<NavigationEventArgs> RequestNavigation;

        public ICommand F4SearchCommand { get; }
        public ICommand SwitchF4ModeCommand { get; }
        public ICommand StartNetworkSimulationCommand { get; }
        public ICommand ClearNetworkGraphicsCommand { get; }
        public ICommand GenerateLockdownCommand { get; }
        public ICommand StopNetworkSimulationCommand { get; }
        public ICommand TriggerRescueCommand { get; }

        // ================= 构造函数 =================
        public MapViewModel()
        {
            GraphicsOverlays.Add(_blindSpotOverlay);
            GraphicsOverlays.Add(_level1Overlay);
            GraphicsOverlays.Add(_highLevelOverlay);
            GraphicsOverlays.Add(_diffusionOverlay);
            GraphicsOverlays.Add(_f4CommunityOverlay);
            GraphicsOverlays.Add(_f4Level1HospitalOverlay);
            GraphicsOverlays.Add(_networkDiffusionOverlay);
            GraphicsOverlays.Add(_lockdownOverlay);
            GraphicsOverlays.Add(_rescueOverlay);

            SearchCommand = new RelayCommand(async () => { SelectedResult = null; await PerformSearchAsync(SearchText); OnPropertyChanged(nameof(IsShowingResults)); });
            LoadAnalysisCommand = new RelayCommand(ExecuteLoadAnalysis);
            CalcBlindSpotCommand = new RelayCommand(ExecuteCalcBlindSpot);
            CalcEquityCommand = new RelayCommand(ExecuteCalcEquity);
            SwitchModuleCommand = new RelayCommand(ExecuteSwitchModule);

            F4SearchCommand = new RelayCommand(async () => await PerformF4SearchAsync(F4SearchText));
            SwitchF4ModeCommand = new RelayCommand(SwitchF4Mode);
            StartNetworkSimulationCommand = new RelayCommand(ExecuteStartNetworkSimulation);
            StopNetworkSimulationCommand = new RelayCommand(ExecuteClearNetworkGraphics);
            ClearNetworkGraphicsCommand = new RelayCommand(ExecuteClearNetworkGraphics);
            GenerateLockdownCommand = new RelayCommand(ExecuteGenerateLockdown);
            TriggerRescueCommand = new RelayCommand(obj => { });

            InitializeMap();
        }

        // ================= 模块切换 =================
        private void ExecuteSwitchModule(object parameter)
        {
            string module = parameter as string;
            CurrentModule = module;
            LeftPanelVisibility = Visibility.Visible;

            // 清理
            if (module != "F4")
            {
                _f4CommunityOverlay.IsVisible = false;
                _f4Level1HospitalOverlay.IsVisible = false;
                _diffusionOverlay.IsVisible = false;
                _networkDiffusionOverlay.IsVisible = false;
                _lockdownOverlay.IsVisible = false;
                ExecuteClearNetworkGraphics(null);
            }
            if (module != "F5")
            {
                _rescueOverlay.IsVisible = false;
                _rescueTimer?.Stop();
                IsRescueDashboardVisible = Visibility.Collapsed;
            }

            IsF1ConfigVisible = Visibility.Collapsed;
            IsF2ConfigVisible = Visibility.Collapsed;
            IsF4ConfigVisible = Visibility.Collapsed;
            F1StatsVisibility = Visibility.Collapsed;
            StatsPanelVisibility = Visibility.Collapsed;

            _level1Overlay.IsVisible = false;
            _highLevelOverlay.IsVisible = false;
            _blindSpotOverlay.IsVisible = false;
            HideAllMmpkLayers();

            if (module == "F1")
            {
                IsF1Active = true;
                StatusMessage = "F1 资源感知: 医院点位模式";
                IsF1ConfigVisible = Visibility.Visible;
                _level1Overlay.IsVisible = true;
                _highLevelOverlay.IsVisible = true;
                F1StatsVisibility = Visibility.Visible;
            }
            else if (module == "F2")
            {
                IsF1Active = false;
                StatusMessage = "F2 可达性分析: 请加载分析";
                IsF2ConfigVisible = Visibility.Visible;
                _blindSpotOverlay.IsVisible = true;
                if (_isAnalysisStarted) UpdateLayerVisibility();
            }
            else if (module == "F3")
            {
                IsF1Active = false;
                StatusMessage = "F3 压力监测 (开发中)";
            }
            else if (module == "F4")
            {
                IsF1Active = false;
                StatusMessage = "F4 风险模拟: 全局选点模式";
                IsF4ConfigVisible = Visibility.Visible;
                _f4CommunityOverlay.IsVisible = true;
                _f4Level1HospitalOverlay.IsVisible = true;
                _diffusionOverlay.IsVisible = true;
                _networkDiffusionOverlay.IsVisible = true;
                _lockdownOverlay.IsVisible = true;
                LoadF4DataAsync();
                IsDiffusionActive = true;
                CurrentF4Mode = "Neighborhood";
                DiffusionStatus = "请在上方搜索并锁定一个小区";
            }
            else if (module == "F5")
            {
                IsF1Active = false;
                StatusMessage = "F5 急救响应: 请在地图上点击位置模拟求救信号";
                _f4Level1HospitalOverlay.IsVisible = true;
                _rescueOverlay.IsVisible = true;
                _rescueOverlay.Graphics.Clear();
                LoadF4Level1HospitalPointsAsync();
            }
        }

        // ================= F1/F2 基础方法 (修复丢失) =================
        private async void InitializeMap()
        {
            try
            {
                string token = "96cd361c8473c7c2d2c96bd05c598a2c";
                string vecUrl = @"http://t0.tianditu.gov.cn/vec_w/wmts?SERVICE=WMTS&REQUEST=GetTile&VERSION=1.0.0&LAYER=vec&STYLE=default&TILEMATRIXSET=w&FORMAT=tiles&TILEMATRIX={level}&TILEROW={row}&TILECOL={col}&tk=" + token;
                string cvaUrl = @"http://t0.tianditu.gov.cn/cva_w/wmts?SERVICE=WMTS&REQUEST=GetTile&VERSION=1.0.0&LAYER=cva&STYLE=default&TILEMATRIXSET=w&FORMAT=tiles&TILEMATRIX={level}&TILEROW={row}&TILECOL={col}&tk=" + token;

                WebTiledLayer baseLayer = new WebTiledLayer(vecUrl, new List<string> { "0", "1", "2", "3", "4", "5", "6", "7" });
                WebTiledLayer labelLayer = new WebTiledLayer(cvaUrl, new List<string> { "0", "1", "2", "3", "4", "5", "6", "7" });
                Basemap myBasemap = new Basemap(baseLayer);
                myBasemap.BaseLayers.Add(labelLayer);

                Map = new Map(myBasemap);
                Map.InitialViewpoint = new Viewpoint(32.060, 118.796, 150000);

                await AddNanjingLayerAsync();
                await LoadMmpkLayers();
                await AddHospitalPointsAsync();
                await AddHighLevelHospitalsAsync();

                ExecuteSwitchModule("F1");
            }
            catch (Exception ex) { StatusMessage = $"初始化失败: {ex.Message}"; }
        }

        private async Task AddNanjingLayerAsync()
        {
            if (!File.Exists(ShpPath)) return;
            try
            {
                ShapefileFeatureTable table = new ShapefileFeatureTable(ShpPath);
                FeatureLayer layer = new FeatureLayer(table);
                layer.Renderer = new SimpleRenderer(new SimpleFillSymbol(SimpleFillSymbolStyle.Null, System.Drawing.Color.Transparent, new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.Red, 2)));
                Map.OperationalLayers.Add(layer);
                await layer.LoadAsync();
            }
            catch { }
        }

        private async Task AddHospitalPointsAsync()
        {
            try
            {
                using (var db = new NanjingContext())
                {
                    var list = await db.Hospitals.Where(h => h.Level == 1 && h.WgsLongitude != null).ToListAsync();
                    SimpleMarkerSymbol sym = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.Blue, 6);
                    foreach (var h in list) _level1Overlay.Graphics.Add(new Graphic(new MapPoint(h.WgsLongitude.Value, h.WgsLatitude.Value, SpatialReferences.Wgs84), sym) { Attributes = { ["Level"] = 1 } });
                }
            }
            catch { }
        }

        private async Task AddHighLevelHospitalsAsync()
        {
            try
            {
                using (var db = new NanjingContext())
                {
                    var list = await db.Hospitals.Where(h => (h.Level == 2 || h.Level == 3) && h.WgsLongitude != null).ToListAsync();
                    SimpleMarkerSymbol sym = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.Green, 8);
                    foreach (var h in list) _highLevelOverlay.Graphics.Add(new Graphic(new MapPoint(h.WgsLongitude.Value, h.WgsLatitude.Value, SpatialReferences.Wgs84), sym) { Attributes = { ["Level"] = 2 } });
                }
            }
            catch { }
        }

        private void ZoomToLocation(SearchResultItem item)
        {
            if (item == null) return;
            RequestNavigation?.Invoke(this, new NavigationEventArgs { Center = new MapPoint(item.Lon, item.Lat, SpatialReferences.Wgs84), Scale = 5000, ResultItem = item });
            StatusMessage = $"定位: {item.Name}";
            SearchResults = null;
            _searchText = item.Name; OnPropertyChanged(nameof(SearchText));
        }

        private void ZoomToDistrict(string districtName)
        {
            if (_districtViewpoints.TryGetValue(districtName, out var vp))
            {
                RequestNavigation?.Invoke(this, new NavigationEventArgs { Center = vp.TargetGeometry.Extent.GetCenter(), Scale = 100000, IsDistrictZoom = true, DistrictEnvelope = vp.TargetGeometry.Extent });
                StatusMessage = $"切换至: {districtName}";
            }
        }

        public void UpdateStatisticsFromGraphics(Envelope currentExtent)
        {
            if (currentExtent == null) return;
            int l1 = 0, l2 = 0;
            Envelope wgsExtent = (currentExtent.SpatialReference.Wkid != 4326) ? GeometryEngine.Project(currentExtent, SpatialReferences.Wgs84) as Envelope : currentExtent;
            if (wgsExtent == null) return;

            foreach (var overlay in GraphicsOverlays)
            {
                if (!overlay.IsVisible) continue;
                foreach (var graphic in overlay.Graphics)
                {
                    if (GeometryEngine.Contains(wgsExtent, graphic.Geometry))
                    {
                        if (graphic.Attributes.ContainsKey("Level"))
                        {
                            var level = Convert.ToInt32(graphic.Attributes["Level"]);
                            if (level == 1) l1++; else if (level == 2 || level == 3) l2++;
                        }
                    }
                }
            }
            Level1Count = l1; Level2Count = l2;
        }

        // ================= F2 核心方法 (修复丢失) =================
        private void ExecuteLoadAnalysis(object obj)
        {
            _isAnalysisStarted = true;
            UpdateLayerVisibility();
            StatusMessage = "等时圈分析已加载。";
        }

        private async Task LoadMmpkLayers()
        {
            if (!File.Exists(MmpkPath)) return;
            try
            {
                var mmpk = await MobileMapPackage.OpenAsync(MmpkPath);
                if (mmpk.Maps.Count > 0)
                {
                    var sourceMap = mmpk.Maps[0];
                    var allLayers = new List<FeatureLayer>();
                    await CollectFeatureLayersRecursive(sourceMap.OperationalLayers, allLayers);
                    foreach (var layer in allLayers)
                    {
                        var cloned = layer.Clone() as FeatureLayer;
                        if (cloned == null) continue;
                        string name = cloned.Name.ToLower();
                        bool isWalk = name.Contains("walk");
                        bool isDrive = name.Contains("drive");
                        bool isAll = name.Contains("123") || name.Contains("all") || name.Contains("23");
                        bool isL1 = (name.Contains("1") || name.Contains("3a")) && !isAll;

                        if (isWalk && isL1) _layerWalkL1 = cloned;
                        else if (isDrive && isL1) _layerDriveL1 = cloned;
                        else if (isWalk && isAll) _layerWalkAll = cloned;
                        else if (isDrive && isAll) _layerDriveAll = cloned;

                        cloned.IsVisible = false; cloned.Opacity = 0.6;
                        Map.OperationalLayers.Add(cloned);
                    }
                }
            }
            catch { }
        }

        private async Task CollectFeatureLayersRecursive(LayerCollection layers, List<FeatureLayer> result)
        {
            foreach (var layer in layers) { await layer.LoadAsync(); if (layer is FeatureLayer fl) result.Add(fl); else if (layer is GroupLayer gl) await CollectFeatureLayersRecursive(gl.Layers, result); }
        }

        private void HideAllMmpkLayers()
        {
            if (_layerWalkL1 != null) _layerWalkL1.IsVisible = false;
            if (_layerDriveL1 != null) _layerDriveL1.IsVisible = false;
            if (_layerWalkAll != null) _layerWalkAll.IsVisible = false;
            if (_layerDriveAll != null) _layerDriveAll.IsVisible = false;
            LegendWalkL1 = Visibility.Collapsed; LegendDriveL1 = Visibility.Collapsed;
            LegendWalkAll = Visibility.Collapsed; LegendDriveAll = Visibility.Collapsed;
        }

        private void UpdateLayerVisibility()
        {
            HideAllMmpkLayers();
            if (!_isAnalysisStarted) return;
            var target = GetCurrentLayer();
            if (target != null)
            {
                target.IsVisible = true;
                if (target == _layerWalkL1) LegendWalkL1 = Visibility.Visible;
                else if (target == _layerDriveL1) LegendDriveL1 = Visibility.Visible;
                else if (target == _layerWalkAll) LegendWalkAll = Visibility.Visible;
                else if (target == _layerDriveAll) LegendDriveAll = Visibility.Visible;
            }
            _blindSpotOverlay.Graphics.Clear();
        }

        private FeatureLayer GetCurrentLayer()
        {
            if (SelectedHospitalTypeIndex == 0) return (SelectedModeIndex == 0) ? _layerWalkL1 : _layerDriveL1;
            else return (SelectedModeIndex == 0) ? _layerWalkAll : _layerDriveAll;
        }

        private async void CheckF2Update()
        {
            if (_isAnalysisStarted) UpdateLayerVisibility();
            if (StatsPanelVisibility == Visibility.Visible) ExecuteCalcEquity(null);
        }

        private async void ExecuteCalcBlindSpot(object obj)
        {
            var layer = GetCurrentLayer();
            if (layer == null) return;
            _blindSpotOverlay.Graphics.Clear();
            try
            {
                var dGeo = await GetUnionGeometry(ShpPath, true); var sGeo = await GetUnionGeometry(layer);
                if (dGeo != null && sGeo != null)
                {
                    var blind = GeometryEngine.Difference(GeometryEngine.Project(dGeo, SpatialReferences.Wgs84), GeometryEngine.Project(sGeo, SpatialReferences.Wgs84));
                    if (blind != null && !blind.IsEmpty) _blindSpotOverlay.Graphics.Add(new Graphic(blind, new SimpleFillSymbol(SimpleFillSymbolStyle.Solid, System.Drawing.Color.FromArgb(150, 60, 60, 60), null)));
                }
            }
            catch { }
        }

        private async void ExecuteCalcEquity(object obj)
        {
            var layer = GetCurrentLayer();
            if (layer == null) return;
            StatsPanelVisibility = Visibility.Visible; DistrictStats.Clear();
            try
            {
                var sGeo = await GetUnionGeometry(layer);
                sGeo = GeometryEngine.Project(sGeo, SpatialReferences.Wgs84);
                ShapefileFeatureTable table = new ShapefileFeatureTable(ShpPath); await table.LoadAsync();
                foreach (var f in await table.QueryFeaturesAsync(new QueryParameters { WhereClause = "1=1" }))
                {
                    var dGeo = GeometryEngine.Project(f.Geometry, SpatialReferences.Wgs84);
                    var inter = GeometryEngine.Intersection(dGeo, sGeo);
                    double rate = (GeometryEngine.Area(inter) / GeometryEngine.Area(dGeo)) * 100.0;
                    Application.Current.Dispatcher.Invoke(() => DistrictStats.Add(new DistrictStat { Name = f.Attributes["Name"]?.ToString() ?? "未知", CoverageRate = rate }));
                }
            }
            catch { }
        }

        private async Task<Geometry> GetUnionGeometry(FeatureLayer layer)
        {
            var res = await layer.FeatureTable.QueryFeaturesAsync(new QueryParameters { WhereClause = "1=1" });
            return GeometryEngine.Union(res.Select(f => f.Geometry).Where(g => g != null));
        }
        private async Task<Geometry> GetUnionGeometry(string path, bool isShp)
        {
            ShapefileFeatureTable table = new ShapefileFeatureTable(path); await table.LoadAsync();
            var res = await table.QueryFeaturesAsync(new QueryParameters { WhereClause = "1=1" });
            return GeometryEngine.Union(res.Select(f => f.Geometry).Where(g => g != null));
        }

        // ================= F1 Search Logic =================
        private async Task PerformSearchAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) { SearchResults = null; return; }
            using (var db = new NanjingContext())
            {
                var hospitals = await db.Hospitals.Where(h => h.Name.Contains(query) && h.WgsLongitude != null)
                    .Select(h => new SearchResultItem { Name = h.Name, Type = "医疗机构", Lat = h.WgsLatitude.Value, Lon = h.WgsLongitude.Value, DetailInfo = $"地址:{h.Address}\n类型:{h.LevelLabel}" }).Take(5).ToListAsync();
                var communities = await db.Communities.Where(c => c.Name.Contains(query) && c.WgsLongitude != null)
                    .Select(c => new SearchResultItem { Name = c.Name, Type = "住宅小区", Lat = c.WgsLatitude.Value, Lon = c.WgsLongitude.Value, DetailInfo = $"街道:{c.Street}\n类型:{c.Type}" }).Take(5).ToListAsync();
                SearchResults = hospitals.Concat(communities).ToList();
            }
        }

        // ================= F5 急救核心逻辑 =================
        public async void SimulateRescue(MapPoint patientLocation)
        {
            if (CurrentModule != "F5") return;

            try
            {
                _rescueOverlay.Graphics.Clear();
                IsRescueDashboardVisible = Visibility.Visible;
                UpdateRescueStatus(RescueState.ToPatient, "正在计算最优急救方案...", "#FFD700");

                _patientGraphic = new Graphic(patientLocation, new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.Red, 12));
                _rescueOverlay.Graphics.Add(_patientGraphic);

                await FindNearestHospital(patientLocation);
                if (_nearestHospitalPoint == null) return;

                StatusMessage = "正在请求高德实时路况...";
                var leg1Info = await GetAmapSmartRouteAsync(_nearestHospitalPoint, patientLocation, 0);
                var leg2Info = await GetAmapSmartRouteAsync(patientLocation, _nearestHospitalPoint, 5);

                if (leg1Info == null || leg2Info == null)
                {
                    StatusMessage = "路径规划失败";
                    return;
                }

                double totalSeconds = leg1Info.TotalDuration + leg2Info.TotalDuration + 180;
                double totalKm = (leg1Info.TotalDistance + leg2Info.TotalDistance) / 1000.0;
                StatusMessage = $"方案生成: 总里程 {totalKm:F1}km | 预计总耗时 {totalSeconds / 60:F0}分钟";

                _rescueOverlay.Graphics.Add(new Graphic(leg1Info.Geometry, new SimpleLineSymbol(SimpleLineSymbolStyle.Dash, System.Drawing.Color.SkyBlue, 3)));

                var startPoint = leg1Info.SmartPoints[0].Point;
                _ambulanceGraphic = new Graphic(startPoint, new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Triangle, System.Drawing.Color.Blue, 18));
                _rescueOverlay.Graphics.Add(_ambulanceGraphic);

                _pathLeg2 = leg2Info.Geometry;
                _tempLeg2SmartPoints = leg2Info.SmartPoints;

                StartSmartRescueAnimation(leg1Info.SmartPoints, RescueState.ToPatient);
            }
            catch (Exception ex) { StatusMessage = $"模拟出错: {ex.Message}"; }
        }

        private void StartSmartRescueAnimation(List<SmartRoutePoint> smartPoints, RescueState state)
        {
            _currentRescueState = state;
            _rescueSmartPoints = smartPoints;
            _rescuePathIndex = 0;

            _rescueTimer?.Stop();
            _rescueTimer = new System.Timers.Timer(30);

            _rescueTimer.Elapsed += (s, e) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_rescuePathIndex < _rescueSmartPoints.Count - 1)
                    {
                        var currentSmartPt = _rescueSmartPoints[_rescuePathIndex];
                        var nextSmartPt = _rescueSmartPoints[_rescuePathIndex + 1];

                        _ambulanceGraphic.Geometry = currentSmartPt.Point;

                        double angle = CalculateHeading(currentSmartPt.Point, nextSmartPt.Point);
                        var symbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Triangle,
                            state == RescueState.ToPatient ? System.Drawing.Color.DodgerBlue : System.Drawing.Color.OrangeRed, 18)
                        { Angle = angle };
                        _ambulanceGraphic.Symbol = symbol;

                        double progress = (double)_rescuePathIndex / _rescueSmartPoints.Count;
                        string action = state == RescueState.ToPatient ? "🚑 赶往现场" : "🏥 紧急送医";
                        string roadInfo = string.IsNullOrEmpty(currentSmartPt.RoadName) ? "未知道路" : currentSmartPt.RoadName;
                        double displaySpeed = currentSmartPt.Speed * 3.6;

                        UpdateRescueStatus(state,
                            $"{action}\n🛣 {roadInfo} ({displaySpeed:F0} km/h)\n进度: {progress:P0}",
                            state == RescueState.ToPatient ? "#00BFFF" : "#FF4500");

                        _rescuePathIndex++;
                    }
                    else
                    {
                        _rescueTimer.Stop();
                        HandleSmartStateTransition();
                    }
                });
            };
            _rescueTimer.Start();

            if (smartPoints.Count > 0)
            {
                var pts = new PointCollection(SpatialReferences.Wgs84);
                foreach (var p in smartPoints) pts.Add(p.Point);
                RequestNavigation?.Invoke(this, new NavigationEventArgs { DistrictEnvelope = new Polyline(pts).Extent, IsDistrictZoom = true });
            }
        }

        private async void HandleSmartStateTransition()
        {
            if (_currentRescueState == RescueState.ToPatient)
            {
                UpdateRescueStatus(RescueState.OnScene, "🩹 抵达现场！正在进行急救处置...", "#32CD32");
                await Task.Delay(2000);

                _rescueOverlay.Graphics.Add(new Graphic(_pathLeg2, new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.OrangeRed, 4)));
                StartSmartRescueAnimation(_tempLeg2SmartPoints, RescueState.ToHospital);
            }
            else if (_currentRescueState == RescueState.ToHospital)
            {
                UpdateRescueStatus(RescueState.Idle, "✅ 任务完成：病人已送达医院", "#FFFFFF");
                StatusMessage = "急救任务结束，数据已归档。";
            }
        }

        private double CalculateHeading(MapPoint p1, MapPoint p2)
        {
            double dx = p2.X - p1.X;
            double dy = p2.Y - p1.Y;
            double radians = Math.Atan2(dy, dx);
            double degrees = radians * (180 / Math.PI);
            return -degrees + 90;
        }

        private void UpdateRescueStatus(RescueState state, string text, string colorHex)
        {
            RescueDashboardText = text;
            RescueDashboardColor = colorHex;
        }

        private async Task<RouteResultInfo> GetAmapSmartRouteAsync(MapPoint start, MapPoint end, int strategy)
        {
            using (var client = new HttpClient())
            {
                string startStr = $"{start.X:F6},{start.Y:F6}";
                string endStr = $"{end.X:F6},{end.Y:F6}";
                string url = $"https://restapi.amap.com/v3/direction/driving?origin={startStr}&destination={endStr}&strategy={strategy}&key={AmapKey}";

                try
                {
                    var jsonStr = await client.GetStringAsync(url);
                    // ★★★ 修复 CS0103: JsonConvert 已正确引用 ★★★
                    var data = JsonConvert.DeserializeObject<AmapRouteResponse>(jsonStr);

                    if (data?.status == "1" && data.route?.paths?.Count > 0)
                    {
                        var pathData = data.route.paths[0];
                        var result = new RouteResultInfo
                        {
                            TotalDistance = double.Parse(pathData.distance),
                            TotalDuration = double.Parse(pathData.duration),
                            SmartPoints = new List<SmartRoutePoint>()
                        };

                        var allPoints = new PointCollection(SpatialReferences.Wgs84);

                        foreach (var step in pathData.steps)
                        {
                            double stepDist = double.Parse(step.distance);
                            double stepTime = double.Parse(step.duration);
                            double stepSpeed = stepTime > 0 ? stepDist / stepTime : 10;

                            var polylineStr = step.polyline.Split(';');
                            foreach (var pStr in polylineStr)
                            {
                                var xy = pStr.Split(',');
                                if (xy.Length >= 2)
                                {
                                    double gcjLon = double.Parse(xy[0]);
                                    double gcjLat = double.Parse(xy[1]);
                                    var wgsPt = CoordTransform.GCJ02ToWGS84(gcjLon, gcjLat);

                                    allPoints.Add(wgsPt);
                                    result.SmartPoints.Add(new SmartRoutePoint
                                    {
                                        Point = wgsPt,
                                        Speed = stepSpeed,
                                        RoadName = step.road
                                    });
                                }
                            }
                        }
                        result.Geometry = new Polyline(allPoints);
                        return result;
                    }
                }
                catch { }
            }
            return null;
        }

        // ================= F4 核心实现 (高德API版) =================
        private async void LoadF4DataAsync()
        {
            try
            {
                await LoadF4CommunityPointsAsync();
                await LoadF4Level1HospitalPointsAsync();
            }
            catch (Exception ex) { StatusMessage = $"F4 数据加载失败: {ex.Message}"; }
        }

        private async Task LoadF4CommunityPointsAsync()
        {
            using (var db = new NanjingContext())
            {
                var communities = await db.Communities.Where(c => c.WgsLongitude != null).ToListAsync();
                _f4CommunityOverlay.Graphics.Clear();
                var sym = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.FromArgb(150, 30, 144, 255), 6);
                foreach (var c in communities) _f4CommunityOverlay.Graphics.Add(new Graphic(new MapPoint(c.WgsLongitude.Value, c.WgsLatitude.Value, SpatialReferences.Wgs84), sym));
            }
        }

        private async Task LoadF4Level1HospitalPointsAsync()
        {
            using (var db = new NanjingContext())
            {
                var hospitals = await db.Hospitals.Where(h => h.Level == 1 && h.WgsLongitude != null).ToListAsync();
                _f4Level1HospitalOverlay.Graphics.Clear();
                var sym = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Cross, System.Drawing.Color.FromArgb(200, 255, 0, 0), 10);
                foreach (var h in hospitals) _f4Level1HospitalOverlay.Graphics.Add(new Graphic(new MapPoint(h.WgsLongitude.Value, h.WgsLatitude.Value, SpatialReferences.Wgs84), sym));
            }
        }

        private async Task PerformF4SearchAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) { F4SearchResults = null; return; }
            using (var db = new NanjingContext())
            {
                F4SearchResults = await db.Communities.Where(c => c.Name.Contains(query) && c.WgsLongitude != null)
                    .Select(c => new SearchResultItem { Name = c.Name, Type = "住宅小区", Lat = c.WgsLatitude.Value, Lon = c.WgsLongitude.Value, DetailInfo = $"街道:{c.Street}" })
                    .Take(5).ToListAsync();
            }
        }

        private async void HandleF4CommunitySelection(SearchResultItem item)
        {
            if (item == null) return;
            _f4StartPoint = new MapPoint(item.Lon, item.Lat, SpatialReferences.Wgs84);
            RequestNavigation?.Invoke(this, new NavigationEventArgs { Center = _f4StartPoint, Scale = 8000, ResultItem = item });
            CurrentDay = 0;
            UpdateDiffusionBuffer();
            await FindNearestHospital(_f4StartPoint);
            StatusMessage = $"已锁定小区 [{item.Name}]，F4 模块就绪";
            F4SearchResults = null;
            F4SearchText = item.Name;
        }

        private void UpdateDiffusionBuffer()
        {
            try
            {
                _diffusionOverlay.Graphics.Clear();
                if (_f4StartPoint == null || CurrentDay < 0) return;

                double currentRadius = InitialRadius + (CurrentDay * DailyIncrement);
                if (currentRadius > 10000) currentRadius = 10000;

                var centerProj = GeometryEngine.Project(_f4StartPoint, SpatialReferences.WebMercator) as MapPoint;
                var bufferProj = GeometryEngine.Buffer(centerProj, currentRadius);
                var bufferWgs84 = GeometryEngine.Project(bufferProj, SpatialReferences.Wgs84);
                _lastNeighborhoodBuffer = bufferWgs84;

                int greenAndBlue = Math.Min((int)((CurrentDay / 7.0) * 200), 200);
                byte alpha = (byte)Math.Max(50, 150 - (CurrentDay * 14));
                var color = System.Drawing.Color.FromArgb(alpha, 255, greenAndBlue, greenAndBlue);
                var borderColor = System.Drawing.Color.FromArgb(255, 255, greenAndBlue, greenAndBlue);

                var fillSymbol = new SimpleFillSymbol(SimpleFillSymbolStyle.Solid, color, new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, borderColor, 2));
                _diffusionOverlay.Graphics.Add(new Graphic(bufferWgs84, fillSymbol));
                _diffusionOverlay.Graphics.Add(new Graphic(_f4StartPoint, new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.Red, 8)));

                DiffusionStatus = $"Day {CurrentDay}: 覆盖半径 {currentRadius}米";
            }
            catch { }
        }

        private async Task FindNearestHospital(MapPoint startPoint)
        {
            try
            {
                using (var db = new NanjingContext())
                {
                    var hospitals = await db.Hospitals.Where(h => h.Level == 1 && h.WgsLongitude != null).ToListAsync();
                    if (!hospitals.Any()) { NearestHospitalInfo = "无三甲医院"; return; }

                    Hospital nearest = null;
                    double minDis = double.MaxValue;
                    MapPoint nearestPt = null;

                    foreach (var h in hospitals)
                    {
                        var pt = new MapPoint(h.WgsLongitude.Value, h.WgsLatitude.Value, SpatialReferences.Wgs84);
                        double d = GeometryEngine.Distance(startPoint, pt);
                        if (d < minDis) { minDis = d; nearest = h; nearestPt = pt; }
                    }

                    if (nearest != null)
                    {
                        NearestHospitalInfo = $"目标医院: {nearest.Name}\n距离: {minDis * 111:F1} km";
                        _nearestHospitalPoint = nearestPt;
                    }
                }
            }
            catch { }
        }

        private void SwitchF4Mode(object parameter)
        {
            string mode = parameter as string;
            CurrentF4Mode = mode;
        }

        private void UpdateF4ModeVisibility()
        {
            IsNeighborhoodModeVisible = CurrentF4Mode == "Neighborhood" ? Visibility.Visible : Visibility.Collapsed;
            IsNetworkModeVisible = CurrentF4Mode == "Network" ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void ExecuteStartNetworkSimulation(object obj)
        {
            if (_f4StartPoint == null || _nearestHospitalPoint == null)
            {
                StatusMessage = "请先在上方搜索并选择一个小区";
                return;
            }

            try
            {
                StatusMessage = "正在请求高德API多策略路径...";
                _networkDiffusionOverlay.Graphics.Clear();

                // ★★★ 修复 CS1579: int[] 数组定义正确 ★★★
                int[] strategies = new int[] { 0, 2, 5 };
                var allGeometries = new List<Geometry>();

                foreach (var strategy in strategies)
                {
                    var routeInfo = await GetAmapSmartRouteAsync(_f4StartPoint, _nearestHospitalPoint, strategy);
                    if (routeInfo != null && !routeInfo.Geometry.IsEmpty)
                    {
                        allGeometries.Add(routeInfo.Geometry);
                        var routeSymbol = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.FromArgb(120, 255, 69, 0), 4);
                        _networkDiffusionOverlay.Graphics.Add(new Graphic(routeInfo.Geometry, routeSymbol));
                    }
                }

                if (allGeometries.Count == 0)
                {
                    StatusMessage = "路径规划失败，请检查Key或网络";
                    return;
                }

                var unionRoute = GeometryEngine.Union(allGeometries);
                var routeProj = GeometryEngine.Project(unionRoute, SpatialReferences.WebMercator);
                var corridorProj = GeometryEngine.Buffer(routeProj, CorridorWidth);
                var corridorWgs84 = GeometryEngine.Project(corridorProj, SpatialReferences.Wgs84);

                _lastNetworkCorridor = corridorWgs84;

                var corridorSymbol = new SimpleFillSymbol(SimpleFillSymbolStyle.Solid, System.Drawing.Color.FromArgb(40, 255, 165, 0), null);
                _networkDiffusionOverlay.Graphics.Insert(0, new Graphic(corridorWgs84, corridorSymbol));

                StatusMessage = $"模拟完成：融合 {allGeometries.Count} 条真实路径";
                // ★★★ 修复 CS0120: 使用事件代替直接调用 MapView ★★★
                RequestNavigation?.Invoke(this, new NavigationEventArgs { DistrictEnvelope = corridorWgs84.Extent, IsDistrictZoom = true });
            }
            catch (Exception ex) { StatusMessage = $"模拟出错: {ex.Message}"; }
        }

        private void ExecuteClearNetworkGraphics(object obj)
        {
            _networkDiffusionOverlay.Graphics.Clear();
            _lastNetworkCorridor = null;
        }

        private async void ExecuteGenerateLockdown(object obj)
        {
            if (_lastNeighborhoodBuffer == null && _lastNetworkCorridor == null)
            {
                StatusMessage = "无数据：请先运行邻域或网络模拟";
                return;
            }

            try
            {
                StatusMessage = "计算封控方案 (区分高/中低风险)...";
                _lockdownOverlay.Graphics.Clear();
                _lockdownOverlay.IsVisible = true;

                var list = new List<Geometry>();
                if (_lastNeighborhoodBuffer != null) list.Add(_lastNeighborhoodBuffer);
                if (_lastNetworkCorridor != null) list.Add(_lastNetworkCorridor);

                var coreZone = GeometryEngine.Union(list);

                var coreSymbol = new SimpleFillSymbol(SimpleFillSymbolStyle.DiagonalCross, System.Drawing.Color.FromArgb(60, 255, 0, 0),
                    new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.Red, 3));
                _lockdownOverlay.Graphics.Add(new Graphic(coreZone, coreSymbol));

                var coreProj = GeometryEngine.Project(coreZone, SpatialReferences.WebMercator);
                var preventProj = GeometryEngine.Buffer(coreProj, 500);
                var preventZone = GeometryEngine.Project(preventProj, SpatialReferences.Wgs84);
                var preventRing = GeometryEngine.Difference(preventZone, coreZone);

                var preventSymbol = new SimpleFillSymbol(SimpleFillSymbolStyle.Solid, System.Drawing.Color.FromArgb(50, 255, 165, 0),
                    new SimpleLineSymbol(SimpleLineSymbolStyle.Dash, System.Drawing.Color.Orange, 2));
                _lockdownOverlay.Graphics.Add(new Graphic(preventRing, preventSymbol));

                StatusMessage = "✅ 决策方案：红色高风险区 + 黄色中低风险区";

                if (preventZone != null)
                {
                    RequestNavigation?.Invoke(this, new NavigationEventArgs { DistrictEnvelope = preventZone.Extent, IsDistrictZoom = true });
                }
            }
            catch (Exception ex) { StatusMessage = $"封控生成失败: {ex.Message}"; }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}