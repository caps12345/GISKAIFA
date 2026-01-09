using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.UI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using WpfMapApp2.Models;

namespace WpfMapApp2
{
    public class MapViewModel : INotifyPropertyChanged
    {
        private Map _map;
        private string _statusMessage = "系统初始化中...";

        // 当前分析配置
        private PressureAnalysisService.AnalysisOptions _currentOptions = new PressureAnalysisService.AnalysisOptions();

        public MapViewModel()
        {
            InitializeMap();
        }

        private async void InitializeMap()
        {
            try
            {
                // 1. 底图
                string token = "96cd361c8473c7c2d2c96bd05c598a2c";
                string vecUrl = $@"http://t0.tianditu.gov.cn/vec_w/wmts?SERVICE=WMTS&REQUEST=GetTile&VERSION=1.0.0&LAYER=vec&STYLE=default&TILEMATRIXSET=w&FORMAT=tiles&TILEMATRIX={{level}}&TILEROW={{row}}&TILECOL={{col}}&tk={token}";
                string cvaUrl = $@"http://t0.tianditu.gov.cn/cva_w/wmts?SERVICE=WMTS&REQUEST=GetTile&VERSION=1.0.0&LAYER=cva&STYLE=default&TILEMATRIXSET=w&FORMAT=tiles&TILEMATRIX={{level}}&TILEROW={{row}}&TILECOL={{col}}&tk={token}";
                Basemap basemap = new Basemap(new WebTiledLayer(vecUrl));
                basemap.ReferenceLayers.Add(new WebTiledLayer(cvaUrl));
                Map = new Map(basemap);

                // 2. 行政区
                await LoadDistrictLayer();

                // 3. 初始加载压力图
                // 默认都设为 false，等待用户手动勾选或点击分析
                await RefreshPressureLayer(_currentOptions, true, false);

                // 4. 医院点
                await LoadHospitalLayers();
            }
            catch (Exception ex)
            {
                StatusMessage = $"初始化失败: {ex.Message}";
            }
        }

        // ==========================================
        // [F4] 刷新压力分析 (修复版：状态同步)
        // ==========================================
        public async Task RefreshPressureLayer(PressureAnalysisService.AnalysisOptions options, bool showIdw, bool showKde)
        {
            _currentOptions = options;
            StatusMessage = $"正在分析: {(options.OnlyHighEnd ? "高端资源" : "全量资源")} | {options.DiseaseType}...";

            // 1. 彻底移除旧图层
            var layersToRemove = Map.OperationalLayers.Where(l => l.Name.StartsWith("医疗压力_")).ToList();
            foreach (var l in layersToRemove) Map.OperationalLayers.Remove(l);

            // 2. 后台计算
            List<PressureAnalysisService.CommunityPressureResult> results = null;
            await Task.Run(() =>
            {
                using (var context = new NanjingContext())
                {
                    var h = context.Hospitals.Where(x => x.Longitude != null).ToList();
                    var c = context.Communities.Where(x => x.Longitude != null && x.FinalPopulation > 0).ToList();
                    var service = new PressureAnalysisService();
                    results = service.CalculatePressure(h, c, _currentOptions);
                }
            });

            if (results != null)
            {
                // 3. 根据传入的开关状态生成图层
                await LoadIDWLayerFromResults(results, showIdw);
                await LoadKDELayerFromResults(results, showKde);
            }

            StatusMessage = "分层诊断分析完成";
        }

