# 神光同步 — 三设备 RGB 统一控制器

> **⚠ 本文档描述的是旧版 Python + 本地网页实现（保留作协议档案）。**
> 当前默认入口是原生版：根目录 **`PrismSync.exe`**，说明见 **[`PrismSync说明.md`](PrismSync说明.md)**。
> 原生版单文件约 218 KB，无浏览器/无 Python，托盘常驻；协议要点与本次修正见
> [`交接文档.md`](交接文档.md) 第八章。

让 **七彩虹 X15 AT23 笔记本键盘**、**狼蛛 HERO 68 HE 键盘**、**迈从 V9 Turbo+ 耳机** 的灯光由**同一个时钟、同一套效果引擎**驱动。

> **要接手这个项目，先读 [`交接文档.md`](交接文档.md)** —— 三设备完整协议、踩过的坑、
> 每个文件的职责、验证方法、以及"改这里会不会崩"的注意事项，都在那里。
> 本 README 是协议档案与实现细节，交接文档是给下一个工程师的地图。

三家的官方驱动都不提供公开 SDK，本项目全部走逆向：
- 七彩虹：Clevo WMI（参考开源 `hz128/clevomanager`）
- 狼蛛：**完全对齐官方 AULA_CTRL 客户端**（PyInstaller 解包后拿到明文协议与键位表）
- 迈从：从 M HUB 网页版 `main.js` 逆向出协议框架，卡在 USB 控制传输层

---

## 一、当前状态

| 设备 | 控制通道 | 能力 | 状态 |
|---|---|---|---|
| 七彩虹 X15 AT23 笔记本键盘 | Clevo WMI `CLEVO_GET.SetKBLED` | 分区逐帧控色（**实测为全局单色**） | ✅ 已打通 |
| 狼蛛 HERO 68 HE | HID output report，cmd `0x08` 逐键流 | 逐键逐帧 + 20 种固件灯效 | ✅ 已打通 |
| 迈从 V9 Turbo+ 耳机 | **HID output report（`0x6008` / usage_page `0xFF00`，65 字节）** | 6 种固件灯效 + 颜色 + 亮度 | 🟡 HID 已通，缺帧格式 |

**三设备统一效果**当前已可实现：七彩虹 + 狼蛛是严格逐帧同步，耳机走固件效果对齐。

---

## 二、设备识别

| 设备 | VID:PID | 控制接口 |
|---|---|---|
| 七彩虹 X15 AT23 | 笔记本内置，DMI `NP5x_6x_7x_SNx` / INSYDE BIOS → 蓝天 Clevo NP50SNE 模具 | WMI `root\WMI` → `CLEVO_GET` (实例 `ACPI\PNP0C14\0_0`) |
| 狼蛛 HERO 68 HE | `0x372E:0x103E`（BY Tech） | HID `MI_02 & Col01`，usage_page `0xFF60`，report id 9 |
| 迈从 V9 Turbo+ 耳机 | `0x3837:0x600A`（C-Media 音频复合）/ `0x3837:0x6008`（WCH 主控） | HID `MI_00 & Col01/02/05/06`，usage_page `0xFF21/0xFF22/0xFF90/0xFF92` |

---

## 三、协议档案

### 3.1 Clevo WMI（七彩虹笔记本键盘）

```
命名空间 : root\WMI
类       : CLEVO_GET
方法     : SetKBLED(uint32 Data)
```

| 动作 | 值 |
|---|---|
| 开灯 / 关灯 | `0xE007F007` / `0xE0003000` |
| 左区 / 中区 / 右区 / 灯条 | `0xF0000000` / `0xF1000000` / `0xF2000000` / `0xF3000000` |
| 亮度 | `0xF4000000 \| level`（off 0 / low 63 / mid 127 / high 255） |
| 固件效果 | Random `0x70000000`、Breathe `0x1002A000`、Cycle `0x33010000`、Wave `0xB0000000`、Dance `0x80000000`、Tempo `0x90000000`、Flash `0xA0000000` |

**颜色打包**（不是常见 RGB 顺序）：`value = (Blue << 16) + (Red << 8) + Green`，再 `| 分区值`。
例：左区纯红 = `0xF000FF00`。

> **实测结论**：X15 AT23 的背光是**全局单色**——left/mid/right 分区值都被接受，但所有键一起亮，无法分区。所以笔记本在同步里表现为「整把键盘一个颜色」。

