# 运行全部测试。
. "$PSScriptRoot/_dotnet.ps1"

$root = Split-Path $PSScriptRoot -Parent
$solution = Join-Path $root 'ClipboardManager.sln'

& $DotnetExe test $solution -c Debug --logger 'console;verbosity=normal'
if ($LASTEXITCODE -ne 0) { throw "测试失败，退出码 $LASTEXITCODE" }
