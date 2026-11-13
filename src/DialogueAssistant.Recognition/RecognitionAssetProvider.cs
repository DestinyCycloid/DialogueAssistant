using System.Security.Cryptography;

namespace DialogueAssistant.Recognition;

/// <summary>解析并校验独立程序随包提供的剧情识别素材目录。</summary>
public sealed class RecognitionAssetProvider
{
    /// <summary>指定应用根目录；未传入时从当前程序发布目录解析资源。</summary>
    public RecognitionAssetProvider(string? applicationDirectory = null)
    {
        ApplicationDirectory = applicationDirectory ?? AppContext.BaseDirectory;
    }

    /// <summary>应用程序独立资源根目录。</summary>
    public string ApplicationDirectory { get; }

    /// <summary>获取包含清单和模板的识别资源目录；资源缺失或校验失败时明确报错。</summary>
    public string GetRecognitionDirectory()
    {
        var directory = Path.Combine(ApplicationDirectory, "assets", "recognition");
        var manifestPath = Path.Combine(directory, "manifest.sha256");
        if (!File.Exists(manifestPath))
            throw new DirectoryNotFoundException($"独立识别资源目录不存在或清单缺失：{directory}");

        ValidateManifest(directory, manifestPath);
        return directory;
    }

    /// <summary>验证清单中的每个资源哈希，并拒绝目录外相对路径。</summary>
    private static void ValidateManifest(string directory, string manifestPath)
    {
        var root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        // 独立版只发布并校验当前识别管线实际读取的两张模板图；清单其余条目属于其他功能。
        var requiredAssets = new[] { "icon_option.png", "1920x1080/disabled_ui.png" };
        var entries = File.ReadAllLines(manifestPath)
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length == 2 && requiredAssets.Contains(parts[1], StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (entries.Length != requiredAssets.Length)
            throw new InvalidDataException($"识别资源清单缺少当前主线功能所需条目：{manifestPath}");

        foreach (var entry in entries)
        {
            var relativePath = entry[1].Replace('/', Path.DirectorySeparatorChar);
            var assetPath = Path.GetFullPath(Path.Combine(directory, relativePath));
            if (!assetPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"识别资源清单包含目录外路径：{entry[1]}");
            if (!File.Exists(assetPath))
                throw new FileNotFoundException($"独立识别资源缺失：{assetPath}", assetPath);

            using var stream = File.OpenRead(assetPath);
            var actualHash = Convert.ToHexString(SHA256.HashData(stream));
            if (!actualHash.Equals(entry[0], StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"独立识别资源完整性校验失败：{entry[1]}");
        }
    }
}
