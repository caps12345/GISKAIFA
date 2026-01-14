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
                // ★★★ 在这里添加许可证代码 (必须在 Initialize 之前) ★★★
                // 将下方引号内的内容替换为你链接里获取到的那一长串字符
                ArcGISRuntimeEnvironment.SetLicense("runtimestandard,1000,rud3141592653,30-Jun-2026,HC4XTK8EL7E5TPCEJ196");

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