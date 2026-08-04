# Changelog

**KitDevTools** 的重要变更记录于此文件。

格式基于 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，
版本号遵循 [Semantic Versioning](https://semver.org/lang/zh-CN/)。

## [Unreleased]

### Added

- **AutoSlay** — 开发模式标题屏菜单可启动官方 AutoSlay 冒烟机器人，可选种子。不再出现在开发侧栏。仅单人；Steam 正式包无法使用 `--autoslay`，因此直接调用 `AutoSlayer.Start`，并在跑完后阻止机器人退出游戏进程。
- 首次作为独立产品发布：局内侧栏、浏览器、作弊、存档、日志及相关开发工具。
- KitModPanel 为可选：存在时通过 KitLib 宿主 API 注册进度保护等相关设置页。
- **卡牌浏览器拖放** — 可将卡牌拖到面板外：上边 → 抽牌堆，右边 → 弃牌堆，下边 → 手牌。
- **悬停打开** — 鼠标悬停侧栏图标即可打开对应面板（无需点击）。
- **Mod 测试房间传送** — 房间浏览器提供 **Mod 测试 — 篝火** 与 **Mod 测试 — 宝箱房**，额外生成假玩家以在单人开发局中预览官方多人 UI。
- **日志导出截图** — 导出 ZIP 可包含游戏截图（会先隐藏 KitLib 界面）、额外图片、问题描述、官方风格分类芯片（图标 + `Bug - 这就是个bug`）、表情，以及最近一场战斗快照。
- **伪联机的进阶与修饰符** — `PseudoCoopLobbyHost.LaunchOptions` 新增进阶等级与运行修饰符列表，启动时可复现已有运行，不再沿用角色选择界面上次的进阶且不带修饰符。
- **MCP `get_map_state`** — 用于路线规划的完整本章地图：每个节点的 row/col、类型与子节点（边），以及已走路径、当前位置、下一步可达节点与 Boss。地图界面关闭时同样可用。
- **MCP `dev_ui`** — 按 index 列出并操作普通 UI 控件（按钮、勾选框、下拉框、文本框），无需鼠标即可驱动菜单与对话框。也可驱动 mod 添加的、本身并非按钮而是自行处理 `gui_input` 的控件——行、面板拖柄、底栏控件等；并提供 `focus` 动作，用于那些按 Godot `FocusEntered` 而非按下来响应的界面，选角色界面正是靠它才变得可驱动。
- **MCP `dev_read_text`** — 读取当前屏幕上的可见文本（标签、提示、面板、预测）并去除 BBCode，令 agent 能读出预览与提示的实际内容。
- **MCP `dev_add_potion`** — 按 model id 给本地玩家授予药水，相当于 `dev_add_card` / `dev_add_relic` 的药水版。传 `search` 可在无对局时列出 id。
- **MCP `dev_hover` 支持 mod 添加的控件** — 没有 `OnFocus()` 重写的节点改为发出 `mouse_entered` 信号驱动（与真实鼠标触发的信号相同），因此 mod 面板、面板行与注入的图标同样可以悬停。列出的实例会标注 `via=focus|signal|none`；`release_previous: false` 可叠加悬停，使悬停打开的面板仍能被读取。

### Changed

- **作弊侧栏页签** — Cards、Cheats、Card Test、存档等作弊侧栏入口改由本产品注册，不再由 KitLib Core 登记。
- **伪联机测试驾驶** — SyncBot 幻影、开房与模拟对端战斗/地图补丁改由本产品承载，并调用 Core NetPlay API。KitAI 不再拥有这套测试驾驶。
- **面板切换** — 再次打开已访问过的开发面板更流畅。
- **手牌投放区** — 底部投放标签改为 **手牌**。
- **移除 AI Host 标签** — 开发侧栏不再显示 AI 机器人入口；AI 控制将随 KitAI 独立面板提供。

### Fixed

- **Mod 测试 — 篝火** — 在稳定版游戏构建上离开预览房间后，会正确恢复单人布局。
- **伪联机重开** — 同一次游戏进程内第二次开伪联机不再失败。此前对局结束时从不释放 33771 端口上的 ENet host，之后每次开房都以 `CantCreate` 失败，只能重启游戏。
- **伪联机种子** — 填写种子不再让「开始伪联机」直接失败。种子实际已应用并同步，只是标准选角色界面拒绝该 UI 通知，而这个拒绝此前会一路抛出、把整个开房流程带崩。
