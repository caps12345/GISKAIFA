using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.UI;
using Esri.ArcGISRuntime.UI.Controls;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
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
using System.Windows.Controls;
using System.Windows.Input;
using WpfMapApp2.Models;
using WpfMapApp2.Utils;

namespace WpfMapApp2
{
    // ================= 1. 辅助类定义 =================

    public class RouteResultInfo
    {
        public Polyline Geometry { get; set; }
        public double TotalDistance { get; set; }
        public double TotalDuration { get; set; } 
    }
  

    public class NavigationEventArgs : EventArgs
    {
        public bool IsDistrictZoom { get; set; }
        public Envelope DistrictEnvelope { get; set; }
        public string DistrictName { get; set; }
        public SearchResultItem ResultItem { get; set; }
        public MapPoint Center { get; set; }
        public double Scale { get; set; }
    }

    public class SearchResultItem
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public double Lat { get; set; }
        public double Lon { get; set; }
        public string DetailInfo { get; set; }
    }

    public class AmapRouteResponse
    {
        public string status { get; set; }
        public AmapRouteResult route { get; set; }
    }
    public class AmapRouteResult { public List<AmapPath> paths { get; set; } }
    public class AmapPath
    {
        public string distance { get; set; }
        public string duration { get; set; } // 新增：映射API返回的duration字段 [cite: 1]
        public List<AmapStep> steps { get; set; }
    }

    public class AmapStep
    {
        public string polyline { get; set; }
        public string road { get; set; }
    }

    // ★★★ F2 统计图表的核心模型 ★★★
    public class DistrictStat
    {
        public string Name { get; set; }
        public double CoverageRate { get; set; }
        public string RateText => $"{CoverageRate:F1}%";
        // 用于 UI 条形图绑定的宽度，根据覆盖率动态计算
        public double BarWidth => CoverageRate * 1.8;
        // 用于 UI 条形图颜色的绑定 (红/黄/绿)
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

    // ================= 2. MapViewModel 主类 =================
    public class MapViewModel : INotifyPropertyChanged
    {
        // ★★★ 路径配置 ★★★
        private const string ShpPath = @"D:\GIS_DATA\Data\南京区县.shp";
        private const string MmpkPath = @"D:\GIS_DATA\Data\nanjing.mmpk";
        private const string WaterShpPath = @"D:\GIS_DATA\Data\南京市水系.shp";
        private const string PathShpL1 = @"D:\GIS_DATA\Data\nearest_level1.shp";
        private const string PathShpL2 = @"D:\GIS_DATA\Data\nearest_level2.shp";
        private const string PathShpL3 = @"D:\GIS_DATA\Data\nearest_level3.shp";
        private const string RoadNetworkPath = @"D:\GIS_DATA\Data\road_project.shp";
        private const string AmapKey = "d858a21cb4bfc1ffb236ed80f34bdc57";

        private Map _map;
        private string _statusMessage = "系统初始化...";
        private Geometry _cachedBlindSpot = null;

        public GraphicsOverlayCollection GraphicsOverlays { get; set; } = new GraphicsOverlayCollection();

        // 基础图层
        private GraphicsOverlay _blindSpotOverlay = new GraphicsOverlay { Id = "BlindSpot" };
        private GraphicsOverlay _level1Overlay = new GraphicsOverlay { Id = "Level1Hospitals" };
        private GraphicsOverlay _highLevelOverlay = new GraphicsOverlay { Id = "HighLevelHospitals", MinScale = 50000 };
        private GraphicsOverlay _routeOverlay = new GraphicsOverlay { Id = "RouteOverlay" };
        // 在 private GraphicsOverlay _routeOverlay ... 下方添加
        private GraphicsOverlay _emergencyOverlay = new GraphicsOverlay { Id = "EmergencyOverlay" };
        // ★★★ 关键：F1 区域高亮图层 ★★★
        public GraphicsOverlay HighlightOverlay { get; } = new GraphicsOverlay { Id = "HighlightOverlay" };

        // F4/F5 图层
        private GraphicsOverlay _f4CommunityOverlay = new GraphicsOverlay { Id = "F4Communities", MinScale = 50000 };
        private GraphicsOverlay _f4Level1HospitalOverlay = new GraphicsOverlay { Id = "F4Level1Hospitals" };
        private GraphicsOverlay _diffusionOverlay = new GraphicsOverlay { Id = "DiffusionOverlay" };
        private GraphicsOverlay _networkDiffusionOverlay = new GraphicsOverlay { Id = "NetworkDiffusion" };
        private GraphicsOverlay _lockdownOverlay = new GraphicsOverlay { Id = "LockdownLayer" };
        private GraphicsOverlay _f5SiteOverlay = new GraphicsOverlay { Id = "F5Sites" };

        public Map Map { get => _map; set { _map = value; OnPropertyChanged(); } }
        public string StatusMessage { get => _statusMessage; set { _statusMessage = value; OnPropertyChanged(); } }

        // 面板显隐
        private Visibility _leftPanelVisibility = Visibility.Collapsed;
        public Visibility LeftPanelVisibility { get => _leftPanelVisibility; set { _leftPanelVisibility = value; OnPropertyChanged(); } }
        private Visibility _isF1ConfigVisible = Visibility.Collapsed;
        public Visibility IsF1ConfigVisible { get => _isF1ConfigVisible; set { _isF1ConfigVisible = value; OnPropertyChanged(); } }
        private Visibility _isF2ConfigVisible = Visibility.Collapsed;
        public Visibility IsF2ConfigVisible { get => _isF2ConfigVisible; set { _isF2ConfigVisible = value; OnPropertyChanged(); } }
        private Visibility _isF3ConfigVisible = Visibility.Collapsed;
        public Visibility IsF3ConfigVisible { get => _isF3ConfigVisible; set { _isF3ConfigVisible = value; OnPropertyChanged(); } }
        private Visibility _isF4ConfigVisible = Visibility.Collapsed;
        public Visibility IsF4ConfigVisible { get => _isF4ConfigVisible; set { _isF4ConfigVisible = value; OnPropertyChanged(); } }
        private Visibility _isF5ConfigVisible = Visibility.Collapsed;
        public Visibility IsF5ConfigVisible { get => _isF5ConfigVisible; set { _isF5ConfigVisible = value; OnPropertyChanged(); } }

        private Visibility _f1StatsVisibility = Visibility.Collapsed;
        public Visibility F1StatsVisibility { get => _f1StatsVisibility; set { _f1StatsVisibility = value; OnPropertyChanged(); } }
        private Visibility _statsPanelVisibility = Visibility.Collapsed;
        public Visibility StatsPanelVisibility { get => _statsPanelVisibility; set { _statsPanelVisibility = value; OnPropertyChanged(); } }

        public bool IsF1Active { get; set; } = false;
        private string _currentModule = "Welcome";
        public string CurrentModule { get => _currentModule; set { _currentModule = value; OnPropertyChanged(); } }

        // F1 属性
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
        public List<SearchResultItem> CommunitySearchResults
        {
            get => _communitySearchResults;
            set
            {
                _communitySearchResults = value;
                OnPropertyChanged();
                // ★★★ 修改：有数据就自动显示列表 ★★★
                IsShowingCommunityResults = value?.Any() == true;
            }
        }
        private bool _isShowingCommunityResults;
        public bool IsShowingCommunityResults
        {
            get => _isShowingCommunityResults;
            set { _isShowingCommunityResults = value; OnPropertyChanged(); }
        }
        private SearchResultItem _selectedCommunity;
        public SearchResultItem SelectedCommunity
        {
            get => _selectedCommunity;
            set
            {
                _selectedCommunity = value;
                OnPropertyChanged();

                if (value != null)
                {
                    // 1. 选中状态
                    ZoomToLocation(value);
                    _ = QueryPathsForCommunityAsync(value.Name);

                    IsPathLegendVisible = true;

                    // ★★★ 核心修复：绝对不要清空数据源！只通过变量隐藏列表 ★★★
                    // CommunitySearchResults = null;  <-- 这行必须删掉！它是罪魁祸首！
                    IsShowingCommunityResults = false; // <-- 改用这行来隐藏列表

                    _communitySearchText = value.Name;
                    OnPropertyChanged(nameof(CommunitySearchText));
                }
                else
                {
                    // 2. 空状态 (保持之前的修复)
                    IsPathLegendVisible = false;
                    NearestPaths.Clear();
                    _routeOverlay.Graphics.Clear();
                    StatusMessage = "就绪";
                }
            }
        }

        private ObservableCollection<NearestPathResult> _nearestPaths = new ObservableCollection<NearestPathResult>();
        public ObservableCollection<NearestPathResult> NearestPaths { get => _nearestPaths; set { _nearestPaths = value; OnPropertyChanged(); } }

        // 添加在 NearestPaths 属性定义的后面
        private string _emergencyTimeText = "-- 分钟";
        public string EmergencyTimeText
        {
            get => _emergencyTimeText;
            set { _emergencyTimeText = value; OnPropertyChanged(); }
        }
        public ICommand StartEmergencyCommand { get; private set; }

        private string _selectedDistrict;
        public string SelectedDistrict { get => _selectedDistrict; set { _selectedDistrict = value; OnPropertyChanged(); if (!string.IsNullOrEmpty(value)) ZoomToDistrict(value); } }

        private readonly Dictionary<string, Viewpoint> _districtViewpoints = new Dictionary<string, Viewpoint> { { "全南京市", new Viewpoint(new Envelope(118.3, 31.2, 119.2, 32.6, SpatialReferences.Wgs84)) }, { "玄武区", new Viewpoint(new Envelope(118.78, 32.03, 118.92, 32.12, SpatialReferences.Wgs84)) }, { "秦淮区", new Viewpoint(new Envelope(118.75, 31.98, 118.88, 32.05, SpatialReferences.Wgs84)) }, { "建邺区", new Viewpoint(new Envelope(118.67, 31.95, 118.78, 32.05, SpatialReferences.Wgs84)) }, { "鼓楼区", new Viewpoint(new Envelope(118.70, 32.03, 118.80, 32.12, SpatialReferences.Wgs84)) }, { "浦口区", new Viewpoint(new Envelope(118.30, 31.90, 118.75, 32.25, SpatialReferences.Wgs84)) }, { "栖霞区", new Viewpoint(new Envelope(118.78, 32.08, 119.25, 32.25, SpatialReferences.Wgs84)) }, { "雨花台区", new Viewpoint(new Envelope(118.65, 31.90, 118.85, 32.02, SpatialReferences.Wgs84)) }, { "江宁区", new Viewpoint(new Envelope(118.55, 31.60, 119.15, 32.10, SpatialReferences.Wgs84)) }, { "六合区", new Viewpoint(new Envelope(118.60, 32.15, 119.10, 32.60, SpatialReferences.Wgs84)) }, { "溧水区", new Viewpoint(new Envelope(118.85, 31.35, 119.25, 31.85, SpatialReferences.Wgs84)) }, { "高淳区", new Viewpoint(new Envelope(118.75, 31.15, 119.15, 31.55, SpatialReferences.Wgs84)) } };
        public List<string> Districts => _districtViewpoints.Keys.ToList();

        private int _l1, _l2, _l3;
        public int Level1Count { get => _l1; set { _l1 = value; OnPropertyChanged(); } }
        public int Level2Count { get => _l2; set { _l2 = value; OnPropertyChanged(); } }
        public int Level3Count { get => _l3; set { _l3 = value; OnPropertyChanged(); } }

        // F2 属性
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

        // F4/F5 属性
        private string _f4SearchText;
        public string F4SearchText { get => _f4SearchText; set { _f4SearchText = value; OnPropertyChanged(); } }
        private List<SearchResultItem> _f4SearchResults;
        public List<SearchResultItem> F4SearchResults { get => _f4SearchResults; set { _f4SearchResults = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsShowingF4Results)); } }
        public bool IsShowingF4Results => F4SearchResults?.Any() == true && !string.IsNullOrWhiteSpace(F4SearchText);
        private SearchResultItem _selectedF4Community;
        public SearchResultItem SelectedF4Community { get => _selectedF4Community; set { _selectedF4Community = value; OnPropertyChanged(); if (value != null) HandleF4CommunitySelection(value); } }
        private string _currentF4Mode = "Neighborhood";
        public string CurrentF4Mode { get => _currentF4Mode; set { _currentF4Mode = value; OnPropertyChanged(); UpdateF4ModeVisibility(); } }
        private Visibility _isNeighborhoodModeVisible = Visibility.Visible;
        public Visibility IsNeighborhoodModeVisible { get => _isNeighborhoodModeVisible; set { _isNeighborhoodModeVisible = value; OnPropertyChanged(); } }
        private Visibility _isNetworkModeVisible = Visibility.Collapsed;
        public Visibility IsNetworkModeVisible { get => _isNetworkModeVisible; set { _isNetworkModeVisible = value; OnPropertyChanged(); } }
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
        private double _corridorWidth = 200;
        public double CorridorWidth { get => _corridorWidth; set { _corridorWidth = value; OnPropertyChanged(); } }
        private MapPoint _f4StartPoint = null;
        private MapPoint _nearestHospitalPoint = null;
        private Geometry _lastNeighborhoodBuffer;
        private Geometry _lastNetworkCorridor;
        private System.Timers.Timer _networkAnimationTimer = null;
        public ObservableCollection<OptimizationResultItem> OptimizationResults { get; set; } = new ObservableCollection<OptimizationResultItem>();

        // Commands
        public ICommand HospitalSearchCommand { get; }
        public ICommand CommunitySearchCommand { get; }
        public ICommand LoadAnalysisCommand { get; }
        public ICommand CalcBlindSpotCommand { get; }
        public ICommand CalcEquityCommand { get; }
        public ICommand SwitchModuleCommand { get; }
        public ICommand F4SearchCommand { get; }
        public ICommand SwitchF4ModeCommand { get; }
        public ICommand StartNetworkSimulationCommand { get; }
        public ICommand ClearNetworkGraphicsCommand { get; }
        public ICommand GenerateLockdownCommand { get; }
        public ICommand StartOptimizationCommand { get; }
        public ICommand ZoomToSiteCommand { get; }
        public ICommand ShowWelcomeCommand => new RelayCommand(() => IsWelcomeVisible = true);

        public event EventHandler<NavigationEventArgs> RequestNavigation;
        public List<PressureAnalysisService.CommunityPressureResult> LastCalculationResults { get; private set; }

        private bool _isLoading = false;
        public bool IsLoading { get => _isLoading; set { _isLoading = value; OnPropertyChanged(); } }
        private PressureAnalysisService.AnalysisOptions _currentOptions = new PressureAnalysisService.AnalysisOptions();

        public MapViewModel()
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            // 注册所有图层 
            // ★★★ 确保 HighlightOverlay 被添加，否则高亮不显示 ★★★
            GraphicsOverlays.Add(_blindSpotOverlay);
            GraphicsOverlays.Add(_level1Overlay);
            GraphicsOverlays.Add(_highLevelOverlay);
            GraphicsOverlays.Add(_routeOverlay);
            GraphicsOverlays.Add(_emergencyOverlay);
            GraphicsOverlays.Add(HighlightOverlay);

            GraphicsOverlays.Add(_diffusionOverlay);
            GraphicsOverlays.Add(_f4CommunityOverlay);
            GraphicsOverlays.Add(_f4Level1HospitalOverlay);
            GraphicsOverlays.Add(_networkDiffusionOverlay);
            GraphicsOverlays.Add(_lockdownOverlay);
            GraphicsOverlays.Add(_f5SiteOverlay);

            // 初始化命令
            HospitalSearchCommand = new RelayCommand(async () => { SelectedHospital = null; await SearchHospitalsAsync(HospitalSearchText); OnPropertyChanged(nameof(IsShowingHospitalResults)); });

            CommunitySearchCommand = new RelayCommand(async () =>
            {
                SelectedCommunity = null; // 这行代码现在会触发上面写好的 else 逻辑，自动隐藏面板
                await SearchCommunitiesAsync(CommunitySearchText);
                OnPropertyChanged(nameof(IsShowingCommunityResults));
            });
            LoadAnalysisCommand = new RelayCommand(ExecuteLoadAnalysis);
            CalcBlindSpotCommand = new RelayCommand(ExecuteCalcBlindSpot);
            CalcEquityCommand = new RelayCommand(ExecuteCalcEquity);
            SwitchModuleCommand = new RelayCommand(ExecuteSwitchModule);
            StartEmergencyCommand = new RelayCommand(ExecuteStartEmergency);

            F4SearchCommand = new RelayCommand(async () => await PerformF4SearchAsync(F4SearchText));
            SwitchF4ModeCommand = new RelayCommand(SwitchF4Mode);
            StartNetworkSimulationCommand = new RelayCommand(ExecuteStartNetworkSimulation);
            ClearNetworkGraphicsCommand = new RelayCommand(ExecuteClearNetworkGraphics);
            GenerateLockdownCommand = new RelayCommand(ExecuteGenerateLockdown);

            StartOptimizationCommand = new RelayCommand(ExecuteStartOptimization);
            ZoomToSiteCommand = new RelayCommand(obj => { if (obj is MapPoint pt) RequestNavigation?.Invoke(this, new NavigationEventArgs { Center = pt, Scale = 50000 }); });

            InitializeMap();
        }

        private void CheckF2Update() { if (_isAnalysisStarted) UpdateLayerVisibility(); if (StatsPanelVisibility == Visibility.Visible) ExecuteCalcEquity(null); }
        private bool _isWelcomeVisible = true;
        public bool IsWelcomeVisible { get => _isWelcomeVisible; set { _isWelcomeVisible = value; OnPropertyChanged(); } }
        
        private string _statsTitle = "南京市医疗资源统计";
        public string StatsTitle { get => _statsTitle; set { _statsTitle = value; OnPropertyChanged(); } }
        private async void ExecuteSwitchModule(object parameter)
        {
            _ambulanceTimer?.Stop();           // 停止计时器
            _emergencyOverlay.Graphics.Clear(); // 清空图标
            _emergencyOverlay.IsVisible = false; // 默认隐藏
            IsWelcomeVisible = false;
            string module = parameter as string;
            CurrentModule = module;
            LeftPanelVisibility = Visibility.Visible;
            
            // 1. 全局清理
            if (module != "F4")
            {
                _f4CommunityOverlay.IsVisible = false; _f4Level1HospitalOverlay.IsVisible = false;
                _diffusionOverlay.IsVisible = false; _networkDiffusionOverlay.IsVisible = false;
                _lockdownOverlay.IsVisible = false; ExecuteStopNetworkSimulation(null);
            }
            if (module != "F5") _f5SiteOverlay.IsVisible = false;
            if (module != "F3" && Map?.OperationalLayers != null)
            {
                var f3Layers = Map.OperationalLayers.Where(l => l.Name.StartsWith("医疗压力_")).ToList();
                foreach (var l in f3Layers) l.IsVisible = false;
            }

            IsF1ConfigVisible = Visibility.Collapsed; IsF2ConfigVisible = Visibility.Collapsed; IsF3ConfigVisible = Visibility.Collapsed;
            IsF4ConfigVisible = Visibility.Collapsed; IsF5ConfigVisible = Visibility.Collapsed;
            F1StatsVisibility = Visibility.Collapsed; StatsPanelVisibility = Visibility.Collapsed;

            _level1Overlay.IsVisible = false; _highLevelOverlay.IsVisible = false; _routeOverlay.IsVisible = false; _blindSpotOverlay.IsVisible = false;

            // ★★★ F1 高亮图层默认关闭，F1 中打开 ★★★
            HighlightOverlay.IsVisible = false;

            HideAllMmpkLayers(); IsPathLegendVisible = false;

            // 2. 模块激活
            if (module == "F1")
            {
                StatusMessage = "资源感知: 医院点位模式"; IsF1ConfigVisible = Visibility.Visible; F1StatsVisibility = Visibility.Visible;
                IsF1Active = true;
                _level1Overlay.IsVisible = true; _highLevelOverlay.IsVisible = true; _routeOverlay.IsVisible = true;
                HighlightOverlay.IsVisible = true; // F1 开启高亮
                _emergencyOverlay.IsVisible = true;
            }
            else if (module == "F2")
            {
                StatusMessage = "可达性分析: 请加载分析"; IsF2ConfigVisible = Visibility.Visible; _blindSpotOverlay.IsVisible = true;
                IsF1Active = false;
                if (_isAnalysisStarted) UpdateLayerVisibility();
            }
            else if (module == "F3")
            {
                StatusMessage = "压力监测: 供需分层诊断"; IsF3ConfigVisible = Visibility.Visible;
                IsF1Active = false;
                _level1Overlay.IsVisible = true; _highLevelOverlay.IsVisible = true;
            }
            else if (module == "F4")
            {
                StatusMessage = "风险模拟: 全局选点模式"; IsF4ConfigVisible = Visibility.Visible;
                IsF1Active = false;
                _f4CommunityOverlay.IsVisible = true; _f4Level1HospitalOverlay.IsVisible = true;
                _diffusionOverlay.IsVisible = true; _networkDiffusionOverlay.IsVisible = true; _lockdownOverlay.IsVisible = true;
                LoadF4DataAsync(); CurrentF4Mode = "Neighborhood"; UpdateF4ModeVisibility();
            }
            // 查找 ExecuteSwitchModule 方法中的 if (module == "F5") 分支 (约 584行)
            // 修改如下：

            else if (module == "F5")
            {
                StatusMessage = "决策支持: 智能选址优化";
                IsF5ConfigVisible = Visibility.Visible;
                _f5SiteOverlay.IsVisible = true;
                IsF1Active = false;
                // ★★★ 修改：加载图层，但不自动计算盲区 ★★★
                // 确保三甲(L1)和全部(All)的图层都加载进内存
                if (!_isAnalysisStarted) await ExecuteLoadAnalysisAndFindDriveAllAsync();

                // 移除这两行自动操作：
                // SelectedModeIndex = 1; SelectedHospitalTypeIndex = 1; UpdateLayerVisibility();
                // _blindSpotOverlay.IsVisible = true; if (_cachedBlindSpot == null) ExecuteCalcBlindSpot(null);

                // 仅确保图层容器可见，内容由用户点击生成
                _blindSpotOverlay.IsVisible = true;
            }
        }

        private async void InitializeMap()
        {
            try
            {
                IsLoading = true;
                string token = "96cd361c8473c7c2d2c96bd05c598a2c";
                string vecUrl = @"http://t0.tianditu.gov.cn/vec_w/wmts?SERVICE=WMTS&REQUEST=GetTile&VERSION=1.0.0&LAYER=vec&STYLE=default&TILEMATRIXSET=w&FORMAT=tiles&TILEMATRIX={level}&TILEROW={row}&TILECOL={col}&tk=" + token;
                string cvaUrl = @"http://t0.tianditu.gov.cn/cva_w/wmts?SERVICE=WMTS&REQUEST=GetTile&VERSION=1.0.0&LAYER=cva&STYLE=default&TILEMATRIXSET=w&FORMAT=tiles&TILEMATRIX={level}&TILEROW={row}&TILECOL={col}&tk=" + token;
                WebTiledLayer baseLayer = new WebTiledLayer(vecUrl, new List<string> { "0", "1", "2", "3", "4", "5", "6", "7" });
                WebTiledLayer labelLayer = new WebTiledLayer(cvaUrl, new List<string> { "0", "1", "2", "3", "4", "5", "6", "7" });
                Basemap myBasemap = new Basemap(baseLayer); myBasemap.BaseLayers.Add(labelLayer);
                Map = new Map(myBasemap); Map.InitialViewpoint = new Viewpoint(32.060, 118.796, 150000);

                await AddNanjingLayerAsync();
                await LoadMmpkLayers();
                await AddHospitalPointsAsync();
                await AddHighLevelHospitalsAsync();

                bool wasWelcomeVisible = IsWelcomeVisible; ExecuteSwitchModule("F1"); IsWelcomeVisible = wasWelcomeVisible; StatusMessage = "就绪";
            }
            catch (Exception ex) { StatusMessage = $"初始化失败: {ex.Message}"; }
            finally { IsLoading = false; }
        }

        // ================= F1 核心方法 =================
        private async Task SearchHospitalsAsync(string query) { if (string.IsNullOrWhiteSpace(query)) { HospitalSearchResults = null; return; } using (var db = new NanjingContext()) { var hospitals = await db.Hospitals.Where(h => h.Name.Contains(query) && h.WgsLongitude != null).Select(h => new SearchResultItem { Name = h.Name, Type = "医疗机构", Lat = h.WgsLatitude.Value, Lon = h.WgsLongitude.Value, DetailInfo = $"地址:{h.Address}\n类型:{h.LevelLabel}" }).Take(10).ToListAsync(); HospitalSearchResults = hospitals; } }
        private async Task SearchCommunitiesAsync(string query) { if (string.IsNullOrWhiteSpace(query)) { CommunitySearchResults = null; return; } using (var db = new NanjingContext()) { var communities = await db.Communities.Where(c => c.Name.Contains(query) && c.WgsLongitude != null).Select(c => new SearchResultItem { Name = c.Name, Type = "住宅小区", Lat = c.WgsLatitude.Value, Lon = c.WgsLongitude.Value, DetailInfo = $"街道:{c.Street}\n类型:{c.Type}" }).Take(10).ToListAsync(); CommunitySearchResults = communities; } }
        private async Task QueryPathsForCommunityAsync(string communityName) 
        { 
            _routeOverlay.Graphics.Clear();
            NearestPaths.Clear(); 
            //IsPathLegendVisible = false; 
            StatusMessage = $"计算 {communityName} 最优就医路径..."; 
            await QuerySinglePathAsync(PathShpL1, communityName, "三甲医院", System.Drawing.Color.FromArgb(255, 255, 50, 50), 6.0); 
            await QuerySinglePathAsync(PathShpL2, communityName, "综合医院", System.Drawing.Color.FromArgb(255, 0, 191, 255), 4.0); 
            await QuerySinglePathAsync(PathShpL3, communityName, "诊所/卫生所", System.Drawing.Color.FromArgb(255, 0, 255, 0), 2.0);
            if (SelectedCommunity == null || SelectedCommunity.Name != communityName) return;

            if (NearestPaths.Count > 0)
            {
                StatusMessage = $"已显示 {NearestPaths.Count} 条路网路径";
                // 这一行可以留着，也可以注释掉，因为 SelectedCommunity 已经强制打开面板了
                IsPathLegendVisible = true;
            }
            else StatusMessage = "未找到该小区的预计算路径数据";
        }
        private async Task QuerySinglePathAsync(string shpPath, string communityName, string typeName, System.Drawing.Color color, double width) { if (!File.Exists(shpPath)) return; try { ShapefileFeatureTable table = new ShapefileFeatureTable(shpPath); await table.LoadAsync(); var queryParams = new QueryParameters { WhereClause = $"小区名 = '{communityName}'" }; var results = await table.QueryFeaturesAsync(queryParams); var feature = results.FirstOrDefault(); if (feature == null) { var allFeatures = await table.QueryFeaturesAsync(new QueryParameters { WhereClause = "1=1" }); feature = allFeatures.FirstOrDefault(f => f.Attributes.ContainsKey("小区名") && f.Attributes["小区名"]?.ToString().Trim() == communityName.Trim()); } if (feature != null && feature.Geometry != null) { var routeGeo = GeometryEngine.Project(feature.Geometry, SpatialReferences.Wgs84); SimpleLineSymbol lineSymbol = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, color, width); _routeOverlay.Graphics.Add(new Graphic(routeGeo, lineSymbol)); double dist = 0; if (feature.Attributes.ContainsKey("Total_Leng")) dist = Convert.ToDouble(feature.Attributes["Total_Leng"]); string distText = (dist > 0) ? $"{dist / 1000.0:F1} km" : "计算中"; Application.Current.Dispatcher.Invoke(() => NearestPaths.Add(new NearestPathResult { TypeName = typeName, TargetHospitalName = feature.Attributes["名称"]?.ToString() ?? "未知", DistanceText = distText, ColorCode = $"#{color.R:X2}{color.G:X2}{color.B:X2}" })); } } catch { } }
        private void ZoomToLocation(SearchResultItem item) { if (item == null) return; RequestNavigation?.Invoke(this, new NavigationEventArgs { Center = new MapPoint(item.Lon, item.Lat, SpatialReferences.Wgs84), Scale = 5000, ResultItem = item }); StatusMessage = $"定位: {item.Name}"; }

        // ★★★ F1 区域高亮逻辑 ★★★
        private readonly SimpleFillSymbol _highlightFillSymbol = new SimpleFillSymbol(SimpleFillSymbolStyle.Solid, System.Drawing.Color.FromArgb(80, 255, 255, 0), new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.Yellow, 3));
        private async void ZoomToDistrict(string districtName)
        {
            if (_districtViewpoints.TryGetValue(districtName, out var vp))
            {
                // 1. 清理
                HighlightOverlay.Graphics.Clear();
                // 2. 查询并绘制高亮
                if (_districtLayer != null && districtName != "全南京市")
                {
                    try
                    {
                        var queryParams = new QueryParameters { WhereClause = $"NAME LIKE '%{districtName.Replace("区", "")}%'" };
                        var result = await _districtLayer.FeatureTable.QueryFeaturesAsync(queryParams);
                        var feature = result.FirstOrDefault();
                        if (feature != null && feature.Geometry != null) HighlightOverlay.Graphics.Add(new Graphic(feature.Geometry, _highlightFillSymbol));
                    }
                    catch { }
                }
                // 3. 导航
                RequestNavigation?.Invoke(this, new NavigationEventArgs { Center = vp.TargetGeometry.Extent.GetCenter(), Scale = 100000, IsDistrictZoom = true, DistrictEnvelope = vp.TargetGeometry.Extent, DistrictName = districtName });
                // 4. 更新统计
                UpdateStatisticsByDistrict(districtName);
            }
        }
        public void ClearHighlight() { HighlightOverlay.Graphics.Clear(); }

        // F1 统计逻辑
        public void UpdateStatisticsByDistrict(string districtName)
        {
            try
            {
                using (var db = new NanjingContext())
                {
                    var query = db.Hospitals.AsQueryable();

                    // --- 新增代码开始 ---
                    if (string.IsNullOrEmpty(districtName) || districtName.Contains("南京") || districtName == "全南京市")
                    {
                        StatsTitle = "南京市医疗资源统计";
                    }
                    else
                    {
                        StatsTitle = $"{districtName}医疗资源统计";
                        // 原有的筛选逻辑
                        string pureName = districtName.Replace("区", "");
                        query = query.Where(h => h.District.Contains(pureName));
                    }
                    // --- 新增代码结束 ---

                    // 原有的统计逻辑保持不变 (这里的 L2 统计是准确的)
                    var stats = new { L1 = query.Count(h => h.Level == 1), L2 = query.Count(h => h.Level == 2), L3 = query.Count(h => h.Level == 3) };
                    Level1Count = stats.L1; Level2Count = stats.L2; Level3Count = stats.L3;
                }
            }
            catch { }
        }


        public void UpdateStatisticsFromGraphics(Envelope currentExtent) { if (currentExtent == null) return; int l1 = 0, l2 = 0; Envelope wgsExtent = (currentExtent.SpatialReference.Wkid != 4326) ? GeometryEngine.Project(currentExtent, SpatialReferences.Wgs84) as Envelope : currentExtent; if (wgsExtent == null) return; foreach (var overlay in GraphicsOverlays) { if (!overlay.IsVisible) continue; foreach (var graphic in overlay.Graphics) { if (graphic.Geometry != null && GeometryEngine.Contains(wgsExtent, graphic.Geometry)) { if (graphic.Attributes.ContainsKey("Level")) { var level = Convert.ToInt32(graphic.Attributes["Level"]); if (level == 1) l1++; else if (level == 2 || level == 3) l2++; } } } } Level1Count = l1; Level2Count = l2; }

        // ================= F2 核心方法 =================

        private int _selectedF5HospitalTypeIndex = 1; // 默认全部
        public int SelectedF5HospitalTypeIndex
        {
            get => _selectedF5HospitalTypeIndex;
            set
            {
                _selectedF5HospitalTypeIndex = value;
                OnPropertyChanged();
                // 切换选项时，清空之前的盲区缓存和显示，强制用户重新识别或计算
                _cachedBlindSpot = null;
                _blindSpotOverlay.Graphics.Clear();
            }
        }
        private void ExecuteLoadAnalysis(object obj) { _isAnalysisStarted = true; UpdateLayerVisibility(); StatusMessage = "等时圈分析已加载。"; }
        private async Task LoadMmpkLayers() { if (!File.Exists(MmpkPath)) return; try { var mmpk = await MobileMapPackage.OpenAsync(MmpkPath); if (mmpk.Maps.Count > 0) { var sourceMap = mmpk.Maps[0]; var allLayers = new List<FeatureLayer>(); await CollectFeatureLayersRecursive(sourceMap.OperationalLayers, allLayers); foreach (var layer in allLayers) { var cloned = layer.Clone() as FeatureLayer; if (cloned == null) continue; string name = cloned.Name.ToLower(); bool isWalk = name.Contains("walk"); bool isDrive = name.Contains("drive"); bool isAll = name.Contains("123") || name.Contains("all") || name.Contains("23"); bool isL1 = (name.Contains("1") || name.Contains("3a")) && !isAll; if (isWalk && isL1) _layerWalkL1 = cloned; else if (isDrive && isL1) _layerDriveL1 = cloned; else if (isWalk && isAll) _layerWalkAll = cloned; else if (isDrive && isAll) _layerDriveAll = cloned; cloned.IsVisible = false; cloned.Opacity = 0.6; Map.OperationalLayers.Add(cloned); } } } catch { } }
        private async Task CollectFeatureLayersRecursive(LayerCollection layers, List<FeatureLayer> result) { foreach (var layer in layers) { await layer.LoadAsync(); if (layer is FeatureLayer fl) result.Add(fl); else if (layer is GroupLayer gl) await CollectFeatureLayersRecursive(gl.Layers, result); } }
        private void HideAllMmpkLayers() { if (_layerWalkL1 != null) _layerWalkL1.IsVisible = false; if (_layerDriveL1 != null) _layerDriveL1.IsVisible = false; if (_layerWalkAll != null) _layerWalkAll.IsVisible = false; if (_layerDriveAll != null) _layerDriveAll.IsVisible = false; LegendWalkL1 = Visibility.Collapsed; LegendDriveL1 = Visibility.Collapsed; LegendWalkAll = Visibility.Collapsed; LegendDriveAll = Visibility.Collapsed; }
        private void UpdateLayerVisibility() { HideAllMmpkLayers(); if (!_isAnalysisStarted) return; var target = GetCurrentLayer(); if (target != null) { target.IsVisible = true; if (target == _layerWalkL1) LegendWalkL1 = Visibility.Visible; else if (target == _layerDriveL1) LegendDriveL1 = Visibility.Visible; else if (target == _layerWalkAll) LegendWalkAll = Visibility.Visible; else if (target == _layerDriveAll) LegendDriveAll = Visibility.Visible; } _blindSpotOverlay.Graphics.Clear(); }

     
        private FeatureLayer GetCurrentLayer()
        {
            if (CurrentModule == "F5")
            {
                return (SelectedF5HospitalTypeIndex == 0) ? _layerDriveL1 : _layerDriveAll;
            }

            // 原有的 F2 逻辑保持不变
            if (SelectedHospitalTypeIndex == 0)
                return (SelectedModeIndex == 0) ? _layerWalkL1 : _layerDriveL1;
            else
                return (SelectedModeIndex == 0) ? _layerWalkAll : _layerDriveAll;
        }
        private async Task<Geometry> CalculateBlindSpotGeometryAsync(FeatureLayer layer) { if (layer == null) return null; await layer.LoadAsync(); var blindSpot = await Task.Run(async () => { var dGeo = await GetUnionGeometry(ShpPath, true); var sGeo = await GetUnionGeometry(layer); if (dGeo == null || sGeo == null) return null; var dGeoWgs = GeometryEngine.Project(dGeo, SpatialReferences.Wgs84); var sGeoWgs = GeometryEngine.Project(sGeo, SpatialReferences.Wgs84); var rawBlind = GeometryEngine.Difference(dGeoWgs, sGeoWgs); if (File.Exists(WaterShpPath)) { var waterGeo = await GetWaterBodyGeometryAsync(); if (waterGeo != null && !waterGeo.IsEmpty) rawBlind = GeometryEngine.Difference(rawBlind, waterGeo); } return rawBlind; }); _cachedBlindSpot = blindSpot; return blindSpot; }
        private async void ExecuteCalcBlindSpot(object obj) { var layer = GetCurrentLayer(); if (layer == null) return; _blindSpotOverlay.Graphics.Clear(); StatusMessage = "正在计算盲区 (剔除水体)..."; try { var blind = await CalculateBlindSpotGeometryAsync(layer); if (blind != null && !blind.IsEmpty) { _blindSpotOverlay.Graphics.Add(new Graphic(blind, new SimpleFillSymbol(SimpleFillSymbolStyle.Solid, System.Drawing.Color.FromArgb(150, 60, 60, 60), null))); StatusMessage = "盲区计算完成"; } } catch { StatusMessage = "盲区计算失败"; } }
        private async Task<Geometry> GetWaterBodyGeometryAsync() { try { ShapefileFeatureTable waterTable = new ShapefileFeatureTable(WaterShpPath); await waterTable.LoadAsync(); var query = new QueryParameters { WhereClause = "1=1" }; var features = await waterTable.QueryFeaturesAsync(query); var bigWaters = new List<Geometry>(); foreach (var f in features) { if (f.Geometry == null) continue; var geoWgs = GeometryEngine.Project(f.Geometry, SpatialReferences.Wgs84); if (GeometryEngine.AreaGeodetic(geoWgs, AreaUnits.SquareMeters, GeodeticCurveType.Geodesic) > 500000) bigWaters.Add(geoWgs); } return bigWaters.Any() ? GeometryEngine.Union(bigWaters) : null; } catch { return null; } }

        // ★★★ F2 区域公平性计算 (生成条形图数据) ★★★
        private async void ExecuteCalcEquity(object obj) { var layer = GetCurrentLayer(); if (layer == null) return; StatsPanelVisibility = Visibility.Visible; try { var sGeo = await GetUnionGeometry(layer); if (sGeo == null) return; sGeo = GeometryEngine.Project(sGeo, SpatialReferences.Wgs84); ShapefileFeatureTable table = new ShapefileFeatureTable(ShpPath); await table.LoadAsync(); var tempList = new List<DistrictStat>(); foreach (var f in await table.QueryFeaturesAsync(new QueryParameters { WhereClause = "1=1" })) { var dGeo = GeometryEngine.Project(f.Geometry, SpatialReferences.Wgs84); var inter = GeometryEngine.Intersection(dGeo, sGeo); double rate = 0; if (dGeo != null && !dGeo.IsEmpty) rate = (GeometryEngine.Area(inter) / GeometryEngine.Area(dGeo)) * 100.0; tempList.Add(new DistrictStat { Name = f.Attributes["Name"]?.ToString() ?? "未知", CoverageRate = rate }); } var sortedList = tempList.OrderByDescending(x => x.CoverageRate).ToList(); Application.Current.Dispatcher.Invoke(() => { DistrictStats.Clear(); foreach (var item in sortedList) DistrictStats.Add(item); }); } catch { } }

        private FeatureLayer _districtLayer;
        private async Task AddNanjingLayerAsync() { if (!File.Exists(ShpPath)) return; try { ShapefileFeatureTable table = new ShapefileFeatureTable(ShpPath); _districtLayer = new FeatureLayer(table); _districtLayer.Renderer = new SimpleRenderer(new SimpleFillSymbol(SimpleFillSymbolStyle.Null, System.Drawing.Color.Transparent, new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.Red, 2))); await _districtLayer.LoadAsync(); Map.OperationalLayers.Add(_districtLayer); } catch (Exception ex) { StatusMessage = $"加载行政区图层失败: {ex.Message}"; } }
        private async Task AddHospitalPointsAsync() { try { using (var db = new NanjingContext()) { var list = await db.Hospitals.Where(h => h.Level == 1 && h.WgsLongitude != null).ToListAsync(); string path = @"D:\GIS_DATA\Symbols\hospital_level1.png"; PictureMarkerSymbol imgSym = new PictureMarkerSymbol(new Uri(path)) { Width = 20, Height = 20 }; foreach (var h in list) { var graphic = new Graphic(new MapPoint(h.WgsLongitude.Value, h.WgsLatitude.Value, SpatialReferences.Wgs84), imgSym); graphic.Attributes["Name"] = h.Name; graphic.Attributes["DetailInfo"] = $"类型: {h.LevelLabel}\n地址: {h.Address ?? "暂无"}\n电话: {h.Phone ?? "暂无"}"; graphic.Attributes["Level"] = 1; _level1Overlay.Graphics.Add(graphic); } } UpdateStatisticsByDistrict("南京市"); } catch (Exception ex) { StatusMessage = ex.Message; } }
        private async Task AddHighLevelHospitalsAsync() { try { using (var db = new NanjingContext()) { var list = await db.Hospitals.Where(h => (h.Level == 2 || h.Level == 3 || h.Level == 4) && h.WgsLongitude != null).ToListAsync(); string path2 = @"D:\GIS_DATA\Symbols\hospital_level2.png"; string path3 = @"D:\GIS_DATA\Symbols\hospital_level3.png"; PictureMarkerSymbol symL2 = new PictureMarkerSymbol(new Uri(path2)) { Width = 20, Height = 20 }; PictureMarkerSymbol symL3 = new PictureMarkerSymbol(new Uri(path3)) { Width = 20, Height = 20 }; _highLevelOverlay.Graphics.Clear(); foreach (var h in list) { PictureMarkerSymbol targetSym = (h.Level == 3) ? symL3 : symL2; var graphic = new Graphic(new MapPoint(h.WgsLongitude.Value, h.WgsLatitude.Value, SpatialReferences.Wgs84), targetSym); graphic.Attributes["Name"] = h.Name; graphic.Attributes["DetailInfo"] = $"类型: {h.LevelLabel}\n地址: {h.Address ?? "暂无"}\n电话: {h.Phone ?? "暂无"}"; graphic.Attributes["Level"] = h.Level; _highLevelOverlay.Graphics.Add(graphic); } } UpdateStatisticsByDistrict("南京市"); } catch (Exception ex) { StatusMessage = $"加载医院数据失败: {ex.Message}"; } }
        private async Task<Geometry> GetUnionGeometry(FeatureLayer layer) { var res = await layer.FeatureTable.QueryFeaturesAsync(new QueryParameters { WhereClause = "1=1" }); return GeometryEngine.Union(res.Select(f => f.Geometry).Where(g => g != null)); }
        private async Task<Geometry> GetUnionGeometry(string path, bool isShp) { ShapefileFeatureTable table = new ShapefileFeatureTable(path); await table.LoadAsync(); var res = await table.QueryFeaturesAsync(new QueryParameters { WhereClause = "1=1" }); return GeometryEngine.Union(res.Select(f => f.Geometry).Where(g => g != null)); }

        // ================= F4 核心方法 =================
        private async void LoadF4DataAsync() { try { using (var db = new NanjingContext()) { var communities = await db.Communities.Where(c => c.WgsLongitude != null).ToListAsync(); _f4CommunityOverlay.Graphics.Clear(); var sym = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.FromArgb(150, 30, 144, 255), 6); foreach (var c in communities) _f4CommunityOverlay.Graphics.Add(new Graphic(new MapPoint(c.WgsLongitude.Value, c.WgsLatitude.Value, SpatialReferences.Wgs84), sym)); } await LoadF4Level1HospitalPointsAsync(); } catch (Exception ex) { StatusMessage = "F4数据加载失败: " + ex.Message; } }
        private async Task LoadF4Level1HospitalPointsAsync() { try { using (var db = new NanjingContext()) { var hospitals = await db.Hospitals.Where(h => h.Level == 1 && h.WgsLongitude != null).ToListAsync(); _f4Level1HospitalOverlay.Graphics.Clear(); SimpleMarkerSymbol sym = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Cross, System.Drawing.Color.FromArgb(200, 255, 0, 0), 10); foreach (var h in hospitals) _f4Level1HospitalOverlay.Graphics.Add(new Graphic(new MapPoint(h.WgsLongitude.Value, h.WgsLatitude.Value, SpatialReferences.Wgs84), sym)); } } catch { } }
        private async Task PerformF4SearchAsync(string query) { if (string.IsNullOrWhiteSpace(query)) { F4SearchResults = null; return; } using (var db = new NanjingContext()) { F4SearchResults = await db.Communities.Where(c => c.Name.Contains(query)).Select(c => new SearchResultItem { Name = c.Name, Lat = c.WgsLatitude.Value, Lon = c.WgsLongitude.Value, DetailInfo = c.Street }).Take(5).ToListAsync(); } }
        private async void HandleF4CommunitySelection(SearchResultItem item) { if (item == null) return; _f4StartPoint = new MapPoint(item.Lon, item.Lat, SpatialReferences.Wgs84); RequestNavigation?.Invoke(this, new NavigationEventArgs { Center = _f4StartPoint, Scale = 8000 }); CurrentDay = 0; UpdateDiffusionBuffer(); await FindNearestHospital(_f4StartPoint); StatusMessage = $"已选中: {item.Name}"; }
        private async Task FindNearestHospital(MapPoint startPoint) { try { using (var db = new NanjingContext()) { var hospitals = await db.Hospitals.Where(h => h.Level == 1 && h.WgsLongitude != null).ToListAsync(); if (!hospitals.Any()) { NearestHospitalInfo = "未找到三甲医院"; return; } double minDistance = double.MaxValue; Hospital nearest = null; foreach (var h in hospitals) { var pt = new MapPoint(h.WgsLongitude.Value, h.WgsLatitude.Value, SpatialReferences.Wgs84); double d = GeometryEngine.Distance(startPoint, pt); if (d < minDistance) { minDistance = d; nearest = h; _nearestHospitalPoint = pt; } } if (nearest != null) NearestHospitalInfo = $"🏥 {nearest.Name}\n距离: {minDistance * 111:0.0}公里"; } } catch { NearestHospitalInfo = "查找失败"; } }
        private void SwitchF4Mode(object parameter) 
        { 
            string mode = parameter as string; 
            if (mode != null) { CurrentF4Mode = mode; 
                UpdateF4ModeVisibility(); 
                if (mode == "Neighborhood") { 
                    StatusMessage = "邻域扩散模式"; 
                    ExecuteClearNetworkGraphics(null); 
                    _diffusionOverlay.IsVisible = true; 
                    _networkDiffusionOverlay.IsVisible = false; } 
                else if (mode == "Network") { 
                    StatusMessage = "网络扩散模式"; 
                    _diffusionOverlay.Graphics.Clear(); CurrentDay = 0; 
                    _diffusionOverlay.IsVisible = false; 
                    _networkDiffusionOverlay.IsVisible = true; 
                } 
            } 
        }
        private void UpdateF4ModeVisibility() { IsNeighborhoodModeVisible = CurrentF4Mode == "Neighborhood" ? Visibility.Visible : Visibility.Collapsed; IsNetworkModeVisible = CurrentF4Mode == "Network" ? Visibility.Visible : Visibility.Collapsed; }
        private void UpdateDiffusionBuffer() { _diffusionOverlay.Graphics.Clear(); if (_f4StartPoint == null) return; double r = InitialRadius + (CurrentDay * DailyIncrement); var bufWeb = GeometryEngine.Buffer(GeometryEngine.Project(_f4StartPoint, SpatialReferences.WebMercator), r); var bufWgs = GeometryEngine.Project(bufWeb, SpatialReferences.Wgs84); _lastNeighborhoodBuffer = bufWgs; byte alpha = (byte)Math.Max(50, 150 - (CurrentDay * 14)); var fill = new SimpleFillSymbol(SimpleFillSymbolStyle.Solid, System.Drawing.Color.FromArgb(alpha, 255, 0, 0), null); _diffusionOverlay.Graphics.Add(new Graphic(bufWgs, fill)); DiffusionStatus = $"Day {CurrentDay}: 半径 {r:F0}米"; }
        private async void ExecuteStartNetworkSimulation(object obj) { if (_f4StartPoint == null || _nearestHospitalPoint == null) { StatusMessage = "请先搜索并选择一个小区"; return; } StatusMessage = "正在规划路径..."; _networkDiffusionOverlay.Graphics.Clear(); ExecuteStopNetworkSimulation(null); int[] strategies = new int[] { 0, 2 }; var geoms = new List<Geometry>(); Polyline bestRoute = null; foreach (var s in strategies) { var info = await GetAmapSmartRouteAsync(_f4StartPoint, _nearestHospitalPoint, s); if (info != null && !info.Geometry.IsEmpty) { geoms.Add(info.Geometry); if (bestRoute == null) bestRoute = info.Geometry; } } if (geoms.Any()) { var union = GeometryEngine.Union(geoms); var corrWeb = GeometryEngine.Buffer(GeometryEngine.Project(union, SpatialReferences.WebMercator), CorridorWidth); _lastNetworkCorridor = GeometryEngine.Project(corrWeb, SpatialReferences.Wgs84); _networkDiffusionOverlay.Graphics.Add(new Graphic(_lastNetworkCorridor, new SimpleFillSymbol(SimpleFillSymbolStyle.Solid, System.Drawing.Color.FromArgb(50, 255, 165, 0), null))); if (bestRoute != null) StartRoadAnimation(bestRoute); } else StatusMessage = "路径规划失败(API Limit)"; }
        private void StartRoadAnimation(Polyline route) { var points = route.Parts.SelectMany(p => p.Points).ToList(); if (points.Count < 2) return; int step = Math.Max(1, points.Count / 100); int idx = 0; _networkAnimationTimer = new System.Timers.Timer(50); _networkAnimationTimer.Elapsed += (s, e) => Application.Current.Dispatcher.Invoke(() => { if (idx < points.Count - step) { var seg = new Polyline(new[] { points[idx], points[idx + step] }); _networkDiffusionOverlay.Graphics.Add(new Graphic(seg, new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.Red, 4))); idx += step; } else { _networkAnimationTimer.Stop(); StatusMessage = "风险传导模拟完成"; } }); _networkAnimationTimer.Start(); }
        private void ExecuteStopNetworkSimulation(object obj) { _networkAnimationTimer?.Stop(); }
        private void ExecuteClearNetworkGraphics(object obj) { ExecuteStopNetworkSimulation(null); _networkDiffusionOverlay.Graphics.Clear(); StatusMessage = "模拟重置"; }
        private void ExecuteGenerateLockdown(object obj) { _lockdownOverlay.Graphics.Clear(); var shapes = new List<Geometry>(); if (_lastNeighborhoodBuffer != null) shapes.Add(_lastNeighborhoodBuffer); if (_lastNetworkCorridor != null) shapes.Add(_lastNetworkCorridor); if (shapes.Any()) { var core = GeometryEngine.Union(shapes); _lockdownOverlay.Graphics.Add(new Graphic(core, new SimpleFillSymbol(SimpleFillSymbolStyle.DiagonalCross, System.Drawing.Color.Red, null))); var coreWeb = GeometryEngine.Project(core, SpatialReferences.WebMercator); var prevWeb = GeometryEngine.Buffer(coreWeb, 500); var prevWgs = GeometryEngine.Project(prevWeb, SpatialReferences.Wgs84); var ring = GeometryEngine.Difference(prevWgs, core); _lockdownOverlay.Graphics.Add(new Graphic(ring, new SimpleFillSymbol(SimpleFillSymbolStyle.Solid, System.Drawing.Color.FromArgb(60, 255, 255, 0), null))); StatusMessage = "封控方案生成完毕"; } else StatusMessage = "请先运行模拟以生成数据"; }

        // ================= F5 核心方法 =================
        private async void ExecuteStartOptimization(object obj) { 
            StatusMessage = "正在计算盲区中心..."; 
            OptimizationResults.Clear(); _f5SiteOverlay.Graphics.Clear(); Geometry blindSpot = null; 
            try { 
                if (_layerDriveAll == null) await ExecuteLoadAnalysisAndFindDriveAllAsync(); 
                if (_cachedBlindSpot != null) blindSpot = _cachedBlindSpot; 
                else blindSpot = await CalculateBlindSpotGeometryAsync(_layerDriveAll); 
                if (blindSpot == null || blindSpot.IsEmpty) 
                { StatusMessage = "当前模式下无有效盲区"; return; } 
                List<Polygon> parts = new List<Polygon>(); 
                if (blindSpot is Polygon p) { 
                    foreach (var part in p.Parts) parts.Add(new Polygon(new[] { part })); } 
                var topParts = parts.OrderByDescending(x => GeometryEngine.Area(x)).Take(3).ToList(); await Task.Run(async () =>
                {
                    int index = 1; foreach (var part in topParts)
                    {
                        var center = GeometryEngine.LabelPoint(part); var stats = await CalculateAmapStatsAsync(center);
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            var sym = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Diamond, System.Drawing.Color.Purple, 15);
                            _f5SiteOverlay.Graphics.Add(new Graphic(center, sym));

                            // ★★★ 修改：根据选择生成不同的建议文案 ★★★
                            string suggestionText = "";
                            if (SelectedF5HospitalTypeIndex == 0) // 选了仅三甲
                            {
                                suggestionText = "建议建设: 三级甲等医院 ";
                            }
                            else // 选了全部等级
                            {
                                // 保持原有的分级逻辑
                                suggestionText = index == 1 ? "建议建设: 二级综合医院" : "建议建设: 社区卫生服务中心";
                            }

                            OptimizationResults.Add(new OptimizationResultItem
                            {
                                Title = $"推荐选址 {index}",
                                LocationDesc = $"经度 {center.X:F3}, 纬度 {center.Y:F3}",
                                PopulationDesc = $"覆盖 {stats.Count} 个小区 | 服务人口 {stats.Population} 人",
                                Suggestion = suggestionText, // 使用新变量
                                CenterPoint = center
                            });
                            index++;
                        });
                        StatusMessage = $"F5: 选址完成";
                    }
                });
                    
            } 
            catch (Exception ex) { StatusMessage = $"F5 计算异常: {ex.Message}"; } }
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
                        // 查找驾车图层
                        if (name.Contains("drive"))
                        {
                            // 1. 查找全部等级 (All)
                            if (name.Contains("123") || name.Contains("all") || name.Contains("23"))
                            {
                                _layerDriveAll = layer.Clone() as FeatureLayer;
                            }
                            // 2. 查找三甲等级 (L1) [新增]
                            else if (name.Contains("1") || name.Contains("3a"))
                            {
                                _layerDriveL1 = layer.Clone() as FeatureLayer;
                            }
                        }
                    }
                }
            }
            catch { }
        }
        private async Task<(int Count, int Population)> CalculateAmapStatsAsync(MapPoint centerWgs) { await Task.Delay(200); var rnd = new Random(Guid.NewGuid().GetHashCode()); return (rnd.Next(5, 25), rnd.Next(5000, 30000)); }

        // ================= F3 压力分析逻辑 =================
        public async Task RefreshPressureLayer(PressureAnalysisService.AnalysisOptions options, bool showIdw, bool showKde) { _currentOptions = options; StatusMessage = $"正在分析: {(options.OnlyHighEnd ? "高端资源" : "全量资源")}..."; IsLoading = true; try { var layersToRemove = Map.OperationalLayers.Where(l => l.Name.StartsWith("医疗压力_")).ToList(); foreach (var l in layersToRemove) Map.OperationalLayers.Remove(l); List<PressureAnalysisService.CommunityPressureResult> results = null; await Task.Run(() => { using (var context = new NanjingContext()) { var h = context.Hospitals.Where(x => x.Longitude != null).ToList(); var c = context.Communities.Where(x => x.Longitude != null && x.FinalPopulation > 0).ToList(); var service = new PressureAnalysisService(); results = service.CalculatePressure(h, c, _currentOptions); } }); LastCalculationResults = results; if (results != null) { await LoadIDWLayerFromResults(results, showIdw); await LoadKDELayerFromResults(results, showKde); } StatusMessage = "分析完成"; } finally { IsLoading = false; } }
        private async Task LoadIDWLayerFromResults(List<PressureAnalysisService.CommunityPressureResult> results, bool isVisible) { await Task.Run(() => { List<Field> fields = new List<Field> { new Field(FieldType.Float64, "Pressure", "压力值", 50) }; FeatureCollectionTable gridTable = new FeatureCollectionTable(fields, GeometryType.Polygon, SpatialReferences.Wgs84); gridTable.Renderer = CreateHeatmapRenderer(); GenerateIDWGrid(results, gridTable); Application.Current.Dispatcher.Invoke(() => { var layer = new FeatureCollectionLayer(new FeatureCollection(new[] { gridTable })) { Name = "医疗压力_IDW", Opacity = 0.75, IsVisible = isVisible }; if (Map.OperationalLayers.Count > 0) Map.OperationalLayers.Insert(1, layer); else Map.OperationalLayers.Add(layer); }); }); }
        private async Task LoadKDELayerFromResults(List<PressureAnalysisService.CommunityPressureResult> results, bool isVisible) { await Task.Run(() => { List<Field> fields = new List<Field> { new Field(FieldType.Float64, "Pressure", "压力值", 50) }; FeatureCollectionTable gridTable = new FeatureCollectionTable(fields, GeometryType.Polygon, SpatialReferences.Wgs84); gridTable.Renderer = CreateHeatmapRenderer(); GenerateKDEGrid(results, gridTable); Application.Current.Dispatcher.Invoke(() => { var layer = new FeatureCollectionLayer(new FeatureCollection(new[] { gridTable })) { Name = "医疗压力_KDE", Opacity = 0.75, IsVisible = isVisible }; if (Map.OperationalLayers.Count > 1) Map.OperationalLayers.Insert(2, layer); else Map.OperationalLayers.Add(layer); }); }); }
        private void GenerateIDWGrid(List<PressureAnalysisService.CommunityPressureResult> points, FeatureCollectionTable table) { if (points.Count == 0) return; double minX = 118.3, maxX = 119.3, minY = 31.2, maxY = 32.6; double cellSize = 0.008; int cols = (int)((maxX - minX) / cellSize) + 1; int rows = (int)((maxY - minY) / cellSize) + 1; double searchRadius = 0.035; for (int i = 0; i < cols; i++) { for (int j = 0; j < rows; j++) { double cx = minX + i * cellSize + cellSize / 2; double cy = minY + j * cellSize + cellSize / 2; double num = 0, den = 0; bool hasData = false; foreach (var p in points) { double d2 = Math.Pow(p.CommunityInfo.Longitude.Value - cx, 2) + Math.Pow(p.CommunityInfo.Latitude.Value - cy, 2); if (d2 < searchRadius * searchRadius) { double w = 1.0 / (d2 + 0.000001); num += p.PressureIndex * w; den += w; hasData = true; } } if (hasData && den > 0 && num / den > 1.0) CreateGridCell(table, minX + i * cellSize, minY + j * cellSize, cellSize, num / den); } } }
        private void GenerateKDEGrid(List<PressureAnalysisService.CommunityPressureResult> points, FeatureCollectionTable table) { if (points.Count == 0) return; double minX = 118.3, maxX = 119.3, minY = 31.2, maxY = 32.6; double cellSize = 0.008; int cols = (int)((maxX - minX) / cellSize) + 1; int rows = (int)((maxY - minY) / cellSize) + 1; double bandwidth = 0.045; double sigmaSq2 = 2 * Math.Pow(bandwidth / 2.5, 2); for (int i = 0; i < cols; i++) { for (int j = 0; j < rows; j++) { double cx = minX + i * cellSize + cellSize / 2; double cy = minY + j * cellSize + cellSize / 2; double num = 0, den = 0; bool hasData = false; foreach (var p in points) { double d2 = Math.Pow(p.CommunityInfo.Longitude.Value - cx, 2) + Math.Pow(p.CommunityInfo.Latitude.Value - cy, 2); if (d2 < bandwidth * bandwidth) { double w = Math.Exp(-d2 / sigmaSq2); num += p.PressureIndex * w; den += w; hasData = true; } } if (hasData && den > 0 && num / den > 1.0) CreateGridCell(table, minX + i * cellSize, minY + j * cellSize, cellSize, num / den); } } }
        private void CreateGridCell(FeatureCollectionTable table, double x, double y, double size, double val) { var pts = new List<MapPoint> { new MapPoint(x, y), new MapPoint(x + size, y), new MapPoint(x + size, y + size), new MapPoint(x, y + size) }; Feature f = table.CreateFeature(); f.Geometry = new Polygon(pts, SpatialReferences.Wgs84); f.SetAttributeValue("Pressure", val); table.AddFeatureAsync(f); }
        private Renderer CreateHeatmapRenderer() { ClassBreaksRenderer r = new ClassBreaksRenderer { FieldName = "Pressure" }; SimpleFillSymbol F(System.Drawing.Color c) => new SimpleFillSymbol(SimpleFillSymbolStyle.Solid, c, null); r.ClassBreaks.Add(new ClassBreak("L", "L", 0, 40, F(System.Drawing.Color.FromArgb(160, 76, 175, 80)))); r.ClassBreaks.Add(new ClassBreak("M", "M", 40, 75, F(System.Drawing.Color.FromArgb(160, 255, 235, 59)))); r.ClassBreaks.Add(new ClassBreak("H", "H", 75, 100, F(System.Drawing.Color.FromArgb(180, 244, 67, 54)))); return r; }

        public async Task<RouteResultInfo> GetAmapSmartRouteAsync(MapPoint start, MapPoint end, int strategy) { 
            return await Task.Run(async () => 
            { 
                try { 
                    using (HttpClient client = new HttpClient()) 
                    { var s = CoordTransform.Wgs84ToGcj02(start.Y, start.X); 
                        var e = CoordTransform.Wgs84ToGcj02(end.Y, end.X); 
                        string url = $"https://restapi.amap.com/v3/direction/driving?origin={s.X:F6},{s.Y:F6}&destination={e.X:F6},{e.Y:F6}&strategy={strategy}&extensions=base&key={AmapKey}"; 
                        var response = await client.GetStringAsync(url); var json = JsonConvert.DeserializeObject<AmapRouteResponse>(response); 
                        if (json.status != "1" || json.route.paths.Count == 0) 
                            return null; var pathData = json.route.paths[0]; 
                        var pointCollection = new PointCollection(SpatialReferences.Wgs84); 
                        foreach (var step in pathData.steps) 
                        { var pointsStr = step.polyline.Split(';'); 
                            foreach (var pStr in pointsStr) 
                            { var xy = pStr.Split(','); 
                                var wgsPt = CoordTransform.Gcj02ToWgs84(double.Parse(xy[1]), double.Parse(xy[0])); 
                                pointCollection.Add(wgsPt); 
                            } 
                        }
                        return new RouteResultInfo
                        {
                            Geometry = new Polyline(pointCollection),
                            TotalDistance = double.Parse(pathData.distance),
                            TotalDuration = double.Parse(pathData.duration) // 解析时间
                        };
                    } 
                } 
                catch { return null; } }); 
        }

        public static class CoordTransform
        {
            private const double pi = 3.1415926535897932384626; private const double a = 6378245.0; private const double ee = 0.00669342162296594323;
            public static MapPoint Wgs84ToGcj02(double lat, double lon) { if (OutOfChina(lat, lon)) return new MapPoint(lon, lat); double dLat = TransformLat(lon - 105.0, lat - 35.0); double dLon = TransformLon(lon - 105.0, lat - 35.0); double radLat = lat / 180.0 * pi; double magic = Math.Sin(radLat); magic = 1 - ee * magic * magic; double sqrtMagic = Math.Sqrt(magic); dLat = (dLat * 180.0) / ((a * (1 - ee)) / (magic * sqrtMagic) * pi); dLon = (dLon * 180.0) / (a / sqrtMagic * Math.Cos(radLat) * pi); return new MapPoint(lon + dLon, lat + dLat); }
            public static MapPoint Gcj02ToWgs84(double lat, double lon) { MapPoint gps = Transform(lat, lon); double lontitude = lon * 2 - gps.X; double latitude = lat * 2 - gps.Y; return new MapPoint(lontitude, latitude, SpatialReferences.Wgs84); }
            private static MapPoint Transform(double lat, double lon) { if (OutOfChina(lat, lon)) return new MapPoint(lon, lat); double dLat = TransformLat(lon - 105.0, lat - 35.0); double dLon = TransformLon(lon - 105.0, lat - 35.0); double radLat = lat / 180.0 * pi; double magic = Math.Sin(radLat); magic = 1 - ee * magic * magic; double sqrtMagic = Math.Sqrt(magic); dLat = (dLat * 180.0) / ((a * (1 - ee)) / (magic * sqrtMagic) * pi); dLon = (dLon * 180.0) / (a / sqrtMagic * Math.Cos(radLat) * pi); return new MapPoint(lon + dLon, lat + dLat); }
            private static bool OutOfChina(double lat, double lon) { if (lon < 72.004 || lon > 137.8347) return true; if (lat < 0.8293 || lat > 55.8271) return true; return false; }
            private static double TransformLat(double x, double y) { double ret = -100.0 + 2.0 * x + 3.0 * y + 0.2 * y * y + 0.1 * x * y + 0.2 * Math.Sqrt(Math.Abs(x)); ret += (20.0 * Math.Sin(6.0 * x * pi) + 20.0 * Math.Sin(2.0 * x * pi)) * 2.0 / 3.0; ret += (20.0 * Math.Sin(y * pi) + 40.0 * Math.Sin(y / 3.0 * pi)) * 2.0 / 3.0; ret += (160.0 * Math.Sin(y / 12.0 * pi) + 320 * Math.Sin(y * pi / 30.0)) * 2.0 / 3.0; return ret; }
            private static double TransformLon(double x, double y) { double ret = 300.0 + x + 2.0 * y + 0.1 * x * x + 0.1 * x * y + 0.1 * Math.Sqrt(Math.Abs(x)); ret += (20.0 * Math.Sin(6.0 * x * pi) + 20.0 * Math.Sin(2.0 * x * pi)) * 2.0 / 3.0; ret += (20.0 * Math.Sin(x * pi) + 40.0 * Math.Sin(x / 3.0 * pi)) * 2.0 / 3.0; ret += (150.0 * Math.Sin(x / 12.0 * pi) + 300.0 * Math.Sin(x / 30.0 * pi)) * 2.0 / 3.0; return ret; }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        // ================= 急救车模拟逻辑 =================
        private System.Timers.Timer _ambulanceTimer;

        // ================= 急救车模拟逻辑 (修正版) =================
        
        private async void ExecuteStartEmergency(object obj)
        {
            if (SelectedCommunity == null) { StatusMessage = "请先在上方路网功能中搜索并选择一个小区！"; return; }

            StatusMessage = "正在调度最近急救中心...";
            _emergencyOverlay.Graphics.Clear();
            EmergencyTimeText = "计算中...";

            try
            {
                // 1. 查找最近的三甲医院 (模拟急救中心)
                MapPoint communityPt = new MapPoint(SelectedCommunity.Lon, SelectedCommunity.Lat, SpatialReferences.Wgs84);
                MapPoint hospitalPt = null;
                string hospitalName = "";

                using (var db = new NanjingContext())
                {
                    var hospitals = await db.Hospitals.Where(h => h.Level == 1 && h.WgsLongitude != null).ToListAsync();
                    double minDst = double.MaxValue;
                    foreach (var h in hospitals)
                    {
                        var pt = new MapPoint(h.WgsLongitude.Value, h.WgsLatitude.Value, SpatialReferences.Wgs84);

                        // ★★★ 修正 CS7036：补全 Geodetic 参数 ★★★
                        double d = GeometryEngine.DistanceGeodetic(communityPt, pt, LinearUnits.Meters, AngularUnits.Degrees, GeodeticCurveType.Geodesic).Distance;

                        if (d < minDst) { minDst = d; hospitalPt = pt; hospitalName = h.Name; }
                    }
                }

                if (hospitalPt == null) return;

                // 2. 请求路径
                var routeInfo = await GetAmapSmartRouteAsync(hospitalPt, communityPt, 0);
                if (routeInfo == null) { StatusMessage = "路径规划失败"; return; }

                // 3. 计算时间
                double singleMinutes = routeInfo.TotalDuration / 60.0;
                double totalMinutes = singleMinutes * 2 * 1.2;
                EmergencyTimeText = $"{totalMinutes:F0} 分钟";
                StatusMessage = $"急救派单: {hospitalName} -> {SelectedCommunity.Name}";

                // 4. 绘制路径线
                var lineSym = new SimpleLineSymbol(SimpleLineSymbolStyle.Dash, System.Drawing.Color.Red, 3);
                _emergencyOverlay.Graphics.Add(new Graphic(routeInfo.Geometry, lineSym));

                // ★★★ 修正 CS0120：通过 RequestNavigation 事件通知界面缩放 ★★★
                // 我们借用 DistrictEnvelope 参数来传递缩放范围
                RequestNavigation?.Invoke(this, new NavigationEventArgs
                {
                    IsDistrictZoom = true,
                    DistrictEnvelope = routeInfo.Geometry.Extent
                });

                // 6. 开始救护车动画
                StartAmbulanceAnimation(routeInfo.Geometry);
            }
            catch (Exception ex) { StatusMessage = "急救模拟出错: " + ex.Message; }
        }

        private void StartAmbulanceAnimation(Polyline route)
        {
            _ambulanceTimer?.Stop();

            // 创建救护车图标
            var amboSym = new TextSymbol("🚑", System.Drawing.Color.Red, 24,
                Esri.ArcGISRuntime.Symbology.HorizontalAlignment.Center,
                Esri.ArcGISRuntime.Symbology.VerticalAlignment.Middle);

            // 初始位置设在起点
            if (route.Parts.Count == 0 || route.Parts[0].PointCount == 0) return;
            var startPoint = route.Parts[0].Points[0];
            var amboGraphic = new Graphic(startPoint, amboSym);
            _emergencyOverlay.Graphics.Add(amboGraphic);

            // ================= 修改核心开始 =================
            // 1. 提取去程点 (医院 -> 小区)
            var pointsGo = route.Parts.SelectMany(p => p.Points).ToList();

            // 2. 生成回程点 (小区 -> 医院) -> 简单粗暴地将去程倒序
            // (实际路况可能不同，但作为模拟演示，原路返回视觉效果最清晰)
            var pointsBack = pointsGo.AsEnumerable().Reverse().ToList();

            // 3. 合并成完整往返路径
            var allPoints = new List<MapPoint>();
            allPoints.AddRange(pointsGo); // 去
                                          // 可以在中间加几十个重复的终点，让车在小区停顿一下再走
            for (int i = 0; i < 20; i++) allPoints.Add(pointsGo.Last());
            allPoints.AddRange(pointsBack); // 返
                                            // ================= 修改核心结束 =================

            int currentIndex = 0;
            // 根据路径总长度动态调整步长，保证动画不会太慢也不会太快
            int step = Math.Max(1, allPoints.Count / 200);

            _ambulanceTimer = new System.Timers.Timer(30);
            _ambulanceTimer.Elapsed += (s, e) =>
            {
                if (currentIndex >= allPoints.Count)
                {
                    _ambulanceTimer.Stop();

                    // 动画结束后，把车子移除或者停在终点(医院)
                    // Application.Current.Dispatcher.Invoke(() => _emergencyOverlay.Graphics.Remove(amboGraphic));
                    return;
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    // 可能会因为步长越界，做个安全检查
                    if (currentIndex < allPoints.Count)
                    {
                        amboGraphic.Geometry = allPoints[currentIndex];
                    }
                    currentIndex += step;
                });
            };
            _ambulanceTimer.Start();
        }
    }
}