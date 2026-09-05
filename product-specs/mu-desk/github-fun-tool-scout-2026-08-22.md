# MU Desk 有意思的小工具侦察（2026-08-22）

这份清单不再按“效率工具”筛选，而按三个标准筛选：第一眼想玩、能让桌面产生反馈、可以与 MU Desk 已有能力合成出自己的性格。

## 1. 手势法阵

按住鼠标侧键或中键，光标拖出一条发光轨迹。画圆、S、闪电、三角等符号后触发动作；识别成功时轨迹闭合成法阵并消散。

- 圆：呼出快捷轮盘。
- 闪电：启动动态拾取。
- 方框：把框内内容交给选区工具。
- 螺旋：打开临时货架。
- 自定义符号：启动程序、发送快捷键或运行命令。

参考：[MouseEffects](https://github.com/ltrudu/MouseEffects) 已有 Procedural Sigil、Runes、闪电、水波等 DirectX 透明覆盖层效果；[RadialDeck](https://github.com/alienware377/RadialDeck-Handy-Touch-Buttons-and-Multitouch-Gestures) 已实现圆、半圆、S、旋转和多指手势识别。两者都是 MIT。

判断：最好玩也最像 MU Desk 自己的功能。它不是单独的装饰，而是给现有快捷轮盘增加一套“施法式入口”。首版只识别圆、V、Z 三种符号即可。

## 2. 桌面魔法 / 光标生态

让点击和移动真正影响桌面：

- 点击长出一朵小花或晶体，几秒后凋谢。
- 快速移动留下萤火虫、樱花或墨迹。
- 长按形成小型引力井，把粒子吸到光标周围。
- 双击出现水面涟漪或像素爆炸。
- 空闲时少量种子飘落，用户一动鼠标便散开。

参考：[MouseEffects](https://github.com/ltrudu/MouseEffects) 是 .NET/DirectX 11 的插件式透明覆盖层，已经展示 46 种效果，包括黑洞、传送门、花朵生长、晶体生长、墨水、水波、符文和小游戏；[cursor-trail](https://github.com/nayutalienx/cursor-trail) 展示了较窄的 Win32 透明轨迹实现。

判断：不要照搬 46 个效果。MU 版只做 4 个完成度高的主题包，每包统一点击、移动、空闲三种反馈。可与光标画廊联动，换光标皮肤时同时换特效主题。

## 3. 桌面镜片

一个可拖动、可缩放的透明“异世界镜片”，只改变镜片下面那块屏幕：

- CRT 曲面与扫描线。
- Game Boy 四阶绿。
- ASCII / 点阵化。
- 万花筒。
- 水下波纹或故障画面。
- 时间延迟：镜片里看到的是 1–5 秒前的桌面。

参考：[ShaderGlass](https://github.com/mausimus/ShaderGlass) 可把 1200+ RetroArch shader 用在桌面、单个窗口或透明浮动镜片上；[MouseEffects](https://github.com/ltrudu/MouseEffects) 也有 ASCII、CRT、万花筒、放大镜、水波和屏幕扭曲。

判断：完整 shader 系统太大，但单一“镜片窗口 + 5 个固定效果”很适合 vibecoding。MU Desk 已有屏幕采集基础，先做延迟镜片或 Game Boy 镜片会最容易形成演示效果。ShaderGlass 为 GPL-3.0，只借鉴概念和交互，不复制代码。

## 4. 音频边境

音乐播放时，屏幕四边出现很薄的光带、呼吸波或粒子；低频让底边起伏，高频点亮两侧，静音后慢慢熄灭。它不是音乐播放器，也不是大块频谱，而是让整个桌面有环境反应。

参考：[Paraline](https://github.com/SamXop123/Paraline) 使用 WASAPI loopback 和透明点击穿透覆盖层，把系统音频变成屏幕边缘波纹、极光、粒子与发光边框。

判断：视觉记忆点很强，代码边界也清楚。首版只做一个“紫色呼吸边框”，支持灵敏度、粗细和全屏自动暂停；以后可让桌宠跟随节拍轻微点头。

## 5. AI 等待街机

当 Codex/Agent 进入长时间思考或执行状态，屏幕边缘弹出一个 20–60 秒的透明小游戏：打掉飘来的小行星、让小人跨过窗口缝、用光标保护任务进度条。Agent 完成时关卡立即结算并显示“任务完成”。

参考：[Agent Arcade](https://github.com/DanWahlin/agent-arcade) 是 MIT 的 Tauri/Phaser 透明桌面街机，明确为等待 Codex、Claude Code 等 Agent 工作而设计；[MouseEffects](https://github.com/ltrudu/MouseEffects) 也把 Space Invaders、Missile Command 类小游戏做进了桌面覆盖层。

判断：很有传播性，但不该做六款游戏。MU 版先做一个 30 秒“光标防御”即可，并且必须由用户主动打开，不能 Agent 一工作就强制打扰。

## 6. 文件黑洞

把现有临时货架换一种入口：屏幕边缘出现一个小黑洞。文件拖进去时只记录路径，不移动原文件；文件图标被拉伸、旋转、吸入，随后黑洞显示里面有几件东西。点击黑洞展开现有货架。

参考灵感来自 [MouseEffects](https://github.com/ltrudu/MouseEffects) 的 Black Hole、Gravity Well 与 Portal，但业务内核完全复用 MU Desk 已有临时货架。

判断：这是最省代码、最容易让现有功能突然变有趣的一项。它不是新模块，只是临时货架的第二种“皮肤/入口”。拖放完成前必须明确显示“仅收纳引用，不移动文件”。

## 7. 窗口果冻 / 桌面物理日

拖动窗口时边缘像果冻一样延迟，快速甩动会有轻微惯性；特殊“物理日”模式下，MU Desk 自己的浮窗和贴边工具可以弹跳、碰撞或受重力影响。

参考：[Deskwarp](https://github.com/doebalov/Deskwarp) 尝试把 Compiz 的 Wobbly Windows 带到 Windows 10/11；[fwm](https://github.com/iluaii/fwm) 在 Wayland 中把窗口做成 Box2D 刚体，可投掷、堆叠、旋转和切换重力。

判断：直接修改所有系统窗口风险高、兼容性差，不适合先做。安全版本只让 MU Desk 自己的货架、轮盘、桌宠气泡产生弹性，不碰第三方窗口。

## 8. 打字配音

键盘不是固定机械键盘声，而是可组合的“输入人格”：打字机、木鱼、玻璃珠、8-bit、雨滴、Animalese。按键速度和连续性影响音高与节奏，退格键有专属声音，长时间不输入时落下一个结束音。

参考：[Mechvibes](https://github.com/hainguyents13/mechvibes) 的全局键盘声音思路、[Animalese Typing Desktop](https://github.com/joshxviii/animalese-typing-desktop) 的拟声输入。

判断：实现不难，但默认必须关闭；只响应按键事件类型和节奏，不记录字符内容。最适合做成光标/桌面主题包的一部分，而不是独立首页卡片。

## 推荐优先级

1. **文件黑洞**：最低成本，把已经有用的临时货架变得有记忆点。
2. **手势法阵**：最好地把“有趣”和“真能用”结合起来。
3. **桌面镜片**：演示冲击力最强，且能复用动态拾取的屏幕采集。
4. **音频边境**：适合长期常驻，能让 MU Desk 有独特氛围。
5. **桌面魔法**：适合做主题系统，但需要控制性能和视觉噪音。
6. **AI 等待街机**：传播性强，等 Codex 状态接入稳定后再做。

## 不走的方向

- 不再做普通番茄钟、提醒器、剪贴板、OCR、取色器等 PowerToys 式功能。
- 不复制另一个完整桌宠；现有 LightPet 只吸收“跟随节拍、和粒子互动”等新行为。
- 不做纯壁纸引擎；效果必须能与鼠标、声音、文件、窗口或 Agent 状态互动。
- 不碰需要注入第三方进程、驱动或高风险 DWM Hook 的首版方案。
