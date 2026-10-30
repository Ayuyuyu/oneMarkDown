<#
.SYNOPSIS
    安装 OneMarkDown 加载项。

.DESCRIPTION
    从分发包里运行：AddIn\ 与本脚本所在的 installer\ 目录平级（即 package\AddIn 与 package\installer）。
    本脚本只做安装：
      1. 结束占用旧 DLL 的 dllhost 代理进程
      2. 把 AddIn\ 里的文件复制到 %LOCALAPPDATA%\OneMarkDown\bin
      3. 写 COM 注册与 OneNote 加载项项

    默认写当前用户（HKCU）；加 -Machine 写机器级（HKLM），会弹 UAC。
    当前 OneNote 版本只从 HKLM 读取待加载列表，推荐用 -Machine。

.PARAMETER Machine
    写 HKLM，需要管理员权限。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\install.ps1 -Machine
#>
[CmdletBinding()]
param(
    [switch]$Machine
)

$ErrorActionPreference = 'Stop'

$packageRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

# 分发包布局是 installer\ 与 AddIn\ 平级（AddIn 在 installer 的上一级）；
# 也兼容把 AddIn\ 直接放在脚本旁边的自制包，两处都探测一次。
$candidates = @(
    (Join-Path (Split-Path -Parent $packageRoot) 'AddIn'),
    (Join-Path $packageRoot 'AddIn')
)
$Source = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $Source) {
    throw ("安装包不完整：找不到 AddIn 目录（已查找 {0}）。请把压缩包完整解压后再运行本脚本。" -f ($candidates -join '、'))
}

$clsid = '{7C4E1B2A-9F3D-4E58-9A61-2B8D5C0F7E31}'
$progId = 'OneMarkDown.Connect'
$assemblySimpleName = 'OneMarkDown.AddIn'
$addinClassName = 'OneMarkDown.Connect'
$targetDir = Join-Path $env:LOCALAPPDATA 'OneMarkDown\bin'

function Write-Step($text) { Write-Host "==> $text" -ForegroundColor Cyan }
function Write-Ok($text) { Write-Host "    $text" -ForegroundColor Green }

function Test-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

