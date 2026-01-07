using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.UI;
using Esri.ArcGISRuntime.UI.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;

namespace WpfMapApp2
{
    public class MapViewModel : INotifyPropertyChanged
    {
        private Map _map;
        private string _statusMessage = "系统初始化中...";

        public MapViewModel()
        {
            InitializeMap();
        }

        /// <summary>
        /// 初始化地图
        /// </summary>
        private void InitializeMap()
        {
            try
            {
                // 使用天地图作为底图
                string token = "96cd361c8473c7c2d2c96bd05c598a2c";

                // 方法1：使用单个URL模板
                string TDTBaseMapStr = @"http://t{subDomain}.tianditu.gov.cn/vec_w/wmts?SERVICE=WMTS&REQUEST=GetTile&VERSION=1.0.0&LAYER=vec&STYLE=default&TILEMATRIXSET=w&FORMAT=tiles&TILEMATRIX={level}&TILEROW={row}&TILECOL={col}&tk=" + token;

                // 方法1：使用您原来的构造函数
                WebTiledLayer webtileBaseLayer = new WebTiledLayer(
                    TDTBaseMapStr,
                    new List<string> { "0", "1", "2", "3", "4", "5", "6", "7" })
                {
                    Name = "天地图矢量底图"
                };               

                // 创建地图
                // 方法1：直接创建包含图层的地图
                _map = new Map(new Basemap(webtileBaseLayer));

                // 设置初始视点
                _map.InitialViewpoint = new Viewpoint(
                    new Envelope(116.0, 30.0, 122.0, 35.0, SpatialReferences.Wgs84));

                StatusMessage = "天地图底图加载成功";
            }
            catch (Exception ex)
            {
                // 天地图加载失败时使用默认底图
                _map = new Map(BasemapStyle.ArcGISTopographic);
                _map.InitialViewpoint = new Viewpoint(
                    new Envelope(116.0, 30.0, 122.0, 35.0, SpatialReferences.Wgs84));
                StatusMessage = $"天地图底图加载失败，使用默认底图: {ex.Message}";

                // 记录详细错误信息
                System.Diagnostics.Debug.WriteLine($"天地图加载错误: {ex}");
            }
        }

        /// <summary>
        /// 地图属性
        /// </summary>
        public Map Map
        {
            get => _map;
            set
            {
                _map = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 状态信息
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}