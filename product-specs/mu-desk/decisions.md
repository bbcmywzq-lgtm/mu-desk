# MU Desk decisions

## Mujun Cue subsoftware addition

Status: Product Definition, Visual System, System Packaging and Implementation Authorization accepted — 2026-09-05

- 用户将 `Mujun Cue` 定义为 `MU Desk` 总包内的子软件名称，而不是 MU Desk 的替代名称。
- Mujun Cue 负责实时屏幕讲解：鼠标指示、聚光灯、局部放大镜、全屏瞬时聚焦、屏幕标注、截图、画板和计时器。
- 第一阶段交付瞬时聚焦、鼠标指示、聚光灯、局部放大、标注和截图；画板与计时器属于第二阶段。
- Mujun Cue 可以拥有自己的模块卡片、模块图标和功能窗口标题；不建立独立托盘、启动项、安装包、更新通道、版本或 About 页面。
- MU Desk 继续统一进程生命周期、暂停/恢复、开机启动、设置、错误出口、数据根目录和 Windows x64 发布。
- 管理与设置界面继承 `light-outlined`；屏幕覆盖层和 HUD 继承 `dark-translucent`；不建立第二套视觉系统。
- 用户授权助手决定未逐项回答的默认参数，但没有授权开始实现。
- 用户在完整产品定义与父子产品关系澄清后明确要求“直接开始开发”；该指令接受当前定义、Desk 视觉继承、统一系统包装和 `modules/mujun-cue/implementation-map.json` 所列 Phase 1 实施范围。
- 该命名作为 MU Product Standard 的显式产品定义 override：允许内部模块使用 `Mujun Cue` 独立名称，但不扩张为独立系统包装身份。

Definition: `modules/mujun-cue/definition.md`

## Reminder Notes and Desktop Companion integration correction

Status: accepted and implementation-authorized — 2026-08-24

- 用户指出“随记在右下角微缩菜单栏里单开一个图标”以及“Pet 不在工具箱里”。
- 已确认两者违反既有 MU Desk 合同中的单一托盘、单一启动项和“桌面伙伴为内部模块”规则。
- 助手明确提出四项修正：随记托管时隐藏独立托盘；Pet 加入工具箱并提供启用、显示/隐藏和设置；MU Desk 统一开机启动与进程生命周期；两个重量不同的能力继续使用隔离工作进程。
- 用户随后明确回复“修改”，接受上述产品、视觉、系统包装和实施范围。
- 不改变随记数据格式、提醒调度规则、桌宠角色包、动画行为和现有独立开发启动器；只修正普通用户入口和托管边界。

Implementation map: `integration-reminder-pet-implementation-map.json`

## Product Definition gate

Status: accepted — 2026-08-22T00:20:09.3863978+08:00

Accepted decisions:

- 2026-08-21: 产品日常名称使用 `MU Desk`。
- 2026-08-21: 英文全称使用 `Modular Utility Desktop`。
- 2026-08-21: MU Desk 是唯一面向普通用户的总入口。
- 2026-08-21: 桌面整理、快捷轮盘、桌面伙伴作为内部模块；独立程序仅保留开发或兼容用途。

Corrections:

- 早期的“栖集、栖格、栖环”等命名方向已明确放弃。
- 不以创作者网名直接命名产品，也不为名称附加过度品牌故事。

Gate evidence:

- 2026-08-22 用户明确回复“接受 Gate A”。
- 本次实施范围为“光标画廊”，不合并快捷轮盘；光标画廊作为 MU Desk 内部按需模块单独定义。
- 兼容启动器仅保留开发或兼容用途，不作为普通用户产品入口。

## Visual System gate

Status: accepted — 2026-08-22T00:24:09.6044002+08:00

Accepted individual directions:

- 2026-08-21: 浅灰白、深色描边、紫色强调的轻量工具 UI 概念获得正面反馈。
- 2026-08-21: 图标家族采用黑白紫、扁平圆润、深色描边的总体方向。
- 2026-08-21: 不只制作主图标，内部模块和系统入口也必须拥有统一图标。

