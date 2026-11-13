using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogueAssistant.Core;
using DialogueAssistant.Recognition;
using DialogueAssistant.Windows;

namespace DialogueAssistant.Desktop;

/// <summary>提供独立桌面窗口的目标选择、画面预览和设置状态。</summary>
public partial class MainWindowViewModel : ObservableObject
{
    /// <summary>负责从已选择的窗口抓取一次客户区帧。</summary>
    private readonly WindowFrameCapture _frameCapture = new();

    /// <summary>负责恢复和保存用户选项的存储服务。</summary>
    private readonly DialogueAssistantSettingsStore _settingsStore = new();

    /// <summary>负责将去敏后的运行状态转换写入本地日志。</summary>
    private readonly DialogueAssistantRunLogger _runLogger = new();

    /// <summary>当前运行会话的取消令牌源。</summary>
    private CancellationTokenSource? _runCancellation;

    /// <summary>当前自动剧情运行循环任务。</summary>
    private Task? _runTask;

    /// <summary>阻止设置恢复期间触发写入。</summary>
    private bool _isInitializing = true;

    /// <summary>保留所有读取到的配置字段，避免基础设置保存时覆盖未来功能选项。</summary>
    private DialogueAssistantOptions _currentOptions = new();

    /// <summary>当前可选择的可见顶层窗口。</summary>
    [ObservableProperty]
    private ObservableCollection<GameWindowDescriptor> _windows = [];

    /// <summary>当前绑定的目标窗口。</summary>
    [ObservableProperty]
    private GameWindowDescriptor? _selectedWindow;

    /// <summary>最近一次成功捕获并转换的预览图像。</summary>
    [ObservableProperty]
    private BitmapSource? _previewImage;

    /// <summary>自动剧情运行循环当前是否启动。</summary>
    [ObservableProperty]
    private bool _isRunning;

    /// <summary>向用户展示的运行状态或错误信息。</summary>
    [ObservableProperty]
    private string _statusText = "选择目标窗口并手动启动自动剧情；识别不确定、模型缺失或窗口非前台时不会执行输入。";

    /// <summary>是否启用无选项对话的自动快进设置。</summary>
    [ObservableProperty]
    private bool _quickSkipEnabled = true;

    /// <summary>当前选项策略索引：0 为首项，1 为随机，2 为末项，3 为不选择。</summary>
    [ObservableProperty]
    private int _selectedOptionIndex;

    /// <summary>点击普通对话选项前等待的毫秒数。</summary>
    [ObservableProperty]
    private int _afterChooseOptionDelayMilliseconds = 500;

    /// <summary>创建视图模型、恢复设置并加载可见窗口列表。</summary>
    public MainWindowViewModel()
    {
        _currentOptions = _settingsStore.Load();
        _quickSkipEnabled = _currentOptions.QuickSkipEnabled;
        _selectedOptionIndex = _currentOptions.OptionSelection switch
        {
            DialogueOptionSelection.First => 0,
            DialogueOptionSelection.Random => 1,
            DialogueOptionSelection.Last => 2,
            DialogueOptionSelection.None => 3,
            _ => 0
        };
        _afterChooseOptionDelayMilliseconds = (int)Math.Clamp(_currentOptions.AfterChooseOptionDelay.TotalMilliseconds, 0, 10_000);
        RefreshWindows();
        _isInitializing = false;
    }

    /// <summary>重新枚举可见顶层窗口并尽量保留原目标选择。</summary>
    [RelayCommand]
    private void RefreshWindows()
    {
        var previousHandle = SelectedWindow?.Handle ?? nint.Zero;
        var windows = WindowCatalog.GetVisibleWindows();
        Windows = new ObservableCollection<GameWindowDescriptor>(windows);
        SelectedWindow = windows.FirstOrDefault(window => window.Handle == previousHandle)
                         ?? windows.FirstOrDefault();
        StatusText = $"找到 {windows.Count} 个可见窗口。自动剧情仅在点击启动后运行，点击停止可取消。";
    }

