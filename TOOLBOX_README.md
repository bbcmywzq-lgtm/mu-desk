# MU Desk

用一个入口管理本机桌面工具；轻量模块可以内置，具有独立后台职责的工具通过进程接口调用。

## 当前版本

- Mujun Orbit · 快捷轮盘（原四向轮盘，直接运行在工具箱进程中）
- Mujun Cue · 屏幕讲解（聚焦、指示、聚光、局部放大、标注与截屏）
- Mujun Clip · 动态拾取（短录屏与精选关键帧）
- Mujun Tip · 光标装扮（原光标画廊，浏览并应用本机皮肤）
- Mujun Grid · 桌面整理（原栖格，保留原布局、规则和设置）
- Mujun Drop · 文件暂存（原临时货架，保存路径，不移动原文件）
- Mujun Memo · 随记提醒（便签、提醒与桌面卡片，由原 ReminderNotes 工作进程托管）
- Mujun Pal · 桌面伙伴（由原 LightPet 工作进程托管）
- CISP 题库（名称不变，包含练习与错题本）
- 关闭主窗口只会收进托盘；双击托盘图标或再次运行工具箱可重新打开
- 开机启动项只有 `PersonalToolbox`
- Tip 按需打开，关闭窗口后不会留下额外后台进程

普通界面使用英文短名与中文功能说明，完整名称使用 Mujun 前缀。名称调整不改变可执行文件名、启动项、配置键、管道或数据目录；不需要迁移已有数据。

## 使用

```powershell
.\build-toolbox.ps1
.\publish-toolbox.ps1
.\run-toolbox.ps1
```

发布文件位于：

```text
artifacts\PersonalToolbox-win-x64\PersonalToolbox.exe
```

## 模块约定

模块实现 `Toolbox.Core.IToolModule`，由宿主统一调用：

- `Start` / `Stop`：加载或卸载后台能力
- `SetPaused`：响应“暂停所有工具”
- `OpenSettings`：打开模块自己的配置页
- `StateChanged` / `Error`：把状态和错误交给宿主窗口与托盘显示

Orbit 仍保留兼容开发入口 `MouseRing.exe`，与工具箱模块共享运行锁，不会同时安装两套鼠标钩子。

## Mujun Cue

局部放大镜现采用 GPU 实时捕获与合成；全屏聚焦使用独立线程和连续镜头控制，倍率、位移与中途操作一起平滑处理。性能测量与已知验证边界见 `product-specs/mu-desk/modules/mujun-cue/gpu-rendering-validation.md`。

在 MU Desk 中启用后，长按 `Caps Lock` 会以鼠标所在位置为中心临时放大，松开即平滑复原；短按仍保持原本的大小写切换。聚焦期间滚动滚轮可调整倍率，默认 `2×`，范围 `1.25×–5×`。工具栏还提供鼠标圆环、激光笔、点击脉冲、聚光灯、局部放大镜、屏幕标注、当前屏幕截取与区域截取。

紧急复原可使用连续三次 `Esc` 或 `Ctrl+Alt+Esc`，也可以从托盘执行“复原 Mujun Cue”。截图默认同时写入剪贴板和“图片\Mujun Cue”。
