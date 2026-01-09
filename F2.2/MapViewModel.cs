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

        public MapViewModel()
        {
            GraphicsOverlays.Add(_blindSpotOverlay);
            GraphicsOverlays.Add(_level1Overlay);
            GraphicsOverlays.Add(_highLevelOverlay);

            SearchCommand = new RelayCommand(async () => { SelectedResult = null; await PerformSearchAsync(SearchText); OnPropertyChanged(nameof(IsShowingResults)); });
            LoadAnalysisCommand = new RelayCommand(ExecuteLoadAnalysis);
            CalcBlindSpotCommand = new RelayCommand(ExecuteCalcBlindSpot);
            CalcEquityCommand = new RelayCommand(ExecuteCalcEquity);
            SwitchModuleCommand = new RelayCommand(ExecuteSwitchModule);

            InitializeMap();
        }

        // ================= 模块切换 (修改) =================
        private void ExecuteSwitchModule(object parameter)
        {
            string module = parameter as string;

            // ★★★ 更新当前模块标记 (View层绑定用) ★★★
            CurrentModule = module;

            LeftPanelVisibility = Visibility.Visible;

            if (module == "F1")
            {
                IsF1Active = true;
                StatusMessage = "F1 资源感知: 医院点位模式";
                IsF1ConfigVisible = Visibility.Visible;
                IsF2ConfigVisible = Visibility.Collapsed;
                _level1Overlay.IsVisible = true;
                _highLevelOverlay.IsVisible = true;
                _blindSpotOverlay.IsVisible = false;
                HideAllMmpkLayers();
                F1StatsVisibility = Visibility.Visible;
                StatsPanelVisibility = Visibility.Collapsed;
            }
            else if (module == "F2")
            {
                IsF1Active = false;
                StatusMessage = "F2 可达性分析: 请加载分析";
                IsF1ConfigVisible = Visibility.Collapsed;
                IsF2ConfigVisible = Visibility.Visible;
                _level1Overlay.IsVisible = false;
                _highLevelOverlay.IsVisible = false;
                _blindSpotOverlay.IsVisible = true;
                if (_isAnalysisStarted) UpdateLayerVisibility();
                F1StatsVisibility = Visibility.Collapsed;
            }
            else if (module == "F3")
            {
                IsF1Active = false;
                StatusMessage = "F3 压力监测 (开发中)";
                IsF1ConfigVisible = Visibility.Collapsed;
                IsF2ConfigVisible = Visibility.Collapsed;
                F1StatsVisibility = Visibility.Collapsed;
                StatsPanelVisibility = Visibility.Collapsed;
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

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}