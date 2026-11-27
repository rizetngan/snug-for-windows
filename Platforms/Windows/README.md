# Snug for Windows

[简体中文](README.md) · [English](README.en.md) · [项目主页](../../README.md)

Snug 为 UU远程 提供手机原生级的显示与触控体验。本贡献在 [原项目](https://github.com/zephyr4123/snug) 2.5.0 基础上新增 Windows 平台支持，延续原版 App 资源库、手机屏、窗口还原、触控与截图交互，保留 macOS 实现及原有版权。Windows 0.2.51 为独立维护的正式发布版；上游已明确暂不合并 Windows 移植，Windows 平台后续按独立维护路线推进。

0.2.48 新增 UU 手机端底部安全区、Chrome/Edge 左侧网页缩放浮条、最长10分钟的手机屏后台保留，以及标准／扩展／全面屏比例校准。新底部安全区会同时调整 Snug 导航和被管理的应用窗口，避免与 UU 原生操作栏重叠。手机主动断开后如需立即释放，可在 Snug 关闭「手机屏」。详见 [0.2.48 图文产品说明](../../docs/0.2.48/README.md)。

**0.2.51 更新：** 支持 iPhone 13 Pro 刘海顶部安全区、切换应用时重新校正窗口铺放和底部工具栏层级；左侧缩放轨道兼容更多已支持应用。


## 0.3.0 候选版 · CrossDesk 与无扩展缩放

- 同一手机工作区支持 UU Remote 与 CrossDesk；CrossDesk 模式由 Windows 运行主机、Android 运行控制端
- CrossDesk 绝对触控事件通过独立输入标记进入共享触控内核，处理点击、滑动、长按和滚动
- Chrome / Edge 使用 Windows 原生标签识别与相对缩放，不需要浏览器扩展或 Native Messaging
- 可选 UU 单手机控制端机型自动匹配（默认关闭）及五种通用比例预设

[0.3.0 图文说明与架构图](../../docs/0.3.0/README.md)。当前为候选版；426 项核心测试通过，真实 CrossDesk 手机连接及 UU 回归仍在验收。
## 使用

1. 安装 Windows x64 版本的 UU 远程，以及 Snug 安装程序。首次安装需要一次正常的管理员授权，用于部署官方签名虚拟显示驱动，并注册 SYSTEM 权限的 UU 捕获监视启动任务。
2. 手机通过 UU 连接电脑，在屏幕列表切换到 Snug Phone，使用屏幕触控模式。
3. 在资源库查找、打开 App 或选择已有窗口；底部返回条回到资源库。窗口铺屏、滚动、长按及字号操作由 Snug 处理。
4. 可在设置打开截图（默认关闭）。进入 App 后点底部相机，复制当前窗口图片；点预览可以裁剪后再次复制。
5. SYSTEM 后台监视器只观察 UU 显示捕获生命周期，通过本机单向管道向 Snug 发送固定状态数据。它能判断捕获活动，不能确认控制端设备类型或物理显示器身份。监视器启动过晚或 ETW 事件丢失时，Snug 会回退到未知/兼容模式，不会臆测为已断开。

## PowerShell / Agent 安装

在 **Windows PowerShell** 中直接安装最新稳定版：

```powershell
$ErrorActionPreference = 'Stop'
$script = Join-Path $env:TEMP 'install-snug.ps1'
Invoke-WebRequest -UseBasicParsing 'https://raw.githubusercontent.com/rizetngan/snug-for-windows/main/scripts/install-windows.ps1' -OutFile $script
& $script
```

脚本会验证 Release 的 SHA-256，在安装虚拟显示驱动前要求确认 UU 已断开，并触发正常的管理员授权提示。**Agent 安装提示词：** [查看并复制](../../README.md#使用-agent-安装)。

脚本源码：[scripts/install-windows.ps1](../../scripts/install-windows.ps1)。只下载并校验：使用 `& $script -DownloadOnly`。

## 构建与打包

依赖：Windows x64（Windows 10 22H2 已实测），.NET SDK 10.0.404（global.json 允许同 feature band 的较新补丁），NuGet 还原所需网络；打包还需 Windows Desktop Runtime 10.0.15 与 Inno Setup 7。首次安装需要管理员权限，编译和纯逻辑测试不需要。

从仓库根目录进入 Platforms/Windows：

    cd Platforms\Windows
    build-snug.cmd
    run-snug.cmd

脚本按顺序选择 SNUG_DOTNET_ROOT、项目 .tools/dotnet、DOTNET_ROOT、标准 Program Files/dotnet。非标准 SDK 路径可通过 **当前终端** 环境变量指定。编译器桥使用相同 SDK resolver 和 global.json；不会修改系统 CET 策略或 SDK 二进制。

PowerShell 示例：

    $env:SNUG_DOTNET_ROOT = 'C:\Program Files\dotnet'
    $env:SNUG_ISCC = 'C:\Program Files (x86)\Inno Setup 7\ISCC.exe'
    .\build-snug.cmd
    .\package-snug.cmd

输出：artifacts/installer/Snug-Setup-0.2.51-x64.exe。安装包内置指定 .NET runtime，无需目标电脑另外安装 SDK。驱动分发许可、版本及校验见 installer/THIRD_PARTY_NOTICES.md；安装器本身尚未签名。支持 CET 的系统可用 -p:SnugUseLegacyWindowsCETWorkaround=false 使用 SDK 原生编译路径。

## 架构

- Snug.Windows.Core：独立的窗口/恢复账本、手机尺寸与适配、触控翻译、菜单惯性、UU 会话状态及截图裁剪几何，不依赖 WPF。
- Snug.Windows：WPF 设置及手机界面，Win32 窗口/两轴输入、虚拟显示器管理、UU 只读观察、原像素窗口截图。
- installer：固定版本签名 VDD 25.7.23、NefCon 1.21.1，以及 SYSTEM 启动的 UU 捕获监视任务。监视器通过限定用户 SID 的本地管道通信；卸载只移除 Snug 管理的任务与设备资源。

## 已验证与限制

- 371 项核心测试通过（0.2.51）；真实 Windows 测试涵盖窗口搬运/恢复、VS Code 跨进程字号恢复、Chrome 标签及双轴滚动、资源库拖动、强制退出、独立窗口截图和裁剪。
- 菜单惯性经过真实 WPF 定时器测试；真实 iPhone 上的手机屏退出/重新进入及 UU 断开后的窗口还原、虚拟屏释放已验收。截图裁剪后的原生剪贴板写入和完整手势矩阵仍待验收。
- Windows 使用固定的 VDD 支持模式，动态宽度步长为 20 点；多物理屏恢复、部分应用字号、跨虚拟桌面、GPU/受保护窗口的截图兼容仍待测试。
- Windows UU 会话判定采用多信号融合：可见浮动窗口、本机被控设备 UIA 表格、当前推流日志及 SYSTEM ETW 捕获清单；远程 TCP 对端仅作为诊断上下文，不再作为会话存在证据，80/443 连接不会延长会话。设备类型和实际推流显示器身份仍未知。
- Windows 当前采用手动安装升级；实际设备识别及其他平台差异见上文。

## 测试

    dotnet test Snug.Windows.sln -c Release

使用非默认 SDK 时先设置 SNUG_DOTNET_ROOT；测试/构建脚本可直接使用相同路径。PhoneUiSmoke.ps1 支持独立 UI 预览（不等同于真实手机验收）；CapturePhoneVdd.ps1 通过 PMv2 获取真实物理像素；MenuDragProbe 支持 --programmatic 进行不抢占鼠标的惯性定时器验证。探测 UU 的工具 Probe-UuLocalController.ps1 仅用于本地只读诊断。

## 致谢与许可

Snug 原作者 zephyr4123，项目前身 UUyc-companion（AoyamaShou）。延续原版的完整版权、提交历史和社区致谢；Windows 平台另感谢 Virtual Display Driver 与 NefCon。见仓库根目录 [MIT 许可证](../../LICENSE) 与 [第三方许可](installer/THIRD_PARTY_NOTICES.md)。Snug 非 UU远程 官方软件。
0.2.42 起，同一次 UU 会话内通过 UU 切换主屏和 Snug Phone，无需销毁或重建虚拟屏。使用 App 资源库中的「还原窗口」将受管理窗口归还，手机屏与资源库保持可用。会话结束或关闭手机屏开关后恢复窗口并释放虚拟屏。
