<#
.SYNOPSIS
    卸载 OneMarkDown。

.DESCRIPTION
    移除当前用户与机器级（如有管理员权限）的注册项，并删除安装目录。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\installer\uninstall.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$clsid = '{7C4E1B2A-9F3D-4E58-9A61-2B8D5C0F7E31}'
$progId = 'OneMarkDown.Connect'
$targetDir = Join-Path $env:LOCALAPPDATA 'OneMarkDown'

function Write-Step($text) { Write-Host "==> $text" -ForegroundColor Cyan }
function Write-Ok($text) { Write-Host "    $text" -ForegroundColor Green }

function Test-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

# 机器级注册项需要管理员才能清掉，存在时自动提权重跑。
$machineAddinKey = "HKLM:\SOFTWARE\Microsoft\Office\OneNote\Addins\$progId"
if ((Test-Path $machineAddinKey) -and -not (Test-Admin)) {
    Write-Host '检测到机器级注册项，正在请求管理员权限...' -ForegroundColor Yellow
    Start-Process powershell -Verb RunAs -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$($MyInvocation.MyCommand.Path)`"") | Out-Null
    Write-Host '已在新窗口中以管理员身份继续。' -ForegroundColor Yellow
    return
}

function Remove-Scope {
    param([string]$SoftwareRoot, [string]$ClassesRoot, [string]$ScopeLabel)

    Write-Step "移除注册项（$ScopeLabel）"

    $addinKey = Join-Path $SoftwareRoot "Microsoft\Office\OneNote\Addins\$progId"
    if (Test-Path $addinKey) {
        Remove-Item $addinKey -Recurse -Force
        Write-Ok $addinKey
    }

    foreach ($path in @(
            (Join-Path $ClassesRoot $progId),
            (Join-Path $ClassesRoot "CLSID\$clsid"))) {
        if (Test-Path $path) {
            Remove-Item $path -Recurse -Force
            Write-Ok $path
        }
    }

    foreach ($resiliency in @(
            (Join-Path $SoftwareRoot 'Microsoft\Office\16.0\OneNote\Resiliency'),
            (Join-Path $SoftwareRoot 'Microsoft\Office\OneNote\Resiliency'))) {
        foreach ($sub in @('DisabledItems', 'AddinTrust', 'StartupItems')) {
            $path = Join-Path $resiliency $sub
            if (Test-Path $path) {
                Remove-Item $path -Recurse -Force
                Write-Ok "已清理 $path"
            }
        }
    }
}

Remove-Scope -SoftwareRoot 'HKCU:\Software' -ClassesRoot 'HKCU:\Software\Classes' -ScopeLabel '当前用户'

if (Test-Admin) {
    Remove-Scope -SoftwareRoot 'HKLM:\SOFTWARE' -ClassesRoot 'HKLM:\SOFTWARE\Classes' -ScopeLabel '机器级'
}
else {
    Write-Step '跳过机器级注册项'
    Write-Ok '当前不是管理员；如需清理 HKLM，请以管理员身份重跑本脚本'
}

Write-Step '结束占用加载项的代理进程'
# 加载项通过 AppID + DllSurrogate 跑在 dllhost.exe 里，退出 OneNote 不会结束它，
# 而它一直锁着 OneMarkDown.AddIn.dll。不先结束它就删不掉安装目录。
$surrogates = Get-CimInstance Win32_Process -Filter "Name='dllhost.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -and $_.CommandLine -match ([regex]::Escape($clsid)) }
if ($surrogates) {
    foreach ($surrogate in $surrogates) {
        Stop-Process -Id $surrogate.ProcessId -Force -ErrorAction SilentlyContinue
        Write-Ok ("已结束 dllhost.exe (PID {0})" -f $surrogate.ProcessId)
    }

    Start-Sleep -Milliseconds 600
}
else {
    Write-Ok '没有代理进程占用'
}

Write-Step "删除安装目录 $targetDir"
if (Test-Path $targetDir) {
    # DLL 释放可能有延迟，重试几次
    for ($attempt = 0; $attempt -lt 5; $attempt++) {
        try {
            Remove-Item $targetDir -Recurse -Force -ErrorAction Stop
            break
        }
        catch {
            Start-Sleep -Milliseconds 400
        }
    }

    if (-not (Test-Path $targetDir)) {
        Write-Ok '已删除'
    }
    else {
        # 兜底：目录整体改名搬走。文件名被占用时往往还能移动父目录，
        # 这样至少注册失效、路径干净，残留文件重启后可手动删。
        $stale = Join-Path $env:TEMP ('OneMarkDown.old.' + (Get-Date -Format 'yyyyMMddHHmmss'))
        try {
            Move-Item $targetDir $stale -Force -ErrorAction Stop
            Write-Note ("文件仍被占用，已移动到 {0}，重启后可手动删除" -f $stale)
        }
        catch {
            Write-Note ("无法删除 {0}：请完全退出 OneNote（任务管理器确认无 ONENOTE.EXE）后重试" -f $targetDir)
        }
    }
}

Write-Host ''
Write-Host '卸载完成。请重启 OneNote。' -ForegroundColor Green