Corrections:

- 放弃精细发光的 3D 浮岛插图方向。
- 角色不应成为所有图标的共同主体。

Unresolved:

- 收敛首页图标和其他系统图标的 16–32 px 简化方式。
- 确认角色在首页、空状态、引导和关于页的具体出现范围。
- 确认是否需要额外插图以及每张插图的实际位置。

Gate evidence:

- 2026-08-22 用户明确回复“接受 Gate B”。
- 光标画廊使用 `light-outlined`，采用列表—详情结构，不使用角色插图。
- 光标画廊模块图标采用“光标箭头 + 画廊卡片”方向，正式图标待 Gate D 后生产。

### MU Desk application icon decision

Status: accepted — 2026-08-22

- 用户明确回复“KEYI”，接受现有 MU Desk 主图标概念作为正式方向。
- 主隐喻固定为“圆角统一窗口 + 三个工具模块 + 紫色承载底座”。
- 主图标用于 EXE、标题栏、任务栏、Alt+Tab、托盘和 About；模块入口继续使用各自功能图标。
- 不在主图标中加入角色头像、作者 ID、字母 `MU` 或额外品牌故事。

Implementation authorization: accepted — 2026-08-22T00:52:07.7056907+08:00

- 用户明确回复“接受主图标 Gate D”。
- 授权范围仅为 `mu-desk-app-icon-implementation-map.json` 列出的 EXE、窗口、任务栏、Alt+Tab 和托盘图标接入及重新发布。

Size correction — 2026-08-22:

- 用户提供任务栏截图并明确指出主图标“太小了”。
- 保持已接受的造型、颜色和隐喻不变；清除源图轻微绿幕色差，并把有效透明安全边距收紧为 7.5%，使主体居中且明显放大。
- v1 作为历史证据保留；正式接入资产切换到 `artifacts/mu-desk-app-icon-v2`。

Redesign correction — 2026-08-22:

- 用户提供第二张实机任务栏截图并指出新版“还是不对劲”，明确要求考虑重绘、重订。
- v1/v2 的共同问题被确认是小尺寸结构失效，而非单纯透明边距：细窗口框、三个内部格与底座同时争夺 16–32 px 空间，呈现为微缩 UI 截图而不是清晰的应用符号。
- 当前主图标方向撤回；既有文件作为历史和回退资产保留，不覆盖、不删除，也不作为新概念造型锚点。
- 视觉系统状态改为 `corrected`，后续 Gate C 与 Gate D 按 MU 流程回到 `pending`；在用户接受新方向前不生产或接入替代图标。
- 新视觉定位见 `app-icon-redesign-brief.md`。
- 已生成 `artifacts/mu-desk-app-icon-redesign-concepts-v1` 概念集并在 24 px 导出检查；A「Module Dock」语义和轮廓最稳定，B 易读成文件/窗口堆叠，C 易读成拼图或通用模块品牌。此判断是设计建议，不构成用户接受。
- 2026-08-22 用户明确回复 `A`，选择 Module Dock 作为主图标重订的母方向；该回复只接受母方向，不等于接受完整 Gate B，也不授权生产或接入。
- 已生成 `artifacts/mu-desk-app-icon-module-dock-concepts-v2`，包括 A1 开放托盘、A2 紫色底座、A3 完整应用块，以及三者的 24 px/100% 任务栏比较；尚待用户选择具体变体。
- 2026-08-22 用户在 A1/A2/A3 比较与 A3 推荐后回复 `KEYI`；记录为接受 A3「完整应用块」作为替代主图标方向。该回复不自动接受完整 Gate B，也不授权生产、接入或发布。
- 2026-08-22 用户随后明确要求“你就改吧，别一步步接收了”。记录为对本次窄范围主图标修订的合并接受与实施授权：A3 视觉方向、沿用既有系统包装、生产正式资产、替换既有图标接入点、构建、测试及本地发布；不授权 UI、模块行为或其他资产的顺带修改。
- A3 正式稿已完成：纯色紫色实体应用块、三个白色模块和深色 U 形底座；全尺寸 PNG 与九尺寸 ICO 通过 QA，源 ICO 与接入文件哈希一致，完整构建/测试/发布通过，发布 EXE 提取与实时主窗口标题栏检查均显示新版图标。
- 2026-08-22 用户再次提供真实任务栏对比，指出 A3 的方块相对相邻应用仍偏小，并要求不要再让用户逐次核对。修正为平台优先的尺寸特化：16–64 px ICO/PNG 条目裁到外轮廓并光学铺满位图；大尺寸资源保留标准呼吸边距。该修正不改变已接受的 A3 造型、颜色或语义。
- 补偿版重新发布并以 DPI 感知方式截取真实 Windows 任务栏；与相邻 Chrome、飞书、ChatGPT、网易云和 Edge 图标比较后，用户明确确认“现在大小差不多了”。当前 16–64 px 光学占比由此锁定。
- 此次实机结论已回写 MU Product Standard 1.0.1：以后 Windows 应用图标必须提供 16–64 px 尺寸特化，并由产品流程完成 DPI 感知的真实任务栏邻接比较，不把尺寸校验转嫁给用户。

