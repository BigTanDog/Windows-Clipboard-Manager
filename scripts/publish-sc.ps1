# 形态 A：自包含单文件（免运行时，体积大；产品计划 D-01 的备选形态）。
. "$PSScriptRoot/_dotnet.ps1"
. "$PSScriptRoot/_release.ps1"

$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src/ClipboardManager.App/ClipboardManager.App.csproj'
$output = Join-Path $root 'artifacts/大体积-免运行时'

& $DotnetExe publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:SatelliteResourceLanguages=en `
    -o $output
if ($LASTEXITCODE -ne 0) { throw "发布失败，退出码 $LASTEXITCODE" }

Get-ChildItem -LiteralPath $output -File | Sort-Object Length -Descending |
    Select-Object -First 5 Name, @{ Name = 'SizeMB'; Expression = { [math]::Round($_.Length / 1MB, 2) } } |
    Format-Table -AutoSize

# 同步到 release/（文件名带版本号，见 _release.ps1）
Copy-ToRelease -ExePath (Join-Path $output 'ClipboardManager.exe') -BaseName 'ClipboardManager_完整版_免装运行时'
