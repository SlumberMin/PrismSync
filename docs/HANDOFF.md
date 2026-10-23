# 神光同步 — 交接文档

三台设备的 RGB 用**同一个时钟、同一套效果引擎**驱动：

| 设备  | 型号                             | 位置（桌面从左到右）     | 可控粒度           |
| --- | ------------------------------ | -------------- | -------------- |
| 耳机  | 迈从 MCHOSE V9 Turbo+            | 最左 `0.00–0.13` | **单区**（固件限制）   |
| 笔记本 | 七彩虹 将星 X15 AT23（蓝天 NP50SNE 模具） | 中间 `0.21–0.53` | **单区**（实测全局单色） |
| 键盘  | 狼蛛 AULA HERO 68 HE             | 右侧 `0.60–1.00` | **67 键逐键**     |

> **为什么有两台是单区**：笔记本背光虽然协议支持 left/mid/right 分区，但这台机型所有键一起亮；  
> 耳机是固件渲染，PC 只能设"模式 + 一个颜色"。所以"分区流动"只发生在键盘上，  
> 另外两台靠**颜色在时间上的推移**参与流动。



---

## 一、快速上手

```powershell
# 双击即可（会自动提权、起本地服务、开一个无地址栏窗口）
.\启动神光同步.bat
```

界面里：三设备连接状态 → 选效果 → 调速度/亮度 → 点「启动」。  
关掉窗口会自己释放设备、退出进程。

命令行等价物：

```powershell
python rgbsync\main.py --effect trio --speed 1.2 --power 0.9
.\run_sync.ps1                     # 同上，但会自己弹 UAC 提权
.\run_sync.ps1 -NoLaptop           # 不驱动笔记本，不需要管理员
```

**首次使用必须先注册 Clevo WMI（只需一次，管理员）**：

```powershell
powershell -ExecutionPolicy Bypass -File .\register_clevo.ps1
```

它会：拷 `CLEVOMOF.dll` 到 `SysWOW64` → 写 `WmiAcpi\MofImagePath` 注册表 →  
验证 `root\WMI` 里出现 `CLEVO_GET` 类 → 试开灯 / 左区红 / 中区绿。  
全部步骤写进 `%TEMP%\rgbsync\gate_clevo.txt`。

---

## 二、三个设备的协议（全部实测打通）

### 2.1 笔记本 — Clevo WMI

```
命名空间   root\WMI
类         CLEVO_GET
实例       ACPI\PNP0C14\0_0
方法       SetKBLED(uint32)
```

参数编码（**magic values 是从 clevomanager 逆向 + 实测验证的**）：

| 用途   | 值                                                                  |
| ---- | ------------------------------------------------------------------ |
| 开灯   | `0xE007F007`                                                       |
| 左区   | `0xF0000000` ｜ 中区 `0xF1000000` ｜ 右区 `0xF2000000` ｜ 灯条 `0xF3000000` |
| 颜色打包 | `(B<<16) + (R<<8) + G`，与分区值按位或                                     |

例：`0xF000FF00` = 左区纯红。**调 SetKBLED 必须管理员**，否则 `Access denied`。

**本机实测**：这台 X15 AT23 只有**全局单色**，分区值都作用在同一片，所以适配器按单区处理。

代码：`rgbsync/adapters/clevo.py` + `clevo_bridge.ps1`  
（Python 通过常驻 PowerShell 子进程下发，避免每次调用都重启 PS）

### 2.2 键盘 — 狼蛛 HERO 68 HE

```
VID:PID        0x372E:0x103E
接口           usage_page 0xFF60（MI_02 & Col01）
报文           HID output report，report id 9，64 字节
校验和         chk = (0xF6 - sum(payload[0:63])) & 0xFF
```

**逐键上色（自定义模式）**

```
前置  0x01/0x80 [0x10]              进入自定义
      0x04/0x02 [0x13, 0x00]        效果槽 19 = 自定义
      0x04/0x04 [0x64, 0x04]        亮度 / 速度
逐键  0x06/0x00 [kid_hi, kid_lo, R, G, B] × ≤11 键
提交  0x01/0x00 []
```

> ⚠️ **一个包只解析第一个色段**。把多个 `[R,G,B,count,ids...]` 塞进一个 56 字节包，  
> 固件只认第一段 —— 曾经导致"67 键只有 7 个变色"。所以：**一段一包**。