**前置条件**：`C:\Windows\SysWOW64\clevomof.dll` + 注册表
`HKLM\SYSTEM\CurrentControlSet\Services\WmiAcpi\MofImagePath = syswow64\clevomof.dll`，
否则 `root\WMI` 里没有 `CLEVO_GET` 类。注册脚本 `register_clevo.ps1`（需管理员）。
调用 `SetKBLED` 也需要管理员权限。

### 3.2 AULA HERO 68 HE（HID output report，官方对齐）

协议取自官方客户端 **AULA_CTRL_V1.0.17**（PyInstaller 解包，`configs/test.py` 为明文源码）。

**报文结构**：

```
[0] report id 0x09   [1] cmd   [2] param  [3] reserved
[4] total packets   [5] packet index   [6] payload 长度   [7:] payload
[63] CRC = (0xFF - sum(packet[0:63])) & 0xFF
```

> WebHID 抓包不含 report id，所以抓包 `[0] == packet[1]`。
> 我早期逆向得到的 `0xF6 - sum` 与官方 `0xFF - sum` **完全等价**（差值 0x09 就是 report id 本身）。

**命令表**：

| cmd | 用途 | payload |
|---|---|---|
| `0x82` | 读设备 UID（param `0x01`） | 空；响应 `[7:13]` 是 6 字节 UID |
| `0x04` | 切换固件灯效 / 单键设色 | `[效果ID, 0x07, R, G, B, 0x14, 0x01]`（param `0x01`） |
| `0x04` | 进入自定义模式（param `0x02`） | `[0x13, 0x00]` |
| `0x04` | 亮度/速度（param `0x04`） | `[0x64, 0x04]` |
| `0x06` | **逐键设色** | `[键号>>8, 键号&0xFF, R, G, B]`，每包 ≤11 键 |
| `0x08` | 逐键颜色流（按颜色分段） | `[R, G, B, 本段键数] + 键号×1`，每段 ≤52 键 |
| `0x0E` | **灯箱**（键盘外圈 18 颗 LED） | 每灯 `[R, G, B, 1] + 灯号`，9 灯一包共 2 包 |
| `0x01` | 使能（param `0x80`）/ 提交（param `0x00`） | `[0x10]` / 空 |
| `0x84` | 提交 / 应用 | 空 |
| `0x83` | 上传灯组定义 | 每包 9 个键号（大端 u16），共 8 组 |

> 封装已用抓包原文做过**逐字节回归**：86 条真实报文全部重建一致（86/86）。

**逐键上色的官方流程**（`core/keyboard_protocol.py::set_custom_colors`）：

```
前置  0x01/0x80 [0x10]              进入自定义
      0x04/0x02 [0x13, 0x00]        效果槽 19 = 自定义
      0x04/0x04 [0x64, 0x04]        亮度 / 速度
逐帧  0x06/0x00 [kid_hi, kid_lo, R, G, B] × ≤11 键
提交  0x01/0x00 []
```

### ⚠ 决定性发现：一个包只能带一个色段

这是本项目耗时最久的一个坑，也是"协议全对但键盘不变色"的真正原因。

固件**只解析每包的第一个 `[R,G,B,count,ids...]` 色段**，后面的色段会被忽略。所以：

- `cmd 0x08` 把一个颜色组（67 键同色）单独放一包 → **整把键盘正确变色**（实测全绿）
- 把一个 67 色的渐变拆成 67 个色段、11 个塞进一包 → **只有 7 个键变色**，
  其余键保持旧色，屏幕上就是一坨"蓝 WASD + 白右侧 + 青顶排"的残影

正确做法（`hero68_final.py` / `rgbsync.adapters.hero68.apply`）：

```python
for 每种色 -> 每 52 键切成一段 -> 一段一包，total_pkts = 包数，pkt_idx = 序号
```

成本：纯色 1–2 包（0.3 ms）；67 段全渐变 8.4 ms（够 30 fps）；量化到 16 色阶只要 2.6 ms。
适配器默认 `bands=16`，用色阶把渐变压到 ~19 包，30 fps 下余量充足。

**HID 集合**：狼蛛有两个厂商集合，都实测可用——`MI_02 & Col01`（usage_page `0xFF60`）与
`MI_02 & Col02`（usage_page `0xFF00`）。官方客户端的 `TARGET_USAGE_PAGES` 也是 `[0xFF60, 0xFFE0]`，
说明不同命令可能落在不同集合，排查新命令时必须两个都试。

