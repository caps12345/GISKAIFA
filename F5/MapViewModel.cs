using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.UI;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using WpfMapApp2.Models;
using WpfMapApp2.Utils;

namespace WpfMapApp2
{
    // ================= 辅助类 =================
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
        public double BarWidth => CoverageRate * 1.8;
        public string BarColor => CoverageRate < 50 ? "#FF5252" : (CoverageRate < 80 ? "#FFC107" : "#4CAF50");
    }

    public class NearestPathResult
    {
        public string TargetHospitalName { get; set; }
        public string DistanceText { get; set; }
        public string TypeName { get; set; }
        public string ColorCode { get; set; }
    }

    public class OptimizationResultItem
    {
        public string Title { get; set; }
        public string LocationDesc { get; set; }
        public string PopulationDesc { get; set; }
        public string Suggestion { get; set; }
        public MapPoint CenterPoint { get; set; }
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

    // ================= MapViewModel =================
    public class MapViewModel : INotifyPropertyChanged
    {
        // ★★★ 路径配置 (请确认路径正确) ★★★
        private const string ShpPath = @"D:\GIS_DATA\Data\南京区县.shp";
        private const string MmpkPath = @"D:\GIS_DATA\Data\nanjing.mmpk";
        private const string WaterShpPath = @"D:\GIS_DATA\Data\南京市水系.shp";
        private const string PathShpL1 = @"D:\GIS_DATA\Data\nearest_level1.shp";
        private const string PathShpL2 = @"D:\GIS_DATA\Data\nearest_level2.shp";
        private const string PathShpL3 = @"D:\GIS_DATA\Data\nearest_level3.shp";
        private const string RoadNetworkPath = @"D:\GIS_DATA\Data\road_project.shp";

        private Map _map;
        private string _statusMessage = "系统初始化...";

        // 核心变量
        private Geometry _cachedBlindSpot = null;

        public GraphicsOverlayCollection GraphicsOverlays { get; set; } = new GraphicsOverlayCollection();

        private GraphicsOverlay _blindSpotOverlay = new GraphicsOverlay { Id = "BlindSpot" };
        private GraphicsOverlay _level1Overlay = new GraphicsOverlay { Id = "Level1Hospitals" };
        private GraphicsOverlay _highLevelOverlay = new GraphicsOverlay { Id = "HighLevelHospitals", MinScale = 50000 };
        private GraphicsOverlay _routeOverlay = new GraphicsOverlay { Id = "RouteOverlay" };
        private GraphicsOverlay _f4CommunityOverlay = new GraphicsOverlay { Id = "F4Communities" };
        private GraphicsOverlay _f4Level1HospitalOverlay = new GraphicsOverlay { Id = "F4Level1Hospitals" };
        private GraphicsOverlay _diffusionOverlay = new GraphicsOverlay { Id = "DiffusionOverlay" };
        private GraphicsOverlay _networkDiffusionOverlay = new GraphicsOverlay { Id = "NetworkDiffusion" };
        private GraphicsOverlay _f5SiteOverlay = new GraphicsOverlay { Id = "F5Sites" };

        public Map Map { get => _map; set { _map = value; OnPropertyChanged(); } }
        public string StatusMessage { get => _statusMessage; set { _statusMessage = value; OnPropertyChanged(); } }

        // 显隐控制
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
        private Visibility _isF5ConfigVisible = Visibility.Collapsed;
        public Visibility IsF5ConfigVisible { get => _isF5ConfigVisible; set { _isF5ConfigVisible = value; OnPropertyChanged(); } }

        public bool IsF1Active { get; set; } = false;
        private string _currentModule = "F1";
        public string CurrentModule { get => _currentModule; set { _currentModule = value; OnPropertyChanged(); } }

        // F1
        private int _searchTypeIndex = 0;
        public int SearchTypeIndex { get => _searchTypeIndex; set { _searchTypeIndex = value; OnPropertyChanged(); } }
        private bool _isPathLegendVisible = false;
        public bool IsPathLegendVisible { get => _isPathLegendVisible; set { _isPathLegendVisible = value; OnPropertyChanged(); } }
        private string _hospitalSearchText;
        public string HospitalSearchText { get => _hospitalSearchText; set { _hospitalSearchText = value; OnPropertyChanged(); if (!string.IsNullOrWhiteSpace(value)) _ = SearchHospitalsAsync(value); } }
        private List<SearchResultItem> _hospitalSearchResults;
        public List<SearchResultItem> HospitalSearchResults { get => _hospitalSearchResults; set { _hospitalSearchResults = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsShowingHospitalResults)); } }
        public bool IsShowingHospitalResults => HospitalSearchResults?.Any() == true && !string.IsNullOrWhiteSpace(HospitalSearchText);
        private SearchResultItem _selectedHospital;
        public SearchResultItem SelectedHospital { get => _selectedHospital; set { _selectedHospital = value; OnPropertyChanged(); if (value != null) { ZoomToLocation(value); HospitalSearchResults = null; _hospitalSearchText = value.Name; OnPropertyChanged(nameof(HospitalSearchText)); } } }
        private string _communitySearchText;
        public string CommunitySearchText { get => _communitySearchText; set { _communitySearchText = value; OnPropertyChanged(); if (!string.IsNullOrWhiteSpace(value)) _ = SearchCommunitiesAsync(value); } }
        private List<SearchResultItem> _communitySearchResults;
        public List<SearchResultItem> CommunitySearchResults { get => _communitySearchResults; set { _communitySearchResults = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsShowingCommunityResults)); } }
        public bool IsShowingCommunityResults => CommunitySearchResults?.Any() == true && !string.IsNullOrWhiteSpace(CommunitySearchText);
        private SearchResultItem _selectedCommunity;
        public SearchResultItem SelectedCommunity { get => _selectedCommunity; set { _selectedCommunity = value; OnPropertyChanged(); if (value != null) { ZoomToLocation(value); _ = QueryPathsForCommunityAsync(value.Name); CommunitySearchResults = null; _communitySearchText = value.Name; OnPropertyChanged(nameof(CommunitySearchText)); } } }
        private ObservableCollection<NearestPathResult> _nearestPaths = new ObservableCollection<NearestPathResult>();
        public ObservableCollection<NearestPathResult> NearestPaths { get => _nearestPaths; set { _nearestPaths = value; OnPropertyChanged(); } }
        private string _selectedDistrict;
        public string SelectedDistrict { get => _selectedDistrict; set { _selectedDistrict = value; OnPropertyChanged(); if (!string.IsNullOrEmpty(value)) ZoomToDistrict(value); } }
        private readonly Dictionary<string, Viewpoint> _districtViewpoints = new Dictionary<string, Viewpoint> { { "全南京市", new Viewpoint(new Envelope(118.3, 31.2, 119.2, 32.6, SpatialReferences.Wgs84)) }, { "玄武区", new Viewpoint(new Envelope(118.78, 32.03, 118.92, 32.12, SpatialReferences.Wgs84)) }, { "秦淮区", new Viewpoint(new Envelope(118.75, 31.98, 118.88, 32.05, SpatialReferences.Wgs84)) }, { "建邺区", new Viewpoint(new Envelope(118.67, 31.95, 118.78, 32.05, SpatialReferences.Wgs84)) }, { "鼓楼区", new Viewpoint(new Envelope(118.70, 32.03, 118.80, 32.12, SpatialReferences.Wgs84)) }, { "浦口区", new Viewpoint(new Envelope(118.30, 31.90, 118.75, 32.25, SpatialReferences.Wgs84)) }, { "栖霞区", new Viewpoint(new Envelope(118.78, 32.08, 119.25, 32.25, SpatialReferences.Wgs84)) }, { "雨花台区", new Viewpoint(new Envelope(118.65, 31.90, 118.85, 32.02, SpatialReferences.Wgs84)) }, { "江宁区", new Viewpoint(new Envelope(118.55, 31.60, 119.15, 32.10, SpatialReferences.Wgs84)) }, { "六合区", new Viewpoint(new Envelope(118.60, 32.15, 119.10, 32.60, SpatialReferences.Wgs84)) }, { "溧水区", new Viewpoint(new Envelope(118.85, 31.35, 119.25, 31.85, SpatialReferences.Wgs84)) }, { "高淳区", new Viewpoint(new Envelope(118.75, 31.15, 119.15, 31.55, SpatialReferences.Wgs84)) } };
        public List<string> Districts => _districtViewpoints.Keys.ToList();
        private int _level1Count; public int Level1Count { get => _level1Count; set { _level1Count = value; OnPropertyChanged(); } }
        private int _level2Count; public int Level2Count { get => _level2Count; set { _level2Count = value; OnPropertyChanged(); } }

        // F2
        public ObservableCollection<DistrictStat> DistrictStats { get; set; } = new ObservableCollection<DistrictStat>();
        private FeatureLayer _layerWalkL1, _layerDriveL1, _layerWalkAll, _layerDriveAll;
        private bool _isAnalysisStarted = false;
        private int _selectedModeIndex = 0;
        private int _selectedHospitalTypeIndex = 0;
        private Visibility _legendWalkL1 = Visibility.Collapsed; private Visibility _legendDriveL1 = Visibility.Collapsed; private Visibility _legendWalkAll = Visibility.Collapsed; private Visibility _legendDriveAll = Visibility.Collapsed;
        public Visibility LegendWalkL1 { get => _legendWalkL1; set { _legendWalkL1 = value; OnPropertyChanged(); } }
        public Visibility LegendDriveL1 { get => _legendDriveL1; set { _legendDriveL1 = value; OnPropertyChanged(); } }
        public Visibility LegendWalkAll { get => _legendWalkAll; set { _legendWalkAll = value; OnPropertyChanged(); } }
        public Visibility LegendDriveAll { get => _legendDriveAll; set { _legendDriveAll = value; OnPropertyChanged(); } }
        public int SelectedModeIndex { get => _selectedModeIndex; set { _selectedModeIndex = value; OnPropertyChanged(); CheckF2Update(); } }
        public int SelectedHospitalTypeIndex { get => _selectedHospitalTypeIndex; set { _selectedHospitalTypeIndex = value; OnPropertyChanged(); CheckF2Update(); } }

        // F4
        private string _currentF4Mode = "Neighborhood";
        private Visibility _isNeighborhoodModeVisible = Visibility.Visible; private Visibility _isNetworkModeVisible = Visibility.Collapsed;
        public string CurrentF4Mode { get => _currentF4Mode; set { _currentF4Mode = value; OnPropertyChanged(); UpdateF4ModeVisibility(); } }
        public Visibility IsNeighborhoodModeVisible { get => _isNeighborhoodModeVisible; set { _isNeighborhoodModeVisible = value; OnPropertyChanged(); } }
        public Visibility IsNetworkModeVisible { get => _isNetworkModeVisible; set { _isNetworkModeVisible = value; OnPropertyChanged(); } }
        private bool _isDiffusionActive = false; public bool IsDiffusionActive { get => _isDiffusionActive; set { _isDiffusionActive = value; OnPropertyChanged(); } }
        private string _diffusionSearchText; public string DiffusionSearchText { get => _diffusionSearchText; set { _diffusionSearchText = value; OnPropertyChanged(); } }
        private List<SearchResultItem> _diffusionResults; public List<SearchResultItem> DiffusionResults { get => _diffusionResults; set { _diffusionResults = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsShowingDiffusionResults)); } }
        public bool IsShowingDiffusionResults => DiffusionResults?.Any() == true && !string.IsNullOrWhiteSpace(DiffusionSearchText);
        private SearchResultItem _selectedDiffusionResult; public SearchResultItem SelectedDiffusionResult { get => _selectedDiffusionResult; set { _selectedDiffusionResult = value; OnPropertyChanged(); if (value != null) SelectDiffusionStartPoint(value); } }
        private int _currentDay = 0; public int CurrentDay { get => _currentDay; set { if (_currentDay != value) { _currentDay = value; OnPropertyChanged(); UpdateDiffusionBuffer(); } } }
        private double _initialRadius = 500; public double InitialRadius { get => _initialRadius; set { if (_initialRadius != value) { _initialRadius = value; OnPropertyChanged(); UpdateDiffusionBuffer(); } } }
        private double _dailyIncrement = 200; public double DailyIncrement { get => _dailyIncrement; set { if (_dailyIncrement != value) { _dailyIncrement = value; OnPropertyChanged(); UpdateDiffusionBuffer(); } } }
        private string _diffusionStatus = "准备就绪"; public string DiffusionStatus { get => _diffusionStatus; set { _diffusionStatus = value; OnPropertyChanged(); } }
        private string _networkStartSearchText; public string NetworkStartSearchText { get => _networkStartSearchText; set { _networkStartSearchText = value; OnPropertyChanged(); } }
        private List<SearchResultItem> _networkStartResults; public List<SearchResultItem> NetworkStartResults { get => _networkStartResults; set { _networkStartResults = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsShowingNetworkStartResults)); } }
        public bool IsShowingNetworkStartResults => NetworkStartResults?.Any() == true && !string.IsNullOrWhiteSpace(NetworkStartSearchText);
        private SearchResultItem _selectedNetworkStartResult; public SearchResultItem SelectedNetworkStartResult { get => _selectedNetworkStartResult; set { _selectedNetworkStartResult = value; OnPropertyChanged(); if (value != null) HandleNetworkStartSelection(value); } }
        private string _nearestHospitalInfo = "未选择起始小区"; public string NearestHospitalInfo { get => _nearestHospitalInfo; set { _nearestHospitalInfo = value; OnPropertyChanged(); } }
        private double _corridorWidth = 300; public double CorridorWidth { get => _corridorWidth; set { _corridorWidth = value; OnPropertyChanged(); } }
        private int _animationSpeed = 100; public int AnimationSpeed { get => _animationSpeed; set { _animationSpeed = value; OnPropertyChanged(); } }
        private MapPoint _diffusionStartPoint = null; private MapPoint _networkStartPoint = null; private MapPoint _nearestHospitalPoint = null;
        private FeatureLayer _roadNetworkLayer = null; private System.Timers.Timer _networkAnimationTimer = null; private List<Feature> _selectedRoads = new List<Feature>();
        private bool _isNetworkSimulationRunning = false;

        // F5
        public ObservableCollection<OptimizationResultItem> OptimizationResults { get; set; } = new ObservableCollection<OptimizationResultItem>();

        // Commands
        public ICommand HospitalSearchCommand { get; }
        public ICommand CommunitySearchCommand { get; }
        public ICommand LoadAnalysisCommand { get; }
        public ICommand CalcBlindSpotCommand { get; }
        public ICommand CalcEquityCommand { get; }
        public ICommand SwitchModuleCommand { get; }
        public ICommand StartDiffusionCommand { get; }
        public ICommand DiffusionSearchCommand { get; }
        public ICommand UpdateDiffusionCommand { get; }
        public ICommand SwitchF4ModeCommand { get; }
        public ICommand NetworkSearchStartCommand { get; }
        public ICommand StartNetworkSimulationCommand { get; }
        public ICommand StopNetworkSimulationCommand { get; }
        public ICommand ClearNetworkGraphicsCommand { get; }
        public ICommand StartOptimizationCommand { get; }
        public ICommand ZoomToSiteCommand { get; }

        public event EventHandler<NavigationEventArgs> RequestNavigation;

        public MapViewModel()
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            GraphicsOverlays.Add(_blindSpotOverlay); GraphicsOverlays.Add(_level1Overlay); GraphicsOverlays.Add(_highLevelOverlay); GraphicsOverlays.Add(_routeOverlay);
            GraphicsOverlays.Add(_diffusionOverlay); GraphicsOverlays.Add(_f4CommunityOverlay); GraphicsOverlays.Add(_f4Level1HospitalOverlay); GraphicsOverlays.Add(_networkDiffusionOverlay);
            GraphicsOverlays.Add(_f5SiteOverlay);

            HospitalSearchCommand = new RelayCommand(async () => { SelectedHospital = null; await SearchHospitalsAsync(HospitalSearchText); OnPropertyChanged(nameof(IsShowingHospitalResults)); });
            CommunitySearchCommand = new RelayCommand(async () => { SelectedCommunity = null; await SearchCommunitiesAsync(CommunitySearchText); OnPropertyChanged(nameof(IsShowingCommunityResults)); });
            LoadAnalysisCommand = new RelayCommand(ExecuteLoadAnalysis);
            CalcBlindSpotCommand = new RelayCommand(ExecuteCalcBlindSpot);
            CalcEquityCommand = new RelayCommand(ExecuteCalcEquity);
            SwitchModuleCommand = new RelayCommand(ExecuteSwitchModule);
            StartDiffusionCommand = new RelayCommand(ExecuteStartDiffusion);
            DiffusionSearchCommand = new RelayCommand(async () => await PerformDiffusionSearchAsync(DiffusionSearchText));
            UpdateDiffusionCommand = new RelayCommand(ExecuteUpdateDiffusion);
            SwitchF4ModeCommand = new RelayCommand(SwitchF4Mode);
            NetworkSearchStartCommand = new RelayCommand(async () => await PerformNetworkStartSearchAsync(NetworkStartSearchText));
            StartNetworkSimulationCommand = new RelayCommand(ExecuteStartNetworkSimulation);
            StopNetworkSimulationCommand = new RelayCommand(ExecuteStopNetworkSimulation);
            ClearNetworkGraphicsCommand = new RelayCommand(ExecuteClearNetworkGraphics);
            StartOptimizationCommand = new RelayCommand(ExecuteStartOptimization);

            // ★★★ 2. 修改：定位缩放比例设为 50000 (放大视野) ★★★
            ZoomToSiteCommand = new RelayCommand(obj => { if (obj is MapPoint pt) RequestNavigation?.Invoke(this, new NavigationEventArgs { Center = pt, Scale = 50000 }); });

            InitializeMap();
        }

        private void CheckF2Update() { if (_isAnalysisStarted) UpdateLayerVisibility(); if (StatsPanelVisibility == Visibility.Visible) ExecuteCalcEquity(null); }

        private async void ExecuteSwitchModule(object parameter)
        {
            string module = parameter as string; CurrentModule = module; LeftPanelVisibility = Visibility.Visible;
            if (module != "F4") { _f4CommunityOverlay.Graphics.Clear(); _f4Level1HospitalOverlay.Graphics.Clear(); _f4CommunityOverlay.IsVisible = false; _f4Level1HospitalOverlay.IsVisible = false; _diffusionOverlay.IsVisible = false; _networkDiffusionOverlay.IsVisible = false; }
            if (module != "F5") { _f5SiteOverlay.IsVisible = false; }
            IsF1ConfigVisible = Visibility.Collapsed; IsF2ConfigVisible = Visibility.Collapsed; IsF4ConfigVisible = Visibility.Collapsed; IsF5ConfigVisible = Visibility.Collapsed;
            F1StatsVisibility = Visibility.Collapsed; StatsPanelVisibility = Visibility.Collapsed;
            _level1Overlay.IsVisible = false; _highLevelOverlay.IsVisible = false; _blindSpotOverlay.IsVisible = false; _routeOverlay.IsVisible = false;
            HideAllMmpkLayers(); IsPathLegendVisible = false;

            if (module == "F1") { IsF1Active = true; StatusMessage = "F1 资源感知: 医院点位模式"; IsF1ConfigVisible = Visibility.Visible; _level1Overlay.IsVisible = true; _highLevelOverlay.IsVisible = true; _routeOverlay.IsVisible = true; F1StatsVisibility = Visibility.Visible; }
            else if (module == "F2") { IsF1Active = false; StatusMessage = "F2 可达性分析: 请加载分析"; IsF2ConfigVisible = Visibility.Visible; _blindSpotOverlay.IsVisible = true; if (_isAnalysisStarted) UpdateLayerVisibility(); }
            else if (module == "F3") { IsF1Active = false; StatusMessage = "F3 压力监测 (开发中)"; }
            else if (module == "F4") { IsF1Active = false; StatusMessage = "F4 风险模拟: 邻域扩散模式"; IsF4ConfigVisible = Visibility.Visible; _diffusionOverlay.IsVisible = true; _f4CommunityOverlay.IsVisible = true; _f4Level1HospitalOverlay.IsVisible = true; _networkDiffusionOverlay.IsVisible = true; _f4CommunityOverlay.MinScale = 50000; _f4Level1HospitalOverlay.MinScale = 0; LoadF4DataAsync(); IsDiffusionActive = true; _diffusionStartPoint = null; _diffusionOverlay.Graphics.Clear(); CurrentDay = 0; DiffusionStatus = "选择初始小区开始模拟"; CurrentF4Mode = "Neighborhood"; }
            else if (module == "F5")
            {
                IsF1Active = false;
                StatusMessage = "F5 决策支持: 智能选址优化";
                IsF5ConfigVisible = Visibility.Visible;
                _f5SiteOverlay.IsVisible = true;

                // ★★★ 1. 修改：F5 强制锁定为 驾车 + 全等级 ★★★
                if (!_isAnalysisStarted) await ExecuteLoadAnalysisAndFindDriveAllAsync();

                SelectedModeIndex = 1; // 驾车 (1)
                SelectedHospitalTypeIndex = 1; // 全等级 (1)

                // 确保 F2 图层更新到对应状态
                UpdateLayerVisibility();
                _blindSpotOverlay.IsVisible = true;

                // 计算或复用盲区
                if (_cachedBlindSpot != null)
                {
                    _blindSpotOverlay.Graphics.Clear();
                    _blindSpotOverlay.Graphics.Add(new Graphic(_cachedBlindSpot, new SimpleFillSymbol(SimpleFillSymbolStyle.Solid, System.Drawing.Color.FromArgb(150, 60, 60, 60), null)));
                    StatusMessage = "F5 就绪: 已复用驾车+全等级盲区";
                }
                else
                {
                    ExecuteCalcBlindSpot(null); // 触发通用计算
                    StatusMessage = "F5 就绪: 正在计算驾车+全等级盲区 (剔除水体)...";
                }
            }
        }

        private async void InitializeMap() { try { string token = "96cd361c8473c7c2d2c96bd05c598a2c"; string vecUrl = @"http://t0.tianditu.gov.cn/vec_w/wmts?SERVICE=WMTS&REQUEST=GetTile&VERSION=1.0.0&LAYER=vec&STYLE=default&TILEMATRIXSET=w&FORMAT=tiles&TILEMATRIX={level}&TILEROW={row}&TILECOL={col}&tk=" + token; string cvaUrl = @"http://t0.tianditu.gov.cn/cva_w/wmts?SERVICE=WMTS&REQUEST=GetTile&VERSION=1.0.0&LAYER=cva&STYLE=default&TILEMATRIXSET=w&FORMAT=tiles&TILEMATRIX={level}&TILEROW={row}&TILECOL={col}&tk=" + token; WebTiledLayer baseLayer = new WebTiledLayer(vecUrl, new List<string> { "0", "1", "2", "3", "4", "5", "6", "7" }); WebTiledLayer labelLayer = new WebTiledLayer(cvaUrl, new List<string> { "0", "1", "2", "3", "4", "5", "6", "7" }); Basemap myBasemap = new Basemap(baseLayer); myBasemap.BaseLayers.Add(labelLayer); Map = new Map(myBasemap); Map.InitialViewpoint = new Viewpoint(32.060, 118.796, 150000); await AddNanjingLayerAsync(); await LoadMmpkLayers(); await AddHospitalPointsAsync(); await AddHighLevelHospitalsAsync(); ExecuteSwitchModule("F1"); } catch (Exception ex) { StatusMessage = $"初始化失败: {ex.Message}"; } }
        // F1 Methods
        private async Task SearchHospitalsAsync(string query) { if (string.IsNullOrWhiteSpace(query)) { HospitalSearchResults = null; return; } using (var db = new NanjingContext()) { var hospitals = await db.Hospitals.Where(h => h.Name.Contains(query) && h.WgsLongitude != null).Select(h => new SearchResultItem { Name = h.Name, Type = "医疗机构", Lat = h.WgsLatitude.Value, Lon = h.WgsLongitude.Value, DetailInfo = $"地址:{h.Address}\n类型:{h.LevelLabel}" }).Take(10).ToListAsync(); HospitalSearchResults = hospitals; } }
        private async Task SearchCommunitiesAsync(string query) { if (string.IsNullOrWhiteSpace(query)) { CommunitySearchResults = null; return; } using (var db = new NanjingContext()) { var communities = await db.Communities.Where(c => c.Name.Contains(query) && c.WgsLongitude != null).Select(c => new SearchResultItem { Name = c.Name, Type = "住宅小区", Lat = c.WgsLatitude.Value, Lon = c.WgsLongitude.Value, DetailInfo = $"街道:{c.Street}\n类型:{c.Type}" }).Take(10).ToListAsync(); CommunitySearchResults = communities; } }
        private async Task QueryPathsForCommunityAsync(string communityName) { _routeOverlay.Graphics.Clear(); NearestPaths.Clear(); IsPathLegendVisible = false; StatusMessage = $"计算 {communityName} 最优就医路径..."; await QuerySinglePathAsync(PathShpL1, communityName, "三甲医院", System.Drawing.Color.FromArgb(255, 255, 50, 50), 6.0); await QuerySinglePathAsync(PathShpL2, communityName, "综合医院", System.Drawing.Color.FromArgb(255, 0, 191, 255), 4.0); await QuerySinglePathAsync(PathShpL3, communityName, "诊所/卫生所", System.Drawing.Color.FromArgb(255, 0, 255, 0), 2.0); if (NearestPaths.Count > 0) { StatusMessage = $"已显示 {NearestPaths.Count} 条路网路径"; IsPathLegendVisible = true; } else StatusMessage = "未找到该小区的预计算路径数据"; }
        private async Task QuerySinglePathAsync(string shpPath, string communityName, string typeName, System.Drawing.Color color, double width) { if (!File.Exists(shpPath)) return; try { ShapefileFeatureTable table = new ShapefileFeatureTable(shpPath); await table.LoadAsync(); var queryParams = new QueryParameters { WhereClause = $"小区名 = '{communityName}'" }; var results = await table.QueryFeaturesAsync(queryParams); var feature = results.FirstOrDefault(); if (feature == null) { var allFeatures = await table.QueryFeaturesAsync(new QueryParameters { WhereClause = "1=1" }); feature = allFeatures.FirstOrDefault(f => f.Attributes.ContainsKey("小区名") && f.Attributes["小区名"]?.ToString().Trim() == communityName.Trim()); } if (feature != null && feature.Geometry != null) { var routeGeo = GeometryEngine.Project(feature.Geometry, SpatialReferences.Wgs84); SimpleLineSymbol lineSymbol = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, color, width); _routeOverlay.Graphics.Add(new Graphic(routeGeo, lineSymbol)); double dist = 0; if (feature.Attributes.ContainsKey("Total_Leng")) dist = Convert.ToDouble(feature.Attributes["Total_Leng"]); string distText = (dist > 0) ? $"{dist / 1000.0:F1} km" : "计算中"; Application.Current.Dispatcher.Invoke(() => NearestPaths.Add(new NearestPathResult { TypeName = typeName, TargetHospitalName = feature.Attributes["名称"]?.ToString() ?? "未知", DistanceText = distText, ColorCode = $"#{color.R:X2}{color.G:X2}{color.B:X2}" })); } } catch { } }
        private void ZoomToLocation(SearchResultItem item) { if (item == null) return; RequestNavigation?.Invoke(this, new NavigationEventArgs { Center = new MapPoint(item.Lon, item.Lat, SpatialReferences.Wgs84), Scale = 5000, ResultItem = item }); StatusMessage = $"定位: {item.Name}"; }
        private void ZoomToDistrict(string districtName) { if (_districtViewpoints.TryGetValue(districtName, out var vp)) { RequestNavigation?.Invoke(this, new NavigationEventArgs { Center = vp.TargetGeometry.Extent.GetCenter(), Scale = 100000, IsDistrictZoom = true, DistrictEnvelope = vp.TargetGeometry.Extent }); StatusMessage = $"切换至: {districtName}"; } }
        public void UpdateStatisticsFromGraphics(Envelope currentExtent) { if (currentExtent == null) return; int l1 = 0, l2 = 0; Envelope wgsExtent = (currentExtent.SpatialReference.Wkid != 4326) ? GeometryEngine.Project(currentExtent, SpatialReferences.Wgs84) as Envelope : currentExtent; if (wgsExtent == null) return; foreach (var overlay in GraphicsOverlays) { if (!overlay.IsVisible) continue; foreach (var graphic in overlay.Graphics) { if (GeometryEngine.Contains(wgsExtent, graphic.Geometry)) { if (graphic.Attributes.ContainsKey("Level")) { var level = Convert.ToInt32(graphic.Attributes["Level"]); if (level == 1) l1++; else if (level == 2 || level == 3) l2++; } } } } Level1Count = l1; Level2Count = l2; }

        // F2 & Common Logic
        private void ExecuteLoadAnalysis(object obj) { _isAnalysisStarted = true; UpdateLayerVisibility(); StatusMessage = "等时圈分析已加载。"; }
        private async Task LoadMmpkLayers() { if (!File.Exists(MmpkPath)) return; try { var mmpk = await MobileMapPackage.OpenAsync(MmpkPath); if (mmpk.Maps.Count > 0) { var sourceMap = mmpk.Maps[0]; var allLayers = new List<FeatureLayer>(); await CollectFeatureLayersRecursive(sourceMap.OperationalLayers, allLayers); foreach (var layer in allLayers) { var cloned = layer.Clone() as FeatureLayer; if (cloned == null) continue; string name = cloned.Name.ToLower(); bool isWalk = name.Contains("walk"); bool isDrive = name.Contains("drive"); bool isAll = name.Contains("123") || name.Contains("all") || name.Contains("23"); bool isL1 = (name.Contains("1") || name.Contains("3a")) && !isAll; if (isWalk && isL1) _layerWalkL1 = cloned; else if (isDrive && isL1) _layerDriveL1 = cloned; else if (isWalk && isAll) _layerWalkAll = cloned; else if (isDrive && isAll) _layerDriveAll = cloned; cloned.IsVisible = false; cloned.Opacity = 0.6; Map.OperationalLayers.Add(cloned); } } } catch { } }
        private async Task CollectFeatureLayersRecursive(LayerCollection layers, List<FeatureLayer> result) { foreach (var layer in layers) { await layer.LoadAsync(); if (layer is FeatureLayer fl) result.Add(fl); else if (layer is GroupLayer gl) await CollectFeatureLayersRecursive(gl.Layers, result); } }
        private void HideAllMmpkLayers() { if (_layerWalkL1 != null) _layerWalkL1.IsVisible = false; if (_layerDriveL1 != null) _layerDriveL1.IsVisible = false; if (_layerWalkAll != null) _layerWalkAll.IsVisible = false; if (_layerDriveAll != null) _layerDriveAll.IsVisible = false; LegendWalkL1 = Visibility.Collapsed; LegendDriveL1 = Visibility.Collapsed; LegendWalkAll = Visibility.Collapsed; LegendDriveAll = Visibility.Collapsed; }
        private void UpdateLayerVisibility() { HideAllMmpkLayers(); if (!_isAnalysisStarted) return; var target = GetCurrentLayer(); if (target != null) { target.IsVisible = true; if (target == _layerWalkL1) LegendWalkL1 = Visibility.Visible; else if (target == _layerDriveL1) LegendDriveL1 = Visibility.Visible; else if (target == _layerWalkAll) LegendWalkAll = Visibility.Visible; else if (target == _layerDriveAll) LegendDriveAll = Visibility.Visible; } _blindSpotOverlay.Graphics.Clear(); }
        private FeatureLayer GetCurrentLayer() { if (SelectedHospitalTypeIndex == 0) return (SelectedModeIndex == 0) ? _layerWalkL1 : _layerDriveL1; else return (SelectedModeIndex == 0) ? _layerWalkAll : _layerDriveAll; }

        private async Task<Geometry> CalculateBlindSpotGeometryAsync(FeatureLayer layer)
        {
            if (layer == null) return null;
            await layer.LoadAsync();
            var blindSpot = await Task.Run(async () =>
            {
                var dGeo = await GetUnionGeometry(ShpPath, true);
                var sGeo = await GetUnionGeometry(layer);
                if (dGeo == null || sGeo == null) return null;
                var dGeoWgs = GeometryEngine.Project(dGeo, SpatialReferences.Wgs84);
                var sGeoWgs = GeometryEngine.Project(sGeo, SpatialReferences.Wgs84);
                var rawBlind = GeometryEngine.Difference(dGeoWgs, sGeoWgs);
                if (File.Exists(WaterShpPath))
                {
                    var waterGeo = await GetWaterBodyGeometryAsync();
                    if (waterGeo != null && !waterGeo.IsEmpty) rawBlind = GeometryEngine.Difference(rawBlind, waterGeo);
                }
                return rawBlind;
            });
            _cachedBlindSpot = blindSpot;
            return blindSpot;
        }

        private async void ExecuteCalcBlindSpot(object obj)
        {
            var layer = GetCurrentLayer();
            if (layer == null) return;
            _blindSpotOverlay.Graphics.Clear();
            StatusMessage = "正在计算盲区 (剔除水体)...";
            try
            {
                var blind = await CalculateBlindSpotGeometryAsync(layer);
                if (blind != null && !blind.IsEmpty)
                {
                    _blindSpotOverlay.Graphics.Add(new Graphic(blind, new SimpleFillSymbol(SimpleFillSymbolStyle.Solid, System.Drawing.Color.FromArgb(150, 60, 60, 60), null)));
                    StatusMessage = "盲区计算完成";
                }
            }
            catch { StatusMessage = "盲区计算失败"; }
        }

        private async Task<Geometry> GetWaterBodyGeometryAsync()
        {
            try
            {
                ShapefileFeatureTable waterTable = new ShapefileFeatureTable(WaterShpPath);
                await waterTable.LoadAsync();
                var query = new QueryParameters { WhereClause = "1=1" };
                var features = await waterTable.QueryFeaturesAsync(query);
                var bigWaters = new List<Geometry>();
                foreach (var f in features)
                {
                    if (f.Geometry == null) continue;
                    var geoWgs = GeometryEngine.Project(f.Geometry, SpatialReferences.Wgs84);
                    if (GeometryEngine.AreaGeodetic(geoWgs, AreaUnits.SquareMeters, GeodeticCurveType.Geodesic) > 500000)
                        bigWaters.Add(geoWgs);
                }
                return bigWaters.Any() ? GeometryEngine.Union(bigWaters) : null;
            }
            catch { return null; }
        }

        private async void ExecuteCalcEquity(object obj)
        {
            var layer = GetCurrentLayer();
            if (layer == null) return;
            StatsPanelVisibility = Visibility.Visible;
            try
            {
                var sGeo = await GetUnionGeometry(layer);
                if (sGeo == null) return;
                sGeo = GeometryEngine.Project(sGeo, SpatialReferences.Wgs84);
                ShapefileFeatureTable table = new ShapefileFeatureTable(ShpPath);
                await table.LoadAsync();
                var tempList = new List<DistrictStat>();
                foreach (var f in await table.QueryFeaturesAsync(new QueryParameters { WhereClause = "1=1" }))
                {
                    var dGeo = GeometryEngine.Project(f.Geometry, SpatialReferences.Wgs84);
                    var inter = GeometryEngine.Intersection(dGeo, sGeo);
                    double rate = 0;
                    if (dGeo != null && !dGeo.IsEmpty) rate = (GeometryEngine.Area(inter) / GeometryEngine.Area(dGeo)) * 100.0;
                    tempList.Add(new DistrictStat { Name = f.Attributes["Name"]?.ToString() ?? "未知", CoverageRate = rate });
                }
                var sortedList = tempList.OrderByDescending(x => x.CoverageRate).ToList();
                Application.Current.Dispatcher.Invoke(() => { DistrictStats.Clear(); foreach (var item in sortedList) DistrictStats.Add(item); });
            }
            catch { }
        }

        private async Task AddNanjingLayerAsync() { if (!File.Exists(ShpPath)) return; try { ShapefileFeatureTable table = new ShapefileFeatureTable(ShpPath); FeatureLayer layer = new FeatureLayer(table); layer.Renderer = new SimpleRenderer(new SimpleFillSymbol(SimpleFillSymbolStyle.Null, System.Drawing.Color.Transparent, new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.Red, 2))); Map.OperationalLayers.Add(layer); await layer.LoadAsync(); } catch { } }
        private async Task AddHospitalPointsAsync() { try { using (var db = new NanjingContext()) { var list = await db.Hospitals.Where(h => h.Level == 1 && h.WgsLongitude != null).ToListAsync(); SimpleMarkerSymbol sym = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.Blue, 6); foreach (var h in list) _level1Overlay.Graphics.Add(new Graphic(new MapPoint(h.WgsLongitude.Value, h.WgsLatitude.Value, SpatialReferences.Wgs84), sym) { Attributes = { ["Level"] = 1 } }); } } catch { } }
        private async Task AddHighLevelHospitalsAsync() { try { using (var db = new NanjingContext()) { var list = await db.Hospitals.Where(h => (h.Level == 2 || h.Level == 3) && h.WgsLongitude != null).ToListAsync(); SimpleMarkerSymbol sym = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.Green, 8); foreach (var h in list) _highLevelOverlay.Graphics.Add(new Graphic(new MapPoint(h.WgsLongitude.Value, h.WgsLatitude.Value, SpatialReferences.Wgs84), sym) { Attributes = { ["Level"] = 2 } }); } } catch { } }
        private async Task<Geometry> GetUnionGeometry(FeatureLayer layer) { var res = await layer.FeatureTable.QueryFeaturesAsync(new QueryParameters { WhereClause = "1=1" }); return GeometryEngine.Union(res.Select(f => f.Geometry).Where(g => g != null)); }
        private async Task<Geometry> GetUnionGeometry(string path, bool isShp) { ShapefileFeatureTable table = new ShapefileFeatureTable(path); await table.LoadAsync(); var res = await table.QueryFeaturesAsync(new QueryParameters { WhereClause = "1=1" }); return GeometryEngine.Union(res.Select(f => f.Geometry).Where(g => g != null)); }

        // F4
        private void ExecuteStartDiffusion(object obj) { IsDiffusionActive = true; DiffusionStatus = "邻域扩散模式已激活，请选择初始小区"; _diffusionOverlay.IsVisible = true; }
        private async Task PerformDiffusionSearchAsync(string query) { if (string.IsNullOrWhiteSpace(query)) { DiffusionResults = null; return; } using (var db = new NanjingContext()) { DiffusionResults = await db.Communities.Where(c => c.Name.Contains(query) && c.WgsLongitude != null).Select(c => new SearchResultItem { Name = c.Name, Type = "住宅小区", Lat = c.WgsLatitude.Value, Lon = c.WgsLongitude.Value, DetailInfo = $"街道:{c.Street}\n人口:{c.FinalPopulation ?? 0}" }).Take(5).ToListAsync(); } }
        private void SelectDiffusionStartPoint(SearchResultItem item) { if (item == null) return; _diffusionStartPoint = new MapPoint(item.Lon, item.Lat, SpatialReferences.Wgs84); _diffusionOverlay.Graphics.Clear(); RequestNavigation?.Invoke(this, new NavigationEventArgs { Center = _diffusionStartPoint, Scale = 5000, ResultItem = item }); DiffusionStatus = $"已选择初始小区: {item.Name}"; CurrentDay = 0; UpdateDiffusionBuffer(); }
        private void UpdateDiffusionBuffer() { try { _diffusionOverlay.Graphics.Clear(); if (_diffusionStartPoint == null || CurrentDay < 0) return; double currentRadiusMeters = InitialRadius + (CurrentDay * DailyIncrement); if (currentRadiusMeters > 10000) currentRadiusMeters = 10000; DiffusionStatus = $"第 {CurrentDay} 天 - 扩散半径: {currentRadiusMeters:F0}米"; var webMercatorPoint = GeometryEngine.Project(_diffusionStartPoint, SpatialReferences.WebMercator) as MapPoint; if (webMercatorPoint == null) return; var buffer = GeometryEngine.Buffer(webMercatorPoint, currentRadiusMeters); var bufferWgs84 = GeometryEngine.Project(buffer, SpatialReferences.Wgs84); int red = 255; int greenAndBlue = Math.Min((int)((CurrentDay / 7.0) * 200), 200); byte alpha = (byte)Math.Max(50, 150 - (CurrentDay * 14)); System.Drawing.Color color = System.Drawing.Color.FromArgb(alpha, red, greenAndBlue, greenAndBlue); System.Drawing.Color borderColor = System.Drawing.Color.FromArgb(255, red, greenAndBlue, greenAndBlue); var graphic = new Graphic(bufferWgs84, new SimpleFillSymbol(SimpleFillSymbolStyle.Solid, color, new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, borderColor, 2))); _diffusionOverlay.Graphics.Add(graphic); _diffusionOverlay.Graphics.Add(new Graphic(_diffusionStartPoint, new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.Red, 8))); } catch (Exception ex) { DiffusionStatus = $"更新缓冲区失败: {ex.Message}"; } }
        private void ExecuteUpdateDiffusion(object obj) { if (_diffusionStartPoint == null) { DiffusionStatus = "请先选择初始小区"; return; } UpdateDiffusionBuffer(); }
        private async void LoadF4DataAsync() { try { await LoadF4CommunityPointsAsync(); await LoadF4Level1HospitalPointsAsync(); StatusMessage = "F4 数据加载完成"; } catch (Exception ex) { StatusMessage = $"F4 数据加载失败: {ex.Message}"; } }
        private async Task LoadF4CommunityPointsAsync() { try { using (var db = new NanjingContext()) { var communities = await db.Communities.Where(c => c.WgsLongitude != null && c.WgsLatitude != null).ToListAsync(); _f4CommunityOverlay.Graphics.Clear(); SimpleMarkerSymbol sym = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.FromArgb(150, 30, 144, 255), 6); foreach (var c in communities) _f4CommunityOverlay.Graphics.Add(new Graphic(new MapPoint(c.WgsLongitude.Value, c.WgsLatitude.Value, SpatialReferences.Wgs84), sym)); } } catch { } }
        private async Task LoadF4Level1HospitalPointsAsync() { try { using (var db = new NanjingContext()) { var hospitals = await db.Hospitals.Where(h => h.Level == 1 && h.WgsLongitude != null).ToListAsync(); _f4Level1HospitalOverlay.Graphics.Clear(); SimpleMarkerSymbol sym = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Cross, System.Drawing.Color.FromArgb(200, 255, 0, 0), 10); foreach (var h in hospitals) _f4Level1HospitalOverlay.Graphics.Add(new Graphic(new MapPoint(h.WgsLongitude.Value, h.WgsLatitude.Value, SpatialReferences.Wgs84), sym)); } } catch { } }
        private void SwitchF4Mode(object parameter) { string mode = parameter as string; if (mode != null) { CurrentF4Mode = mode; StatusMessage = $"F4 风险模拟: {mode}"; } }
        private void UpdateF4ModeVisibility() { IsNeighborhoodModeVisible = CurrentF4Mode == "Neighborhood" ? Visibility.Visible : Visibility.Collapsed; IsNetworkModeVisible = CurrentF4Mode == "Network" ? Visibility.Visible : Visibility.Collapsed; }
        private async Task PerformNetworkStartSearchAsync(string query) { if (string.IsNullOrWhiteSpace(query)) { NetworkStartResults = null; return; } using (var db = new NanjingContext()) { NetworkStartResults = await db.Communities.Where(c => c.Name.Contains(query) && c.WgsLongitude != null).Select(c => new SearchResultItem { Name = c.Name, Type = "住宅小区", Lat = c.WgsLatitude.Value, Lon = c.WgsLongitude.Value, DetailInfo = $"街道:{c.Street}" }).Take(5).ToListAsync(); } }
        private async void HandleNetworkStartSelection(SearchResultItem item) { if (item == null) return; _networkStartPoint = new MapPoint(item.Lon, item.Lat, SpatialReferences.Wgs84); RequestNavigation?.Invoke(this, new NavigationEventArgs { Center = _networkStartPoint, Scale = 5000, ResultItem = item }); await FindNearestHospital(_networkStartPoint); }
        private async Task FindNearestHospital(MapPoint startPoint) { try { using (var db = new NanjingContext()) { var hospitals = await db.Hospitals.Where(h => h.Level == 1 && h.WgsLongitude != null).ToListAsync(); if (!hospitals.Any()) { NearestHospitalInfo = "未找到三甲医院"; return; } double minDistance = double.MaxValue; Hospital nearest = null; foreach (var h in hospitals) { var pt = new MapPoint(h.WgsLongitude.Value, h.WgsLatitude.Value, SpatialReferences.Wgs84); double d = GeometryEngine.Distance(startPoint, pt); if (d < minDistance) { minDistance = d; nearest = h; _nearestHospitalPoint = pt; } } if (nearest != null) NearestHospitalInfo = $"🏥 {nearest.Name}\n距离: {minDistance * 111:0.0}公里"; } } catch { NearestHospitalInfo = "查找失败"; } }
        private async Task LoadRoadNetworkLayer() { if (_roadNetworkLayer != null) return; if (!File.Exists(RoadNetworkPath)) { StatusMessage = "路网文件不存在"; return; } try { ShapefileFeatureTable table = new ShapefileFeatureTable(RoadNetworkPath); await table.LoadAsync(); _roadNetworkLayer = new FeatureLayer(table) { IsVisible = false, Opacity = 0.7 }; _roadNetworkLayer.Renderer = new SimpleRenderer(new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.FromArgb(150, 100, 100, 100), 1.5)); Map.OperationalLayers.Add(_roadNetworkLayer); } catch { } }
        private async void ExecuteStartNetworkSimulation(object obj) { if (_networkStartPoint == null || _nearestHospitalPoint == null) { StatusMessage = "请先选择起始小区"; return; } await LoadRoadNetworkLayer(); _networkDiffusionOverlay.Graphics.Clear(); var polyline = new PolylineBuilder(SpatialReferences.Wgs84); polyline.AddPoint(_networkStartPoint); polyline.AddPoint(_nearestHospitalPoint); var path = polyline.ToGeometry(); var webPath = GeometryEngine.Project(path, SpatialReferences.WebMercator) as Polyline; var buffer = GeometryEngine.Buffer(webPath, CorridorWidth); var bufferWgs84 = GeometryEngine.Project(buffer, SpatialReferences.Wgs84) as Polygon; _networkDiffusionOverlay.Graphics.Add(new Graphic(path, new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.Yellow, 3))); _networkDiffusionOverlay.Graphics.Add(new Graphic(bufferWgs84, new SimpleFillSymbol(SimpleFillSymbolStyle.Solid, System.Drawing.Color.FromArgb(60, 255, 165, 0), null))); if (_roadNetworkLayer != null) { var roads = await _roadNetworkLayer.FeatureTable.QueryFeaturesAsync(new QueryParameters { Geometry = bufferWgs84, SpatialRelationship = SpatialRelationship.Intersects }); _selectedRoads = roads.ToList(); StartRoadAnimation(_selectedRoads); } }
        private void StartRoadAnimation(List<Feature> roads) { if (!roads.Any()) return; ExecuteStopNetworkSimulation(null); _networkAnimationTimer = new System.Timers.Timer(AnimationSpeed); int idx = 0; _networkAnimationTimer.Elapsed += (s, e) => Application.Current.Dispatcher.Invoke(() => { if (idx < roads.Count) { var r = roads[idx++]; if (r.Geometry != null) _networkDiffusionOverlay.Graphics.Add(new Graphic(r.Geometry, new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.Red, 3))); } else ExecuteStopNetworkSimulation(null); }); _networkAnimationTimer.Start(); }
        private void ExecuteStopNetworkSimulation(object obj) { _networkAnimationTimer?.Stop(); }
        private void ExecuteClearNetworkGraphics(object obj) { _networkDiffusionOverlay.Graphics.Clear(); }

        // F5
        private async void ExecuteStartOptimization(object obj)
        {
            StatusMessage = "F5: 正在计算盲区中心...";
            OptimizationResults.Clear();
            _f5SiteOverlay.Graphics.Clear();

            Geometry blindSpot = null;
            try
            {
                if (_layerDriveAll == null) await ExecuteLoadAnalysisAndFindDriveAllAsync();

                // ★★★ 使用缓存或通用方法 ★★★
                if (_cachedBlindSpot != null)
                {
                    blindSpot = _cachedBlindSpot;
                }
                else
                {
                    blindSpot = await CalculateBlindSpotGeometryAsync(_layerDriveAll);
                }

                if (blindSpot == null || blindSpot.IsEmpty)
                {
                    StatusMessage = "当前模式下无有效盲区";
                    return;
                }

                List<Polygon> parts = new List<Polygon>();
                if (blindSpot is Polygon p) { foreach (var part in p.Parts) parts.Add(new Polygon(new[] { part })); }
                var topParts = parts.OrderByDescending(x => GeometryEngine.Area(x)).Take(3).ToList();

                await Task.Run(async () =>
                {
                    int index = 1;
                    foreach (var part in topParts)
                    {
                        var center = GeometryEngine.LabelPoint(part);

                        // 模拟数据
                        var stats = await CalculateAmapStatsAsync(center);

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            var sym = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Diamond, System.Drawing.Color.Purple, 15);
                            _f5SiteOverlay.Graphics.Add(new Graphic(center, sym));

                            OptimizationResults.Add(new OptimizationResultItem
                            {
                                Title = $"推荐选址 {index}",
                                LocationDesc = $"经度 {center.X:F3}, 纬度 {center.Y:F3}",
                                PopulationDesc = $"覆盖 {stats.Count} 个小区 | 服务人口 {stats.Population} 人",
                                Suggestion = index == 1 ? "建议建设: 二级综合医院" : "建议建设: 社区卫生服务中心",
                                CenterPoint = center
                            });
                            index++;
                        });
                    }
                    StatusMessage = $"F5: 选址完成";
                });
            }
            catch (Exception ex) { StatusMessage = $"F5 计算异常: {ex.Message}"; }
        }

        private async Task ExecuteLoadAnalysisAndFindDriveAllAsync()
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
                        string name = layer.Name.ToLower();
                        bool isDrive = name.Contains("drive");
                        bool isAll = name.Contains("123") || name.Contains("all") || name.Contains("23");

                        if (isDrive && isAll)
                        {
                            _layerDriveAll = layer.Clone() as FeatureLayer;
                            break;
                        }
                    }
                }
            }
            catch { }
        }

        private async Task<(int Count, int Population)> CalculateAmapStatsAsync(MapPoint centerWgs)
        {
            await Task.Delay(200);
            var rnd = new Random(Guid.NewGuid().GetHashCode());
            int count = rnd.Next(5, 25);
            int pop = rnd.Next(5000, 30000);
            return (count, pop);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}