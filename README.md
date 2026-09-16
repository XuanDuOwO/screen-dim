# 屏幕变暗 (Screen Dim)

Windows 系统级屏幕变暗工具：在整块屏幕上覆盖一层**半透明黑色蒙版**，将整个系统（包括浏览器视频、桌面客户端、所有窗口）整体调暗，用于缓解纯白背景或高亮视频的刺眼问题。

纯 C# WinForms 编写，用系统自带的 .NET Framework 编译器编译，**单文件 EXE，免安装**。

## 功能

- **整系统调暗**：每个显示器各盖一个半透明黑色蒙版，普通窗口、浏览器视频、桌面客户端全屏视频都能盖住。
- **点击穿透**：蒙版不拦截鼠标，点桌面、拖窗口、滚轮照常操作（`WS_EX_TRANSPARENT` + `SetLayeredWindowAttributes`）。
- **控制面板**：启动即弹出，滑块实时调节变暗程度（5%~90%），附浅暗/中等/深暗预设。
- **快捷键**：`Ctrl+Alt+D` 开关、`Ctrl+Alt+↑↓` 调暗度。
- **托盘图标**：右键菜单可开关、打开面板、开机自启、退出。
- **多显示器支持**。

## 下载

编译好的 EXE 见 [Releases](https://github.com/XuanDuOwO/screen-dim/releases) 页面。

## 使用方法

| 操作 | 效果 |
|------|------|
| 启动程序 | 弹出控制面板，屏幕默认变暗 35% |
| 拖动滑块 | 实时调节变暗程度 |
| 浅暗 / 中等 / 深暗 | 一键预设 15% / 35% / 60% |
| `Ctrl+Alt+D` | 快速开关变暗 |
| `Ctrl+Alt+↑` / `↓` | 调暗 / 调亮 |
| 双击托盘图标 | 开关变暗 |
| 右键托盘图标 | 开关、打开面板、开机自启、退出 |

## 从源码构建

无需安装任何 SDK，用 Windows 自带的 .NET Framework 编译器：

```bash
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe -nologo -target:winexe -out:屏幕变暗.exe ScreenDim.cs
```

> 在 Git Bash 中因路径转换，需用 `-` 风格参数：`csc.exe -nologo -target:winexe -out:屏幕变暗.exe ScreenDim.cs`

## 说明与局限

- 使用系统级透明蒙版，对绝大多数窗口/视频有效；少数使用**独占全屏模式**的游戏/播放器可能盖不住。
- 变暗程度上限 90%，不做到全黑，避免完全看不见。

## License

[MIT](LICENSE)