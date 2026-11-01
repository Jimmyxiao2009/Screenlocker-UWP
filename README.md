# 锁屏文字（Screenlocker）

Windows 10 Mobile 上的小工具：**把自己写的文字显示到锁屏的"详细状态"和动态磁贴上**。
设置页里写模板（支持换行，最多三行），插入占位符，磁贴和锁屏就会跟着刷新。

- 工程：`Screenlocker/Screenlocker.csproj`，解决方案 `Screenlocker.sln`
- 目标：UWP，`TargetPlatformVersion 10.0.15063.0`，最低 `10.0.10586.0`，ARM / x86
- 依赖：只用了 `Microsoft.NETCore.UniversalWindowsPlatform 5.4.7`（6.x 起要求最低 16299，W10M 只能停在 5.x）

## 占位符

| 写这个 | 出来的是 | 例子 |
|--------|----------|------|
| `{time}` | 24 小时制时间 | `20:31` |
| `{time12}` | 12 小时制时间 | `8:31` |
| `{date}` | 日期 | `2026/10/5` |
| `{md}` | 月/日 | `10/5` |
| `{weekday}` | 星期 | `周一` |
| `{battery}` | 电量百分比 | `85%` |
| `{batteryNum}` | 电量数字（不带 %） | `85` |
| `{charging}` | 充电时显示闪电，否则空 | `⚡` |
| `{city}` | 城市名 | `杭州` |
| `{temp}` | 温度（℃ 数值） | `21.4` |
| `{weather}` | 天气描述 | `多云` |
| `{humidity}` | 相对湿度 | `68%` |
| `{lat}` / `{lon}` | 经纬度 | `30.274` / `120.155` |

默认模板：

```
{time}  {weekday}
电量 {battery}{charging}
{city} {temp}°C {weather}
```

写错的名字（比如 `{batery}`）不会被替换，会原样显示在磁贴上，方便自己发现。
模板超过三行时，多出来的行会并进第三行（锁屏详细状态只有三个位置）。

## 怎么让它出现在锁屏上

1. 打开应用 → 写模板 → 保存并刷新。磁贴上立刻就有字。
2. 锁屏详细状态要手动选一次：**设置 → 个性化 → 锁屏界面 → "选择显示详细状态的应用" → 锁屏文字**。
   （锁屏"快速状态"那一栏显示的是徽章，本应用的徽章默认是电量百分比。）
3. 后台定时任务按间隔刷新，任务列表里划掉应用也不会停。

技术细节：详细状态的文字来自**宽版磁贴通知**里的 `hint-lockDetailedStatus1..3`，
同时宽版 binding 里也带 `text id=1..3`，两种取值方式都能拿到。
用的是主磁贴，所以就算没把磁贴固定到开始屏幕，锁屏详细状态照样更新。

## 数据来源

- 电量 / 充电 / 时间 / 日期：本机读取。电量走三层兜底：`PowerManager` → `Battery.AggregateBattery` 报告 → `Windows.Phone.Devices.Power.Battery`。
- 定位：`Geolocator`。首次使用会弹定位授权；被拒或超时就用 12 小时内的旧坐标。
- 天气：`open-meteo`（免费、不用 API Key），当前温度 / 天气代码 / 湿度。
- 城市名：`BigDataCloud` 反查定位。失败就显示经纬度；设置里也能手填固定城市名。
- 缓存：天气 10 分钟、定位 12 小时、城市名 6 小时，避免磁贴刷新时猛发请求。断网时用上次的值。

## 构建

```powershell
# 桌面调试（x86 Debug，跑得快，.NET Native 关）
& 'C:\Program Files (x86)\Microsoft Visual Studio\2017\Community\MSBuild\15.0\Bin\MSBuild.exe' `
  Screenlocker.sln /restore /p:Configuration=Debug /p:Platform=x86

# 出手机侧载包（ARM Release，含 .NET Native）
& 'C:\Program Files (x86)\Microsoft Visual Studio\2017\Community\MSBuild\15.0\Bin\MSBuild.exe' `
  Screenlocker\Screenlocker.csproj /p:Configuration=Release /p:Platform=ARM `
  /p:GenerateAppxPackageOnBuild=true /p:AppxBundle=Never /p:AppxBundlePlatforms=ARM `
  /p:UapAppxPackageBuildMode=SideloadOnly
```

