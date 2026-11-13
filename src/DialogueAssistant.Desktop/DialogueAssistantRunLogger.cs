using System.IO;
using System.Text;
using DialogueAssistant.Core;

namespace DialogueAssistant.Desktop;

/// <summary>将自动剧情运行状态转换写入当前用户的本地日志，不保存 OCR 文本或截图。</summary>
public sealed class DialogueAssistantRunLogger
{
    /// <summary>串行保护状态比较与日志文件追加。</summary>
    private readonly object _syncRoot = new();

    /// <summary>当前日志目录。</summary>
    private readonly string _logDirectory;

    /// <summary>上次写入的识别状态与动作组合，用于抑制重复逐帧日志。</summary>
    private string? _lastOutcomeKey;

    /// <summary>创建运行日志服务；未指定目录时使用当前用户 LocalApplicationData。</summary>
    public DialogueAssistantRunLogger(string? directory = null)
    {
        _logDirectory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DialogueAssistant", "logs");
    }

    /// <summary>仅在识别状态、候选动作或处理状态改变时记录一条去敏日志。</summary>
    public void WriteOutcome(DialogueFrameOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        var key = $"{outcome.Recognition.State}|{outcome.Action.Type}|{outcome.Status}";
        lock (_syncRoot)
        {
            if (string.Equals(_lastOutcomeKey, key, StringComparison.Ordinal)) return;
            _lastOutcomeKey = key;
            AppendLine($"帧处理 state={outcome.Recognition.State}; confidence={outcome.Recognition.Confidence:F2}; action={outcome.Action.Type}; status={outcome.Status}");
        }
    }

    /// <summary>写入运行生命周期事件；调用方不得传入 OCR 文本、密钥或截图数据。</summary>
    public void WriteEvent(string eventName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        lock (_syncRoot)
        {
            _lastOutcomeKey = null;
            AppendLine(eventName);
        }
    }

    /// <summary>以 UTF-8 无 BOM 追加到按日期区分的本地日志文件。</summary>
    private void AppendLine(string message)
    {
        try
        {
            Directory.CreateDirectory(_logDirectory);
            var path = Path.Combine(_logDirectory, $"run-{DateTime.Now:yyyyMMdd}.log");
            var line = $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}";
            File.AppendAllText(path, line, new UTF8Encoding(false));
        }
        catch (IOException)
        {
            // 诊断日志不可用时不应中断自动剧情或输入安全检查。
        }
        catch (UnauthorizedAccessException)
        {
            // 诊断日志不可用时不应中断自动剧情或输入安全检查。
        }
    }
}
