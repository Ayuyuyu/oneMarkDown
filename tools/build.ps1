<#
.SYNOPSIS
    编译 OneMarkDown，（默认）并打包成分发目录 dist\。

.DESCRIPTION
    编译职责从安装脚本里拆出来：
      - 本脚本：编译、跑测试、打包
      - installer\install.ps1：只负责把文件复制到位 + 写注册表，不做任何编译

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\tools\build.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\tools\build.ps1 -NoPack -Test
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',

    # 只编译，不打包 dist
    [switch]$NoPack,

    # 编译后跑单元测试
    [switch]$Test,

    # 先删掉 bin/obj 再来
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'

$toolsRoot = $PSScriptRoot
$repoRoot = Split-Path -Parent $toolsRoot
$solution = Join-Path $repoRoot 'OneMarkDown.sln'
$distRoot = Join-Path $repoRoot 'dist'

function Write-Step($text) { Write-Host "==> $text" -ForegroundColor Cyan }
function Write-Ok($text) { Write-Host "    $text" -ForegroundColor Green }

if (-not (Test-Path $solution)) {
    throw "找不到解决方案：$solution"
}

if ($Clean) {
    Write-Step '清理 bin/obj'
    foreach ($relative in @(
            'src\OneMarkDown.AddIn\bin', 'src\OneMarkDown.AddIn\obj',
            'src\OneMarkDown.Shell\bin', 'src\OneMarkDown.Shell\obj',
            'tests\OneMarkDown.Tests\bin', 'tests\OneMarkDown.Tests\obj')) {
        $path = Join-Path $repoRoot $relative
        if (Test-Path $path) {
            Remove-Item $path -Recurse -Force
        }
    }
    Write-Ok '已清理'
}

# 热键工具常驻时会锁住 bin 目录下的 DLL，编译会失败，先请它让路。
$running = Get-Process OneMarkDown -ErrorAction SilentlyContinue
if ($running) {
    Write-Step '结束正在运行的热键工具'
    foreach ($process in $running) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        Write-Ok ("已结束 PID {0}" -f $process.Id)
    }
    Start-Sleep -Milliseconds 600
}

Write-Step "编译（$Configuration）"
$useDotnet = [bool](Get-Command dotnet -ErrorAction SilentlyContinue)

if ($useDotnet) {
    & dotnet build $solution -c $Configuration --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build 失败。' }
}
else {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $msbuild = $null
    if (Test-Path $vswhere) {
        $msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
    }

    if (-not $msbuild) { throw '既没有 dotnet，也没有找到 MSBuild。' }

    & $msbuild $solution /t:Build "/p:Configuration=$Configuration" /v:minimal
    if ($LASTEXITCODE -ne 0) { throw 'MSBuild 编译失败。' }
}
Write-Ok '编译完成'

if ($Test) {
    Write-Step '运行单元测试'
    if ($useDotnet) {
        & dotnet test $solution -c $Configuration --no-build --nologo
        if ($LASTEXITCODE -ne 0) { throw '单元测试失败。' }
    }
    else {
        Write-Ok '没有 dotnet，跳过（测试需要 dotnet test）'
    }
    Write-Ok '测试通过'
}

$addinOutput = Join-Path $repoRoot "src\OneMarkDown.AddIn\bin\$Configuration"
$shellOutput = Join-Path $repoRoot "src\OneMarkDown.Shell\bin\$Configuration"

if ($NoPack) {
    Write-Host ''
    Write-Host ('编译产物：' + $addinOutput) -ForegroundColor Green
    return
}

Write-Step '打包到 dist'
foreach ($required in @($addinOutput, $shellOutput)) {
    if (-not (Test-Path $required)) {
        throw "缺少编译输出：$required"
    }
}

if (Test-Path $distRoot) {
    Remove-Item $distRoot -Recurse -Force
}

$distAddin = Join-Path $distRoot 'AddIn'
$distInstaller = Join-Path $distRoot 'installer'
New-Item -ItemType Directory -Path $distAddin, $distInstaller -Force | Out-Null

# 加载项的依赖（Markdig / AngleSharp / System.*）
Get-ChildItem $addinOutput -Filter '*.dll' -File | ForEach-Object {
    Copy-Item $_.FullName -Destination $distAddin -Force
}

# 热键工具（独立形态，随包一起发布；加载项本身不依赖它）
foreach ($name in @('OneMarkDown.exe', 'OneMarkDown.exe.config')) {
    $source = Join-Path $shellOutput $name
    if (Test-Path $source) {
        Copy-Item $source -Destination $distAddin -Force
    }
}

# 安装脚本（samples/fidelity.md 留在仓库里做验收，不进分发包）
Copy-Item (Join-Path $repoRoot 'installer\*.ps1') -Destination $distInstaller -Force

$files = Get-ChildItem $distRoot -Recurse -File
Write-Ok ("{0} 个文件，{1:N1} MB" -f $files.Count, (($files | Measure-Object Length -Sum).Sum / 1MB))

# 版本号取 Directory.Build.props 里的 <Version>
$version = '0.0.0'
$propsPath = Join-Path $repoRoot 'Directory.Build.props'
if (Test-Path $propsPath) {
    [xml]$props = Get-Content $propsPath -Raw -Encoding UTF8
    $candidate = @($props.Project.PropertyGroup.Version) | Where-Object { $_ } | Select-Object -First 1
    if ($candidate) { $version = ([string]$candidate).Trim() }
}

$packageName = 'OneMarkDown-v{0}-{1}' -f $version, (Get-Date -Format 'yyyyMMdd-HHmmss')
$zipPath = Join-Path $distRoot ($packageName + '.zip')

Write-Step '打包 zip'
# 只压 AddIn 和 installer，内容直接位于压缩包根目录：
# 解压时资源管理器自己会建一个与压缩包同名的文件夹，再套一层就会变成双嵌套。
Compress-Archive -Path $distAddin, $distInstaller -DestinationPath $zipPath -CompressionLevel Optimal -Force
Write-Ok ("{0}  {1:N1} MB" -f (Split-Path $zipPath -Leaf), ((Get-Item $zipPath).Length / 1MB))

Write-Host ''
Write-Host ('分发目录：' + $distRoot) -ForegroundColor Green
Write-Host ('压缩包：  ' + $zipPath) -ForegroundColor Green
Write-Host '安装：cd dist\installer; powershell -ExecutionPolicy Bypass -File .\install.ps1 -Machine' -ForegroundColor Gray
