# 构建整个解决方案（Debug；带 -warnaserror，警告即失败）。
. "$PSScriptRoot/_dotnet.ps1"

$root = Split-Path $PSScriptRoot -Parent
$solution = Join-Path $root 'ClipboardManager.sln'

& $DotnetExe build $solution -c Debug -warnaserror
if ($LASTEXITCODE -ne 0) { throw "构建失败，退出码 $LASTEXITCODE" }
