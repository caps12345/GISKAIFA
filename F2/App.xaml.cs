using System.Configuration;
using System.Data;
using System.Windows;
using Esri.ArcGISRuntime;

namespace WpfMapApp2
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            try
            {
                // 简化 ArcGIS 运行时初始化
                ArcGISRuntimeEnvironment.Initialize();

                // 启用时间戳偏移支持
                ArcGISRuntimeEnvironment.EnableTimestampOffsetSupport = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ArcGIS 运行时初始化失败: {ex.Message}", "初始化错误");
                this.Shutdown();
            }
        }
    }
}