**键位表**：官方按设备 UID 索引（`configs/keyboard_uid.py` 的 `ButtonLayout._LAYOUTS`）。
本机 HERO 68 HE 的 UID = `110000000003` → 5 行 67 键。设备自己上报的灯组（cmd `0x83`）是
8 组共 68 键，键号 = `1, 15..77, 98, 99, 102, 103`。

**固件灯效表**（cmd `0x04` 的效果 ID，抓自 A HUB）：

| ID | 名称 | ID | 名称 | ID | 名称 |
|---|---|---|---|---|---|
| `0x01` | 常亮灯效 | `0x08` | 繁星点点 | `0x0F` | 旋转风车 |
| `0x02` | 呼吸模式 | `0x09` | 踏雪无痕 | `0x10` | 七彩瀑布 |
| `0x03` | 梦幻彩虹 | `0x0A` | 川流不息 | `0x11` | 花开富贵 |
| `0x04` | 一触即发 | `0x0B` | 随波逐流 | `0x12` | 旋转风暴 |
| `0x05` | 雨中漫步 | `0x0C` | 如影随形 | `0x13` | 自定义 |
| `0x06` | 彩虹转轮 | `0x0D` | 正弦光波 | `0x0E` | 左右扫描 |
| `0x07` | 涟漪扩散 | | | | |

### 3.3 MCHOSE V9 Turbo+ 耳机

耳机在系统里是**两台 USB 设备**（同一副耳机拆成两半）：

| 地址 | 角色 | 说明 |
|---|---|---|
| `0x3837:0x6008` | **WCH 主控，灯控** | product `V9 Turbo`，mfr `wch.cn`，单一 HID 集合 `usage_page 0xFF00` |
| `0x3837:0x600A` | C-Media 声卡 | 7.1 音频 + 电话 HID（`0xFF82/0xFF90/0xFF21/0xFF22`），**与灯无关** |

**协议（USBPcap 抓包 + 设备 ACK 双重验证）**

线上报文 **64 字节**，第一字节是 **report id `0xAA`**：

```
aa 27 00 00 00 0a 01 <mode> 05 <dir> 03 00 <R> <G> <B> 00 …
^^                      ^^       ^^                       ^^
report id               [7]      [9]                      [12..14]
```

| 命令 | 含义 |
|---|---|
| `0x07` | 状态查询，回包里带 `V9 Turbo` 字符串 |
| `0x27` | 设灯光：`[7]`=模式，`[9]`=方向，`[12..14]`=RGB |

固件模式：`0` 常亮 / `1` 呼吸 / `2` 繁星 / `3` 循环 / `4` 随波。

**Windows 的坑（这是整整卡住的关键）**

`HidD_SetOutputReport` 要求 **65 字节 buffer**，并会**丢掉它的第 0 字节**。所以要发上面的
64 字节，buffer 必须是 `[0x00] + <64 字节报文>`。发 63/64 字节一律 `err 87`。

**两次错误结论的更正**

1. 早期以为必须装 USBPcap / 换 WinUSB —— 不需要。`0x6008` 用标准 HID 就能写，
   而且**不碰 `0x600A` 的 C-Media 音频**（那条路会掉 7.1 声音，已永久排除）。
2. 早期以为 "report id 是 0、线上 63 字节" —— 那是 **pcap 解析器的 bug**：
   USBPcap 的 `headerLen` 字段是 **27**（不是 28），硬编码 28 会吞掉每条中断报文的
   第一个字节，正好把 report id `0xAA` 藏了起来。**解析 pcap 必须用 `headerLen` 字段。**

**同步能力**：耳机是固件渲染，不能逐键画。但可以设成常亮模式、逐帧换色，
效果等同单区设备。

**⚠️ 双句柄陷阱（最隐蔽的一个坑）**

设备对每条输出报文都回一条输入报文。如果用 `CreateFileW` + `HidD_SetOutputReport`
写入、却用另一个 hidapi 句柄读取，**ACK 会积压在被写入那个句柄的队列里**——没人排空，
队列一满，固件就**静默停止接受新的输出报文**（`HidD_SetOutputReport` 依旧返回 True）。

