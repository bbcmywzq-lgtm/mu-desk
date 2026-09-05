# Mujun Cue 系统包装

Parent SKU: `MU-DESK`  
Module ID: `mujun-cue`  
Gate: System Packaging — accepted 2026-09-05

## Ownership

- 显示身份：`Mujun Cue` 子软件；父产品和唯一 Windows 包身份：`MU Desk`。
- 不生成 Cue 独立 EXE、托盘、启动项、安装包、版本、更新器或 About。
- 由 MU Desk 启动、暂停、恢复、关闭和回收全部钩子、覆盖层与捕获资源。
- 首页、MU Desk 托盘子菜单、统一设置和可选快捷轮盘动作是普通入口。

## Settings and data

- 设置进入现有 `ToolboxSettings`，由 `%LocalAppData%\MU Desk` 下的统一设置文件持久化和迁移。
- 截图默认保存到 `Pictures\Mujun Cue`，用户可在统一设置中更改；写入失败必须显示真实错误。
- 画板项目在第二阶段定义独立版本化格式；第一阶段不创建该数据目录。
- 无账号、遥测、网络访问、屏幕上传、隐藏更新检查或管理员权限要求。

## Runtime and recovery

- 只允许一个 Cue 模块实例、一个全局键盘钩子和每显示器一个覆盖表面。
- 空闲时不持续捕获屏幕；局部放大镜开启时才运行帧刷新。
- MU Desk“暂停所有工具”关闭 Cue 触发与可见覆盖；恢复后回到待命，不自动重新打开临时效果。
- `Esc` 连按三次、`Ctrl+Alt+Esc`、托盘“全部复原”和进程退出均恢复 1×、释放输入并关闭覆盖层。
- 退出或模块故障不能影响 MU Desk 其他模块；错误进入统一托盘错误出口。

## Compatibility

- Windows 10/11 x64，继承 MU Desk 当前 .NET 10 WPF 发布。
- 支持普通桌面、浏览器、Office、聊天/会议软件和普通全屏窗口。
- 不承诺 DRM、独占全屏游戏、反作弊、高权限窗口或第三方单窗口捕获合成。

## Accepted basis

- 用户明确把 Mujun Cue 定义为 Desk 总包内的子软件，并要求直接开始开发。
- 现有已集成模块和数据保持不变，不因本次接入回滚或迁移。