**固件灯效（cmd `0x04`）**：payload `[效果ID, 0x00, R, G, B, 0x14, 0x01]`

| 效果   | ID | 效果   | ID | 效果   | ID |
| ---- | -- | ---- | -- | ---- | -- |
| 常亮   | 01 | 涟漪扩散 | 07 | 正弦光波 | 0d |
| 呼吸   | 02 | 繁星点点 | 08 | 左右扫描 | 0e |
| 梦幻彩虹 | 03 | 踏雪无痕 | 09 | 旋转风车 | 0f |
| 一触即发 | 04 | 川流不息 | 0a | 七彩瀑布 | 10 |
| 雨中漫步 | 05 | 随波逐流 | 0b | 花开富贵 | 11 |
| 彩虹转轮 | 06 | 如影随形 | 0c | 旋转风暴 | 12 |
|      |    |      |    | 自定义  | 13 |

**键位映射（关键）**

设备 UID = `110000000003`。AULA 客户端 `configs/keyboard_uid.py` 的  
`ButtonLayout.get_layout_or_none(uid)` 返回的是

```python
{"k1": 0.0555, "k15": 0.1666, "k16": 0.2777, ...}   # 67 项
```

**这是一个 dict，value 就是该键的横向位置 0..1**。

> ⚠️ **不要把它当"二维行列列表"遍历**。那样会把 key 字符串拆成字符  
> （`"k15"` → `'k','1','5'`），导致 `pos.get(15)` 全部兜底成 `0.5`，  
> **整把键盘一个颜色**。这是曾经"灯效和预览对不上"的真正原因。

代码：`rgbsync/adapters/hero68.py`

### 2.3 耳机 — 迈从 V9 Turbo+

同一副耳机在系统里是**两台 USB 设备**：

| 地址              | 角色            | 说明                                                            |
| --------------- | ------------- | ------------------------------------------------------------- |
| `0x3837:0x6008` | **WCH 主控，灯控** | product `V9 Turbo`，mfr `wch.cn`，唯一 HID 集合 `usage_page 0xFF00` |
| `0x3837:0x600A` | C-Media 声卡    | 7.1 音频 + 电话 HID，**与灯无关，别碰**                                   |

**报文（64 字节，第一字节是 report id `0xAA`）**

```
aa 27 00 00 00 0a 01 <mode> 05 <dir> <bright> 00 <R> <G> <B> 00 …
^^                      ^^       ^^        ^^
report id               [7]      [9]       [10]         [12..14]
```

`mode`：`0` 常亮 / `1` 呼吸 / `2` 繁星 / `3` 循环 / `4` 随波  
命令：`0x07` 状态查询（回包带 `V9 Turbo` 和当前 RGB）、`0x27` 设色

**Windows 层两个致命细节**

1. `HidD_SetOutputReport` 需要 **65 字节 buffer 并丢弃第 0 字节**，  
   所以 buffer 必须是 `[0x00] + <64 字节报文>`。发 63/64 一律 `err 87`。
2. **读写必须用同一个 hidapi 句柄**，且每次写完排空 ACK。  
   用 `CreateFileW`+`HidD` 写入、另一句柄读取时，设备的 ACK 会积压在被写句柄的队列里，  
   队列一满固件**静默拒收新帧**（API 仍返回 True）。  
   症状极具误导性：**单次写、间隔 2.5 秒有效；连续写、间隔 0.4 秒完全不动**。

代码：`rgbsync/adapters/mchose.py`

**客观验证手段**：`0x07` 查询的回包里带设备当前 RGB，设什么就回什么 ——  
不靠肉眼就能确认写入是否被应用。

---

## 三、抓包与逆向的方法（以后加设备照这个走）

### USBPcap 解析（`usb_pcap_parse.py`）

> ⚠️ **`headerLen` 字段是 27（control 是 28），绝对不能硬编码**。  
> 硬编码 28 会吞掉每条中断报文的第一个字节 —— 耳机那次正好把  
> **report id `0xAA`** 吃掉了，于是长期误判成"report id 就是 0、线上 63 字节"。

字段偏移（实测）：`status@10`、`function@14`、`info@16`、`bus@17`、`dev@19`、  
`ep@21`、`xfer@22`、`dlen@23`，payload 从 `headerLen` 开始。

