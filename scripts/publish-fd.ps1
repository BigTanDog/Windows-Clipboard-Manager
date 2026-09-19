# 形态 B：框架依赖单文件（默认交付形态，需目标机安装 .NET 8 Desktop Runtime）。
. "$PSScriptRoot/_dotnet.ps1"
. "$PSScriptRoot/_release.ps1"

$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src/ClipboardManager.App/ClipboardManager.App.csproj'
$output = Join-Path $root 'artifacts/小体积-需装运行时'

# 注意：单文件压缩（EnableCompressionInSingleFile）只支持自包含发布，
# 框架依赖形态不能启用（SDK 报 NETSDK1176），也不需要 —— 依赖由目标机运行时提供。
& $DotnetExe publish $project -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none -p:SatelliteResourceLanguages=en `
    -o $output
if ($LASTEXITCODE -ne 0) { throw "发布失败，退出码 $LASTEXITCODE" }

Get-ChildItem -LiteralPath $output -File | Sort-Object Length -Descending |
    Select-Object -First 5 Name, @{ Name = 'SizeMB'; Expression = { [math]::Round($_.Length / 1MB, 2) } } |
    Format-Table -AutoSize

# 同步到 release/（文件名带版本号，见 _release.ps1）
Copy-ToRelease -ExePath (Join-Path $output 'ClipboardManager.exe') -BaseName 'ClipboardManager_小体积_需装NET8运行时'
