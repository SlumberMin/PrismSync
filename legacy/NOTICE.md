# legacy/ — 旧版 Python + 本地网页实现

这里的代码是原项目在改成 C# 原生版之前的实现，**保留作为协议参考，不再是默认入口**。
它包含：

- `gui/`：标准库 HTTP 服务 + 单页 HTML 界面（就是被替掉的「HTML 套壳」）
- `rgbsync/`：Python 效果引擎与三个适配器
- `README.md`：旧版说明 + 大量协议细节
- `usb_pcap_parse.py` / `webhid_capture_v2.py`：抓包工具
- `register_clevo.ps1` / `run_sync.ps1`：Clevo WMI 注册与提权启动
- 其它单设备验证脚本

运行旧版（可选）：

```powershell
pip install hidapi
python gui\launch.py
```

注意：

- **`CLEVOMOF.dll` 没有包含在仓库里**（厂商二进制，不便再分发）。`register_clevo.ps1` 需要它时，
  请从你自己的 Clevo/七彩虹官方驱动包里获取，或先把厂商控制中心装好再提取。
- 旧版依赖 Python 3.12 + hidapi；原生版不需要任何运行时。
- 协议结论以 [../docs/HANDOFF.md](../docs/HANDOFF.md) 为准；键盘 68 键、耳机 WriteFile、键盘包数上限等
  新结论是后来用 C# 版重新验证的，旧 Python 代码里对应位置可能是错的。
