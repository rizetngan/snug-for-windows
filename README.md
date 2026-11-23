<div align="center">

<img src="Assets/AppIcon/AppIcon-1024.png" width="100" alt="Snug 图标">

# Snug for Windows

**让 Windows 电脑在手机上拥有原生级的操作体验**

为 UU 远程打造 · 独立竖屏 · 原生触控 · App 资源库

![Windows x64](https://img.shields.io/badge/Windows-x64-blue) ![Release](https://img.shields.io/badge/version-0.2.47-blue) [![MIT](https://img.shields.io/badge/License-MIT-green)](LICENSE)

简体中文 · [English](README.en.md)

[下载](#下载安装) · [原生体验](#原生体验) · [App 资源库](#app-资源库) · [使用](#使用方法) · [开发](#开发)

</div>

<p align="center">
<img src="Assets/WindowsMedia/phone-running.png" width="30%" alt="Windows 手机屏：App 资源库">
<img src="Assets/WindowsMedia/phone-tour-live.gif" width="30%" alt="实机逐帧演示：搜索终端、打开及进入手机屏">
<img src="Assets/WindowsMedia/phone-gpt-live.png" width="30%" alt="ChatGPT 桌面端在 Snug 手机屏上的实际界面">
<br><sub>App 资源库 · 终端操作演示 · ChatGPT 桌面端</sub>
<br><sub>实际 Windows 虚拟屏采集：动态演示经剪辑，非 UU 手机客户端录屏。</sub>
</p>

Snug for Windows 是 [Snug](https://github.com/zephyr4123/snug) 的独立社区 Windows 移植项目，由 [rizetngan](https://github.com/rizetngan) 维护。基于 Snug 2.5.0，使用 C# / .NET 10 / WPF。macOS 用户请前往上游原项目。

## 下载安装

**0.2.47 正式发布版** · Windows x64（Windows 10 22H2 已实测）

**[下载安装包（Windows x64）](https://github.com/rizetngan/snug-for-windows/releases/download/v0.2.47/Snug-Setup-0.2.47-x64.exe)** · [发布说明](https://github.com/rizetngan/snug-for-windows/releases/tag/v0.2.47) · [SHA-256 校验](https://github.com/rizetngan/snug-for-windows/releases/download/v0.2.47/SHA256SUMS.txt) · [从源码构建](Platforms/Windows/README.md#构建与打包)

安装包：Snug-Setup-0.2.47-x64.exe。本版保留点击延迟修复，同一次 UU 会话内可自由切换主屏与手机屏，并通过 App 资源库还原窗口。优化手机屏创建与释放：Windows 10 22H2 + iPhone 实测检测连接至手机屏就绪 4.43 秒，判定中断至释放完成 2.82 秒（单次记录，不含 UU 自身处理及检测前等待）。内置 .NET 运行时及签名虚拟显示驱动；安装器本身未签名，需管理员授权。若升级时 UU 后台已运行，完整捕获证据可能暂不可用，程序保留兼容检测与 6 秒重连保护；可在断开所有 UU 远控后重启 UU Windows 服务。

已知问题：UU iOS 端键盘收起后直接再次弹出，可能无法自动避让输入框；普通电脑屏幕也可复现。重新点击输入框后再调起键盘可恢复适配。

## PowerShell 安装

在 **Windows PowerShell** 中复制执行：

```powershell
$ErrorActionPreference = 'Stop'
$script = Join-Path $env:TEMP 'install-snug.ps1'
Invoke-WebRequest -UseBasicParsing 'https://raw.githubusercontent.com/rizetngan/snug-for-windows/main/scripts/install-windows.ps1' -OutFile $script
& $script
```

脚本自动下载最新稳定版安装器并验证 SHA-256，随后要求你确认已断开 UU 远程连接，才会打开官方安装程序。安装器包含签名虚拟显示驱动和捕获监视任务，需要 Windows 管理员授权；脚本不会擅自断开 UU、重启服务或关闭系统安全防护。

[查看 PowerShell 安装脚本](scripts/install-windows.ps1)。仅下载并校验而不安装：将最后一行改为 `& $script -DownloadOnly`。

## 使用 Agent 安装

将以下整段指令复制给能够操作 Windows 电脑的 Agent，即可按正式版安装流程执行：

```text
请帮我在这台 Windows x64 电脑安装 Snug for Windows 最新稳定正式版：
https://github.com/rizetngan/snug-for-windows

1. 检查 Windows 版本及架构，仅从此仓库 GitHub Releases 选择最新稳定版的 Snug-Setup-*-x64.exe，不使用第三方镜像或源码构建代替正式安装包。
2. 同时下载同一版本的 SHA256SUMS.txt 并验证安装器 SHA-256；校验失败停止。
3. 检查 Snug 是否已安装，以及 UU 远程是否有正在进行的连接。升级前保留原有设置；如有 UU 会话，先提示我断开，不要擅自中断远程连接或重启 UU 服务。
4. 说明安装程序需要管理员授权，并将安装项目自带的签名虚拟显示驱动与 SYSTEM 级 UU 捕获监视任务。获得我确认后运行官方安装器，不关闭 Windows 安全防护，也不额外安装来源不明的驱动。
5. 安装后启动 Snug，检查应用能否正常打开、版本和手机屏设置是否可用；若尚未安装 UU 远程，请提示需要在电脑及手机端安装，而不是未经确认自行安装。
6. 告诉我安装版本、路径、SHA-256 校验结果、启动检查结果，以及需要我手动完成的 UU 连接或系统授权操作。不删除原有用户设置。
```

## 原生体验

普通远程控制将整个 Windows 桌面缩放到手机屏幕上，小字和控件不易操作。Snug 为 UU 远程创建独立的竖屏，将正在使用的 Windows 窗口适配到手机尺寸；不用在手机上反复缩放整块桌面。

- **窗口铺满**：选择应用窗口后，按手机屏幕尺寸调整窗口；切换时恢复原来的受管理窗口。
- **字号与布局**：兼容的应用可调整字号；支持应用的最小宽度适配。
- **截图与裁剪**：在设置中开启截图后，可以将窗口画面复制到 Windows 剪贴板，再按需裁剪。

<p align="center">
<img src="Assets/WindowsMedia/browser-window-before.png" width="46%" alt="同一 Chrome 测试窗口在 Windows 主屏上的原始大小">
<img src="Assets/WindowsMedia/browser-phone-live.png" width="29%" alt="0.2.39 将Chrome 测试页面适配到 Snug 手机屏，保留底部返回条">
<br><sub>同一测试页面 · 左：原始 Windows 窗口 · 右：Snug 手机屏（0.2.39 实机）</sub>
</p>

Chrome 在手机屏上完整显示，Snug 按窗口最小宽度动态调整显示模式；断开 UU 后，窗口恢复原始位置。图为连接 iPhone 时从电脑采集的实际窗口与虚拟屏画面，并非 UU 手机客户端截图。

### 编码工作场景

<p align="center">
<img src="Assets/WindowsMedia/phone-terminal-live.png" width="30%" alt="真实 Windows 终端：Claude Code CLI 版本与 Node.js 测试结果">
<img src="Assets/WindowsMedia/phone-gpt-live.png" width="30%" alt="ChatGPT 桌面端 Codex 工作区">
<br><sub>真实终端 · ChatGPT 桌面端 · 底部返回条</sub>
</p>

在 App 资源库搜索窗口，打开已有终端并切换到手机屏。演示使用本地 Claude Code CLI 版本查询及 Node.js 测试，未调用模型；虚拟屏采集由电脑端操作完成。ChatGPT 图为用户通过 iPhone 连接 UU 时打开的实际桌面端工作区；账号标识、工作区名称及模型标签已隐藏。

### 手机触控

| 操作 | Snug 手机屏 |
| :--- | :--- |
| 单指滑动 | 滚动页面与列表 |
| 水平滑动 | 对支持横向滚动的区域进行横向操作 |
| 长按 | 支持长按交互 |
| 底部返回条 | 返回 App 资源库 |

实际手势表现与 UU 触控模式及应用兼容性有关。

## App 资源库

无需回到电脑桌面，即可查看运行中的应用、搜索窗口、启动已安装的应用或切换至其他窗口。

<p align="center">
<img src="Assets/WindowsMedia/phone-running.png" width="30%" alt="正在运行的应用与窗口">
<img src="Assets/WindowsMedia/phone-all-apps.png" width="30%" alt="全部应用图标网格">
<img src="Assets/WindowsMedia/phone-search.png" width="30%" alt="搜索 Notepad 应用">
<br><sub>运行中 · 全部 · 搜索 Notepad（资源库实拍与 0.2.38 本地预览）</sub>
</p>

| 操作 | 方式 |
| :--- | :--- |
| 打开窗口 | 轻点对应的窗口条目 |
| 打开应用 | 在「全部」中选择应用 |
| 搜索 | 按应用名或窗口标题查找 |
| 新建与关闭 | 使用应用卡片上的按钮 |
| 返回资源库 | 轻点底部返回条 |

## 手机型号适配

Snug 根据手机型号调整虚拟显示比例，机型库也可与上游同步。首次使用时，在手机屏右上角或 Windows 设置中选择机型；未列出的设备可以使用自定义尺寸。

<p align="center">
<img src="Assets/WindowsMedia/phone-models.png" width="29%" alt="当前机型、自定义分辨率及机型列表">
<br><sub>机型选择：搜索型号并查看屏幕分辨率</sub>
</p>

## Windows 设置

在 Windows 上完成首次设置、选择手机型号、开启触控和截图。关于页面提供当前版本、独立仓库与原项目致谢。

<p align="center">
<img src="Assets/WindowsMedia/windows-settings.png" width="44%" alt="Snug for Windows 0.2.39 设置页">
<img src="Assets/WindowsMedia/windows-about.png" width="44%" alt="Snug for Windows 0.2.39 关于页">
<br><sub>设置 · 关于（0.2.39 实际程序界面）</sub>
</p>

## 使用方法

1. 在电脑与手机安装 [UU 远程](https://uuyc.163.com/)，在电脑安装并运行 Snug。
2. 在设置中选择手机型号并开启手机屏。
3. 手机通过 UU 连接电脑，在屏幕列表选择 Snug Phone，使用屏幕触控模式。
4. 在 App 资源库打开窗口，轻点底部返回条切回资源库。
5. **0.2.42 起：** 同一次 UU 远程会话内，直接用 UU 切换主屏和 Snug Phone，手机屏保持可用。如需归还应用窗口，在 App 资源库使用「还原窗口」。断开远程或关闭手机屏功能后，Snug 恢复受管理窗口并释放虚拟屏。

[详细安装与使用文档](Platforms/Windows/README.md) · [截图来源](Assets/WindowsMedia/README.md)

## 验证与限制

2026-11-18：276 项核心测试、Release 构建及 iPhone 13 Pro + UU 远程实机验收通过：后台监视器恢复、手机屏稳定、Chrome 铺屏、退出重进、断开后窗口还原及虚拟屏释放。

控制端设备类型与实际推流显示器身份仍未知；部分截图、字号兼容、多屏恢复及完整手势矩阵仍待扩大验证。

[验收记录](Platforms/Windows/CAPTURE_MONITOR_RECOVERY_20261118.md) · [更新记录](Platforms/Windows/CHANGELOG.md) · [已知限制](Platforms/Windows/README.md#已验证与限制)

## 开发

Windows 当前维护入口为默认分支 `main`，平台代码位于 [Platforms/Windows](Platforms/Windows/)。

```powershell
git clone https://github.com/rizetngan/snug-for-windows.git
cd snug-for-windows/Platforms/Windows
.\build-snug.cmd
.\run-snug.cmd
```

[构建、测试与打包](Platforms/Windows/README.md) · [移植进度](Platforms/Windows/PORTING_STATUS.md)

## 致谢与许可

感谢 [Snug / zephyr4123](https://github.com/zephyr4123/snug)、[UUyc-companion / AoyamaShou](https://github.com/aoyamashou/UUyc-companion)、原项目社区贡献者、[Virtual Display Driver](https://github.com/VirtualDrivers/Virtual-Display-Driver) 与 [NefCon](https://github.com/nefarius/nefcon)。

保留原项目历史、macOS 源码与版权声明。Windows 版本独立开发、发布和维护，不属于原项目官方 Windows 版本或 UU 远程官方软件。

[MIT License](LICENSE) · [第三方许可](Platforms/Windows/installer/THIRD_PARTY_NOTICES.md)