        private async Task LoadIDWLayerFromResults(List<PressureAnalysisService.CommunityPressureResult> results, bool isVisible)
        {
            await Task.Run(() =>
            {
                List<Field> fields = new List<Field> { new Field(FieldType.Float64, "Pressure", "压力值", 50) };
                FeatureCollectionTable gridTable = new FeatureCollectionTable(fields, GeometryType.Polygon, SpatialReferences.Wgs84);
                gridTable.Renderer = CreateHeatmapRenderer();

                GenerateIDWGrid(results, gridTable);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    var layer = new FeatureCollectionLayer(new FeatureCollection(new[] { gridTable }))
                    {
                        Name = "医疗压力_IDW",
                        Opacity = 0.65,
                        IsVisible = isVisible
                    };
                    if (Map.OperationalLayers.Count > 0) Map.OperationalLayers.Insert(1, layer);
                    else Map.OperationalLayers.Add(layer);
                });
            });
        }

        private async Task LoadKDELayerFromResults(List<PressureAnalysisService.CommunityPressureResult> results, bool isVisible)
        {
            await Task.Run(() =>
            {
                List<Field> fields = new List<Field> { new Field(FieldType.Float64, "Pressure", "压力值", 50) };
                FeatureCollectionTable gridTable = new FeatureCollectionTable(fields, GeometryType.Polygon, SpatialReferences.Wgs84);
                gridTable.Renderer = CreateHeatmapRenderer();

                GenerateKDEGrid(results, gridTable);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    var layer = new FeatureCollectionLayer(new FeatureCollection(new[] { gridTable }))
                    {
                        Name = "医疗压力_KDE",
                        Opacity = 0.65,
                        IsVisible = isVisible
                    };
                    if (Map.OperationalLayers.Count > 1) Map.OperationalLayers.Insert(2, layer);
                    else Map.OperationalLayers.Add(layer);
                });
            });
        }

        // IDW 算法
        private void GenerateIDWGrid(List<PressureAnalysisService.CommunityPressureResult> points, FeatureCollectionTable table)
        {
            if (points.Count == 0) return;
            double minX = 118.3, maxX = 119.3, minY = 31.2, maxY = 32.6;
            double cellSize = 0.008;
            int cols = (int)((maxX - minX) / cellSize) + 1;
            int rows = (int)((maxY - minY) / cellSize) + 1;
            double searchRadius = 0.035;

            for (int i = 0; i < cols; i++)
            {
                for (int j = 0; j < rows; j++)
                {
                    double cx = minX + i * cellSize + cellSize / 2;
                    double cy = minY + j * cellSize + cellSize / 2;
                    double num = 0, den = 0;
                    bool hasData = false;
                    foreach (var p in points)
                    {
                        double d2 = Math.Pow(p.CommunityInfo.Longitude.Value - cx, 2) + Math.Pow(p.CommunityInfo.Latitude.Value - cy, 2);
                        if (d2 < searchRadius * searchRadius)
                        {
                            double w = 1.0 / (d2 + 0.000001);
                            num += p.PressureIndex * w; den += w; hasData = true;
                        }
                    }
                    if (hasData && den > 0 && num / den > 1.0)
                    {
                        CreateGridCell(table, minX + i * cellSize, minY + j * cellSize, cellSize, num / den);
                    }
                }
            }
        }

        // KDE 算法
        private void GenerateKDEGrid(List<PressureAnalysisService.CommunityPressureResult> points, FeatureCollectionTable table)
        {
            if (points.Count == 0) return;
            double minX = 118.3, maxX = 119.3, minY = 31.2, maxY = 32.6;
            double cellSize = 0.008;
            int cols = (int)((maxX - minX) / cellSize) + 1;
            int rows = (int)((maxY - minY) / cellSize) + 1;
            double bandwidth = 0.045;
            double sigmaSq2 = 2 * Math.Pow(bandwidth / 2.5, 2);

            for (int i = 0; i < cols; i++)
            {
                for (int j = 0; j < rows; j++)
                {
                    double cx = minX + i * cellSize + cellSize / 2;
                    double cy = minY + j * cellSize + cellSize / 2;
                    double num = 0, den = 0;
                    bool hasData = false;
                    foreach (var p in points)
                    {
                        double d2 = Math.Pow(p.CommunityInfo.Longitude.Value - cx, 2) + Math.Pow(p.CommunityInfo.Latitude.Value - cy, 2);
                        if (d2 < bandwidth * bandwidth)
                        {
                            double w = Math.Exp(-d2 / sigmaSq2);
                            num += p.PressureIndex * w; den += w; hasData = true;
                        }
                    }
                    if (hasData && den > 0 && num / den > 1.0)
                    {
                        CreateGridCell(table, minX + i * cellSize, minY + j * cellSize, cellSize, num / den);
                    }
                }
            }
        }

        private void CreateGridCell(FeatureCollectionTable table, double x, double y, double size, double val)
        {
            var pts = new List<MapPoint> { new MapPoint(x, y), new MapPoint(x + size, y), new MapPoint(x + size, y + size), new MapPoint(x, y + size) };
            Feature f = table.CreateFeature();
            f.Geometry = new Polygon(pts, SpatialReferences.Wgs84);
            f.SetAttributeValue("Pressure", val);
            table.AddFeatureAsync(f);
        }

        private Renderer CreateHeatmapRenderer()
        {
            ClassBreaksRenderer r = new ClassBreaksRenderer { FieldName = "Pressure" };
            SimpleFillSymbol F(System.Drawing.Color c) => new SimpleFillSymbol(SimpleFillSymbolStyle.Solid, c, null);
            r.ClassBreaks.Add(new ClassBreak("L", "L", 0, 40, F(System.Drawing.Color.FromArgb(180, 0, 255, 0))));
            r.ClassBreaks.Add(new ClassBreak("M", "M", 40, 75, F(System.Drawing.Color.FromArgb(180, 255, 255, 0))));
            r.ClassBreaks.Add(new ClassBreak("H", "H", 75, 100, F(System.Drawing.Color.FromArgb(200, 255, 0, 0))));
            return r;
        }

        private async Task LoadHospitalLayers()
        {
            try
            {
                List<Field> fields = new List<Field> {
                    new Field(FieldType.Text, "Name", "名称", 100), new Field(FieldType.Int32, "Level", "等级", 50),
                    new Field(FieldType.Text, "LevelLabel", "标签", 50), new Field(FieldType.Text, "Phone", "电话", 50),
                    new Field(FieldType.Text, "Score", "评分", 50)
                };

                FeatureCollectionTable tableHigh = new FeatureCollectionTable(fields, GeometryType.Point, SpatialReferences.Wgs84);
                UniqueValueRenderer renderHigh = new UniqueValueRenderer(); renderHigh.FieldNames.Add("Level");
                renderHigh.DefaultSymbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.Red, 14);
                tableHigh.Renderer = renderHigh;

                FeatureCollectionTable tableLow = new FeatureCollectionTable(fields, GeometryType.Point, SpatialReferences.Wgs84);
                UniqueValueRenderer renderLow = new UniqueValueRenderer(); renderLow.FieldNames.Add("Level");
                renderLow.UniqueValues.Add(new UniqueValue("L2", "L2", new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.Blue, 10), 2));
                renderLow.UniqueValues.Add(new UniqueValue("L3", "L3", new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.Green, 8), 3));
                tableLow.Renderer = renderLow;

                await Task.Run(() => {
                    using (var context = new NanjingContext())
                    {
                        var hospitals = context.Hospitals.Where(h => h.Longitude != null).ToList();
                        foreach (var h in hospitals)
                        {
                            if (Math.Abs(h.Longitude.Value) < 0.1) continue;
                            bool isHigh = (h.Level == 1);
                            var target = isHigh ? tableHigh : tableLow;
                            Feature f = target.CreateFeature();
                            f.Geometry = new MapPoint(h.Longitude.Value, h.Latitude.Value, SpatialReferences.Wgs84);
                            f.SetAttributeValue("Name", h.Name); f.SetAttributeValue("Level", h.Level ?? 3); f.SetAttributeValue("LevelLabel", h.LevelLabel); f.SetAttributeValue("Phone", h.Phone); f.SetAttributeValue("Score", h.Score?.ToString());
                            target.AddFeatureAsync(f);
                        }
                    }
                });

                Application.Current.Dispatcher.Invoke(() => {
                    Map.OperationalLayers.Add(new FeatureCollectionLayer(new FeatureCollection(new[] { tableLow })) { Name = "医疗设施分布_基础", Id = "Hospital_Low", MinScale = 100000 });
                    Map.OperationalLayers.Add(new FeatureCollectionLayer(new FeatureCollection(new[] { tableHigh })) { Name = "医疗设施分布_重点", Id = "Hospital_High" });
                });
            }
            catch (Exception ex) { StatusMessage = $"医院加载错误: {ex.Message}"; }
        }

        private async Task LoadDistrictLayer()
        {
            try
            {
                string shpPath = @"C:\Users\mmn\Documents\GitHub\GISKAIFA-1\WpfMapApp2\Data\南京区县.shp";
                if (!File.Exists(shpPath)) { StatusMessage = "未找到行政区划文件"; return; }
                ShapefileFeatureTable shpTable = new ShapefileFeatureTable(shpPath);
                FeatureLayer shpLayer = new FeatureLayer(shpTable);
                SimpleLineSymbol outline = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.Red, 1.5);
                SimpleFillSymbol fill = new SimpleFillSymbol(SimpleFillSymbolStyle.Solid, System.Drawing.Color.FromArgb(10, 255, 0, 0), outline);
                shpLayer.Renderer = new SimpleRenderer(fill);
                Map.OperationalLayers.Add(shpLayer);
                await shpLayer.LoadAsync();
                if (shpLayer.FullExtent != null) Map.InitialViewpoint = new Viewpoint(shpLayer.FullExtent);
            }
            catch (Exception ex) { StatusMessage = $"行政区加载错误: {ex.Message}"; }
        }

        public Map Map { get => _map; set { _map = value; OnPropertyChanged(); } }
        public string StatusMessage { get => _statusMessage; set { _statusMessage = value; OnPropertyChanged(); } }

        // 修复 CS8612 警告：添加 ? 允许为 null
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}