### 抓包流程

```powershell
.\run_capture.ps1        # 自提权，先静默 8 秒再录操作段，用差集找命令
```

`capture_mchose.py` 会自动比对 idle / active 两段，只出现在操作段的报文就是目标命令。

### WebHID 抓包（`webhid_capture_v2.py`）

网页驱动（AULA A HUB、MCHOSE WEB）都是纯 WebHID，用 Playwright 注入钩子抓  
`navigator.hid` 的 `sendReport`，比逆二进制快得多。浏览器被关会自动重启继续抓。

### 官方客户端逆向

AULA 官方 `AULA_CTRL` 是 **PyInstaller 打包的 Python**，`configs/*.py` 是**明文**：

```
C:\Program Files (x86)\AULA\AULA_CTRL\configs\keyboard_uid.py    # 键位表
C:\Program Files (x86)\AULA\AULA_CTRL\configs\device_layout.py   # 设备布局
```

exe 用 `pyinstxtractor-ng` 解包后，`aula_decompile.py` 可以反汇编 pyc 看函数常量表  
（`send_packet`、`set_custom_colors`、`sync_send` 都是这么挖出来的）。

---

## 四、代码结构与文件职责

```
rgbsync/
  core.py                 效果引擎、位置轴、帧模型、统一时钟、fps 统计
  effects.py              效果注册表（CLI 与 GUI 共用，含中文名/说明）
  main.py                 命令行入口
  adapters/
    base.py               适配器抽象：zones / span / global_positions / apply
    clevo.py              笔记本（Clevo WMI，PowerShell 桥接 + 后台线程）
    clevo_bridge.ps1      常驻桥接：stdin 收十六进制 → SetKBLED
    hero68.py             键盘（逐键 cmd 0x06，一段一包，vendor 键位表）
    mchose.py             耳机（report id 0xAA，单句柄读写 + 排空 ACK）
gui/
  server.py               控制台后端：标准库 http.server + JSON API
  launch.py               启动器：自提权、清理旧实例、开 --app 窗口、关窗口退出
  ui/index.html           单页界面（暗色玻璃 + 桌面实时预览）
启动神光同步.bat           双击入口
register_clevo.ps1        Clevo WMI 注册 + GATE 自测（管理员，只需一次）
run_sync.ps1              命令行一键提权启动
device_check.py           三设备连通性自检
kill_mchose.py            彻底关掉 M HUB（含守护进程，管理员）
hero68_final.py           键盘单设备验证（solid / grad / wave）
mchose_fix.py             耳机最小验证脚本，怀疑协议时先跑它
usb_pcap_parse.py         USBPcap .pcap 解析
webhid_capture_v2.py      WebHID 抓包工具（Playwright）
```

### 位置轴（span）

每个适配器有一个 `span = (start, end)`，表示它在桌面上的物理区间。引擎把所有 LED  
映射到 0..1 的轴上：

```python
global_x = span[0] + local_x * (span[1] - span[0])
```

单区设备取 `span` 中点。所以一条色带可以从耳机 → 笔记本 → 键盘依次流过。

### 效果清单（10 个）

三连流 / 极光 / 双色流转 / 流光 / 波浪 / 彗星 / 七彩同步 / 色相渐变 / 呼吸 / 常亮

`TrioFlow` 的 `span=2.5` 让键盘正好跨**一整条彩虹**（红→紫→红），键盘内部同时有多个颜色。

---

## 五、环境与依赖

| 项      | 值                                                                       |
| ------ | ----------------------------------------------------------------------- |
| Python | 3.12.1（`C:\Users\Lie\AppData\Local\Programs\Python\Python312`）          |
| 必需库    | `hidapi`（HID 读写）、`playwright`（仅抓包用）                                     |
| 本机代理   | `http_proxy=http://127.0.0.1:2788`，**访问 127.0.0.1 必须加 `--noproxy '*'`** |
| 权限     | 笔记本 WMI 需要管理员；键盘/耳机不需要                                                  |
| M HUB  | 会和本程序抢设备（每 200ms 重推自己的灯效），启动时自动关                                        |

**已知坑（新接手务必先读）**

