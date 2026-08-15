# Ollama 模型安装器

一个原生 Windows 单文件图形工具。把 EXE 放进 GGUF 模型文件夹，程序会自动识别模型并引导完成导入。

程序采用原创的 AI 羊驼模型导入图标，并内置适配任务栏、窗口和资源管理器的多尺寸图标资源。

## 功能

- 启动时自动扫描 EXE 所在目录的 `.gguf` 文件（也可手动选择或拖入）
- 自动识别同目录的 `mmproj` / `projector` GGUF，并以第二个 `FROM` 导入视觉投影
- 根据 GGUF 文件名自动生成 Ollama 模型名称和 Modelfile 路径
- 自动生成可编辑的 `Modelfile`
- 提供基础、中级、高级三档参数预设，选择后仍可逐项修改
- 上下文长度可选择 4K、8K、16K、32K、64K、128K、256K，生成时自动转换为数字格式
- 设置 `num_ctx`、`temperature`、`top_k`、`top_p`、`min_p`、`repeat_penalty`、`repeat_last_n`、`num_predict`、`seed`、`stop`
- 添加任意自定义参数和中文说明（写入注释）
- 设置 `SYSTEM`、`TEMPLATE`、`LICENSE`、`REQUIRES` 和附加指令
- 实时预览、单独保存、一键执行 `ollama create`
- 实时显示导入日志并支持取消
- 每 5 秒检测 Ollama 后台服务状态并显示版本；导入前若未运行可自动执行 `ollama serve`
- 自动读取已安装模型，显示大小、参数规模、量化和架构，并支持二次确认后删除

## 使用

1. 把 `Ollama模型安装器.exe` 复制到下载好的 GGUF 模型文件夹。
2. 运行 EXE，确认自动识别的文件和模型名称。
3. 选择基础、中级或高级参数预设，按需修改。
4. 在“预览与导入”中检查或直接编辑最终 Modelfile。
5. 点击“一键导入 Ollama”。

电脑需要先安装 Ollama。如果目录内有多个主模型，程序优先选择分片模型的第一片，否则选择体积最大的 GGUF；可在“自动识别”页更换。mmproj 必须与主模型架构匹配，最终能否使用图片还取决于所安装 Ollama 版本对该视觉架构的支持。

## 重新构建

在 Windows PowerShell 中运行：

```powershell
.\build.ps1
```

程序基于 Windows 自带的 .NET Framework/WinForms，不需要安装第三方构建依赖。
