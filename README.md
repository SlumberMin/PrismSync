# PrismSync · 棱镜同步

一个 Windows 原生灯效同步器：把 **笔记本键盘背光（Clevo WMI）**、**狼蛛 HERO 68 HE 键盘（HID 逐键）**、**迈从 V9 Turbo+ 耳机（HID）** 放在同一个 30 fps 时钟上，由同一套效果引擎驱动。

- 原生 C# / WinForms，**单文件 exe 约 218 KB**，无浏览器、无 Python、无安装器
- 托盘常驻：关窗口只是隐藏，灯效继续；托盘菜单可暂停/退出
- 18 个灯效（含屏幕氛围、音频律动、击键涟漪）
- 协议全部来自逆向/实测，源码可自由二改（MIT）

![主界面](docs/screenshots/main-ui.png)

> English: a native Windows RGB sync app for three specific devices (Clevo laptop keyboard backlight, AULA HERO 68 HE keyboard, MCHOSE V9 Turbo+ headset). Single-file 218 KB WinForms executable, tray-resident, 18 effects. Protocol research lives in three small companion repositories.

## 三设备协议研究（独立仓库）

| 研究 | 仓库 |
|---|---|
| 蓝天/Clevo 笔记本键盘背光（WMI `CLEVO_GET.SetKBLED`） | [clevo-wmi-rgb](https://github.com/SlumberMin/clevo-wmi-rgb) |
| 狼蛛 AULA HERO 68 HE（68 键逐键 HID 协议） | [aula-hero68-protocol](https://github.com/SlumberMin/aula-hero68-protocol) |
| 迈从 MCHOSE V9 Turbo+ 耳机（HID 灯控） | [mchose-v9-protocol](https://github.com/SlumberMin/mchose-v9-protocol) |

> Clevo 部分是在**七彩虹 X15 AT23（蓝天 NP50SNE 模具）**上测的。`CLEVO_GET.SetKBLED`
> 与这些 magic values **很可能**也存在于其它蓝天/Clevo 模具的笔记本上，但**没有在其它机型实验过**，
> 分区行为也可能不同（本机虽协议支持 left/mid/right，实际是全局单色）。验证方法见 Clevo 研究仓库。

## 功能

| 功能 | 说明 |
|---|---|
| 三设备同步 | 一个 30 fps 引擎同时驱动三台设备 |
| 18 个效果 | 三连流 / 极光 / 双色流转 / 流光 / 波浪 / 彗星 / 七彩同步 / 色相渐变 / 呼吸 / 常亮 / 星云 / 繁星点点 / 流星雨 / 焰火 / 涟漪 / **击键涟漪** / **屏幕氛围** / **音频律动** |
| 桌面预览 | 按三台设备的真实位置 / 68 键真实键位实时绘制 |
| 托盘 | 关闭窗口 → 托盘隐藏；双击图标 → 唤醒；右键菜单暂停/退出 |
| 单实例 | 重复双击会唤回已有窗口；跨管理员/普通权限也能识别 |
| 设置持久化 | 效果、速度、亮度、颜色、设备开关、耳机固件模式、窗口位置 |
| 设备热插拔 | 后台每 6 秒重试；界面有「重新扫描」 |
| 自动让路 | 启动时自动关闭会抢灯的 MCHOSE HUB（可关闭此行为） |

**击键涟漪**：Raw Input 捕获全局键盘输入，敲哪个键就从该键位置扩散光波。
**屏幕氛围**：每 200 ms 采样一次屏幕（GDI，32×18），键盘/桌面跟着画面颜色走。
**音频律动**：winmm 麦克风电平 + 低/中/高频能量驱动亮度与色相，无麦克风时优雅降级。

## 支持的硬件

| 设备 | 支持情况 |
|---|---|
| 七彩虹 X15 AT23（蓝天 Clevo NP50SNE 模具）键盘背光 | ✅ 单色全局，**需要管理员** |
| 狼蛛 AULA HERO 68 HE 键盘 | ✅ 68 键逐键 |
| 迈从 MCHOSE V9 Turbo+ 耳机 | ✅ 单色（固件常亮模式逐帧跟效果） |

这不是通用 RGB 框架，而是针对上述三台设备的实现；换设备需要写新的适配器（`native/src/Devices/`）。

## 运行

1. 下载 `release/PrismSync.exe`，或自行构建。
2. 双击运行。首次会弹 UAC：点是 → 三台一起控；点否 → 键盘+耳机照常，笔记本提示需要管理员。
3. 选效果 → 调速度/亮度 → 「启动灯效」。
4. 点 × 不会退出，会缩到托盘；退出请用托盘右键菜单。

调试参数：`--no-elevate`（不自动提权）、`--no-devices`（只开界面）、`--effect <id>`（指定启动效果）。

## 构建

要求：Windows 10/11（自带 .NET Framework 4.8）即可，**不需要安装 .NET SDK**。

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File native\build.ps1
# 产物：native\dist\PrismSync.exe（并自动复制到仓库根目录）
```

> ⚠ 必须用 **PowerShell 7（pwsh）**。Windows PowerShell 5.1 会把无 BOM 的 UTF-8 脚本按 GBK 解码，中文路径/字符串会坏。

代码结构见 [docs/NATIVE.md](docs/NATIVE.md)。

## 三个关键实现细节（踩坑换来的）

1. **耳机必须用 `WriteFile` 发 65 字节**（`[0x00] + 64 字节报文`）。
   `HidD_SetOutputReport` 会返回成功、甚至回 ACK，但固件不执行——这是“耳机不受控”的真正原因。可用 `0x07` 查询回包 `00 AA 07 … [13..15]=RGB` 客观验证。
2. **键盘闪烁 = 每帧色包数超过固件单帧处理能力**。
   实测：`cmd 0x08` 整帧 68 个色包会丢尾包（颜色错）；`cmd 0x06`（一包 11 键）连续刷新会整把闪；**`cmd 0x08` 整帧 + HSV 量化到 ≤24 色 + 30 fps 不闪且颜色正确**（当前实现）。
3. **键盘是 68 键**（旧资料里的 67 键少了 LED id 76 = ←，id 72 = Fn 不上报键值）。
   68 键映射由逐颗点亮校准得到，见 `native/src/Core/KeyLayout.cs` 与 `native/keymap/`。

完整协议与逆向过程见 [docs/HANDOFF.md](docs/HANDOFF.md) 和三个研究仓库。

## 目录

```
native/                 原生 C# 源码（29 个 .cs）
  src/App/              控制器、设置、托盘
  src/Core/             引擎、颜色、68 键表、18 个效果
  src/Devices/          HID 原生层、键盘/耳机/笔记本适配器
  src/Capture/          屏幕采样、麦克风、Raw Input
  src/UI/               自绘深色界面、桌面预览
  tools/                校准工具、写入方式测试、截图/图标脚本
release/                预编译 exe
legacy/                 旧版 Python + 本地网页实现（协议参考，已不是默认入口）
docs/                   说明、交接文档、截图
```

## 已知限制

- 耳机固件动画模式（呼吸/繁星/循环/随波）不同步；要同步请用「固件 常亮」。
- 笔记本背光必须管理员权限。
- 「音频律动」用麦克风而非系统回环；「屏幕氛围」约 5 Hz 采样。
- 未做开机自启（会每次弹 UAC）。
- 键盘为稳定显示做了 ≤24 色量化，大跨度彩虹（如三连流）会有轻微色阶感。

## License

MIT，见 [LICENSE](LICENSE)。可自由使用、修改、二次分发；保留版权声明即可。

## 免责声明

本项目与狼蛛（AULA）、迈从（MCHOSE）、七彩虹（Colorful）、蓝天（Clevo）均无关联，所有协议均为对公开软件/硬件行为的逆向研究结果，仅供学习与个人使用。刷写/控制硬件存在风险，请自行承担。文中出现的商标归各自所有者。
