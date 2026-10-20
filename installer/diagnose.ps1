<#
.SYNOPSIS
    一次性收集 OneMarkDown 加载项在本机的加载条件，用于定位"选项卡不显示 / 未加载.运行时错误"。

.DESCRIPTION
    只读脚本，不修改任何设置。检查项：
      环境与位数 / OneNote 版本 / COM 注册（含 32 位视图）/ OneNote 加载项项
      / 韧性记录 / 实际 COM 激活 / 运行中进程里的 CLR 模块 / 日志尾部

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\diagnose.ps1
#>
[CmdletBinding()]
param(
    [string]$OutputFile,
    [switch]$SkipComTest
)

$ErrorActionPreference = 'Continue'

$clsid = '{7C4E1B2A-9F3D-4E58-9A61-2B8D5C0F7E31}'
$progId = 'OneMarkDown.Connect'
$report = New-Object System.Text.StringBuilder

function Add-Line {
    param([string]$Text = '')
    Write-Host $Text
    [void]$report.AppendLine($Text)
}

function Add-Section {
    param([string]$Title)
    Add-Line ''
    Add-Line ('=== ' + $Title + ' ===')
}

Add-Line ('OneMarkDown 诊断  ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))

Add-Section '1. 系统与运行时'
Add-Line ('Windows: ' + [System.Environment]::OSVersion.VersionString)
Add-Line ('PowerShell 位数: ' + $(if ([System.Environment]::Is64BitProcess) { '64' } else { '32' }))
$ndp = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -ErrorAction SilentlyContinue
Add-Line ('.NET Framework Release: ' + $(if ($ndp) { $ndp.Release } else { '未检测到 v4' }))

Add-Section '2. OneNote 与 Office 位数'
$appPathKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\ONENOTE.EXE'
$oneNotePath = (Get-ItemProperty $appPathKey -ErrorAction SilentlyContinue).'(default)'
if (-not $oneNotePath) {
    Add-Line '!! 没有找到经典版 OneNote（ONENOTE.EXE）。新版统一 OneNote 不支持 COM 加载项。'
}
else {
    Add-Line ('OneNote 路径: ' + $oneNotePath)
    try {
        $versionInfo = (Get-Item $oneNotePath).VersionInfo
        Add-Line ('OneNote 版本: ' + $versionInfo.FileVersion)
    }
    catch {
        Add-Line 'OneNote 版本: 读取失败'
    }

    # PE 头判断位数，比看路径可靠
    try {
        $stream = [System.IO.File]::OpenRead($oneNotePath)
        $reader = New-Object System.IO.BinaryReader($stream)
        $stream.Position = 0x3C
        $peOffset = $reader.ReadInt32()
        $stream.Position = $peOffset + 4
        $machine = $reader.ReadUInt16()
        $reader.Dispose()
        $stream.Dispose()
        $bitness = switch ($machine) { 0x8664 { '64 位 (x64)' } 0x14c { '32 位 (x86)' } default { ('未知 0x{0:X}' -f $machine) } }
        Add-Line ('OneNote 位数: ' + $bitness)
    }
    catch {
        Add-Line 'OneNote 位数: 读取失败'
    }
}

$c2r = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Office\ClickToRun\Configuration' -ErrorAction SilentlyContinue
if ($c2r) {
    Add-Line ('Office 平台: ' + $c2r.Platform)
    Add-Line ('Office 版本: ' + $c2r.VersionToReport)
    Add-Line ('Office 产品: ' + $c2r.ProductReleaseIds)
}

Add-Section '3. COM 注册（CLSID）'
foreach ($view in @('Registry64', 'Registry32')) {
    $hive = [Microsoft.Win32.RegistryHive]::LocalMachine
    $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey($hive, [Microsoft.Win32.RegistryView]::$view)
    $key = $base.OpenSubKey("SOFTWARE\Classes\CLSID\$clsid\InprocServer32")
    if ($key) {
        Add-Line ("HKLM[$view] 已注册")
        Add-Line ('    InprocServer32 = ' + $key.GetValue(''))
        Add-Line ('    ThreadingModel = ' + $key.GetValue('ThreadingModel'))
        Add-Line ('    Class          = ' + $key.GetValue('Class'))
        Add-Line ('    Assembly       = ' + $key.GetValue('Assembly'))
        Add-Line ('    RuntimeVersion = ' + $key.GetValue('RuntimeVersion'))
        Add-Line ('    CodeBase       = ' + $key.GetValue('CodeBase'))
        $codeBase = $key.GetValue('CodeBase')
        if ($codeBase) {
            $localPath = ([uri]$codeBase).LocalPath
            Add-Line ('    CodeBase 文件存在: ' + (Test-Path $localPath))
        }
        $key.Close()
    }
    else {
        Add-Line ("HKLM[$view] 未注册")
    }

    $base.Close()
}

foreach ($view in @('Registry64', 'Registry32')) {
    $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, [Microsoft.Win32.RegistryView]::$view)
    $key = $base.OpenSubKey("Software\Classes\CLSID\$clsid\InprocServer32")
    Add-Line ("HKCU[$view]: " + $(if ($key) { '已注册' } else { '未注册' }))
    if ($key) { $key.Close() }
    $base.Close()
}

