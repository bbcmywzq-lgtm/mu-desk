# MU Desk

MU Desk 是一个 Windows 桌面工具箱，用一个入口承载多个日常小工具。它不是单独的 Grid 文档，而是整个桌面工具包的源码仓库。

当前工具箱包含屏幕讲解、快捷轮盘、短录屏、光标装扮、桌面整理、文件暂存、随记提醒、桌面伙伴和 CISP 题库。总包名称是 `MU Desk`；模块在界面里使用短英文名，完整称呼保留 `Mujun` 前缀。

## 模块

| 模块 | 完整名称 | 功能 |
| --- | --- | --- |
| Cue | Mujun Cue | 屏幕聚焦、鼠标指示、聚光、局部放大、标注和截图。 |
| Orbit | Mujun Orbit | 鼠标快捷轮盘，用于快速触发常用操作。 |
| Clip | Mujun Clip | 短录屏与精选关键帧捕获。 |
| Tip | Mujun Tip | 本机光标皮肤浏览与应用。 |
| Grid | Mujun Grid | 桌面分区、智能文件夹、整理规则、搜索、备份与恢复。 |
| Drop | Mujun Drop | 文件临时暂存，只保存路径引用，不移动原文件。 |
| Memo | Mujun Memo | 便签、提醒与桌面卡片，由 ReminderNotes 工作进程托管。 |
| Pal | Mujun Pal | 桌面伙伴，由 LightPet 工作进程托管。 |
| CISP | CISP 题库 | 题目练习与错题复习。 |

## 总包特性

- 一个主窗口和一个通知区域图标管理全部工具。
- 开机启动项统一为 `PersonalToolbox`。
- 关闭主窗口后收进托盘，双击托盘图标或再次运行工具箱可重新打开。
- 轻量模块直接运行在宿主进程中；需要独立后台职责的模块通过工作进程接入。
- MU 命名调整不会改变已有模块的数据目录、配置键、管道和兼容入口。
- Cue 的放大镜采用 GPU 实时捕获与合成，临时聚焦使用连续镜头模型处理缩放和复原。

## 文档

- [TOOLBOX_README.md](TOOLBOX_README.md)：工具箱完整说明、模块约定和 Cue 细节。
- [GRID_README.md](GRID_README.md)：Mujun Grid 的完整桌面整理文档。
- [MOUSE_RING_README.md](MOUSE_RING_README.md)：Orbit / MouseRing 的兼容说明。
- [REMINDER_NOTES_README.md](REMINDER_NOTES_README.md)：Memo 工作进程说明。
- [LIGHTPET_README.md](LIGHTPET_README.md)：Pal 工作进程说明。

## 系统要求

- Windows 10 2004（Build 19041）或更高版本。
- Windows x64。
- 源码构建需要与 `global.json` 匹配的 .NET 10 SDK。

仓库不会上传本机 SDK 目录 `work/dotnet-sdk`。当前开发脚本会优先使用这一路径；如果机器已经全局安装 .NET 10 SDK，也可以直接运行：

```powershell
dotnet build PersonalToolbox.slnx -c Release
```

## 构建与运行

在带有 `work/dotnet-sdk/dotnet.exe` 的开发机上：

```powershell
.\build-toolbox.ps1
.\run-toolbox.ps1
```

生成 Windows x64 工具箱发布目录：

```powershell
.\publish-toolbox.ps1
```

主程序输出位置：

```text
artifacts\PersonalToolbox-win-x64\PersonalToolbox.exe
```

主解决方案：

```text
PersonalToolbox.slnx
```

## 仓库说明

本仓库保存源码和必要运行素材，不包含本机配置、账号信息、SDK、编译缓存、发布包和素材试验目录。

仓库内的第三方或参考素材不代表已经获得公开再分发许可。对外公开发布前，需要单独核对素材授权；当前仓库应按私有源码仓库处理。
