using System.Windows;

namespace DialogueAssistant.Desktop;

/// <summary>配置独立桌面应用启动流程并构造主视图与视图模型。</summary>
public partial class App : Application
{
    /// <summary>启动 WPF 应用并通过构造函数注入主窗口视图模型。</summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var mainWindow = new MainWindow(new MainWindowViewModel());
        MainWindow = mainWindow;
        mainWindow.Show();
    }
}