症状极具误导性：单次写、间隔 2.5 秒 → 有效；连续写、间隔 0.4 秒 → 完全不动。
看起来像"固件需要长间隔"，其实只是队列被塞满。

**正确做法：读写必须用同一个句柄**，且每次写完排空 ACK：

```python
dev.write(frame)                    # 64 字节报文，hidapi 自动带 report id
while dev.read(64, 1):              # 非阻塞排空
    pass
```

客观验证（`0x07` 查询回包里带设备当前 RGB）：

| 设置 | 设备回包 |
|---|---|
| RED `ff0000` | `… 03 00 **ff 00 00** …` |
| GREEN `00ff00` | `… 03 00 **00 ff 00** …` |
| BLUE `0000ff` | `… 03 00 **00 00 ff** …` |

回包 RGB 精确跟随 → 写入确实被设备应用，不再是"猜"。

---

## 四、同步架构

官方 AULA_CTRL 的神光同步就是「一帧广播给所有设备」，每种设备一个编码器
（`core/manager.pyc` 的 `sync_send`）：

```
keyboard          → send_rgb_frame(data) / send_grouped_frame(data)
speaker/headset   → update_frame(data)
mouse_base/pad    → update_frame(data)
mouse             → set_color_all(r, g, b)
```

本项目采用同一思路，但用**位置轴**统一三台设备差异极大的 LED 布局：

```
              ┌───────────────────────────┐
              │       SyncEngine          │  单一时钟 · 单一效果
              │  effect.frame(positions)  │
              └────────────┬──────────────┘
                           │ Frame{zone: RGB}
        ┌──────────────────┼──────────────────┐
        ▼                  ▼                  ▼
  ClevoAdapter      Hero68Adapter      MchoseV9Adapter
  (WMI SetKBLED)    (cmd 0x08 逐键)    (USB 控制传输)
  整把键盘一个色     68 键逐键色        固件效果
```

每个 LED 映射到 0.0–1.0 的水平位置，效果按位置算颜色 → 同一效果在笔记本键盘、
外接键盘、耳机上形状与时序一致，这就是"神光同步"的视觉来源。
笔记本因为是全局单色，落在轴中心 0.5。

---

## 五、效果对照

| 效果 | 七彩虹(Clevo) | 狼蛛 | 迈从 |
|---|---|---|---|
| 常亮 | SetKBLED 静态色 | 固件 `0x01` | 常亮模式 |
| 呼吸 | 固件 `0x1002A000` | 固件 `0x02` | 呼吸模式 |
| 彩虹循环 | 固件 `0x33010000` | 固件 `0x03` 梦幻彩虹 | 循环变色 |
| 波浪 | 固件 `0xB0000000` | 固件 `0x07` 涟漪扩散 | 随波逐流 |
| 逐帧自定义 | ✅ 分区逐帧 | ✅ 逐键逐帧 | ❌ 仅固件效果 |

---

## 六、工程结构

```
启动神光同步.bat            # 双击即用：自提权 → 起服务 → 开无边框窗口
gui/
  server.py                # 控制台后端：标准库 HTTP + JSON API，引擎跑在后台线程
  launch.py                # 启动器（提权、选空闲端口、开 --app 窗口）
  ui/index.html            # 单页界面（暗色玻璃质感 + 实时桌面预览）
rgbsync/
  core.py                  # 效果引擎、位置轴、帧模型、统一时钟、fps 统计
  effects.py               # 效果注册表（CLI 与 GUI 共用，含中文名/说明）
  main.py                  # 命令行入口
  adapters/
    base.py                # 适配器抽象（zones / span / global_positions / apply）
    clevo.py               # 七彩虹（Clevo WMI，PowerShell 桥接）
    clevo_bridge.ps1       # 常驻桥接：stdin 收十六进制 → SetKBLED
    hero68.py              # 狼蛛（逐键 cmd 0x06，一色段一包）
    mchose.py              # 迈从耳机（report id 0xAA，单句柄读写 + 排空 ACK）
register_clevo.ps1         # Clevo WMI 注册 + GATE 自测（管理员，只需一次）
run_sync.ps1               # 命令行一键提权启动
hero68_official.py         # 狼蛛单设备验证
device_check.py            # 三设备连通性自检
```