## System Packaging gate

Status: accepted — 2026-08-22T00:27:27.7125461+08:00

Accepted individual directions:

- 2026-08-21: 一个 SKU 只向普通用户提供一个托盘入口、开机启动项和设置中心。

Gate evidence:

- 2026-08-22 用户明确回复“接受 Gate C”。
- 光标画廊运行在 MU Desk 进程内，只保留 MU Desk 首页和统一托盘入口。
- 元数据迁移到 `%LocalAppData%\MU Desk\cursor-gallery\library.json`；旧库与光标文件不自动删除。
- 模块没有独立设置、About、版本、安装包、更新通道或开机启动项。

## Implementation Authorization gate

Status: accepted — 2026-08-22T00:30:36.5201100+08:00

Authorized scope:

- 按已接受的 Product Definition、Visual System、System Packaging 和 `implementation-map.json` 改造 MU Desk 内的“光标画廊”。
- 生产并接入“光标箭头 + 画廊卡片”正式模块图标。
- 修改范围严格限制为实施映射列出的项目和文件；不顺带重构其他模块或统一整个壳层。

Gate evidence:

- 2026-08-22 用户明确回复“D”，接受 Gate D。

## Dynamic Capture module addition — Product Definition correction

Status: accepted — 2026-08-22T00:36:21.9238608+08:00

User direction received:

- 用户同意继续推进短时屏幕动态拾取工具，并要求使用现有工具技能确定开发风格。
- 用户认可默认约 2 秒、最长约 7 秒，把录像转为更适合 AI 理解的素材，而不是只依赖 GIF。
- 用户最初不要求发送步骤，随后明确修正为“要能有个按钮发给 Codex”。最新要求覆盖此前边界。

Draft decisions requiring complete Gate A acceptance:

- 新模块 ID 使用 `effect-capture`，面向用户的功能名暂定“动态拾取”。
- 模块属于 MU Desk，不建立独立产品、托盘、启动项、版本或安装包。
- 主任务是框选小范围、短时录制，并生成 MP4、关键帧、关键帧拼图与说明文件组成的本地素材包。
- 默认录制 2 秒、最长 7 秒；捕获优先保留快速动画细节，输出只保留少量有时间标记的关键帧。
- 首版只做通用屏幕捕获，不在首版实现浏览器 DOM/CSS/@keyframes 提取；网页增强作为后续独立范围重新决策。
- 首版不录音、不剪辑、不建立素材画廊；只有用户明确点击主按钮后，才把精选关键帧和分析提示提交给 Codex。
- “发送给 Codex”首版始终新建一个 Codex 任务，不增加任务选择器，也不自动向正在进行的任务插入消息。
- Codex 不可用、未登录或发送失败时，本地素材包仍然有效，并显示真实错误；不得把复制路径或仅启动应用伪装为发送成功。

