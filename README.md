# DialogueAssistant（精简版）

独立的 Windows 桌面对话辅助程序，仅保留普通对话快进、普通对话选项识别与选择所需的代码和资源。仓库不依赖 BetterGI 项目或其 NuGet 包。OCR ONNX 模型是现成的数据文件，不参与 C# 编译；构建和发布时会复制到应用资源路径。

## 环境要求

- Windows x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)，用于还原、编译和运行测试
- 运行已发布程序时需要安装 **.NET 10 Desktop Runtime**。当前发布配置是框架依赖型（`SelfContained=false`），不会把 .NET Desktop Runtime 打包进程序。

## 编译

在仓库根目录打开 PowerShell：

```powershell
dotnet restore .\DialogueAssistant.sln
dotnet build .\DialogueAssistant.sln -c Release --no-restore
```

## 运行测试

```powershell
dotnet test .\DialogueAssistant.sln -c Release
```

测试涵盖对话决策、Runner 集成、OCR DB 后处理，以及使用仓库内模型和样例图的 OCR 推理。OCR 推理测试不是连接真实游戏窗口的全链路测试。

## 发布 Windows x64 精简版

```powershell
dotnet publish .\src\DialogueAssistant.Desktop\DialogueAssistant.Desktop.csproj `
  -p:PublishProfile=Windows-x64-minimal `
  -o .\artifacts\DialogueAssistant-win-x64-minimal
```

发布配置位于 `src/DialogueAssistant.Desktop/Properties/PublishProfiles/Windows-x64-minimal.pubxml`。该配置生成 Release、Windows x64、框架依赖型单文件程序；发布目录同时包含运行所需的 PP-OCRv6 模型、字典配置、模板资源和许可证。ONNX 文件作为内容资源复制，不会被编译。

运行：

```powershell
.\artifacts\DialogueAssistant-win-x64-minimal\DialogueAssistant.Desktop.exe
```

生成的 `bin/`、`obj/`、`artifacts/` 和测试结果目录不会纳入 Git。

## 使用方法

先运行原神主程序
然后运行构建路径下的 DialogueAssistant.Desktop.exe
下拉列表选择原神主程序
点击捕获画面，出现画面说明成功
然后点击启动自动剧情
保持原神主程序处于前台，即可使用，会自动点对话和选择选项

## 许可

本仓库按 GNU General Public License v3.0（GPLv3）授权，详见根目录 [`LICENSE`](LICENSE)。随附 OCR 模型也带有其来源包中的许可证声明，详见 `assets/models/PaddleOCR/LICENSE`。