产物在 `Screenlocker\AppPackages\Screenlocker_1.0.0.0_ARM_Test\`：

| 文件 | 用途 |
|------|------|
| `Screenlocker_1.0.0.0_ARM.appx` | 侧载包 |
| `Screenlocker_1.0.0.0_ARM.cer` | 配套证书（本机已信任的话不用再装） |
| `Add-AppDevPackage.ps1` | 手机上直接跑这个装 |
| `Dependencies\arm\` | .NET Native 运行时依赖，手机没装过就一起装 |

也可以直接用 VS2017 打开 sln，选 ARM + 手机（USB，开发者模式）点部署。

### 在电脑上看开发构建

桌面（x86 Debug）可以直接跑，不用打包。先编译，再把松散布局注册成本机开发包：

```powershell
& 'C:\Program Files (x86)\Microsoft Visual Studio\2017\Community\MSBuild\15.0\Bin\MSBuild.exe' `
  Screenlocker\Screenlocker.csproj /p:Configuration=Debug /p:Platform=x86

Add-AppxPackage -Register Screenlocker\bin\x86\Debug\AppxManifest.xml
Start-Process 'shell:appsFolder\ProjectGoofish.Screenlocker_3x7rdjgage6t6!App'
```

需要先打开**开发者模式**（设置 → 系统 → 开发者选项）。注册过一次之后，重新编译完直接启动就是新版本。

桌面上能验的：设置页布局、模板渲染、电量/时间、定位与天气、磁贴更新。
桌面上验不了的：锁屏详细状态（桌面没有这个入口，要看"开始"屏幕上的磁贴），以及手机上的实际排版。

**证书**：复用了旁边 Goofish 工程的测试证书（主题 `CN=ProjectGoofish`，2029 到期），
因为手机侧已经信任它，侧载不用再安装证书。换证书要同时改
`Package.appxmanifest` 的 `Publisher` 和 `Screenlocker.csproj` 的 `PackageCertificateThumbprint`。

## 图标资源

`Assets/` 里的图由脚本生成，改配色或造型请改脚本再重跑：

```powershell
powershell -ExecutionPolicy Bypass -File tools\make-assets.ps1
```

## 已知限制

1. **不能真的"锁屏"**。UWP 没有公开的锁屏 API（`Windows.ApplicationModel.LockScreen` 只能往锁屏上显示状态，不能接管或触发锁屏），
   所以点磁贴只能是打开应用。市面上那些"一键锁屏"应用是应用内自己铺一层锁屏页，不是系统锁屏。
   如果想要那种效果，可以再加一个全屏锁屏页（拦截返回键、需要 PIN 才能出去）。
2. **后台定时刷新最短 15 分钟**，这是系统对定时触发器的硬限制。
3. **温度是天气 API 给的，不是手机传感器**。手机上没有公开的温度传感器 API。
4. 详细状态的位置和能显示几个字由系统锁屏决定，太长会被截断。
5. `uap:LockScreen` 需要系统 10586+，本项目最低就定在 10586。

## 部署到手机（无线，走设备门户）

手机上先打开：**设置 → 更新和安全 → 面向开发人员 → 设备门户**，记下显示的 IP。

```powershell
# 只部署已有的 ARM 包
powershell -ExecutionPolicy Bypass -File tools\deploy-wdp.ps1 -Ip 192.168.3.159

# 重新编译 + 部署 + 启动 + 读回手机日志
powershell -ExecutionPolicy Bypass -File tools\deploy-wdp.ps1 -Ip 192.168.3.159 -Build -Launch
```

脚本走 Device Portal 的 REST 接口，不需要 USB、不需要 WinAppDeployCmd 的配对 PIN。
两个坑已经处理掉了：

- 上传必须是 **multipart 且字段名等于文件名**，PowerShell 的 `-Form` 会被判成 `Missing .appx file`，所以脚本里调 `curl.exe`。
- `curl` 对非 ASCII 路径不友好（工程在 `H:\项目\...`），脚本会先把包复制到临时目录再传。

**每次部署前记得把 `Package.appxmanifest` 里的 `Identity Version` 加一**（例如 `1.0.2.0` → `1.0.3.0`），
否则同版本覆盖安装不会真正替换二进制，手机上跑的还是旧代码。脚本会在版本对不上时给出警告。

排查线上问题时，日志里的两行很有用：

```
磁贴通知状态：Enabled                    ← 不是 Enabled 说明系统关掉了本应用的通知
磁贴 XML：<tile>...</tile>               ← 实际发出去的磁贴内容
```

手机上打开设备门户后，浏览 `http://<手机IP>/` 也能手动装包，界面里的 "Apps → Install app" 同样可用。

### 手机侧还需要手动做两件事

1. **首次启动要允许定位**。弹窗点"允许"；没弹或误点了，去 **设置 → 隐私 → 位置** 里给"锁屏文字"打开。
   不放开的话第三行只剩 `°C`（日志会写 `定位不可用：权限状态 Denied`）。
