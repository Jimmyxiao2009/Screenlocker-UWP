# 通过 Windows Device Portal 把 ARM 包部署到手机（无线，不需要 USB）。
#
#   powershell -ExecutionPolicy Bypass -File tools\deploy-wdp.ps1 -Ip 192.168.3.159
#   powershell -ExecutionPolicy Bypass -File tools\deploy-wdp.ps1 -Ip 192.168.3.159 -Build -Launch
#
# 前提：手机上「设置 → 更新和安全 → 面向开发人员」里打开设备门户（Device Portal）。
# 如果门户设了认证密码，加上 -User / -Password。
#
# 说明：W10M 的设备门户只认 curl 形态的 multipart 上传（字段名必须等于文件名），
# PowerShell 的 -Form 会被判成 "Missing .appx file in uploaded files"，所以这里调 curl.exe。
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Ip,
    [string]$Appx,
    [switch]$Build,
    [switch]$Launch,
    [string]$User,
    [string]$Password,
    [switch]$ShowLog
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'Screenlocker\Screenlocker.csproj'
$packagesRoot = Join-Path $root 'Screenlocker\AppPackages'
$identity = 'ProjectGoofish.Screenlocker'
$pfn = 'ProjectGoofish.Screenlocker_3x7rdjgage6t6'
$msbuild = 'C:\Program Files (x86)\Microsoft Visual Studio\2017\Community\MSBuild\15.0\Bin\MSBuild.exe'
$base = "http://$Ip"

# 不写死版本号：AppPackages 下的目录名带版本，升版本后路径会变
function Find-LatestAppx {
    Get-ChildItem -LiteralPath $packagesRoot -Filter '*.appx' -Recurse -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

if ($Build) {
    if (-not (Test-Path -LiteralPath $msbuild)) { throw "找不到 MSBuild：$msbuild" }
    Write-Host '编译 Release|ARM（含 .NET Native）...'
    & $msbuild $project /p:Configuration=Release /p:Platform=ARM `
        /p:GenerateAppxPackageOnBuild=true /p:AppxBundle=Never /p:AppxBundlePlatforms=ARM `
        /p:UapAppxPackageBuildMode=SideloadOnly /m /v:m /nologo
    if ($LASTEXITCODE -ne 0) { throw "编译失败（退出码 $LASTEXITCODE）" }
}

# 必须在编译之后再解析路径，否则拿到的是上一版的包
if (-not $Appx) {
    $Appx = Find-LatestAppx
    if (-not $Appx) { throw "在 $packagesRoot 下找不到 .appx，先加 -Build 参数" }
}

if (-not (Test-Path -LiteralPath $Appx)) { throw "找不到安装包：$Appx" }

# curl 对非 ASCII 路径不友好（工程在 H:\项目\...），先复制到纯 ASCII 的临时目录
$tmpDir = Join-Path $env:TEMP 'screenlocker-deploy'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$fileName = Split-Path $Appx -Leaf
$tmpAppx = Join-Path $tmpDir $fileName
Copy-Item -LiteralPath $Appx -Destination $tmpAppx -Force
Write-Host ("上传 {0}（{1:N0} 字节）" -f $fileName, (Get-Item -LiteralPath $tmpAppx).Length)

# 取 CSRF token：设备门户要求 POST 带上这个 cookie
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$null = Invoke-WebRequest "$base/" -WebSession $session -UseBasicParsing -TimeoutSec 30
$token = ($session.Cookies.GetCookies($base) | Where-Object { $_.Name -eq 'CSRF-Token' }).Value
if (-not $token) { throw '拿不到 CSRF token，检查设备门户是否已开启' }

$curlArgs = @('-sS', '-m', '900', '-b', "CSRF-Token=$token", '-H', "X-CSRF-Token: $token")
if ($User) { $curlArgs += @('-u', "$User`:$Password") }

Write-Host '部署中（上传 + 安装，可能几分钟）...'
$body = curl.exe @curlArgs -F "$fileName=@$tmpAppx" `
    "$base/api/app/packagemanager/package?package=$fileName" -w "`nHTTP=%{http_code}" 2>&1 | Out-String
Write-Host $body.Trim()

if ($body -notmatch 'HTTP=202') { throw '部署请求未被接受，看上面的响应' }

Write-Host '等待安装完成...'
$ok = $false
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Seconds 5
    try {
        $state = (Invoke-WebRequest "$base/api/app/packagemanager/state" -WebSession $session -UseBasicParsing -TimeoutSec 60).Content
        if ($state -notmatch '"installRunning"\s*:\s*true') { $ok = $true; break }
    }
    catch {
        if ($_.Exception.Response.StatusCode.value__ -eq 404) { $ok = $true; break }
    }
}

# 以包列表为准确认装上了，并核对版本号。
# 注意：刚上传完立刻查可能还没刷新出来，要重试几次。
$installed = $null
for ($i = 0; $i -lt 12; $i++) {
    $packages = (curl.exe -sS -m 90 "$base/api/app/packagemanager/packages" | Out-String | ConvertFrom-Json).InstalledPackages
    $installed = $packages | Where-Object { $_.PackageFamilyName -eq $identity }
    if ($installed) { break }
    Start-Sleep -Seconds 5
}

if (-not $installed) { throw '安装似乎没成功，包列表里没有找到' }

$installedVersion = "$($installed.Version.Major).$($installed.Version.Minor).$($installed.Version.Build).$($installed.Version.Revision)"
$appxVersion = ([xml](Get-Content -LiteralPath (Join-Path (Split-Path -Parent $Appx) '..\..\Package.appxmanifest') -Raw -ErrorAction SilentlyContinue)).Package.Identity.Version
Write-Host ("已安装：{0}  版本 {1}" -f $installed.Name, $installedVersion)
if ($appxVersion -and $appxVersion -ne $installedVersion) {
    Write-Warning "手机上的版本（$installedVersion）和清单里的版本（$appxVersion）不一致，可能没真正替换。记得升 Package.appxmanifest 里的 Version 再部署。"
}

if ($Launch) {
    $aumid = $installed.PackageRelativeId
    $appid = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($aumid))
    $null = Invoke-WebRequest "$base/api/taskmanager/app?appid=$appid" -Method Post `
        -Headers @{ 'X-CSRF-Token' = $token } -WebSession $session -UseBasicParsing -TimeoutSec 120
    Write-Host '已在手机上启动。'
    Start-Sleep -Seconds 20
}

if ($ShowLog -or $Launch) {
    Write-Host '--- 手机上的日志 ---'
    try {
        $bytes = (Invoke-WebRequest "$base/api/filesystem/apps/file?knownfolderid=LocalAppData&filename=screenlocker.log&packagefullname=$($installed.PackageFullName)&path=%5CLocalState" `
                -WebSession $session -UseBasicParsing -TimeoutSec 60).Content | ConvertFrom-Json
        $text = [Text.Encoding]::UTF8.GetString([byte[]]$bytes)
        Write-Host $text.Trim()
    }
    catch {
        Write-Host ("读日志失败（应用还没写过日志？）：{0}" -f $_.Exception.Message)
    }
}