    /// <summary>捕获所选窗口客户区并更新预览图像。</summary>
    [RelayCommand]
    private void Capture()
    {
        if (SelectedWindow is not { } window)
        {
            StatusText = "请先选择目标窗口。";
            return;
        }

        try
        {
            var frame = _frameCapture.Capture(window);
            if (frame is null)
            {
                StatusText = "未能捕获窗口画面。请确认窗口未最小化，并检查游戏是否使用了不支持 GDI 捕获的渲染模式。";
                return;
            }

            var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null,
                frame.Pixels, checked(frame.Width * 4));
            bitmap.Freeze();
            PreviewImage = bitmap;
            StatusText = $"捕获预览成功：{frame.Width} × {frame.Height}，时间 {frame.CapturedAt.LocalDateTime:T}。自动剧情不会在未手动启动时发送输入。";
        }
        catch (Exception exception)
        {
            StatusText = $"捕获失败：{exception.Message}";
        }
    }

    /// <summary>显式初始化 OCR、识别器和运行循环，并在用户停止或目标失效时释放会话。</summary>
    [RelayCommand]
    private async Task Start()
    {
        if (IsRunning) return;
        if (SelectedWindow is not { } window)
        {
            StatusText = "请先选择目标窗口。";
            return;
        }

        var cancellation = new CancellationTokenSource();
        _runCancellation = cancellation;
        try
        {
            // 模型只在用户明确启动时加载；资源缺失或模型不匹配会中止启动，不发送输入。
            using var textRecognizer = new PpOcrV6TextRecognizer();
            var assetDirectory = new RecognitionAssetProvider().GetRecognitionDirectory();
            using var dialogueRecognizer = new TemplateDialogueRecognizer(assetDirectory, textRecognizer);
            var runner = new DialogueAssistantRunner(_frameCapture, new WindowInputAdapter(),
                new DialogueAssistantEngine(dialogueRecognizer));
            runner.FrameProcessed += outcome =>
            {
                _runLogger.WriteOutcome(outcome);
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    StatusText = outcome.Status;
                }));
            };

            IsRunning = true;
            _runLogger.WriteEvent("自动剧情运行开始");
            StatusText = "自动剧情运行中。仅在目标窗口位于前台且识别稳定时发送输入；点击“停止”可立即取消。";
            _runTask = Task.Run(() => runner.RunAsync(window, GetCurrentOptions, cancellation.Token), cancellation.Token);
            await _runTask;
            StatusText = "自动剧情运行已结束。";
            _runLogger.WriteEvent("自动剧情运行结束");
        }
        catch (OperationCanceledException)
        {
            StatusText = "自动剧情已停止。";
            _runLogger.WriteEvent("自动剧情已取消");
        }
        catch (Exception exception)
        {
            StatusText = $"自动剧情停止：{exception.Message}";
            _runLogger.WriteEvent($"自动剧情异常停止 type={exception.GetType().Name}");
        }
        finally
        {
            IsRunning = false;
            _runTask = null;
            _runCancellation = null;
            cancellation.Dispose();
        }
    }

    /// <summary>取消运行循环并等待当前帧处理完成，确保不会再继续发出动作。</summary>
    [RelayCommand]
    private async Task Stop()
    {
        var cancellation = _runCancellation;
        var runTask = _runTask;
        if (cancellation is null || runTask is null) return;

        cancellation.Cancel();
        try
        {
            await runTask;
        }
        catch (OperationCanceledException)
        {
            // 正常停止路径由 Start 命令更新状态。
        }
        catch (Exception exception)
        {
            StatusText = $"停止期间运行循环结束：{exception.Message}";
        }
    }

    /// <summary>生成下一帧使用的当前设置快照。</summary>
    private DialogueAssistantOptions GetCurrentOptions() => _currentOptions with
    {
        Enabled = true,
        QuickSkipEnabled = QuickSkipEnabled,
        OptionSelection = SelectedOptionIndex switch
        {
            1 => DialogueOptionSelection.Random,
            2 => DialogueOptionSelection.Last,
            3 => DialogueOptionSelection.None,
            _ => DialogueOptionSelection.First
        },
        AfterChooseOptionDelay = TimeSpan.FromMilliseconds(Math.Clamp(AfterChooseOptionDelayMilliseconds, 0, 10_000))
    };

    /// <summary>快进开关变更时保存当前设置。</summary>
    partial void OnQuickSkipEnabledChanged(bool value) => SaveOptions();

    /// <summary>选项策略变更时保存当前设置。</summary>
    partial void OnSelectedOptionIndexChanged(int value) => SaveOptions();

    /// <summary>选项点击前等待时间变更时保存当前设置。</summary>
    partial void OnAfterChooseOptionDelayMillisecondsChanged(int value) => SaveOptions();

    /// <summary>将当前设置安全写入用户配置文件。</summary>
    private void SaveOptions()
    {
        if (_isInitializing) return;

        var optionSelection = SelectedOptionIndex switch
        {
            1 => DialogueOptionSelection.Random,
            2 => DialogueOptionSelection.Last,
            3 => DialogueOptionSelection.None,
            _ => DialogueOptionSelection.First
        };
        try
        {
            _currentOptions = _currentOptions with
            {
                QuickSkipEnabled = QuickSkipEnabled,
                OptionSelection = optionSelection,
                AfterChooseOptionDelay = TimeSpan.FromMilliseconds(Math.Clamp(AfterChooseOptionDelayMilliseconds, 0, 10_000))
            };
            _settingsStore.Save(_currentOptions);
            StatusText = "设置已保存。自动剧情不会在未手动启动时发送输入。";
        }
        catch (Exception exception)
        {
            StatusText = $"无法保存设置：{exception.Message}";
        }
    }
}
