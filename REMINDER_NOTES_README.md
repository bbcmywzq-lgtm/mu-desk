# Mujun Memo · 随记提醒

提醒和便签是同一种“随记”。Memo 由 MU Desk 统一管理，保留 ReminderNotes 工作进程负责数据、调度和通知，Pal 可调用它。改名不改变可执行文件、通信协议或数据目录。

## 主要能力

- 一条随记可随时增加或取消提醒时间，不需要在“便签/提醒”之间搬运。
- 随记可以贴到桌面；卡片本身可直接编辑并自动保存，低频操作收在悬停工具和右键菜单中。
- 桌面卡片支持拖动、缩放、锁定、置顶、折叠、颜色、透明度、屏幕/便签吸附，并会记住位置。
- 管理中心采用分类栏、便签列表与编辑区，可统一显示、隐藏、自动排列或救回屏幕外便签。
- 关闭桌面卡片只会取消贴桌面，不会删除随记。
- 提醒支持稍后 10 分钟、稍后 1 小时、完成，以及每天、工作日、每周重复。
- 多条同时到期时逐条显示，不会互相覆盖；重启后会恢复漏掉的提醒。
- 支持搜索、标签、收藏、完成与归档。数据保存在本机 JSON 文件中。

- 单独运行：`run-remindernotes.ps1`
- 单独打包：`package-remindernotes.ps1`
- 后台启动：`ReminderNotes.exe --background`
- 打开提醒页：`ReminderNotes.exe --reminder`
- 打开便签页：`ReminderNotes.exe --note`
- 直接创建随记：`ReminderNotes.exe --create-note "内容"`
- 直接创建提醒：`ReminderNotes.exe --create-reminder "内容" --at "2026-08-24 09:00"`
- 创建后贴到桌面：在以上命令后增加 `--pin`
- 数据目录：`%LocalAppData%\ReminderNotes\data`

程序首次启动会把旧的 `reminders.json` 和 `notes.json` 无损迁移为 `entries.json`，同时保留迁移前备份。桌宠和工具箱只传入页面或创建命令；提醒调度、桌面卡片、界面和存储均由本工具负责。
