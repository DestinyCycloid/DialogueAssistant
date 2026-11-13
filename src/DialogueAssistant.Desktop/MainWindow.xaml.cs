using System.Windows;

namespace DialogueAssistant.Desktop;

/// <summary>独立程序主视图，仅通过构造注入其视图模型。</summary>
public partial class MainWindow : Window
{
    /// <summary>使用注入的视图模型初始化主窗口。</summary>
    public MainWindow(MainWindowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }
}