Gate impact:

- 新模块改变 MU Desk 的模块拓扑，因此 Product Definition 由 accepted 变为 corrected。
- 按 MU Product Process，Visual System、System Packaging 与 Implementation Authorization 同步回到 pending。
- 既有光标画廊的已接受模块合同不作废；后续评审只补齐动态拾取带来的新增决策。

Gate evidence:

- 2026-08-22 用户明确回复“接受 Gate A”。
- 接受范围以 `modules/effect-capture/definition.md` 为准，包含显式“发送给 Codex”按钮和首版排除项。

## Dynamic Capture module — Visual System draft

Status: accepted — 2026-08-22T00:38:31.4092265+08:00

Proposed direction:

- 继承 MU Product Standard 1.0.0，不建立第二套色板或组件语言，也不使用 token override。
- MU Desk 入口与录制完成面板使用 `light-outlined`；全屏框选遮罩、倒计时和录制 HUD 使用 `dark-translucent`。
- 主完成动作是暮光紫实心按钮“发送给 Codex”；按钮附近持续显示将发送的图片数量和说明，点击本身即为明确同意。
- 录制边界和 HUD 使用文字、形状与计时共同表达状态，不只依赖红色。
- 完成面板以用户刚录制的效果为主视觉，不加入角色插图、装饰性插画或持续循环动画。
- 模块图标采用“框选角标 + 连续帧”隐喻，保持 MU 黑白紫、圆润深描边图标家族。
- 详细视觉与状态合同见 `modules/effect-capture/visual-system.md`。

Gate evidence:

- 2026-08-22 用户在 Gate B 提示后明确回复“B”。
- 接受完整视觉合同、无 token override、无角色插图，以及“框选角标 + 连续帧”模块图标方向。

## Dynamic Capture module — System Packaging draft

Status: accepted — 2026-08-22T00:43:52.4626452+08:00

Proposed direction:

- 动态拾取完全继承 MU Desk 的进程、托盘、开机启动、版本、安装包和 About，不建立独立品牌或常驻项。
- 普通入口为首页模块卡片、统一托盘菜单“动态拾取…”和快捷轮盘动作；独立全局快捷键默认不设置，避免系统和第三方录屏快捷键冲突。
- 本地包保存到 `%LocalAppData%\MU Desk\effect-capture\captures`，不会自动删除完整素材；临时残留可安全清理。
- “发送给 Codex”使用本机官方 `codex app-server`，复用 Codex 管理的 ChatGPT 登录，不在 MU Desk 内保存 API Key 或 OAuth token。
- 每次发送新建一个只读 Codex 任务，工作目录限制在该素材包；提交分析说明和关键帧，原始 MP4 默认不作为图片输入上传。
- 发送过程中关闭完成窗口不终止任务；退出整个 MU Desk 时若发送仍在进行，需要明确确认。
- 完整包装、隐私、错误与恢复合同见 `modules/effect-capture/system-packaging.md`。

Gate evidence:

- 2026-08-22 用户在 Gate C 提示后明确回复“C”。
- 接受完整系统包装合同，包括显式发送边界、Codex 管理登录、新建只读任务、本地数据保留和无独立后台身份。

## Dynamic Capture module — Implementation Authorization gate

Status: accepted — 2026-08-22T00:48:52.0238999+08:00

Review-ready scope:

- 仅实现 `effect-capture` 模块以及它在 MU Desk、Quick Ring、共享设置、托盘和 Codex App Server 中必需的连接点。
- 使用 Windows 原生 `Windows.Graphics.Capture`、Direct3D 11 和 Media Foundation；不捆绑 FFmpeg、浏览器扩展、视频编辑器或第二套 AI 客户端。
- 生产并接入已批准的“框选角标 + 连续帧”模块图标。
- 实施与验证顺序、风险、迁移和恢复计划见根目录 `implementation-map.json`。
- 旧的光标画廊实施映射已保存在 `modules/cursor-gallery/implementation-map.snapshot.json`，避免丢失历史授权范围。

