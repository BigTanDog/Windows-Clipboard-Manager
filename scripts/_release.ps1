# 把发布产物放进 release/，文件名带上版本号。
#
# 背景（用户 2026-09-19 要求）：release 目录里的 exe 名字一样、只有时间不同，在 GitHub 上根本分不出
# 哪个是哪一版 —— 现在文件名统一带 `_v<版本>`，与设置窗口底部显示的版本号一致。
#
# 版本号取自**发布出来的 exe 自身的「文件版本」属性**（源头是 Directory.Build.props 的 <Version>），
# 所以不可能出现"文件名写 1.1.2、程序里是 1.1.1"的错位。
#
# release/ 始终只保留当前版本的两个产物（历史版本在 git 历史里），避免仓库体积无限膨胀。

function Copy-ToRelease {
    param(
        [Parameter(Mandatory = $true)][string]$ExePath,
        [Parameter(Mandatory = $true)][string]$BaseName,
        [switch]$SkipLocalDeploy
    )

    if (-not (Test-Path -LiteralPath $ExePath)) {
        throw "找不到发布产物：$ExePath"
    }

    $root = Split-Path $PSScriptRoot -Parent
    $releaseDir = Join-Path $root 'release'
    $null = New-Item -ItemType Directory -Force -Path $releaseDir

    # 1.1.2.0 → 1.1.2（去掉恒为 0 的第四段，与设置窗口里显示的写法一致）
    $fileVersion = (Get-Item -LiteralPath $ExePath).VersionInfo.FileVersion
    $version = ($fileVersion -split '\.')[0..2] -join '.'
    $leaf = "{0}_v{1}.exe" -f $BaseName, $version
    $target = Join-Path $releaseDir $leaf

    Copy-Item -LiteralPath $ExePath -Destination $target -Force
    Write-Host ("  已放入 release：{0}（{1} MB）" -f $leaf, [math]::Round((Get-Item -LiteralPath $target).Length / 1MB, 2))

    # 同一形态的其它版本产物删掉：release/ 只留当前这一版
    Get-ChildItem -LiteralPath $releaseDir -Filter ("{0}_v*.exe" -f $BaseName) |
        Where-Object { $_.Name -ne $leaf } |
        ForEach-Object {
            Remove-Item -LiteralPath $_.FullName -Force
            Write-Host ("  已移除旧版本：{0}" -f $_.Name)
        }

    # 本机日常使用副本的同步（用户 2026-10-05 要求：改了新版本自动拉到测试文件夹）
    Deploy-ToLocalTestCopy -ExePath $ExePath -Skip:$SkipLocalDeploy
}

# 把发布产物覆盖到「本机日常使用的那份便携副本」，让它始终是最新版。
#
# 目录来源：$env:CLIPBOARDMANAGER_TEST_DIR → 否则默认 D:\ClipboardManager测试。
#   目录不存在就**静默跳过** —— 脚本在别人机器上（或本机删掉了这份副本）依然能正常发布，
#   不会因为"本机专属目录缺失"而整体失败。
#
# 覆盖前若该副本正在运行，会先结束它、覆盖完再启动（exe 被占用时无法写入）；
#   数据在它自己的 data\ 目录里，不受影响（只换 exe）。
#
# 只由形态 B（小体积 2.9MB，日常用）调用；形态 A（免运行时 64MB）传 -SkipLocalDeploy，
#   否则本机日常副本会突然变成 64MB。
function Deploy-ToLocalTestCopy {
    param(
        [Parameter(Mandatory = $true)][string]$ExePath,
        [switch]$Skip
    )

    if ($Skip) {
        Write-Host '  已跳过本机日常副本同步（-SkipLocalDeploy）'
        return
    }

    $testDir = if (-not [string]::IsNullOrWhiteSpace($env:CLIPBOARDMANAGER_TEST_DIR)) {
        $env:CLIPBOARDMANAGER_TEST_DIR
    }
    else {
        'D:\ClipboardManager测试'
    }

    if (-not (Test-Path -LiteralPath $testDir)) {
        Write-Host ("  跳过本机日常副本同步：目录不存在（{0}）" -f $testDir)
        return
    }

    $target = Join-Path $testDir 'ClipboardManager.exe'
    $version = ((Get-Item -LiteralPath $ExePath).VersionInfo.FileVersion -split '\.')[0..2] -join '.'

    # 正在运行就先结束：Windows 下占用中的 exe 无法覆盖。
    $running = @(Get-Process -Name 'ClipboardManager' -ErrorAction SilentlyContinue)
    $restart = $false
    if ($running.Count -gt 0) {
        Write-Host ("  本机日常副本正在运行（PID {0}）：先结束、覆盖后自动启动" -f (($running | ForEach-Object { $_.Id }) -join ','))
        $running | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 800
        $restart = $true
    }

    Copy-Item -LiteralPath $ExePath -Destination $target -Force
    Write-Host ("  已覆盖本机日常副本：{0}（v{1}，原 data\ 保留）" -f $target, $version)

    if ($restart) {
        $null = Start-Process -FilePath $target -WorkingDirectory $testDir
        Write-Host '  已重新启动本机日常副本'
    }
}
