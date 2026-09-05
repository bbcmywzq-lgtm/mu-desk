# MU Desk GitHub 小工具侦察（2026-08-22）

## 目标

继续寻找适合用 vibecoding 快速做成 MU Desk 内部模块或快捷动作的 Windows 小工具。筛选优先级：本地优先、功能边界窄、无需管理员权限或常驻服务、适合现有 C# / WPF / .NET 10 技术栈，并且不与现有桌面整理、快捷轮盘、光标画廊、动态拾取、CISP 题库、临时货架重复。

## 首选候选

| 顺位 | MU Desk 方向 | GitHub 参考 | 建议首版 | 适配度 | 粗略难度 |
|---|---|---|---|---:|---:|
| 1 | 选区识字 | [Text Grab](https://github.com/TheJoeFin/Text-Grab)、[Snapboard](https://github.com/Flowdesktech/snapboard) | 框选屏幕 → 本地 WinRT OCR → 结果预览、复制、去空行；不做翻译和批处理 | 5/5 | 2/5 |
| 2 | 窗口图钉 | [Pin-It](https://github.com/Razee4315/Pin-It)、[PowerToys Always on Top](https://github.com/microsoft/PowerToys) | 对当前窗口切换置顶，附 30%–100% 透明度；先不做重启恢复 | 5/5 | 1.5/5 |
| 3 | 剪贴工坊 | [PowerToys Advanced Paste](https://github.com/microsoft/PowerToys)、[ClickPaste](https://github.com/Collective-Software/ClickPaste)、[PasteLab](https://github.com/DrextenMax/Pastelab) | 用户按快捷键后读取当前剪贴板；纯文本粘贴、去空行、大小写、JSON 美化、URL/Base64 编解码；首版不保存历史 | 5/5 | 2/5 |
| 4 | 屏幕量具 | [Snapboard](https://github.com/Flowdesktech/snapboard) | 同一入口放取色器、像素尺、选区 QR/条码识别，复用现有框选和截图基础设施 | 4.5/5 | 2/5 |
| 5 | 操作字幕 | [Carnac](https://github.com/Code52/carnac)、[keyviz](https://github.com/mulaRahul/keyviz) | 屏幕角落显示最近快捷键；默认只显示组合键，不记录普通文字和密码输入 | 4/5 | 2/5 |
| 6 | 保持清醒 | [PowerToys Awake](https://github.com/microsoft/PowerToys)、[Insonnia](https://github.com/iupsilon/Insonnia) | 保持 30 分钟、1 小时、直到指定时间或无限；可选保持屏幕常亮 | 4/5 | 1/5 |
| 7 | 逐字粘贴 | [ClickPaste](https://github.com/Collective-Software/ClickPaste) | 把当前剪贴板文本模拟成按键，解决远程桌面、旧程序或禁止普通粘贴的输入框；长文本二次确认 | 4/5 | 1.5/5 |
| 8 | 窗口切片 | [OnTopReplica](https://github.com/LorenzCK/OnTopReplica) | 把某个窗口或其局部做成实时置顶小窗，支持穿透和透明度 | 4/5 | 3/5 |
| 9 | 虚拟桌面投送 | [VirtualDesktopUtils](https://github.com/woanware/VirtualDesktopUtils) | 弹出 1–9 桌面选择器，把当前窗口移动过去；首版不做跨重启记忆 | 3/5 | 3.5/5 |
| 10 | 护眼提醒 | [EyeRest](https://github.com/necdetsanli/EyeRest) | 20-20-20 定时提醒，会议/全屏时静默，可从托盘或快捷轮盘延后 | 3/5 | 1/5 |
| 11 | 电池守门员 | [BatteryNotifier](https://github.com/Sandip124/BatteryNotifier) | 笔记本低电量/充满阈值提醒；桌面机自动隐藏该功能 | 3/5 | 2/5 |
| 12 | 空格预览 | [QuickLook](https://github.com/QL-Win/QuickLook)、[WinQuickLook](https://github.com/polymind-inc/WinQuickLook) | 只预览图片、纯文本、Markdown 和常见媒体，不追求 100+ 格式 | 2.5/5 | 4.5/5 |

## 推荐的下一批

### A. 选区识字

最适合先做。MU Desk 已经有屏幕框选、截图、多显示器/DPI 处理和全局快捷键，新增核心只剩 Windows OCR、文本排序与结果窗。Text Grab 的 Full-Screen Grab 正是 PowerToys Text Extractor 的来源；Snapboard 则给出了与当前工程几乎相同的 WPF / .NET 10 分层参考。

首版边界：只在用户主动框选后 OCR；图片不落盘、不联网；识别后显示原文并提供复制、单行化、去空行三个动作。

### B. 窗口图钉

功能极窄但日常使用频率高，也很适合放入快捷轮盘。核心是获取前台窗口、切换 `HWND_TOPMOST` 和分层窗口透明度。先不做 Pin-It 的重启恢复和持续纠正，能把风险和状态管理压到很低。

### C. 剪贴工坊

建议做“显式调用的剪贴板变换器”，而不是常驻剪贴板历史。这样既能覆盖纯文本粘贴、JSON 美化、URL/Base64 编解码等高频动作，又不会长期保存密码、验证码、令牌和隐私文本。

## 模块收纳建议

不要把每个小能力都变成首页卡片：

- 动态拾取可逐步扩成“屏幕工具”，容纳选区识字、取色、像素尺、QR 识别；动态录制仍是其中一个明确动作。
- 快捷轮盘适合增加“窗口图钉”“保持清醒”“逐字粘贴”等瞬时动作。
- 剪贴工坊需要预览、撤销和多个转换，适合一个按需窗口和一张首页卡片。
- 操作字幕、护眼提醒、电池守门员属于后台开关，只有在用户确实会长期用时才值得占首页位置。

## 暂不建议

- 完整文件搜索/启动器（如 Lertaro）：需要 NTFS MFT、USN Journal、服务和索引体系，已不属于小工具。
- 完整 QuickLook 克隆：格式渲染器和 Explorer/Open Dialog 兼容会迅速膨胀；若做，只应做四种常用格式。
- 完整截图套件：与动态拾取及 Windows 截图能力重叠；只吸收 OCR、取色、尺子、QR 这些原子能力。
- 完整剪贴板历史：敏感信息持久化、格式兼容、去重与搜索都会扩大隐私和可靠性边界。
- 桌面小组件集合（如 DeskBox）：与现有桌面整理和临时货架重叠，容易把 MU Desk 变成第二个桌面壳。

## 许可证边界

- 较适合阅读并在保留必要声明的前提下借鉴实现：[Text Grab](https://github.com/TheJoeFin/Text-Grab) MIT、[Snapboard](https://github.com/Flowdesktech/snapboard) MIT、[PowerToys](https://github.com/microsoft/PowerToys) MIT、[VirtualDesktopUtils](https://github.com/woanware/VirtualDesktopUtils) MIT、[EyeRest](https://github.com/necdetsanli/EyeRest) MIT、[BatteryNotifier](https://github.com/Sandip124/BatteryNotifier) MIT、[Pin-It](https://github.com/Razee4315/Pin-It) Apache-2.0、[ClickPaste](https://github.com/Collective-Software/ClickPaste) BSD-3-Clause。
- 只借鉴产品思路，除非单独完成许可证评估：[QuickLook](https://github.com/QL-Win/QuickLook) GPL-3.0、[keyviz](https://github.com/mulaRahul/keyviz) GPL-3.0、[OnTopReplica](https://github.com/LorenzCK/OnTopReplica) MS-RL、[Carnac](https://github.com/Code52/carnac) MS-PL。
- 即使是宽松许可证，也不直接搬运 UI、图标、品牌、截图或未核对来源的第三方资源；正式实现前再次核对具体文件与依赖许可证。

## 结论

下一轮最值得按顺序尝试的是：`选区识字` → `窗口图钉` → `剪贴工坊`。三者都能明显补齐 MU Desk 当前空位，首版不需要服务、数据库、Shell 扩展或新进程，也能最大程度复用现有框选、热键、托盘、设置和模块生命周期。