**位置轴（桌面布局）**：每个适配器有一个 `span`，表示它在桌面上的物理区间。
引擎把所有 LED 映射到这条 0..1 的轴上，所以一条色带可以从最左边的耳机流过中间的
笔记本、再扫过右边的键盘：

| 设备 | span | 可控粒度 |
|---|---|---|
| 耳机 | 0.00 – 0.13 | 单区（硬件固件限制） |
| 笔记本 | 0.21 – 0.53 | 单区（实测全局单色） |
| 键盘 | 0.60 – 1.00 | **68 键逐键** |

---

## 七、使用

### 图形界面（推荐）

双击 **`启动神光同步.bat`**。它会自动提权、起本地服务，并用一个无地址栏的窗口
打开界面（看起来就是个独立软件）。

界面能做的事：

- 三个设备卡片：实时连接状态 + 该设备当前颜色的光晕
- **桌面实时预览**：左边耳机、中间笔记本、右边键盘（5 行真实键位，长空格/宽 shift），
  每 130 ms 刷新一次，所见即所得
- 10 个效果一键切换：三连流 / 极光 / 双色流转 / 流光 / 波浪 / 彗星 / 七彩同步 /
  色相渐变 / 呼吸 / 常亮
- 速度、亮度两个滑块，运行中拖动即时生效（不打断动画相位）
- **启动时自动关掉 M HUB**：它会每 200 ms 重推自己的灯效，留着会和我们抢设备
- 底部日志，显示引擎状态与设备接入情况
- **关窗口即释放**：窗口一关，引擎停止、适配器关闭、进程退出，不会留后台服务

> 服务只监听 `127.0.0.1`，不对外。命令行调试可 `python -m gui.server --no-open`。

**退出有三条独立路径**（`gui/launch.py`），确保关掉窗口后一定释放设备：

| 路径 | 触发条件 |
|---|---|
| `proc.poll()` | 启动的浏览器进程退出（正常关窗口） |
| `/api/bye` | 页面 `pagehide` 时 `sendBeacon` 通知 |
| idle 超时 | 12 秒没有任何请求（浏览器崩溃/被强杀） |

任一条命中就 `STATE.stop()` → 等 1.5 秒让引擎线程 `close_adapters()` → `srv.shutdown()`。

**位置轴与键位映射**：AULA 的 `ButtonLayout` 返回的是 `{键名: 横向位置}` 的 **dict**，
适配器直接用它（不是当成行列列表遍历），所以每个键都有真实坐标。GUI 预览用的
也是同一份坐标，所见即所得。

### 命令行

```powershell
# 1) 注册 Clevo WMI（管理员，只需一次；已注册过可跳过）
cd "d:\Users\Lie\Desktop\神光同步"
powershell -ExecutionPolicy Bypass -File .\register_clevo.ps1

# 2) 三设备同步 —— 脚本会自己弹 UAC 提权（笔记本走 WMI 必须管理员）
.\run_sync.ps1
.\run_sync.ps1 -Effect rainbow          # 三设备同色同色相循环
.\run_sync.ps1 -Effect wave -Uniform    # 同色波浪
.\run_sync.ps1 -NoLaptop                # 只驱动外设，不需要管理员
.\run_sync.ps1 -NoHeadset               # 跳过耳机

# 等价的手动方式
python rgbsync\main.py --effect trio --speed 1.2 --power 0.9
python test_sync_hero68.py 6 wave       # 只驱动狼蛛（无需管理员）
```

参数：`--effect {trio,aurora,duo,flow,wave,comet,rainbow,hue,breathe,static}`、
`--speed`（0.3–2.5 倍速）、`--power`（0.1–1.0 亮度）、`--fps`、`--uniform`、
`--no-laptop` / `--no-keyboard` / `--no-mouse`。

**单区设备的处理**：七彩虹背光是全局单色，`ClevoAdapter.single_zone = True`，
引擎给它下发 `Effect.global_color(t)`（波峰的主色、全亮度），而不是按位置采样。
否则波峰移开后它取到 8% 的暗光底噪，看起来像熄灯。

**WMI 延迟解耦**：`SetKBLED` 单次往返毫秒级，远慢于一帧。`ClevoAdapter` 用一个
单槽位只保留最新颜色、后台线程异步下发，中间帧直接丢弃——否则整条链路会被 WMI
拖到个位数帧率。`fps` 那行会打印 `sent=` / `dropped=` 便于确认。
