# 贡献 / 二改指南

欢迎 fork、改效果、适配新设备。

## 环境

- Windows 10/11
- PowerShell 7（`pwsh`）
- 系统自带 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`（不需要 .NET SDK）
- 语言限制：**C# 5**（csc 4.x 默认）——不能用字符串插值、`?.`、表达式体、`nameof`、自动属性初始化等新语法。

## 构建与运行

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File native\build.ps1
```

## 加一个效果（最短路径）

1. 在 `native/src/Core/Effects.cs` 写一个类，继承 `SpatialEffect`（纯空间效果）或直接实现 `IEffect`（有状态/需要采样）。
2. 在 `native/src/Core/EffectRegistry.cs` 的 `Specs` 里加一条 `EffectSpec`，并在 `Create()` 里 new 出来。
3. 重新编译，界面会自动多出一项（左侧列表和预览都吃同一份注册表）。

## 改写入策略前请先读

- 耳机：`HidD_SetOutputReport` 是假成功，必须 `WriteFile` 65 字节（见 `MchoseDevice.cs` 注释）。
- 键盘：整帧色包数要 ≤ 约 24，超过会丢包/错色；`cmd 0x06` 连续刷新会闪。先用
  `native/tools/keytest/键盘写入测试.exe` 验证再改。
- 笔记本：`CLEVO_GET.SetKBLED` 需要管理员；不要在主线程同步调用 WMI。

## 提交

- 一个 PR 一件事，说明动机和验证方式（最好附截图/日志）。
- 不要提交 `native/dist/`、`__pycache__`、个人配置。
- 涉及新硬件的协议，请把抓包/校验方法一并写清楚，方便别人复现。

## 免责声明

逆向研究仅供学习；请遵守当地法律与厂商条款。
