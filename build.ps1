$ErrorActionPreference = 'Stop'
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) {
    throw '未找到 Windows .NET Framework C# 编译器。'
}

$outputDir = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$outputFile = Join-Path $outputDir 'Ollama模型安装器.exe'
$iconSource = Join-Path $PSScriptRoot 'assets\ollama-model-installer-icon.png'
$iconFile = Join-Path $PSScriptRoot 'assets\ollama-model-installer.ico'
$iconBuilder = Join-Path ([IO.Path]::GetTempPath()) 'OllamaModelInstaller.IconBuilder.exe'

if (Test-Path -LiteralPath $iconSource) {
    & $compiler /nologo /target:exe /optimize+ /out:"$iconBuilder" /reference:System.dll /reference:System.Drawing.dll "$PSScriptRoot\tools\IconBuilder.cs"
    if ($LASTEXITCODE -ne 0) { throw "图标工具编译失败，退出代码：$LASTEXITCODE" }
    & $iconBuilder $iconSource $iconFile
    if ($LASTEXITCODE -ne 0) { throw "图标生成失败，退出代码：$LASTEXITCODE" }
    Remove-Item -LiteralPath $iconBuilder -Force -ErrorAction SilentlyContinue
}

& $compiler /nologo /target:winexe /optimize+ /platform:anycpu /win32manifest:"$PSScriptRoot\app.manifest" /win32icon:"$iconFile" /out:"$outputFile" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Runtime.Serialization.dll /reference:System.Windows.Forms.dll "$PSScriptRoot\OllamaModelInstaller.cs"
if ($LASTEXITCODE -ne 0) { throw "编译失败，退出代码：$LASTEXITCODE" }

$item = Get-Item -LiteralPath $outputFile
Write-Output "构建成功：$($item.FullName)"
Write-Output "文件大小：$([Math]::Round($item.Length / 1KB, 1)) KB"
