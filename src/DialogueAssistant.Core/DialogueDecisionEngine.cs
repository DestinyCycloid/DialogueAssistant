namespace DialogueAssistant.Core;

/// <summary>根据识别结果和用户选项生成自动剧情动作，不直接访问窗口或输入设备。</summary>
public sealed class DialogueDecisionEngine
{
    /// <summary>上一次生成输入动作的时间。</summary>
    private DateTimeOffset _lastActionAt = DateTimeOffset.MinValue;

    /// <summary>上一次处理的对话 UI 状态。</summary>
    private DialogueUiState _previousState = DialogueUiState.Unknown;

    /// <summary>上一次识别到的选项数量，用于忽略 OCR 边界框的轻微像素抖动。</summary>
    private int _previousOptionCount = -1;

    /// <summary>上一次识别到的选项文字；内容改变时重新进行稳定帧确认。</summary>
    private string _previousOptionTextSignature = string.Empty;

    /// <summary>连续识别到相同状态与选项布局的帧数。</summary>
    private int _stableFrameCount;

    /// <summary>根据当前帧的识别结果决定是否快进或选择一个选项。</summary>
    public DialogueAction Decide(DialogueRecognitionResult recognition, DialogueAssistantOptions options, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(recognition);
        ArgumentNullException.ThrowIfNull(options);

        // 未识别、置信度不足及非对话画面全部失败关闭，不向游戏发送输入，并清除连续帧状态。
        if (recognition.State is DialogueUiState.Unknown or DialogueUiState.NotDialogue || recognition.Confidence < options.MinimumConfidence)
        {
            ResetStability();
            return DialogueAction.None;
        }

        // UI 状态、候选数或文字内容改变时重新计数；忽略边界框的轻微像素抖动。
        var optionCount = recognition.Options.Count;
        var optionTextSignature = recognition.State == DialogueUiState.Options
            ? string.Join("\u001f", recognition.Options.Select(option => option.Text.Trim()))
            : string.Empty;
        if (_previousState == recognition.State && _previousOptionCount == optionCount
            && string.Equals(_previousOptionTextSignature, optionTextSignature, StringComparison.Ordinal))
            _stableFrameCount++;
        else
        {
            _previousState = recognition.State;
            _previousOptionCount = optionCount;
            _previousOptionTextSignature = optionTextSignature;
            _stableFrameCount = 1;
        }

        if (_stableFrameCount < Math.Max(1, options.RequiredStableFrames))
            return DialogueAction.None;

        // 任意类型输入都遵守节流间隔，避免同一状态的相邻帧重复触发。
        if (now - _lastActionAt < options.MinimumActionInterval)
            return DialogueAction.None;

        if (recognition.State == DialogueUiState.Options)
        {
            if (recognition.Options.Count == 0) return DialogueAction.None;

            // 自定义优先关键词优先于“不选择”策略，与原自动剧情规则保持一致。
            var optionIndex = FindPriorityOption(recognition.Options, options.CustomPriorityOptions,
                options.CustomPriorityOptionsEnabled);
            if (optionIndex < 0 && options.OptionSelection == DialogueOptionSelection.None)
                return DialogueAction.None;

            if (optionIndex < 0 && !options.SkipBuiltInClickOptions)
            {
                optionIndex = FindKeywordOption(recognition.Options, options.BuiltInSelectOptions);
                if (optionIndex < 0 && HasKeywordMatch(recognition.Options, options.PauseOptions, options.DefaultPauseOptions))
                    return DialogueAction.None;
            }

            if (optionIndex < 0)
            {
                optionIndex = options.OptionSelection switch
                {
                    DialogueOptionSelection.Last => recognition.Options.Count - 1,
                    DialogueOptionSelection.Random => Random.Shared.Next(recognition.Options.Count),
                    _ => 0
                };
            }

            var option = recognition.Options[optionIndex];
            var (x, y) = option.Center;
            _lastActionAt = now;
            return new DialogueAction(DialogueActionType.Click, x, y);
        }

        if (recognition.State == DialogueUiState.Dialogue && options.QuickSkipEnabled)
        {
            _lastActionAt = now;
            return new DialogueAction(DialogueActionType.PressSpace);
        }

        return DialogueAction.None;
    }

    /// <summary>按启用的自定义优先关键词查找第一个匹配选项；未命中时返回 -1。</summary>
    private static int FindPriorityOption(IReadOnlyList<DialogueOptionBounds> options, string keywordsText, bool enabled)
    {
        if (!enabled || string.IsNullOrWhiteSpace(keywordsText)) return -1;
        var keywords = keywordsText.Split(new[] { '\r', '\n', ';', '；' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(keyword => keyword.Trim())
            .Where(keyword => keyword.Length > 0)
            .ToArray();
        return FindKeywordOption(options, keywords);
    }

    /// <summary>按一组关键词查找第一个匹配选项；未命中时返回 -1。</summary>
    private static int FindKeywordOption(IReadOnlyList<DialogueOptionBounds> options, IEnumerable<string>? keywords)
    {
        if (keywords is null) return -1;
        var normalizedKeywords = keywords.Where(keyword => !string.IsNullOrWhiteSpace(keyword))
            .Select(keyword => keyword.Trim()).ToArray();
        for (var index = 0; index < options.Count; index++)
        {
            if (normalizedKeywords.Any(keyword => options[index].Text.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                return index;
        }

        return -1;
    }

    /// <summary>判断任一暂停关键词列表是否命中当前识别选项。</summary>
    private static bool HasKeywordMatch(IReadOnlyList<DialogueOptionBounds> options, params IReadOnlyList<string>[] keywordGroups)
    {
        return keywordGroups.Any(group => FindKeywordOption(options, group) >= 0);
    }

    /// <summary>清除连续帧稳定计数；暂停或帧中断后应调用以重新确认状态。</summary>
    public void ResetStability()
    {
        _previousState = DialogueUiState.Unknown;
        _previousOptionCount = -1;
        _previousOptionTextSignature = string.Empty;
        _stableFrameCount = 0;
    }
}