Unresolved:

- 无。2026-08-22 用户在 Gate D 提示后明确回复“D”。

Authorized scope:

- 按根目录 `implementation-map.json` 实现动态拾取、必要的 MU Desk/Quick Ring/设置/托盘连接点以及正式模块图标。
- 执行构建、测试、原生录制验证、视觉与无障碍检查，并使用非敏感测试素材执行一次真实 Codex 发送。
- 不授权发布、删除用户数据、浏览器增强、完整编辑器、云端素材库或任何未列入实施映射的功能。

## Dynamic Capture — standalone test and MU Desk reintegration

Status: integrated — 2026-08-22

- 用户先要求将动态拾取从工具箱入口拆出，以独立测试宿主验证录制与 Codex 发送；测试配置和素材使用独立的 `%LocalAppData%\MU Dynamic Capture Test` 目录。
- 用户发现发送后无法确认 Codex 任务位置。实现补充 Codex 本地任务深链接：发送成功后自动打开对应任务，完成页保留“打开 Codex 任务”，测试宿主和 MU Desk 均提供最近任务入口。
- 用户随后明确要求“可以，放到工具箱吧”，恢复已接受的 MU Desk 首页、统一托盘、Quick Ring、可选全局快捷键、共享设置和退出确认连接点。
- `EffectCapture.TestApp` 与 `run-effect-capture-test.ps1` 仅作为开发/兼容测试启动器保留，不是普通用户入口、独立 SKU、独立托盘、启动项、更新器或安装身份；这符合既有系统包装合同，不重开 Gate。

## Dynamic Capture — visible Codex handoff correction

Status: implemented — 2026-08-22

- 用户确认 App Server 发送仍无法可靠进入可见的 Codex 任务，并明确要求“用点简单的办法”。
- 移除 App Server、后台任务创建、登录轮询、任务编号和“最近任务”入口。
- 完成页主按钮改为“在 Codex 中打开”，使用官方 `codex://new` 深链接，以素材包目录作为新任务工作区，并预填短动效分析提示。
- 官方深链接不会自动提交输入；最终发送由用户在可见的 Codex 输入框中执行。这是新的明确交接与同意边界。
- 本地素材包、MP4、关键帧和接触表格式不变；工具本身不再负责网络上传或 Codex 回执状态。
# Mistake Collector module — Product Definition correction

Status: integrated — direct implementation authorized 2026-08-22

User direction received:

- 用户要求“做一个简单的错题收集器，先支持把错题截图扔进去，后面再做别的”。
- 用户随后修正：“每次是一组截图，我可能一张张加进去”，并明确要求“不要定义了，快做”。

Draft decisions requiring complete Gate A acceptance:

- 新模块 ID 使用 `mistake-collector`，面向用户的功能名为“错题收集”。
- 模块属于 MU Desk，不建立独立产品、进程、托盘、启动项、版本或安装包。
- 首版只做本地截图导入、浏览、打开和删除；一道错题是一组截图，可分多次逐张追加。
- 支持拖入或文件选择 PNG、JPG/JPEG、BMP；不修改或删除用户原始截图。
- 首版不做 OCR、分类、标签、搜索、答案、解析、复习计划、剪贴板监听或云同步。
- 完整草案见 `modules/mistake-collector/definition.md`。

Gate impact:

- 用户以“不要定义了，快做”明确授权直接实现、测试和本地发布，新增模块 Gate A–D 以该指令为证据合并接受。
- 既有模块合同和已经完成的实现不回滚。
- 实现沿用 `light-outlined`，数据保存在 `%LocalAppData%\MU Desk\mistake-collector`；删除只影响模块副本。
- 2026-08-22 `build-toolbox.ps1` 通过，0 warning / 0 error；新增持久化测试验证逐张追加仍在同组且原图不被删除。
- 2026-08-22 已发布到 `artifacts/PersonalToolbox-win-x64/PersonalToolbox.exe`，并完成首页及空状态实机检查。