# 需要机器级注册但当前不是管理员：以同一脚本自提权重跑。
if ($Machine -and -not (Test-Admin)) {
    Write-Host '需要管理员权限写入 HKLM，正在请求提升...' -ForegroundColor Yellow
    # 子进程只需要 -Machine：包路径由它自己的位置推导
    $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$($MyInvocation.MyCommand.Path)`"", '-Machine')
    Start-Process powershell -Verb RunAs -ArgumentList $argList | Out-Null
    Write-Host '已在新窗口中以管理员身份继续，请在该窗口完成 UAC 确认。' -ForegroundColor Yellow
    return
}

Write-Step '检查 .NET Framework 4.8'
Write-Ok ("运行身份: {0}  管理员={1}" -f $env:USERNAME, (Test-Admin))
$ndp = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -ErrorAction SilentlyContinue
if (-not $ndp -or $ndp.Release -lt 528040) {
    throw '未检测到 .NET Framework 4.8（需要 Release >= 528040）。'
}
Write-Ok ("Release = {0}" -f $ndp.Release)

Write-Step '检查经典版 OneNote'
$appPathKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\ONENOTE.EXE'
$oneNotePath = (Get-ItemProperty $appPathKey -ErrorAction SilentlyContinue).'(default)'
if (-not $oneNotePath -or -not (Test-Path $oneNotePath)) {
    throw '没有找到经典版 OneNote（ONENOTE.EXE）。'
}
Write-Ok $oneNotePath

$isX86Office = $oneNotePath -like '*Program Files (x86)*'
Write-Ok ("Office 位数: {0}" -f $(if ($isX86Office) { '32 位' } else { '64 位' }))

$mainDll = Join-Path $Source "$assemblySimpleName.dll"
if (-not (Test-Path $mainDll)) {
    throw "安装包不完整：找不到 $mainDll。"
}

Write-Step '结束占用加载项的代理进程'
# 加载项跑在 dllhost.exe 代理里，升级时它还锁着旧 DLL，不先结束就会复制失败。
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

Write-Step "复制文件到 $targetDir"
New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
# 除了 dll，还要带上 OneMarkDown.exe 与它的 .config：
# 功能区按钮把实际动作委托给这个一次性进程（加载项跑在 dllhost 里，做不了剪贴板/对话框/按键注入）。
Get-ChildItem -Path $Source -File |
    Where-Object { $_.Extension -eq '.dll' -or $_.Extension -eq '.exe' -or $_.Name -like '*.exe.config' } |
    ForEach-Object {
        Copy-Item $_.FullName -Destination $targetDir -Force
        Write-Ok $_.Name
    }
# 不再复制 OneMarkDown.AddIn.dll.config：
# 它是 AutoGenerateBindingRedirects 的产物，而托管在 ONENOTE.EXE 里的 DLL 只认宿主自己的
# 配置文件（ONENOTE.EXE.config），这个文件对加载项完全无效，留在旁边只会增加变量。

Write-Step '解除文件锁定'
Get-ChildItem -Path $targetDir -File | ForEach-Object { Unblock-File $_.FullName }
Write-Ok 'Unblock-File 完成'

$installedDll = Join-Path $targetDir "$assemblySimpleName.dll"
$assemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($installedDll).FullName
Write-Ok $assemblyName

function Register-AddIn {
    param(
        [string]$SoftwareRoot,
        [string]$ClassesRoot,
        [string]$MscoreePath,
        [string]$ScopeLabel,
        # 加载项键只写一处（HKCU），避免同名 ProgId 在 HKCU/HKLM 两处并存、
        # LoadBehavior 一个 3 一个 2，让宿主拿到互相矛盾的判定。
        [switch]$SkipAddinKey
    )

    Write-Step "写入 COM 注册项（$ScopeLabel）"

    $progIdKey = Join-Path $ClassesRoot $progId
    New-Item -Path $progIdKey -Force | Out-Null
    Set-Item -Path $progIdKey -Value $progId
    New-Item -Path (Join-Path $progIdKey 'CLSID') -Force | Out-Null
    Set-Item -Path (Join-Path $progIdKey 'CLSID') -Value $clsid

    $clsidKey = Join-Path $ClassesRoot "CLSID\$clsid"
    New-Item -Path $clsidKey -Force | Out-Null
    Set-Item -Path $clsidKey -Value $progId
    New-Item -Path (Join-Path $clsidKey 'ProgId') -Force | Out-Null
    Set-Item -Path (Join-Path $clsidKey 'ProgId') -Value $progId

    # 与 OneMore 的做法对齐：把 ProgID 写成值，并登记 .NET 托管组件类别。
    New-ItemProperty -Path $clsidKey -Name 'ProgID' -Value $progId -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $clsidKey -Name 'VersionIndependentProgID' -Value $progId -PropertyType String -Force | Out-Null
    # 实测：Get-Item 返回的是只读句柄，SetValue 会报"无法写入到注册表项"；
    # New-ItemProperty -Value '' 才能正确写入空 REG_SZ。
    New-ItemProperty -Path $clsidKey -Name 'Programmable' -Value '' -PropertyType String -Force | Out-Null
    # {62C8FE65-4EBB-45E7-B440-6E39B2CDBF29} = .NET Managed Component 类别
    New-Item -Path (Join-Path $clsidKey 'Implemented Categories\{62C8FE65-4EBB-45E7-B440-6E39B2CDBF29}') -Force | Out-Null

    # 【关键】当前 OneNote 版本已不能在自身进程内宿主托管加载项（ONENOTE.EXE 里
    # 从不出现 mscoree/clr，加载项被降级为 2）。OneMore 之所以能用，是因为它用
    # AppID + DllSurrogate 让 CLR 跑在 dllhost.exe 里，OneNote 通过跨进程 COM 访问。
    # 这里复刻同样的做法。
    New-ItemProperty -Path $clsidKey -Name 'AppID' -Value $clsid -PropertyType String -Force | Out-Null
    $appIdKey = Join-Path $ClassesRoot "AppID\$clsid"
    New-Item -Path $appIdKey -Force | Out-Null
    # 空字符串 = 使用系统默认代理宿主 dllhost.exe
    New-ItemProperty -Path $appIdKey -Name 'DllSurrogate' -Value '' -PropertyType String -Force | Out-Null

    $inprocKey = Join-Path $clsidKey 'InprocServer32'
    New-Item -Path $inprocKey -Force | Out-Null
    # 裸 mscoree.dll：与 OneMore 的真实注册一致（COM 从 System32 解析）。
    # $MscoreePath 参数保留但不再使用，留着以免破坏调用方签名。
    Set-Item -Path $inprocKey -Value 'mscoree.dll'
    New-ItemProperty -Path $inprocKey -Name 'ThreadingModel' -Value 'Both' -PropertyType String -Force | Out-Null
    # 注意：默认值必须是裸 "mscoree.dll"，与 OneMore 完全一致（之前写的是完整路径）。
    New-ItemProperty -Path $inprocKey -Name 'Class' -Value $addinClassName -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $inprocKey -Name 'Assembly' -Value $assemblyName -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $inprocKey -Name 'RuntimeVersion' -Value 'v4.0.30319' -PropertyType String -Force | Out-Null
    # CodeBase 用纯路径而不是 file:/// URI，与 OneMore 的注册写法保持一致。
    New-ItemProperty -Path $inprocKey -Name 'CodeBase' -Value $installedDll -PropertyType String -Force | Out-Null
    Write-Ok "CLSID $clsid"

    if (-not $SkipAddinKey) {
        Write-Step "写入 OneNote 加载项项（$ScopeLabel）"
        $addinKey = Join-Path $SoftwareRoot "Microsoft\Office\OneNote\Addins\$progId"
        New-Item -Path $addinKey -Force | Out-Null
        New-ItemProperty -Path $addinKey -Name 'FriendlyName' -Value 'OneMarkDown' -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $addinKey -Name 'Description' -Value 'Markdown 粘贴与渲染' -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $addinKey -Name 'LoadBehavior' -Value 3 -PropertyType DWord -Force | Out-Null
        Write-Ok 'LoadBehavior = 3'
    }

    Write-Step "清理宿主残留禁用记录（$ScopeLabel）"
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

    $doNotDisable = Join-Path $SoftwareRoot 'Microsoft\Office\16.0\OneNote\Resiliency\DoNotDisableAddinList'
    New-Item -Path $doNotDisable -Force | Out-Null
    New-ItemProperty -Path $doNotDisable -Name $progId -Value 1 -PropertyType DWord -Force | Out-Null
    Write-Ok 'DoNotDisableAddinList 已登记'
}

$mscoree64 = Join-Path $env:WINDIR 'System32\mscoree.dll'
$mscoree32 = Join-Path $env:WINDIR 'SysWOW64\mscoree.dll'

# 二选一，不要同时写：
#   -Machine → 只写 HKLM（避免提权后换账号把 HKCU 写进别人的 hive）
#   否则     → 只写 HKCU（免管理员）
Register-AddIn -SoftwareRoot $(if ($Machine) { 'HKLM:\SOFTWARE' } else { 'HKCU:\Software' }) `
    -ClassesRoot $(if ($Machine) { 'HKLM:\SOFTWARE\Classes' } else { 'HKCU:\Software\Classes' }) `
    -MscoreePath $(if ($isX86Office) { $mscoree32 } else { $mscoree64 }) `
    -ScopeLabel $(if ($Machine) { '机器级' } else { '当前用户' })

Write-Step '验证注册结果'
$checks = @()
foreach ($root in @('HKLM:\SOFTWARE', 'HKCU:\Software')) {
    $k = Join-Path $root "Microsoft\Office\OneNote\Addins\$progId"
    $checks += [pscustomobject]@{ 项目 = "Addins 键 $root"; 结果 = $(if (Test-Path $k) { '存在, LoadBehavior=' + (Get-ItemProperty $k).LoadBehavior } else { '缺失' }) }
}

foreach ($root in @('HKLM:\SOFTWARE\Classes', 'HKCU:\Software\Classes')) {
    $inproc = Join-Path $root "CLSID\$clsid\InprocServer32"
    $checks += [pscustomobject]@{ 项目 = "InprocServer32 $root"; 结果 = $(if (Test-Path $inproc) { '= [' + (Get-Item $inproc).GetValue('') + ']' } else { '缺失' }) }
    $appIdValue = $(if (Test-Path (Join-Path $root "CLSID\$clsid")) { (Get-Item (Join-Path $root "CLSID\$clsid")).GetValue('AppID') } else { $null })
    $surrogate = $(if (Test-Path (Join-Path $root "AppID\$clsid")) { (Get-Item (Join-Path $root "AppID\$clsid")).GetValue('DllSurrogate') } else { $null })
    $checks += [pscustomobject]@{ 项目 = "AppID/Surrogate $root"; 结果 = $(if ($appIdValue) { "AppID=$appIdValue, DllSurrogate=[" + $surrogate + "]" } else { '缺失' }) }
}

$checks | Format-Table -AutoSize | Out-String -Width 200 | Write-Host

$anyAddin = (Test-Path "HKLM:\SOFTWARE\Microsoft\Office\OneNote\Addins\$progId") -or (Test-Path "HKCU:\Software\Microsoft\Office\OneNote\Addins\$progId")
if (-not $anyAddin) {
    Write-Host '!! 加载项键没有写成功，OneNote 不会看到这个加载项。请把上面的完整输出发回。' -ForegroundColor Red
}
else {
    Write-Host '注册完成。请完全退出 OneNote（任务管理器确认无 ONENOTE.EXE）后重新打开。' -ForegroundColor Green
}

Write-Host ''
Write-Host '安装完成。' -ForegroundColor Green
Write-Host '请完全退出 OneNote（包括任务管理器里残留的 ONENOTE.EXE），再重新打开。' -ForegroundColor Yellow
Write-Host "日志目录: $(Join-Path $env:LOCALAPPDATA 'OneMarkDown\logs')" -ForegroundColor Gray
