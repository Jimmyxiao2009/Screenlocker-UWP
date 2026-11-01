# 在电脑上跑开发构建（x86 Debug）：可选编译 → 注册松散布局 → 启动。
# 需要先打开"开发者模式"（设置 → 系统 → 开发者选项）。
#
#   powershell -ExecutionPolicy Bypass -File tools\run-dev.ps1            # 只注册 + 启动
#   powershell -ExecutionPolicy Bypass -File tools\run-dev.ps1 -Build     # 先编译再跑
#   powershell -ExecutionPolicy Bypass -File tools\run-dev.ps1 -Loopback  # 顺带加回环豁免（本机走代理时需要）
[CmdletBinding()]
param(
    [switch]$Build,
    [switch]$Loopback,
    [switch]$Kill
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'Screenlocker\Screenlocker.csproj'
$layout = Join-Path $root 'Screenlocker\bin\x86\Debug\AppxManifest.xml'
$identity = 'ProjectGoofish.Screenlocker'
$msbuild = 'C:\Program Files (x86)\Microsoft Visual Studio\2017\Community\MSBuild\15.0\Bin\MSBuild.exe'

if ($Kill) {
    Stop-Process -Name Screenlocker -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    Write-Host '已结束正在运行的实例。'
}

if ($Build) {
    if (-not (Test-Path -LiteralPath $msbuild)) { throw "找不到 MSBuild：$msbuild" }
    Write-Host '编译 Debug|x86 ...'
    & $msbuild $project /p:Configuration=Debug /p:Platform=x86 /m /v:m /nologo
    if ($LASTEXITCODE -ne 0) { throw "编译失败（退出码 $LASTEXITCODE）" }
}

if (-not (Test-Path -LiteralPath $layout)) {
    throw "找不到构建产物，请先加 -Build 参数：$layout"
}

$pkg = Get-AppxPackage -Name $identity
if (-not $pkg) {
    Add-AppxPackage -Register $layout
    $pkg = Get-AppxPackage -Name $identity
    Write-Host ("已注册：{0} {1}" -f $pkg.Name, $pkg.Version)
}
else {
    Write-Host ("已注册：{0} {1}" -f $pkg.Name, $pkg.Version)
}

$pfn = $pkg.PackageFamilyName

if ($Loopback) {
    # 本机走本地代理（127.0.0.1）时，AppContainer 默认不能访问回环地址，天气会请求失败
    CheckNetIsolation.exe LoopbackExempt -a "-n=$pfn" | Out-Null
    Write-Host '已加入回环豁免。'
}

Start-Process ('shell:appsFolder\{0}!App' -f $pfn)
Start-Sleep -Seconds 8

$proc = Get-Process -Name Screenlocker -ErrorAction SilentlyContinue
if ($proc) {
    Write-Host ("已启动：PID={0}，内存 {1} MB" -f $proc.Id, [math]::Round($proc.WorkingSet64 / 1MB))
}
else {
    Write-Host '启动后进程不存在，看日志：'
}

$log = Join-Path $env:LOCALAPPDATA "Packages\$pfn\LocalState\screenlocker.log"
if (Test-Path -LiteralPath $log) {
    Write-Host ("--- 日志：{0} ---" -f $log)
    Get-Content -LiteralPath $log -Encoding utf8 -Tail 15
}
else {
    Write-Host ("（还没有日志文件：{0}）" -f $log)
}