## Quick Ring and Desktop Organizer — home visual synchronization

Status: integrated — direct implementation authorized 2026-08-22

- 用户指出首页仍有两个工具没有视觉同步，并用截图明确标出“四向轮盘”和“栖格 · 桌面整理”两张模块卡。
- 沿用已接受的 MU 黑白紫 `light-outlined` 方向，不新增品牌、色板或模块包装。
- 四向轮盘正式入口图标使用“四分区圆盘 + 紫色活动扇区”主隐喻；桌面整理正式入口图标使用“四块桌面分区 + 紫色焦点”主隐喻。
- 六张首页模块卡统一为暖白表面和深色描边；常驻开关统一为紫色轨道、深色轮廓与暖白滑块。
- 本次范围只改变 MU Desk 首页视觉和共享开关样式，不改变模块行为、设置、托盘、启动项、数据或独立兼容启动器。
- 用户此前明确要求“你就改吧，别一步步接收了”；本次按该协作方式直接实施、构建、实机检查和本地发布。

## File Shelf module — Product Definition correction

Status: pending Gate A — 2026-08-22

Inventory correction:

- 当前 MU Desk 实机首页包含四向轮盘、动态拾取、CISP 题库、光标画廊和桌面整理；SKU 中旧的 `mistake-collector` 条目已按真实产品状态校正为 `cisp-question-bank`。
- 临时货架只作为 `MU-DESK` 的内部模块加入，不建立独立产品身份。
- 既有模块的实现和历史 Gate 接受不回滚；此次只为新增 `file-shelf` 重新打开 Gate A–D。

Proposed definition:

- 面向用户名称为“临时货架”；界面可使用“搁一下”作为动作短句，不把它建立为第二品牌。
- 用户把本地文件或文件夹拖到屏幕边缘的小货架，切换窗口后再拖出继续使用。
- 同一次放入的多个项目形成一组；成功拖出后，未固定项目从货架移除，固定项目保留。
- 模块只保存路径、放入时间、分组和固定状态；不复制、移动、删除、上传或读取原文件内容。
- 首版不做剪贴板历史、文件搜索/预览、云同步、自动过期、Shell 扩展或独立后台身份。
- 完整草案见 `modules/file-shelf/definition.md`。

Gate impact:

- 用户的“可以，做吧”授权开始产品流程，不构成 Gate A 的明确接受。
- 下一步必须由用户明确接受 Gate A，之后才能起草临时货架的视觉系统。

### File Shelf — Gate A acceptance

Status: accepted — 2026-08-22T16:23:07.5673758+08:00

- 用户明确回复“接受 Gate A”。
- 接受范围以 `modules/file-shelf/definition.md` 为准：内部模块、贴边拖入/拖出、同批分组、固定保留、成功拖出自动移除未固定项、路径元数据持久化，以及对原文件的零复制/移动/删除/上传/内容读取边界。

## File Shelf module — Visual System draft

Status: pending Gate B — 2026-08-22

- 首页卡片继承 `light-outlined`；贴边标签和展开货架使用 `dark-translucent`。
- 默认右侧中部折叠标签约 36 × 92 px；展开面板宽 320 px、高 360–640 px，并保持在任务栏工作区内。
- 面板按拖入批次显示紧凑文件行，只使用 Windows 文件类型图标，不生成内容缩略图。
- 有效/无效拖入、保存失败、路径不可用、拖出成功/失败均有文字与形状反馈。
- 图标方向为“货架横板 + 暂放文件页 + 紫色落点”；不使用文件夹、下载、收件箱或回收站隐喻。
- 无角色插图、无装饰循环动画、无 token override；正式资产等待 Gate B 接受。
- 完整草案见 `modules/file-shelf/visual-system.md`。

### File Shelf — Gate B acceptance

Status: accepted — 2026-08-22T16:24:49.6434117+08:00

