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
        public ObservableCollection<DistrictStat> DistrictStats { get; set; } = new ObservableCollection<DistrictStat>();

        private Visibility _statsPanelVisibility = Visibility.Collapsed;
        public Visibility StatsPanelVisibility { get => _statsPanelVisibility; set { _statsPanelVisibility = value; OnPropertyChanged(); } }

        // 业务图层
        private FeatureLayer _layerWalkL1, _layerDriveL1, _layerWalkAll, _layerDriveAll;
        private bool _isAnalysisStarted = false;

        // 图例控制
        private Visibility _legendWalkL1 = Visibility.Collapsed;
        private Visibility _legendDriveL1 = Visibility.Collapsed;
        private Visibility _legendWalkAll = Visibility.Collapsed;
        private Visibility _legendDriveAll = Visibility.Collapsed;

        public Visibility LegendWalkL1 { get => _legendWalkL1; set { _legendWalkL1 = value; OnPropertyChanged(); } }
        public Visibility LegendDriveL1 { get => _legendDriveL1; set { _legendDriveL1 = value; OnPropertyChanged(); } }
        public Visibility LegendWalkAll { get => _legendWalkAll; set { _legendWalkAll = value; OnPropertyChanged(); } }
        public Visibility LegendDriveAll { get => _legendDriveAll; set { _legendDriveAll = value; OnPropertyChanged(); } }

        // 3. 选项
        private int _selectedModeIndex = 0;
        public int SelectedModeIndex
        {
            get => _selectedModeIndex;
            set { _selectedModeIndex = value; OnPropertyChanged(); if (_isAnalysisStarted) UpdateLayerVisibility(); if (StatsPanelVisibility == Visibility.Visible) ExecuteCalcEquity(null); }
        }

        private int _selectedHospitalTypeIndex = 0;
        public int SelectedHospitalTypeIndex
        {
            get => _selectedHospitalTypeIndex;
            set { _selectedHospitalTypeIndex = value; OnPropertyChanged(); if (_isAnalysisStarted) UpdateLayerVisibility(); if (StatsPanelVisibility == Visibility.Visible) ExecuteCalcEquity(null); }
        }

        public ICommand LoadAnalysisCommand { get; }
        public ICommand CalcBlindSpotCommand { get; }
        public ICommand CalcEquityCommand { get; }
        public ICommand SwitchModuleCommand { get; }

        public MapViewModel()
        {
            GraphicsOverlays.Add(_blindSpotOverlay);
            GraphicsOverlays.Add(_level1Overlay);
            GraphicsOverlays.Add(_highLevelOverlay);

            LoadAnalysisCommand = new RelayCommand(ExecuteLoadAnalysis);
            CalcBlindSpotCommand = new RelayCommand(ExecuteCalcBlindSpot);
            CalcEquityCommand = new RelayCommand(ExecuteCalcEquity);
            SwitchModuleCommand = new RelayCommand(ExecuteSwitchModule);

            InitializeMap();
        }

        private void ExecuteLoadAnalysis(object obj)
        {
            _isAnalysisStarted = true;
            UpdateLayerVisibility();
            StatusMessage = "等时圈分析已加载。";
        }

        private void ExecuteSwitchModule(object parameter)
        {
            string module = parameter as string;
            if (module == "F1")
            {
                StatusMessage = "F1 资源感知: 医院点位模式";
                _level1Overlay.IsVisible = true; _highLevelOverlay.IsVisible = true;
                _blindSpotOverlay.IsVisible = false; HideAllMmpkLayers();
                StatsPanelVisibility = Visibility.Collapsed;
            }
            if (module == "F2")
            {
                StatusMessage = "F2 可达性分析: 请点击左侧按钮加载分析";
                _blindSpotOverlay.IsVisible = true; if (_isAnalysisStarted) UpdateLayerVisibility();
            }
            if (module == "F3") StatusMessage = "F3 压力监测 (开发中)";
        }

        private async void InitializeMap()
        {
            try
            {
                string token = "96cd361c8473c7c2d2c96bd05c598a2c";
                var subDomains = new List<string> { "0", "1", "2", "3", "4", "5", "6", "7" };
                string vecUrl = @"http://t{subDomain}.tianditu.gov.cn/vec_w/wmts?SERVICE=WMTS&REQUEST=GetTile&VERSION=1.0.0&LAYER=vec&STYLE=default&TILEMATRIXSET=w&FORMAT=tiles&TILEMATRIX={level}&TILEROW={row}&TILECOL={col}&tk=" + token;
                string cvaUrl = @"http://t{subDomain}.tianditu.gov.cn/cva_w/wmts?SERVICE=WMTS&REQUEST=GetTile&VERSION=1.0.0&LAYER=cva&STYLE=default&TILEMATRIXSET=w&FORMAT=tiles&TILEMATRIX={level}&TILEROW={row}&TILECOL={col}&tk=" + token;

                WebTiledLayer baseLayer = new WebTiledLayer(vecUrl, subDomains);
                WebTiledLayer labelLayer = new WebTiledLayer(cvaUrl, subDomains);
                Basemap myBasemap = new Basemap(baseLayer);
                myBasemap.BaseLayers.Add(labelLayer);

                Map = new Map(myBasemap);
                Map.InitialViewpoint = new Viewpoint(32.060, 118.796, 150000);

                await AddNanjingLayerAsync();
                await LoadMmpkLayers();
                await AddHospitalPointsAsync();
                await AddHighLevelHospitalsAsync();

                StatusMessage = "系统就绪。";
            }
            catch (Exception ex) { StatusMessage = $"初始化失败: {ex.Message}"; }
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

                        cloned.IsVisible = false;
                        cloned.Opacity = 0.6;
                        Map.OperationalLayers.Add(cloned);
                    }
                }
            }
            catch (Exception ex) { StatusMessage = $"MMPK错误: {ex.Message}"; }
        }

        private async Task CollectFeatureLayersRecursive(LayerCollection layers, List<FeatureLayer> result)
        {
            foreach (var layer in layers)
            {
                await layer.LoadAsync();
                if (layer is FeatureLayer fl) result.Add(fl);
                else if (layer is GroupLayer gl) await CollectFeatureLayersRecursive(gl.Layers, result);
            }
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
                string mode = SelectedModeIndex == 0 ? "步行" : "驾车";
                string type = SelectedHospitalTypeIndex == 0 ? "三甲" : "全部";
                StatusMessage = $"当前: {type} - {mode}";

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
            if (layer == null) { StatusMessage = "请先加载分析图层"; return; }
            StatusMessage = "计算盲区中..."; _blindSpotOverlay.Graphics.Clear();
            try
            {
                var districtGeo = await GetUnionGeometry(ShpPath, true);
                var serviceGeo = await GetUnionGeometry(layer);
                if (districtGeo != null && serviceGeo != null)
                {
                    districtGeo = GeometryEngine.Project(districtGeo, SpatialReferences.Wgs84);
                    serviceGeo = GeometryEngine.Project(serviceGeo, SpatialReferences.Wgs84);
                    var blindGeo = GeometryEngine.Difference(districtGeo, serviceGeo);
                    if (blindGeo != null && !blindGeo.IsEmpty)
                    {
                        var sym = new SimpleFillSymbol(SimpleFillSymbolStyle.Solid, System.Drawing.Color.FromArgb(150, 60, 60, 60), null);
                        _blindSpotOverlay.Graphics.Add(new Graphic(blindGeo, sym));
                        StatusMessage = "盲区已计算。";
                    }
                }
            }
            catch (Exception ex) { StatusMessage = $"计算失败: {ex.Message}"; }
        }

        private async void ExecuteCalcEquity(object obj)
        {
            var layer = GetCurrentLayer();
            if (layer == null) return;
            StatsPanelVisibility = Visibility.Visible;
            StatusMessage = "更新统计..."; DistrictStats.Clear();
            try
            {
                var serviceGeo = await GetUnionGeometry(layer);
                serviceGeo = GeometryEngine.Project(serviceGeo, SpatialReferences.Wgs84);
                ShapefileFeatureTable shpTable = new ShapefileFeatureTable(ShpPath);
                await shpTable.LoadAsync();
                var query = await shpTable.QueryFeaturesAsync(new QueryParameters { WhereClause = "1=1" });
                foreach (var feature in query)
                {
                    var districtGeo = GeometryEngine.Project(feature.Geometry, SpatialReferences.Wgs84);
                    string name = feature.Attributes["Name"]?.ToString() ?? "未知";
                    double total = GeometryEngine.Area(districtGeo);
                    var inter = GeometryEngine.Intersection(districtGeo, serviceGeo);
                    double cover = (inter != null && !inter.IsEmpty) ? GeometryEngine.Area(inter) : 0;
                    double rate = (total > 0) ? (cover / total) * 100.0 : 0;
                    Application.Current.Dispatcher.Invoke(() => DistrictStats.Add(new DistrictStat { Name = name, CoverageRate = rate }));
                }
                StatusMessage = "统计完成。";
            }
            catch { }
        }

        // ★★★ 核心修改：去掉填充色，只保留红框 ★★★
        private async Task AddNanjingLayerAsync()
        {
            if (!File.Exists(ShpPath)) return;
            try
            {
                ShapefileFeatureTable table = new ShapefileFeatureTable(ShpPath);
                FeatureLayer layer = new FeatureLayer(table);

                // 红色边框
                SimpleLineSymbol outline = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.Red, 2);

                // 填充设为 Null（透明且不挡底图）
                SimpleFillSymbol fill = new SimpleFillSymbol(SimpleFillSymbolStyle.Null, System.Drawing.Color.Transparent, outline);

                layer.Renderer = new SimpleRenderer(fill);
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
                    foreach (var h in list) _level1Overlay.Graphics.Add(new Graphic(new MapPoint(h.WgsLongitude.Value, h.WgsLatitude.Value, SpatialReferences.Wgs84), sym));
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
                    foreach (var h in list) _highLevelOverlay.Graphics.Add(new Graphic(new MapPoint(h.WgsLongitude.Value, h.WgsLatitude.Value, SpatialReferences.Wgs84), sym));
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
            ShapefileFeatureTable table = new ShapefileFeatureTable(path);
            await table.LoadAsync();
            var res = await table.QueryFeaturesAsync(new QueryParameters { WhereClause = "1=1" });
            return GeometryEngine.Union(res.Select(f => f.Geometry).Where(g => g != null));
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
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
        private Action<object> _execute;
        public RelayCommand(Action<object> execute) => _execute = execute;
        public bool CanExecute(object p) => true;
        public void Execute(object p) => _execute(p);
        public event EventHandler CanExecuteChanged;
    }
}