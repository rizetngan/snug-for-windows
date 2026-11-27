# Snug Browser Zoom

Snug for Windows 0.2.53+ includes this optional Chrome/Edge companion.

## 安装一次即可

1. 安装并启动 Snug 0.2.53 或更新版本。
2. 在 Chrome 打开 chrome://extensions/；Edge 打开 edge://extensions/。
3. 开启开发者模式，点击「加载已解压的扩展程序」。
4. 选择安装目录下的 BrowserZoomExtension 文件夹（默认 C:\Program Files\Snug\BrowserZoomExtension）。

扩展图标显示连接状态。正常连接后，切换浏览器窗口或标签页，手机屏左侧缩放轨道读取当前真实倍率。浏览器原生同站点缩放规则不变。「默认」恢复浏览器配置的默认倍率。

## 本地通信与权限

扩展只申请 nativeMessaging 权限，调用浏览器 tabs.getZoom / tabs.setZoom 和窗口、活动标签页 ID 接口。不读取网址、标题、网页内容、历史记录，也不注入网页脚本。数据通过当前 Windows 用户的命名管道通信，不开放本地网络端口。安装程序提供本地 Host；Snug 启动时注册 Chrome 和 Edge 用户级 Native Messaging Host。卸载 Snug 后可以从浏览器扩展管理页面移除本扩展。

未安装或连接不可用时，浏览器使用可识别当前标签页的相对缩放，显示「缩放」而非推测百分比。其他暂不可可靠读取倍率的应用同样使用相对缩放。