2. **选锁屏详细状态**：**设置 → 个性化 → 锁屏界面 → 选择显示详细状态的应用 → 锁屏文字**。

### 手机必须能直接上网

温度/城市来自网络接口。如果手机连的 WiFi 本身没有外网（例如这个网段要靠电脑上的代理才能出去），
天气请求会在 10 秒后超时，日志写 `天气请求失败：TaskCanceledException`。这种情况下：

- 换一个能直接上网的 WiFi，或关掉 WiFi 用移动数据；或
- 如果电脑上跑着 Clash 之类，打开它的"允许局域网连接"，然后在手机的
  **设置 → 网络和无线 → Wi-Fi → 管理 → 点该网络 → 代理** 里填电脑的 IP 和端口（如 `192.168.3.108:7897`）。

电量、时间、日期不需要网络，任何情况下都能显示。

## 排查

日志在 `%LOCALAPPDATA%\Packages\ProjectGoofish.Screenlocker_3x7rdjgage6t6\LocalState\screenlocker.log`，
每次刷新、定位结果、异常都会写进去。手机上没法挂调试器，出问题先看这个文件。
设置页底部的状态栏也会显示最近一次的错误（比如"定位不可用""天气请求失败"）。

常见问题：

| 现象 | 原因与处理 |
|------|------------|
| 天气一直失败：`An error occurred while sending the request.` | 电脑上走本地代理（`127.0.0.1:xxxx`）时，UWP 的 AppContainer 默认不能访问回环地址。给开发包加回环豁免：`CheckNetIsolation.exe LoopbackExempt -a "-n=ProjectGoofish.Screenlocker_3x7rdjgage6t6"`。手机直连网络不受影响。 |
| 温度、城市是空的 | 关掉"联网获取天气"或定位被拒时会这样。第三行会只剩 `°C`。 |
| 锁屏上看不到字 | 详细状态要手动选一次：设置 → 个性化 → 锁屏界面 → 选择显示详细状态的应用。 |
| 磁贴不动 | 系统定时器最短 15 分钟；也可以在设置页点"立即刷新"。 |
| 应用启动即崩溃 | 先看日志。桌面跑开发包时，设备专有的 WinRT 类型（如 `Windows.Phone.Devices.Power.Battery`）必须隔离在单独方法里，否则 JIT 会在桌面加载类型失败。 |
| 磁贴不显示文字，退回默认图标 | 磁贴 XML 里不能出现 `&apos;`。Windows 的磁贴渲染器只认 `&amp;` `&lt;` `&gt;` `&quot;` 四个实体，遇到别的（如 `&apos;`）会**静默丢弃整条通知**，`Update()` 却不报错。单引号在 XML 文本和双引号属性里本来就不用转义，直接原样输出即可。日志里的「磁贴 XML」行可以确认实际发出去的内容。 |
| 重新部署后行为没变 | 版本号没升。`Package.appxmanifest` 的 `Identity Version` 必须递增，**同版本覆盖安装不会替换二进制**。`tools\deploy-wdp.ps1` 会核对手机上的版本和清单里的版本并在不一致时警告。 |
| 日志里 `天气请求失败：TaskCanceledException` | 手机没有可用的外网（见上一节），不是代码问题。 |
| 日志里 `定位不可用：权限状态 Denied` | 手机上没允许定位，见上一节。 |

## 文件结构

```
Screenlocker/
  App.xaml(.cs)                  应用入口：启动时刷一次磁贴 + 确保后台任务在册
  Views/MainPage.xaml(.cs)       设置页：模板、占位符、预览、开关
  Services/
    AppSettings.cs               本地设置与缓存（前台 / 后台任务共用）
    TemplateEngine.cs            占位符替换、按行切分、超行合并
    InfoSnapshot.cs              一次采集得到的所有值
    DeviceInfoService.cs         电量 / 充电 / 星期
    WeatherService.cs            定位 + 天气 + 城市名（含缓存与降级）
    TileService.cs               磁贴 XML、锁屏详细状态、徽章
    BackgroundTaskService.cs     定时后台任务的注册 / 注销
  Tasks/TileRefreshTask.cs       后台任务入口（清单里按 EntryPoint 反射激活）
  Package.appxmanifest           锁屏元素、宽磁贴、后台任务、internetClient + location
tools/make-assets.ps1            生成所有图标 / 磁贴图 / 启动图
```

改后台任务的类名或命名空间时，记得同步改 `Package.appxmanifest` 里的 `EntryPoint`。
