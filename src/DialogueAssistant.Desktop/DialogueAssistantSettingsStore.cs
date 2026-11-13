using System.IO;
using DialogueAssistant.Core;
using Newtonsoft.Json;

namespace DialogueAssistant.Desktop;

/// <summary>将独立程序设置保存到当前用户的 LocalApplicationData 目录。</summary>
public sealed class DialogueAssistantSettingsStore
{
    /// <summary>保留易于手工检查的格式并允许未来版本添加新字段。</summary>
    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        Formatting = Formatting.Indented,
        MissingMemberHandling = MissingMemberHandling.Ignore
    };

    /// <summary>当前用户设置文件的完整路径。</summary>
    private readonly string _settingsPath;

    /// <summary>创建设置存储，并可为测试或便携部署指定独立目录。</summary>
    public DialogueAssistantSettingsStore(string? directory = null)
    {
        var settingsDirectory = directory
                               ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DialogueAssistant");
        _settingsPath = Path.Combine(settingsDirectory, "settings.json");
    }

    /// <summary>读取用户设置；文件不存在时返回默认选项。</summary>
    public DialogueAssistantOptions Load()
    {
        if (!File.Exists(_settingsPath)) return new DialogueAssistantOptions();

        try
        {
            var json = File.ReadAllText(_settingsPath);
            return JsonConvert.DeserializeObject<DialogueAssistantOptions>(json, SerializerSettings)
                   ?? new DialogueAssistantOptions();
        }
        catch (JsonException)
        {
            // 配置损坏时使用默认值，不阻止应用启动；不自动覆盖损坏文件。
            return new DialogueAssistantOptions();
        }
        catch (IOException)
        {
            return new DialogueAssistantOptions();
        }
    }

    /// <summary>以临时文件加替换方式持久化选项，避免中断写入破坏原设置。</summary>
    public void Save(DialogueAssistantOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var directory = Path.GetDirectoryName(_settingsPath)
                        ?? throw new InvalidOperationException("设置文件路径缺少目录。");
        Directory.CreateDirectory(directory);

        var temporaryPath = _settingsPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(options, SerializerSettings));
        if (File.Exists(_settingsPath))
            File.Replace(temporaryPath, _settingsPath, null);
        else
            File.Move(temporaryPath, _settingsPath);
    }
}
