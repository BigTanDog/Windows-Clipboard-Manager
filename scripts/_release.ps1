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
        [Parameter(Mandatory = $true)][string]$BaseName
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
}