- 用户在 Gate B 提示后明确回复“b”。
- 接受 `modules/file-shelf/visual-system.md` 的完整方向：浅色首页卡片、深色半透明贴边货架、右侧折叠标签、320 px 展开面板、按批次紧凑条目、货架加文件页图标方向，以及无插图/无循环动画边界。

## File Shelf module — System Packaging draft

Status: pending Gate C — 2026-08-22

- 临时货架完全运行在 MU Desk 进程中，继承唯一托盘、开机启动、设置、版本和安装包。
- 模块默认启用但启动时始终折叠，不抢焦点、不自动展开、不发系统通知。
- 统一首页和托盘提供显示/收起入口；没有独立快捷方式、全局快捷键或任务栏身份。
- 元数据保存在 `%LocalAppData%\MU Desk\file-shelf\shelf.json`，原子写入并保留一个 `.bak` 恢复版本。
- 首版最多 200 个路径；只在显示或拖出时检查可用性，不扫描磁盘或后台监控目录。
- 完整路径不进入日志；主文件损坏、保存失败、离线路径和权限问题保持可见。
- 任何移除、清空、拖出后清理或设置重置都不删除、移动或重命名源文件。
- 完整草案见 `modules/file-shelf/system-packaging.md`。

### File Shelf — Gate C acceptance

Status: accepted — 2026-08-22T16:28:14.1452887+08:00

- 用户在 Gate C 提示后明确回复“c”。
- 接受范围以 `modules/file-shelf/system-packaging.md` 为准：单一 MU Desk 身份、默认启用但折叠、统一首页/托盘入口、路径元数据原子持久化与备份、最多 200 项、无后台扫描/联网，以及源文件零修改边界。

## File Shelf module — Implementation Authorization draft

Status: pending Gate D — 2026-08-22

- 现有动态拾取实施映射已保存为 `modules/effect-capture/implementation-map.snapshot.json`；根 `implementation-map.json` 改为本次临时货架范围。
- 实施只触及 `Toolbox.Core`、`Toolbox.App` 和 `Toolbox.Tests`，不修改 Mouse Ring、Desktop Organizer 或其他兼容应用。
- 技术路线使用 WPF/OLE `FileDrop`、Windows Shell 文件类型图标、单个 Topmost 贴边窗口和 JSON 原子存储；不新增数据库、Shell 扩展、后台目录监控或第三方运行时。
- 完整文件清单、顺序、风险、迁移、验证与回滚见根目录 `implementation-map.json`。

### File Shelf — Gate D acceptance

Status: accepted — 2026-08-22T16:30:27.1552549+08:00

- 用户明确回复“D”。
- 授权按根目录 `implementation-map.json` 实现临时货架核心、贴边窗口、路径元数据存储、MU Desk 首页/托盘/暂停连接点、正式模块图标、测试、构建和本地发布。
- 不授权数据库、Shell 扩展、后台文件监控、剪贴板历史、网络能力、独立进程或其他模块重构。

## File Shelf module — implementation and local release

Status: integrated — 2026-08-22T16:42:04.2187990+08:00

- 新增版本化路径模型、同批分组、固定状态、200 项上限、单次撤销、JSON 原子写入和最近可靠备份恢复。
- 新增一个 MU Desk 进程内的 WPF 贴边窗口：默认右侧折叠，拖入时展开，接受本地 `FileDrop`，按 Windows 拖放结果清理未固定元数据。
- 拖出只发布 Copy/Link 兼容效果，不发布 Move；模块代码没有复制、移动、重命名、删除或读取源文件内容的调用。
- 首页更新为六个两列模块卡，加入临时货架开关、真实数量和显示/收起动作；统一托盘、暂停、启动、关闭和错误路径已经接通。
- 图标技能产出 3×3 可追溯源图和九个同风格状态图标；正式 `file-shelf` 图标拆分为 16、20、24、32、64、128、256 px，自动 QA 无问题，并接入首页与边缘标签。
- `build-toolbox.ps1` 通过：0 warning / 0 error；Toolbox、MouseRing 和 DesktopOrganizer 21/21 回归全部通过。
- `publish-toolbox.ps1` 成功更新 `artifacts/PersonalToolbox-win-x64/PersonalToolbox.exe`。
- 发布成品通过 Windows 实机检查：显示“本机工具 · 6”、临时货架卡片、右侧折叠标签、320 px 展开空状态；最后恢复折叠状态并保持应用运行。
- 首次只显示空货架时没有创建 `%LocalAppData%\MU Desk\file-shelf`，符合首次成功修改时才懒创建数据目录的合同。

