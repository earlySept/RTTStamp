# RTTStamp - J-Link RTT 日志查看器（每行带时间戳）

独立的 J-Link RTT 日志查看工具：直接调用 SEGGER J-Link DLL 读取 RTT 日志，在每条日志前面加上主机时间戳并显示 / 保存。不需要打开 JLinkRTTViewer。

**零依赖**：单文件 C# 源码，使用 Windows 自带的 .NET Framework 编译器（csc.exe）构建，无需安装任何开发环境。

![UI](https://img.shields.io/badge/UI-WPF%20%2B%20Fluent-blue) ![Runtime](https://img.shields.io/badge/Runtime-.NET%20Framework%204.x-green) ![Platform](https://img.shields.io/badge/OS-Windows%2010%2F11%20x64-lightgrey)

## 显示效果

```
[2026-11-19 14:10:32.614] I/[1460:12:178] (bsp_key.c 249)..........>KID_KEY0
[2026-11-19 14:10:32.220] [Mesh recv:]: 0B 00 91 70 08 07 00 00 FF FF 03 A2 B6
[2026-11-19 14:10:35.845] I/bl_queue [1460:12:300] (mg_bl_queue.c 272)send time:2
```

## 功能

### 连接
- 设备下拉可选（GD32F103CBT6 / APM32E103CE / GD32F303CR / STM32F303，支持输入任意 J-Link 支持的芯片名）
- 接口 SWD/JTAG、速率可调、RTT 通道可选（默认 0）
- 断线自动重连（每 2 秒）、目标 CPU 暂停时自动恢复运行
- 多台 J-Link：填序列号选择；支持多开实例同时监控多块板子（`RTTStamp.exe 2`）

### 日志
- 每行加主机时间戳（取该行首字节到达时刻，1ms 轮询，误差毫秒级）
- 三种时间戳格式：年-月-日 时:分:秒.毫秒 / 时:分:秒.毫秒 / 相对连接秒数
- 按级别着色：`I/` `[INFO]` 绿、`D/` 青、`W/` `[WARN]` 黄、`E/` `[ERROR]` 红
- 剥离 ANSI 颜色码（可选，默认开），中文按 GBK 解码
- 自动滚动 + 回看保护：往上翻历史时新日志照常追加，视口不被拽走；滚回底部自动恢复跟随
- 查找（Ctrl+F / F3）、字体缩放（Ctrl+滚轮 / Ctrl+±，Ctrl+0 复位）

### 保存（文件存放在日志目录，默认 `logs\`）
- **实时保存**：勾选后逐条写入 `real_log_年月日_时分秒.log`，清空窗口不影响
- **保存日志**（Ctrl+S）：把当前窗口内容一次性导出为 `log_年月日_时分秒.log`

### J-Link 路径
自动按顺序查找：INI 配置 → 注册表（`HKLM\SOFTWARE\SEGGER\JLink` 的 InstallPath）→ 默认安装路径 → 弹窗手动选择。设置窗口内可随时更换（重启生效）。

## 构建

双击 `build.bat`，或手动执行：

```bat
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe ^
  -nologo -target:winexe -out:RTTStamp.exe ^
  -r:System.Windows.Forms.dll -r:System.Drawing.dll -r:System.Core.dll ^
  -r:PresentationFramework.dll -r:PresentationCore.dll -r:WindowsBase.dll -r:System.Xaml.dll ^
  -lib:"C:\Windows\Microsoft.NET\assembly\GAC_MSIL\PresentationFramework\v4.0_4.0.0.0__31bf3856ad364e35" ^
  -lib:"C:\Windows\Microsoft.NET\assembly\GAC_64\PresentationCore\v4.0_4.0.0.0__31bf3856ad364e35" ^
  -lib:"C:\Windows\Microsoft.NET\assembly\GAC_MSIL\WindowsBase\v4.0_4.0.0.0__31bf3856ad364e35" ^
  -lib:"C:\Windows\Microsoft.NET\assembly\GAC_MSIL\System.Xaml\v4.0_4.0.0.0__b77a5c561934e089" ^
  -codepage:65001 RTTStamp.cs
```

## 快捷键

| 功能 | 快捷键 |
|---|---|
| 打开查找 | `Ctrl + F` |
| 查找下一个 / 上一个 | `F3` / `Shift + F3` |
| 关闭查找 | `Esc` |
| 保存日志 | `Ctrl + S` |
| 清空日志 | `Ctrl + L` |
| 字体缩放 | `Ctrl + 滚轮` 或 `Ctrl + +/-`，`Ctrl + 0` 复位 |
| 复制 / 全选 | `Ctrl + C` / `Ctrl + A` |

## 已知限制

- 时间戳是 PC 收到日志的时刻，不是固件执行时刻（RTT 协议本身不带时间；需要设备侧精确时间请看固件打印的相对时间）
- 与 JLinkRTTViewer / Keil 等共用 J-Link 会争抢，同一时刻一个 J-Link 只能被一个软件连接
- 深色主题下部分原生控件（下拉框/输入框）仍为浅色