1. **USBPcap `headerLen` 是 27**，不是 28（见第三章）
2. **HID 双句柄导致 ACK 积压**（见 2.3）
3. **AULA 布局是 dict**，不是二维列表（见 2.2）
4. **PowerShell 5.1 按 GBK 解码无 BOM 的 UTF-8 脚本**，中文路径会变乱码 ——  
   脚本里用 `$PSScriptRoot` 定位，不要硬编码含中文的绝对路径
5. **同一文件不要并行发两个 Edit**，后写的会覆盖前一个
6. `Start-Process -Verb RunAs` 的 target 是 powershell/cmd 会被安全策略拦，**python 可以**
7. Chrome/Edge 以管理员 token 启动会拒绝，`--app=` 模式实测可用
8. 设备断电前会**保持最后一次设置**，所以"没打开程序灯还亮着"是正常的，不是后台进程

---

## 六、还没做的

- **音频律动**：`core.AudioReactive` 框架在，缺麦克风采样接入
- **笔记本固件模式同步**：现在只用来显示单色，没用 Clevo 自带的 Cycle/Wave 模式
- **耳机预置模式**：`0x27` 的 `mode` 字段已验证，GUI 里没暴露（都走常亮 + 逐帧改色）
- **配置持久化**：效果/速度/亮度不记忆，每次启动是默认值
- **多设备扩展**：加新设备只要写一个 `Adapter` 子类，设好 `span` 和 `apply` 即可

---

## 七、验证清单（改动后自查）

```powershell
# 1) 三设备能否打开
python device_check.py

# 2) 键盘单独跑（不用管理员）
python hero68_final.py wave 8

# 3) 耳机单独跑（怀疑协议时）
python mchose_fix.py

# 4) 引擎跑 10 秒看 fps 与丢帧
python rgbsync\main.py --effect trio --seconds 10

# 5) GUI 后端单独起（不开窗口）
python -m gui.server --port 8790 --no-open
```

正常指标：**29.6–29.7 fps**，笔记本 `thr=0`，耳机 15 Hz（`thr` 是节流计数，不是丢帧）。

---

## 八、原生版（2026-10-23 接续，推荐入口）

旧版「Python + 本地网页」已由 **
ative/ 下的 C# 原生单文件应用**替代，默认入口变成项目根目录的
**PrismSync.exe**（也生成了桌面快捷方式）。完整说明见 **[PrismSync说明.md](PrismSync说明.md)**，旧代码保留作协议档案。

本次接续确认/修正的关键点（与上文不一致时以这里为准）：

1. **键盘是 68 键**（上文 67 键少算了 LED id 76 = ← 键；id 72 = Fn 键，固件不上报键值但灯可亮）。
   68 键物理布局 = 5 行 15/15/14/14/10，右侧一列 Ins/Del/PgUp/PgDn。
   真正可靠的映射来自 
ative/tools/keycal/键盘校准.exe 的逐颗点亮 + 用户按键，已固化进
   
ative/src/Core/KeyLayout.cs 与 
ative/keymap/keymap.json。
2. **耳机必须用 WriteFile 发 65 字节**（[0x00] + 64 字节报文）。
   HidD_SetOutputReport 会返回 True 甚至回 ACK，但固件不执行 —— 之前"耳机不受控"就是这个假成功。
   可用  x07 查询回包  0 AA 07 … [13..15]=RGB 客观验证。
3. **键盘闪烁 = 每帧色包数量超过固件单帧处理能力**：
   - cmd 0x06（一包 11 键）连续刷新会整把闪（纯白也闪）；
   - cmd 0x08 整帧 68 个色包不闪但会丢尾包 → 颜色错；
   - **cmd 0x08 整帧 + HSV 量化到 ≤24 色 + 30fps 不闪且颜色正确**（现方案）；
   - 只发一次后键盘会自己保持颜色，说明固件有保持能力。
4. 笔记本改为 .NET System.Management 直接调 WMI（不再 spawn PowerShell 桥接），单槽位异步下发。
5. 原生版自带屏幕氛围 / 音频律动 / 击键涟漪三个新效果（Raw Input + GDI 屏采 + winmm 麦克风）。

构建：pwsh -NoProfile -ExecutionPolicy Bypass -File native\build.ps1
（必须用 PowerShell 7；5.1 会按 GBK 解码无 BOM UTF-8 脚本，中文路径会坏。）