## Six-module visual consistency remediation

Status: implementation authorized — 2026-08-22

- 用户要求检查当前所有 MU Desk 小工具的视觉统一性；实机范围为首页现有六个模块，LightPet/VPet 仍为独立产品，不计入本次整改。
- 检查确认不需要建立新视觉方向：继续执行已接受的 MU Product Standard 1.0.1，管理、设置、题库与收藏使用 `light-outlined`，轮盘、桌面区域、录制 HUD 与临时货架使用 `dark-translucent`。
- 用户在完整问题清单和整改优先级后明确回复“改吧”，授权按 `visual-consistency-implementation-map.json` 实施、构建、测试、本地发布与实机复查。
- 范围只包含视觉继承、控件层级、功能命名、状态标签和正式产品文案；不改变六个模块的业务行为、数据或系统包装。

## Independent task-window identity correction

Status: integrated — 2026-08-22

- 用户指出多个模块窗口与 MU Desk 共用任务栏分组导致题库难以选中，因此题库、光标画廊和动态拾取结果窗改用各自的显式窗口任务栏身份；进程、托盘、启动项和产品身份仍保持唯一。
- 用户随后追问“他们不是有自己的图标吗”，纠正只拆分任务栏按钮但仍显示主产品图标的不完整处理。
- CISP 题库窗口现在使用与首页一致的代码原生 `C?` 文档图标；光标画廊和动态拾取窗口继续使用既有模块图标；MU Desk 主图标只代表产品外壳。
- 该修正落实已接受的 MU Product Standard 1.0.1 图标层级，不建立模块独立品牌或独立安装身份，因此不重开产品与系统包装 Gate。

### Small-size optical correction

- 用户实机否决首版 CISP 独立图标：有效主体周围留白过宽，任务栏缩放后显得过小；随后进一步指出首页其余模块图标与四向轮盘、桌面整理的主体比例不一致。
- 以用户认可的四向轮盘和桌面整理为基准：46 px 图标框内使用约 34 px 的真实可见主体、深色结构描边、暖白与紫色填充。
- 透明边界测量确认原 PNG 的有效内容并不等同于 64 px 画布：动态拾取约 36×35 px，光标画廊约 52×52 px，临时货架约 40×25 px；继续统一设置图片控件宽高会重复缩小主体。
- 动态拾取、CISP、光标画廊和临时货架因此改为共享主题中的代码原生矢量图标；CISP 和动态拾取窗口图标也复用同一资源，避免首页、标题栏与任务栏比例再次分叉。
## Module naming amendment — 2026-09-05

Status: accepted; implementation authorized for display names only.

- 用户在八项英文短名方案后回复“可以，整体修改一下”。采用 Mujun Cue / Orbit / Clip / Tip / Grid / Drop / Memo / Pal；普通界面使用短名与中文功能描述，CISP 题库除外。
- 本次覆盖 MU Standard 默认功能命名约定，但不建立八套独立产品包装；MU Desk 继续统一入口、托盘、启动、设置和发布。
- 不改变内部 ID、程序集名、进程、管道、数据目录、配置格式或业务行为。现有视觉与系统包装决定继续有效。
- 范围和验证见 `module-naming-implementation-map.json`。
## Drop edge-shelf refinement — 2026-09-05

User accepted the clarified right-edge shelf refinement with “对”. Scoped implementation follows `modules/file-shelf/drop-refresh.md`, reuses the approved MU dark-overlay and Cue reveal approach, and preserves storage/file semantics and system packaging.