Add-Section '4. OneNote 加载项项'
foreach ($scope in @(@('HKCU', 'HKCU:\Software'), @('HKLM', 'HKLM:\SOFTWARE'))) {
    $keyPath = Join-Path $scope[1] "Microsoft\Office\OneNote\Addins\$progId"
    if (Test-Path $keyPath) {
        $item = Get-Item $keyPath
        Add-Line ($scope[0] + ': 已注册, LoadBehavior = ' + $item.GetValue('LoadBehavior') + ', FriendlyName = ' + $item.GetValue('FriendlyName'))
    }
    else {
        Add-Line ($scope[0] + ': 未注册')
    }
}

$dataKey = "HKCU:\Software\Microsoft\Office\OneNote\AddInsData\$progId"
if (Test-Path $dataKey) {
    Add-Line ('AddInsData LoadCount = ' + (Get-Item $dataKey).GetValue('LoadCount'))
}

Add-Section '5. 韧性记录（Office 的禁用/慢启动名单）'
foreach ($root in @('HKCU:\Software\Microsoft\Office\16.0\OneNote\Resiliency', 'HKCU:\Software\Microsoft\Office\OneNote\Resiliency')) {
    if (Test-Path $root) {
        foreach ($sub in @('DisabledItems', 'StartupItems', 'AddinTrust', 'DoNotDisableAddinList')) {
            $path = Join-Path $root $sub
            if (Test-Path $path) {
                $names = (Get-Item $path).GetValueNames()
                Add-Line ("$path -> " + $(if ($names.Count -gt 0) { $names -join ', ' } else { '(空)' }))
            }
        }
    }
}

Add-Section '6. 实际 COM 激活测试'
if ($SkipComTest) {
    Add-Line '已按 -SkipComTest 跳过（本项只创建一次对象，不写注册表）'
}
else {
try {
    $type = [type]::GetTypeFromCLSID([Guid]$clsid)
    $instance = [Activator]::CreateInstance($type)
    Add-Line 'COM 创建: 成功'

    try {
        $xml = $instance.GetCustomUI('Microsoft.OneNote.Content.MsView')
        Add-Line ('GetCustomUI 返回长度: ' + $(if ($xml) { $xml.Length } else { 0 }))
    }
    catch {
        Add-Line ('GetCustomUI 失败: ' + $_.Exception.Message)
    }
}
catch {
    $hresult = ''
    $exception = $_.Exception
    while ($exception) {
        if ($exception.HResult -ne 0) { $hresult = '0x' + ('{0:X8}' -f $exception.HResult); break }
        $exception = $exception.InnerException
    }
    Add-Line ('COM 创建: 失败 ' + $hresult)
    Add-Line ('    错误: ' + $_.Exception.Message)
}
}

Add-Section '7. 运行中的 OneNote 进程模块'
$process = Get-Process ONENOTE -ErrorAction SilentlyContinue
if (-not $process) {
    Add-Line 'OneNote 没有在运行（请启动后再跑一次，这一段才有意义）'
}
else {
    Add-Line ('进程 ID: ' + $process.Id + ', 启动时间: ' + $process.StartTime)
    $interesting = $process.Modules | Where-Object { $_.ModuleName -match 'mscoree|clr\.dll|mscorwks|OneMarkDown|Markdig|AngleSharp' }
    if ($interesting) {
        foreach ($module in $interesting) { Add-Line ('    ' + $module.ModuleName + '  ' + $module.FileName) }
    }
    else {
        Add-Line '    !! 没有加载 mscoree.dll / clr.dll，说明 OneNote 压根没去创建我们的 COM 对象'
    }
}

Add-Section '8. 日志尾部'
# 两个位置都要看：LOCALAPPDATA 建不出来时 Log 会回退到 %TEMP%，只看一处会被误导。
$logCandidates = @(
    (Join-Path $env:LOCALAPPDATA 'OneMarkDown\logs\addin.log'),
    (Join-Path $env:TEMP 'addin.log')
)
foreach ($logPath in $logCandidates) {
    if (Test-Path $logPath) {
        Add-Line ('  [' + $logPath + ']')
        Get-Content $logPath -Encoding UTF8 -Tail 25 | ForEach-Object { Add-Line ('    ' + $_) }
    }
    else {
        Add-Line ('  不存在: ' + $logPath)
    }
}

Add-Line ''
Add-Line '诊断结束。把上面全部内容发回即可。'

if ($OutputFile) {
    [System.IO.File]::WriteAllText($OutputFile, $report.ToString(), (New-Object System.Text.UTF8Encoding($true)))
    Write-Host ''
    Write-Host ('已保存到: ' + $OutputFile) -ForegroundColor Green
}
