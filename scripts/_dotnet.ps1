# 统一解析 .NET SDK 路径。
#
# 背景（产品计划 D-12）：本机系统 PATH 中的 C:\Program Files\dotnet 排在用户 PATH 之前，
# 且它只装了运行时、没有 SDK —— 直接执行 dotnet 会报「No .NET SDKs were found」。
# 因此所有脚本都通过这里拿到确定可用的 dotnet 可执行文件。
#
# 优先级：$env:DOTNET_ROOT → D:\dotnet → PATH 上的 dotnet

$DotnetExe = $null

if ($env:DOTNET_ROOT) {
    $candidate = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
    if (Test-Path -LiteralPath $candidate) { $DotnetExe = $candidate }
}

if (-not $DotnetExe -and (Test-Path -LiteralPath 'D:\dotnet\dotnet.exe')) {
    $DotnetExe = 'D:\dotnet\dotnet.exe'
}

if (-not $DotnetExe) {
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($command) { $DotnetExe = $command.Source }
}

if (-not $DotnetExe) {
    throw '未找到可用的 dotnet。请安装 .NET 8 SDK，或设置环境变量 DOTNET_ROOT 指向 SDK 目录。'
}

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
