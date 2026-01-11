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

    // ================= ViewModel =================
    public class MapViewModel : INotifyPropertyChanged
    {
        // 1. 路径配置
        private const string ShpPath = @"D:\GIS_DATA\Data\南京区县.shp";
        private const string MmpkPath = @"D:\GIS_DATA\Data\nanjing.mmpk";

        // 2. 核心属性
        private Map _map;
        private string _statusMessage = "系统初始化...";

        public GraphicsOverlayCollection GraphicsOverlays { get; set; } = new GraphicsOverlayCollection();
        private GraphicsOverlay _blindSpotOverlay = new GraphicsOverlay { Id = "BlindSpot" };
        private GraphicsOverlay _level1Overlay = new GraphicsOverlay { Id = "Level1Hospitals" };
        private GraphicsOverlay _highLevelOverlay = new GraphicsOverlay { Id = "HighLevelHospitals", MinScale = 50000 };

        // 添加F4专用的图层
        private GraphicsOverlay _f4CommunityOverlay = new GraphicsOverlay { Id = "F4Communities" };
        private GraphicsOverlay _f4Level1HospitalOverlay = new GraphicsOverlay { Id = "F4Level1Hospitals" };
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

        //添加F4属性
        private Visibility _isF4ConfigVisible = Visibility.Collapsed;
        public Visibility IsF4ConfigVisible {get => _isF4ConfigVisible;set { _isF4ConfigVisible = value; OnPropertyChanged(); } }
        // F4模块模式控制
        private string _currentF4Mode = "Neighborhood";
        private Visibility _isNeighborhoodModeVisible = Visibility.Visible;
        private Visibility _isNetworkModeVisible = Visibility.Collapsed;

        public string CurrentF4Mode
        {
            get => _currentF4Mode;
            set
            {
                _currentF4Mode = value;
                OnPropertyChanged();
                UpdateF4ModeVisibility();
            }
        }

        public Visibility IsNeighborhoodModeVisible
        {
            get => _isNeighborhoodModeVisible;
            set { _isNeighborhoodModeVisible = value; OnPropertyChanged(); }
        }

        public Visibility IsNetworkModeVisible
        {
            get => _isNetworkModeVisible;
            set { _isNetworkModeVisible = value; OnPropertyChanged(); }
        }

        // 网络扩散属性
        private string _networkStartSearchText;
        public string NetworkStartSearchText
        {
            get => _networkStartSearchText;
            set { _networkStartSearchText = value; OnPropertyChanged(); }
        }

        private List<SearchResultItem> _networkStartResults;
        public List<SearchResultItem> NetworkStartResults
        {
            get => _networkStartResults;
            set { _networkStartResults = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsShowingNetworkStartResults)); }
        }

        public bool IsShowingNetworkStartResults => NetworkStartResults?.Any() == true && !string.IsNullOrWhiteSpace(NetworkStartSearchText);

        private SearchResultItem _selectedNetworkStartResult;
        public SearchResultItem SelectedNetworkStartResult
        {
            get => _selectedNetworkStartResult;
            set
            {
                _selectedNetworkStartResult = value;
                OnPropertyChanged();
                if (value != null) HandleNetworkStartSelection(value);
            }
        }

        private string _nearestHospitalInfo = "未选择起始小区";
        public string NearestHospitalInfo{get => _nearestHospitalInfo;set { _nearestHospitalInfo = value; OnPropertyChanged(); } }

        private double _corridorWidth = 300;
        public double CorridorWidth
        {
            get => _corridorWidth;
            set { _corridorWidth = value; OnPropertyChanged(); }
        }

        private int _animationSpeed = 100;
        public int AnimationSpeed
        {
            get => _animationSpeed;
            set { _animationSpeed = value; OnPropertyChanged(); }
        }

        // --- 4. F1 功能属性 ---
        public bool IsF1Active { get; set; } = false;

        // ★★★ 新增：当前模块标记 (用于按钮高亮) ★★★
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

        private void CheckF2Update()
        {
            if (_isAnalysisStarted) UpdateLayerVisibility();
            if (StatsPanelVisibility == Visibility.Visible) ExecuteCalcEquity(null);
        }

        // --- 6. 命令 ---
        public ICommand SearchCommand { get; }
        public ICommand LoadAnalysisCommand { get; }
        public ICommand CalcBlindSpotCommand { get; }
        public ICommand CalcEquityCommand { get; }
        public ICommand SwitchModuleCommand { get; }
        public event EventHandler<NavigationEventArgs> RequestNavigation;

        // --- F4 命令 ---
        public ICommand StartDiffusionCommand { get; }
        public ICommand DiffusionSearchCommand { get; }
        public ICommand UpdateDiffusionCommand { get; }  // 添加这行
        public ICommand SwitchF4ModeCommand { get; }
        public ICommand NetworkSearchStartCommand { get; }
        public ICommand StartNetworkSimulationCommand { get; }
        public ICommand StopNetworkSimulationCommand { get; }
        public ICommand ClearNetworkGraphicsCommand { get; }

        // --- F4 功能属性 ---
        private bool _isDiffusionActive = false;
        public bool IsDiffusionActive
        {
            get => _isDiffusionActive;
            set { _isDiffusionActive = value; OnPropertyChanged(); }
        }

        private string _diffusionSearchText;
        public string DiffusionSearchText
        {
            get => _diffusionSearchText;
            set { _diffusionSearchText = value; OnPropertyChanged(); }
        }

        private List<SearchResultItem> _diffusionResults;
        public List<SearchResultItem> DiffusionResults
        {
            get => _diffusionResults;
            set { _diffusionResults = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsShowingDiffusionResults)); }
        }

        public bool IsShowingDiffusionResults => DiffusionResults?.Any() == true && !string.IsNullOrWhiteSpace(DiffusionSearchText);

        private SearchResultItem _selectedDiffusionResult;
        public SearchResultItem SelectedDiffusionResult
        {
            get => _selectedDiffusionResult;
            set { _selectedDiffusionResult = value; OnPropertyChanged(); if (value != null) SelectDiffusionStartPoint(value); }
        }

        private int _currentDay = 0;
        public int CurrentDay
        {
            get => _currentDay;
            set
            {
                if (_currentDay != value)
                {
                    _currentDay = value;
                    OnPropertyChanged();
                    UpdateDiffusionBuffer(); // 这里！拖动Slider时自动更新
                }
            }
        }

        private double _initialRadius = 500;
        public double InitialRadius
        {
            get => _initialRadius;
            set
            {
                if (_initialRadius != value)
                {
                    _initialRadius = value;
                    OnPropertyChanged();
                    UpdateDiffusionBuffer(); // 这里！修改参数时自动更新
                }
            }
        }

        private double _dailyIncrement = 200;
        public double DailyIncrement
        {
            get => _dailyIncrement;
            set
            {
                if (_dailyIncrement != value)
                {
                    _dailyIncrement = value;
                    OnPropertyChanged();
                    UpdateDiffusionBuffer(); // 这里！修改参数时自动更新
                }
            }
        }

        private string _diffusionStatus = "准备就绪";
        public string DiffusionStatus
        {
            get => _diffusionStatus;
            set { _diffusionStatus = value; OnPropertyChanged(); }
        }

        // 网络扩散状态
        private bool _isNetworkSimulationRunning = false;
        private MapPoint _networkStartPoint = null;
        private MapPoint _nearestHospitalPoint = null;
        private FeatureLayer _roadNetworkLayer = null;
        private GraphicsOverlay _networkDiffusionOverlay = new GraphicsOverlay { Id = "NetworkDiffusion" };
        private System.Timers.Timer _networkAnimationTimer = null;
        private List<Feature> _selectedRoads = new List<Feature>();
        // 路径常量
        private const string RoadNetworkPath = @"D:\GIS_DATA\Data\road_project.shp";


        private GraphicsOverlay _diffusionOverlay = new GraphicsOverlay { Id = "DiffusionOverlay" };
        private MapPoint _diffusionStartPoint = null;
        private System.Timers.Timer _simulationTimer;


        public MapViewModel()
        {
            GraphicsOverlays.Add(_blindSpotOverlay);
            GraphicsOverlays.Add(_level1Overlay);
            GraphicsOverlays.Add(_highLevelOverlay);
            GraphicsOverlays.Add(_diffusionOverlay);
            // 添加F4专用的两个图层 
            GraphicsOverlays.Add(_f4CommunityOverlay);
            GraphicsOverlays.Add(_f4Level1HospitalOverlay);
            GraphicsOverlays.Add(_networkDiffusionOverlay);


            SearchCommand = new RelayCommand(async () => { SelectedResult = null; await PerformSearchAsync(SearchText); OnPropertyChanged(nameof(IsShowingResults)); });
            LoadAnalysisCommand = new RelayCommand(ExecuteLoadAnalysis);
            CalcBlindSpotCommand = new RelayCommand(ExecuteCalcBlindSpot);
            CalcEquityCommand = new RelayCommand(ExecuteCalcEquity);
            SwitchModuleCommand = new RelayCommand(ExecuteSwitchModule);

            // 修改F4命令 - 移除模拟按钮，添加更新命令
            StartDiffusionCommand = new RelayCommand(ExecuteStartDiffusion);
            DiffusionSearchCommand = new RelayCommand(async () => await PerformDiffusionSearchAsync(DiffusionSearchText));
            UpdateDiffusionCommand = new RelayCommand(ExecuteUpdateDiffusion);  // 添加这行
                                                                                // 新增命令
            SwitchF4ModeCommand = new RelayCommand(SwitchF4Mode);
            NetworkSearchStartCommand = new RelayCommand(async () => await PerformNetworkStartSearchAsync(NetworkStartSearchText));
            StartNetworkSimulationCommand = new RelayCommand(ExecuteStartNetworkSimulation);
            StopNetworkSimulationCommand = new RelayCommand(ExecuteStopNetworkSimulation);
            ClearNetworkGraphicsCommand = new RelayCommand(ExecuteClearNetworkGraphics);

            InitializeMap();
        }

        // ================= 模块切换 (修改) =================
        private void ExecuteSwitchModule(object parameter)
        {
            string module = parameter as string;
            CurrentModule = module;
            LeftPanelVisibility = Visibility.Visible;

            //清理F4数据，防止重复加载
            if (module != "F4")
            {
                _f4CommunityOverlay.Graphics.Clear();
                _f4Level1HospitalOverlay.Graphics.Clear();
                _f4CommunityOverlay.IsVisible = false;
                _f4Level1HospitalOverlay.IsVisible = false;
            }


            // 隐藏所有其他面板
            IsF1ConfigVisible = Visibility.Collapsed;
            IsF2ConfigVisible = Visibility.Collapsed;
            IsF4ConfigVisible = Visibility.Collapsed;
            F1StatsVisibility = Visibility.Collapsed;
            StatsPanelVisibility = Visibility.Collapsed;

            // 隐藏所有图层
            _level1Overlay.IsVisible = false;
            _highLevelOverlay.IsVisible = false;
            _blindSpotOverlay.IsVisible = false;
            _diffusionOverlay.IsVisible = false;
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
            else if (module == "F4")  // 添加F4分支
            {
                IsF1Active = false;
                StatusMessage = "F4 风险模拟: 邻域扩散模式";
                IsF4ConfigVisible = Visibility.Visible;
                _diffusionOverlay.IsVisible = true;

                //  显示F4专用图层 
                _f4CommunityOverlay.IsVisible = true;
                _f4Level1HospitalOverlay.IsVisible = true;

                // 设置小区图层的显示条件：只有放大到一定比例尺时才显示 
                // 设置最小显示比例尺，当缩放级别小于1:50000时不显示小区点
                _f4CommunityOverlay.MinScale = 50000;

                // ★★★ 医院图层始终显示 ★★★
                _f4Level1HospitalOverlay.MinScale = 0; // 0表示始终显示

                // 隐藏其他模块的图层
                _level1Overlay.IsVisible = false;
                _highLevelOverlay.IsVisible = false;
                _blindSpotOverlay.IsVisible = false;

                // 确保网络扩散图层可见
                _networkDiffusionOverlay.IsVisible = true;


                // 加载F4数据 
                LoadF4DataAsync();

                //  简化：直接激活邻域扩散 
                IsDiffusionActive = true;

                // 重置扩散状态
                _diffusionStartPoint = null;
                _diffusionOverlay.Graphics.Clear();
                CurrentDay = 0;
                DiffusionStatus = "选择初始小区开始模拟";
                // 重置状态
                CurrentF4Mode = "Neighborhood"; // 默认显示邻域扩散

            }
        }
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

                // ★★★ 默认初始化 F1 模块 ★★★
                ExecuteSwitchModule("F1");
            }
            catch (Exception ex) { StatusMessage = $"初始化失败: {ex.Message}"; }
        }

        // ================= F1 功能 =================
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

        // ================= F2 功能 =================
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

        // --- F4 方法实现 ---
        private void ExecuteStartDiffusion(object obj)
        {
            IsDiffusionActive = true;
            DiffusionStatus = "邻域扩散模式已激活，请选择初始小区";
            // 确保扩散图层可见
            _diffusionOverlay.IsVisible = true;
        }
        private async Task PerformDiffusionSearchAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                DiffusionResults = null;
                return;
            }

            using (var db = new NanjingContext())
            {
                var communities = await db.Communities
                    .Where(c => c.Name.Contains(query) && c.WgsLongitude != null)
                    .Select(c => new SearchResultItem
                    {
                        Name = c.Name,
                        Type = "住宅小区",
                        Lat = c.WgsLatitude.Value,
                        Lon = c.WgsLongitude.Value,
                        DetailInfo = $"街道:{c.Street}\n人口:{c.FinalPopulation ?? 0}"
                    })
                    .Take(5)
                    .ToListAsync();

                DiffusionResults = communities;
            }
        }

        private void SelectDiffusionStartPoint(SearchResultItem item)
        {
            if (item == null) return;

            _diffusionStartPoint = new MapPoint(item.Lon, item.Lat, SpatialReferences.Wgs84);

            // 清除之前的图形
            _diffusionOverlay.Graphics.Clear();

            // 缩放到小区位置
            RequestNavigation?.Invoke(this, new NavigationEventArgs
            {
                Center = _diffusionStartPoint,
                Scale = 5000,
                ResultItem = item
            });

            DiffusionStatus = $"已选择初始小区: {item.Name}";
            CurrentDay = 0; // 重置到第0天

            // ★★★ 直接显示扩散圈，不需要延迟 ★★★
            UpdateDiffusionBuffer();
        }

        private void UpdateDiffusionBuffer()
        {
            try
            {
                _diffusionOverlay.Graphics.Clear();

                if (_diffusionStartPoint == null || CurrentDay < 0) return;

                // 计算当前天的扩散半径（米）
                double currentRadiusMeters = InitialRadius + (CurrentDay * DailyIncrement);

                // 确保半径不会过大（比如限制在10公里以内）
                if (currentRadiusMeters > 10000)
                {
                    currentRadiusMeters = 10000;
                    DiffusionStatus = $"第 {CurrentDay} 天 - 扩散半径已限制为10公里";
                }
                else
                {
                    DiffusionStatus = $"第 {CurrentDay} 天 - 扩散半径: {currentRadiusMeters:F0}米";
                }

                // 将WGS84点转换为Web墨卡托投影（单位：米）
                var webMercatorPoint = GeometryEngine.Project(
                    _diffusionStartPoint,
                    SpatialReferences.WebMercator) as MapPoint;

                if (webMercatorPoint == null)
                {
                    DiffusionStatus = "坐标转换失败";
                    return;
                }

                // 使用Web墨卡托坐标创建缓冲区（单位：米）
                var buffer = GeometryEngine.Buffer(webMercatorPoint, currentRadiusMeters);

                // 将缓冲区转换回WGS84坐标以便显示
                var bufferWgs84 = GeometryEngine.Project(buffer, SpatialReferences.Wgs84);

                // 修改颜色计算逻辑：随着天数增加，红色逐渐变浅（透明度增加）
                // 第0天最深红（255,0,0），第7天最浅红（255,200,200）
                int red = 255;

                // 绿色和蓝色分量从0增加到200（让红色逐渐变粉、变浅）
                int greenAndBlue = (int)((CurrentDay / 7.0) * 200);
                greenAndBlue = Math.Min(greenAndBlue, 200);

                // 透明度也从较高逐渐降低（第0天150，第7天50）
                byte alpha = (byte)(150 - (CurrentDay * 14)); // 150, 136, 122, 108, 94, 80, 66, 52
                alpha = Math.Max((byte)50, alpha); // 确保不低于50

                System.Drawing.Color color = System.Drawing.Color.FromArgb(
                    alpha,
                    red,
                    greenAndBlue,
                    greenAndBlue);

                // 修改边框颜色：也随着天数变浅
                System.Drawing.Color borderColor = System.Drawing.Color.FromArgb(
                    255,
                    red,
                    greenAndBlue,
                    greenAndBlue);

                // 创建图形
                var fillSymbol = new SimpleFillSymbol(
                    SimpleFillSymbolStyle.Solid,
                    color,
                    new SimpleLineSymbol(
                        SimpleLineSymbolStyle.Solid,
                        borderColor,
                        2));

                var graphic = new Graphic(bufferWgs84, fillSymbol);
                graphic.Attributes["Day"] = CurrentDay;
                graphic.Attributes["Radius"] = currentRadiusMeters;

                _diffusionOverlay.Graphics.Add(graphic);

                // 在中心点添加标记（使用小一点的标记）
                var centerSymbol = new SimpleMarkerSymbol(
                    SimpleMarkerSymbolStyle.Circle,
                    System.Drawing.Color.Red,
                    8);
                _diffusionOverlay.Graphics.Add(new Graphic(_diffusionStartPoint, centerSymbol));
            }
            catch (Exception ex)
            {
                DiffusionStatus = $"更新缓冲区失败: {ex.Message}";
            }
        }

        private void ExecuteUpdateDiffusion(object obj)
        {
            if (_diffusionStartPoint == null)
            {
                DiffusionStatus = "请先选择初始小区";
                return;
            }

            UpdateDiffusionBuffer();
            DiffusionStatus = $"已更新第 {CurrentDay} 天的扩散范围";
        }


        private void ExecuteStartSimulation(object obj)
        {
            if (_diffusionStartPoint == null)
            {
                DiffusionStatus = "请先选择初始小区";
                return;
            }

            if (_simulationTimer == null)
            {
                _simulationTimer = new System.Timers.Timer(1000); // 1秒间隔
                _simulationTimer.Elapsed += (s, e) =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (CurrentDay < 7)
                        {
                            CurrentDay++;
                        }
                        else
                        {
                            ExecuteStopSimulation(null);
                        }
                    });
                };
            }

            _simulationTimer.Start();
            DiffusionStatus = "模拟进行中...";
        }

        private void ExecuteStopSimulation(object obj)
        {
            _simulationTimer?.Stop();
            DiffusionStatus = "模拟已停止";
        }

        // ================= F4 数据加载 =================
        private async void LoadF4DataAsync()
        {
            try
            {
                await LoadF4CommunityPointsAsync();
                await LoadF4Level1HospitalPointsAsync();
                StatusMessage = "F4 数据加载完成：小区点（蓝色） + 三甲医院（红色）";
            }
            catch (Exception ex)
            {
                StatusMessage = $"F4 数据加载失败: {ex.Message}";
            }
        }

        private async Task LoadF4CommunityPointsAsync()
        {
            try
            {
                using (var db = new NanjingContext())
                {
                    // 获取所有有坐标的小区点
                    var communities = await db.Communities
                        .Where(c => c.WgsLongitude != null && c.WgsLatitude != null)
                        .ToListAsync();

                    // 清空现有图形
                    _f4CommunityOverlay.Graphics.Clear();

                    // 创建小区点符号：蓝色圆点
                    SimpleMarkerSymbol communitySymbol = new SimpleMarkerSymbol(
                        SimpleMarkerSymbolStyle.Circle,
                        System.Drawing.Color.FromArgb(150, 30, 144, 255), // 半透明蓝色
                        6); // 较小的点

                    foreach (var community in communities)
                    {
                        var point = new MapPoint(
                            community.WgsLongitude.Value,
                            community.WgsLatitude.Value,
                            SpatialReferences.Wgs84);

                        var graphic = new Graphic(point, communitySymbol);
                        graphic.Attributes["Type"] = "Community";
                        graphic.Attributes["Name"] = community.Name;
                        graphic.Attributes["Population"] = community.FinalPopulation ?? 0;
                        graphic.Attributes["Street"] = community.Street ?? "未知";

                        _f4CommunityOverlay.Graphics.Add(graphic);
                    }

                    // 记录加载数量
                    DiffusionStatus += $"\n已加载 {communities.Count} 个小区点";
                }
            }
            catch (Exception ex)
            {
                DiffusionStatus = $"加载小区点失败: {ex.Message}";
            }
        }
        //数据加载
        private async Task LoadF4Level1HospitalPointsAsync()
        {
            try
            {
                using (var db = new NanjingContext())
                {
                    // 获取所有三甲医院（Level=1）
                    var hospitals = await db.Hospitals
                        .Where(h => h.Level == 1 && h.WgsLongitude != null && h.WgsLatitude != null)
                        .ToListAsync();

                    // 清空现有图形
                    _f4Level1HospitalOverlay.Graphics.Clear();

                    // 创建三甲医院符号：红色十字
                    SimpleMarkerSymbol hospitalSymbol = new SimpleMarkerSymbol(
                        SimpleMarkerSymbolStyle.Cross,
                        System.Drawing.Color.FromArgb(200, 255, 0, 0), // 红色
                        10); // 稍大一些

                    foreach (var hospital in hospitals)
                    {
                        var point = new MapPoint(
                            hospital.WgsLongitude.Value,
                            hospital.WgsLatitude.Value,
                            SpatialReferences.Wgs84);

                        var graphic = new Graphic(point, hospitalSymbol);
                        graphic.Attributes["Type"] = "Level1Hospital";
                        graphic.Attributes["Name"] = hospital.Name;
                        graphic.Attributes["Category"] = hospital.Category ?? "未知";
                        graphic.Attributes["Address"] = hospital.Address ?? "未知";

                        _f4Level1HospitalOverlay.Graphics.Add(graphic);
                    }

                    // 更新状态
                    DiffusionStatus += $"\n已加载 {hospitals.Count} 个三甲医院点";
                }
            }
            catch (Exception ex)
            {
                DiffusionStatus = $"加载医院点失败: {ex.Message}";
            }
        }

        // F4模块切换
        private void SwitchF4Mode(object parameter)
        {
            string mode = parameter as string;
            if (mode == "Neighborhood" || mode == "Network" || mode == "Lockdown")
            {
                CurrentF4Mode = mode;
                StatusMessage = $"F4 风险模拟: {GetF4ModeDisplayName(mode)}";
            }
        }

        private string GetF4ModeDisplayName(string mode)
        {
            return mode switch
            {
                "Neighborhood" => "邻域扩散模式",
                "Network" => "网络扩散模式",
                "Lockdown" => "封控圈生成",
                _ => "未知模式"
            };
        }

        private void UpdateF4ModeVisibility()
        {
            IsNeighborhoodModeVisible = CurrentF4Mode == "Neighborhood" ? Visibility.Visible : Visibility.Collapsed;
            IsNetworkModeVisible = CurrentF4Mode == "Network" ? Visibility.Visible : Visibility.Collapsed;
        }

        // 网络扩散搜索
        private async Task PerformNetworkStartSearchAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                NetworkStartResults = null;
                return;
            }

            using (var db = new NanjingContext())
            {
                var communities = await db.Communities
                    .Where(c => c.Name.Contains(query) && c.WgsLongitude != null)
                    .Select(c => new SearchResultItem
                    {
                        Name = c.Name,
                        Type = "住宅小区",
                        Lat = c.WgsLatitude.Value,
                        Lon = c.WgsLongitude.Value,
                        DetailInfo = $"街道:{c.Street}\n人口:{c.FinalPopulation ?? 0}"
                    })
                    .Take(5)
                    .ToListAsync();

                NetworkStartResults = communities;
            }
        }

        // 处理起始小区选择
        private async void HandleNetworkStartSelection(SearchResultItem item)
        {
            if (item == null) return;

            _networkStartPoint = new MapPoint(item.Lon, item.Lat, SpatialReferences.Wgs84);

            // 缩放到小区位置
            RequestNavigation?.Invoke(this, new NavigationEventArgs
            {
                Center = _networkStartPoint,
                Scale = 5000,
                ResultItem = item
            });

            // 查找最近的三甲医院
            await FindNearestHospital(_networkStartPoint);
        }

        // 查找最近的三甲医院
        private async Task FindNearestHospital(MapPoint startPoint)
        {
            try
            {
                using (var db = new NanjingContext())
                {
                    var hospitals = await db.Hospitals
                        .Where(h => h.Level == 1 && h.WgsLongitude != null && h.WgsLatitude != null)
                        .ToListAsync();

                    if (!hospitals.Any())
                    {
                        NearestHospitalInfo = "未找到三甲医院";
                        _nearestHospitalPoint = null;
                        return;
                    }

                    double minDistance = double.MaxValue;
                    Hospital nearestHospital = null;

                    foreach (var hospital in hospitals)
                    {
                        var hospitalPoint = new MapPoint(hospital.WgsLongitude.Value, hospital.WgsLatitude.Value, SpatialReferences.Wgs84);
                        double distance = GeometryEngine.Distance(startPoint, hospitalPoint);

                        if (distance < minDistance)
                        {
                            minDistance = distance;
                            nearestHospital = hospital;
                            _nearestHospitalPoint = hospitalPoint;
                        }
                    }

                    if (nearestHospital != null)
                    {
                        NearestHospitalInfo = $"🏥 {nearestHospital.Name}\n" +
                                             $"地址: {nearestHospital.Address}\n" +
                                             $"距离: {minDistance * 111:0.0}公里 (直线距离)";
                    }
                    else
                    {
                        NearestHospitalInfo = "未找到最近的三甲医院";
                        _nearestHospitalPoint = null;
                    }
                }
            }
            catch (Exception ex)
            {
                NearestHospitalInfo = $"查找医院失败: {ex.Message}";
                _nearestHospitalPoint = null;
            }
        }

        // 加载路网图层
        private async Task LoadRoadNetworkLayer()
        {
            if (_roadNetworkLayer != null) return;

            try
            {
                if (!File.Exists(RoadNetworkPath))
                {
                    StatusMessage = $"路网文件不存在: {RoadNetworkPath}";
                    return;
                }

                ShapefileFeatureTable roadTable = new ShapefileFeatureTable(RoadNetworkPath);
                await roadTable.LoadAsync();

                _roadNetworkLayer = new FeatureLayer(roadTable)
                {
                    IsVisible = false, // 默认不显示，只在需要时显示相关路段
                    Name = "南京市路网",
                    Opacity = 0.7
                };

                // 设置样式：灰色细线
                var roadRenderer = new SimpleRenderer(
                    new SimpleLineSymbol(
                        SimpleLineSymbolStyle.Solid,
                        System.Drawing.Color.FromArgb(150, 100, 100, 100), // 半透明灰色
                        1.5)); // 较细的线

                _roadNetworkLayer.Renderer = roadRenderer;

                Map.OperationalLayers.Add(_roadNetworkLayer);
                StatusMessage = "路网图层加载完成";
            }
            catch (Exception ex)
            {
                StatusMessage = $"加载路网失败: {ex.Message}";
            }
        }

        // 开始网络模拟
        private async void ExecuteStartNetworkSimulation(object obj)
        {
            if (_networkStartPoint == null || _nearestHospitalPoint == null)
            {
                StatusMessage = "请先选择起始小区";
                return;
            }

            try
            {
                // 确保路网已加载
                await LoadRoadNetworkLayer();

                // 清除之前的图形
                _networkDiffusionOverlay.Graphics.Clear();

                // 1. 创建欧式路径
                var polyline = CreateEuclideanPath(_networkStartPoint, _nearestHospitalPoint);
                if (polyline == null)
                {
                    StatusMessage = "创建路径失败";
                    return;
                }

                // 2. 创建缓冲区（潜在风险走廊）
                var corridor = CreateCorridorBuffer(polyline, CorridorWidth);
                if (corridor == null)
                {
                    StatusMessage = "创建走廊缓冲区失败";
                    return;
                }

                // 3. 查询相交的路网
                _selectedRoads = await QueryIntersectingRoads(corridor);
                if (!_selectedRoads.Any())
                {
                    StatusMessage = "未找到相交的道路";
                    return;
                }

                // 4. 可视化欧式路径和缓冲区
                VisualizeEuclideanPath(polyline);
                VisualizeCorridor(corridor);

                // 5. 开始动画显示相关路网
                StartRoadAnimation(_selectedRoads);

                StatusMessage = $"网络模拟开始，找到 {_selectedRoads.Count} 条相关道路";
            }
            catch (Exception ex)
            {
                StatusMessage = $"网络模拟失败: {ex.Message}";
            }
        }

        // 创建欧式路径
        private Polyline CreateEuclideanPath(MapPoint start, MapPoint end)
        {
            try
            {
                // 创建简单的两点折线
                var builder = new PolylineBuilder(SpatialReferences.Wgs84);
                builder.AddPoint(start);
                builder.AddPoint(end);
                return builder.ToGeometry();
            }
            catch (Exception ex)
            {
                StatusMessage = $"创建欧式路径失败: {ex.Message}";
                return null;
            }
        }

        // 创建走廊缓冲区
        private Polygon CreateCorridorBuffer(Polyline path, double bufferDistanceMeters)
        {
            try
            {
                // 将WGS84路径转换为Web墨卡托（单位：米）
                var webMercatorPath = GeometryEngine.Project(path, SpatialReferences.WebMercator) as Polyline;
                if (webMercatorPath == null) return null;

                // 创建缓冲区
                var buffer = GeometryEngine.Buffer(webMercatorPath, bufferDistanceMeters) as Polygon;
                if (buffer == null) return null;

                // 转换回WGS84
                return GeometryEngine.Project(buffer, SpatialReferences.Wgs84) as Polygon;
            }
            catch (Exception ex)
            {
                StatusMessage = $"创建缓冲区失败: {ex.Message}";
                return null;
            }
        }

        // 查询相交的路网
        private async Task<List<Feature>> QueryIntersectingRoads(Polygon corridor)
        {
            if (_roadNetworkLayer == null || corridor == null) return new List<Feature>();

            try
            {
                var parameters = new QueryParameters
                {
                    Geometry = corridor,
                    SpatialRelationship = SpatialRelationship.Intersects,
                    ReturnGeometry = true
                };

                var result = await _roadNetworkLayer.FeatureTable.QueryFeaturesAsync(parameters);
                return result.ToList();
            }
            catch (Exception ex)
            {
                StatusMessage = $"查询路网失败: {ex.Message}";
                return new List<Feature>();
            }
        }

        // 可视化欧式路径
        private void VisualizeEuclideanPath(Polyline path)
        {
            var pathSymbol = new SimpleLineSymbol(
                SimpleLineSymbolStyle.Solid,
                System.Drawing.Color.FromArgb(200, 255, 255, 0), // 黄色
                3);

            _networkDiffusionOverlay.Graphics.Add(new Graphic(path, pathSymbol));
        }

        // 可视化走廊缓冲区
        private void VisualizeCorridor(Polygon corridor)
        {
            var corridorSymbol = new SimpleFillSymbol(
                SimpleFillSymbolStyle.Solid,
                System.Drawing.Color.FromArgb(60, 255, 165, 0), // 半透明橙色
                new SimpleLineSymbol(
                    SimpleLineSymbolStyle.Solid,
                    System.Drawing.Color.FromArgb(150, 255, 140, 0), // 稍深的橙色边框
                    2));

            _networkDiffusionOverlay.Graphics.Add(new Graphic(corridor, corridorSymbol));
        }

        // 开始道路动画
        private void StartRoadAnimation(List<Feature> roads)
        {
            if (!roads.Any()) return;

            // 停止之前的动画
            ExecuteStopNetworkSimulation(null);

            // 计算每条道路到起点的距离并排序
            var sortedRoads = roads.OrderBy(road =>
            {
                if (road.Geometry is Polyline line && line.Parts.Count > 0)
                {
                    var firstPoint = line.Parts[0].StartPoint;
                    return GeometryEngine.Distance(_networkStartPoint, firstPoint);
                }
                return double.MaxValue;
            }).ToList();

            _networkAnimationTimer = new System.Timers.Timer(AnimationSpeed);
            int currentIndex = 0;

            _networkAnimationTimer.Elapsed += (s, e) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (currentIndex < sortedRoads.Count)
                    {
                        // 显示当前道路
                        VisualizeRoad(sortedRoads[currentIndex], currentIndex);
                        currentIndex++;

                        StatusMessage = $"网络扩散进度: {currentIndex}/{sortedRoads.Count}";
                    }
                    else
                    {
                        // 动画完成
                        ExecuteStopNetworkSimulation(null);
                        StatusMessage = "网络扩散模拟完成";
                    }
                });
            };

            _networkAnimationTimer.Start();
            _isNetworkSimulationRunning = true;
        }

        // 可视化单条道路
        private void VisualizeRoad(Feature road, int index)
        {
            if (road.Geometry == null) return;

            // 使用渐变色：从起点（红色）到终点（橙色）
            double ratio = (double)index / Math.Max(1, _selectedRoads.Count - 1);
            int red = 255;
            int green = (int)(100 + ratio * 155); // 100-255
            int blue = 0;

            var roadSymbol = new SimpleLineSymbol(
                SimpleLineSymbolStyle.Solid,
                System.Drawing.Color.FromArgb(200, red, green, blue),
                3.5); // 比原始路网粗

            _networkDiffusionOverlay.Graphics.Add(new Graphic(road.Geometry, roadSymbol));
        }

        // 停止网络模拟
        private void ExecuteStopNetworkSimulation(object obj)
        {
            _networkAnimationTimer?.Stop();
            _networkAnimationTimer = null;
            _isNetworkSimulationRunning = false;
        }

        // 清除网络图形
        private void ExecuteClearNetworkGraphics(object obj)
        {
            _networkDiffusionOverlay.Graphics.Clear();
            StatusMessage = "已清除网络扩散图形";
        }



        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}