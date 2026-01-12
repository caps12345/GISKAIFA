using Esri.ArcGISRuntime.Geometry;
using System;

namespace WpfMapApp2.Utils
{
    /// <summary>
    /// 坐标转换工具类 (WGS84 <-> GCJ02 火星坐标系)
    /// </summary>
    public static class GpsUtil
    {
        private const double Pi = 3.1415926535897932384626;
        private const double A = 6378245.0; // 长半轴
        private const double Ee = 0.00669342162296594323; // 扁率

        /// <summary>
        /// WGS-84 转 GCJ-02 (高德/腾讯)
        /// </summary>
        /// <param name="wgsPt">ArcGIS原本的点 (WGS84)</param>
        /// <returns>加密后的点 (GCJ02)</returns>
        public static MapPoint Wgs84ToGcj02(MapPoint wgsPt)
        {
            if (OutOfChina(wgsPt.Y, wgsPt.X))
            {
                return wgsPt;
            }

            double dLat = TransformLat(wgsPt.X - 105.0, wgsPt.Y - 35.0);
            double dLon = TransformLon(wgsPt.X - 105.0, wgsPt.Y - 35.0);
            double radLat = wgsPt.Y / 180.0 * Pi;
            double magic = Math.Sin(radLat);
            magic = 1 - Ee * magic * magic;
            double sqrtMagic = Math.Sqrt(magic);
            dLat = (dLat * 180.0) / ((A * (1 - Ee)) / (magic * sqrtMagic) * Pi);
            dLon = (dLon * 180.0) / (A / sqrtMagic * Math.Cos(radLat) * Pi);

            return new MapPoint(wgsPt.X + dLon, wgsPt.Y + dLat, SpatialReferences.Wgs84);
        }

        /// <summary>
        /// GCJ-02 转 WGS-84 (如果需要把高德的结果画回地图，用这个，虽然本次可能只用高亮现有小区)
        /// </summary>
        public static MapPoint Gcj02ToWgs84(double gcjLat, double gcjLon)
        {
            if (OutOfChina(gcjLat, gcjLon))
            {
                return new MapPoint(gcjLon, gcjLat, SpatialReferences.Wgs84);
            }

            double dLat = TransformLat(gcjLon - 105.0, gcjLat - 35.0);
            double dLon = TransformLon(gcjLon - 105.0, gcjLat - 35.0);
            double radLat = gcjLat / 180.0 * Pi;
            double magic = Math.Sin(radLat);
            magic = 1 - Ee * magic * magic;
            double sqrtMagic = Math.Sqrt(magic);
            dLat = (dLat * 180.0) / ((A * (1 - Ee)) / (magic * sqrtMagic) * Pi);
            dLon = (dLon * 180.0) / (A / sqrtMagic * Math.Cos(radLat) * Pi);

            double mgLat = gcjLat + dLat;
            double mgLon = gcjLon + dLon;

            return new MapPoint(gcjLon * 2 - mgLon, gcjLat * 2 - mgLat, SpatialReferences.Wgs84);
        }

        // 判断是否在中国境内 (境外不偏移)
        private static bool OutOfChina(double lat, double lon)
        {
            if (lon < 72.004 || lon > 137.8347) return true;
            if (lat < 0.8293 || lat > 55.8271) return true;
            return false;
        }

        private static double TransformLat(double x, double y)
        {
            double ret = -100.0 + 2.0 * x + 3.0 * y + 0.2 * y * y + 0.1 * x * y + 0.2 * Math.Sqrt(Math.Abs(x));
            ret += (20.0 * Math.Sin(6.0 * x * Pi) + 20.0 * Math.Sin(2.0 * x * Pi)) * 2.0 / 3.0;
            ret += (20.0 * Math.Sin(y * Pi) + 40.0 * Math.Sin(y / 3.0 * Pi)) * 2.0 / 3.0;
            ret += (160.0 * Math.Sin(y / 12.0 * Pi) + 320 * Math.Sin(y * Pi / 30.0)) * 2.0 / 3.0;
            return ret;
        }

        private static double TransformLon(double x, double y)
        {
            double ret = 300.0 + x + 2.0 * y + 0.1 * x * x + 0.1 * x * y + 0.1 * Math.Sqrt(Math.Abs(x));
            ret += (20.0 * Math.Sin(6.0 * x * Pi) + 20.0 * Math.Sin(2.0 * x * Pi)) * 2.0 / 3.0;
            ret += (20.0 * Math.Sin(x * Pi) + 40.0 * Math.Sin(x / 3.0 * Pi)) * 2.0 / 3.0;
            ret += (150.0 * Math.Sin(x / 12.0 * Pi) + 300.0 * Math.Sin(x / 30.0 * Pi)) * 2.0 / 3.0;
            return ret;
        }
    }
}