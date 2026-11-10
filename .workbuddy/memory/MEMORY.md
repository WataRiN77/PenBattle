# PenBattle 项目备忘

## 概况
Unity 2022.3.17f1c1 + URP，毕设。课桌"弹笔大战"，2~4 人回合制物理对战，先积 3 分者胜。

## 术语（用户定义，代码已按此改名，务必沿用）
### 时间轴
- **小局 Round**：开局到只剩一人 → `RoundStart/EndEvent`、`_roundNumber`
- **回合 Turn**：小局内每位存活玩家各行动一次 → `TurnStarted/EndedEvent`（含 roundNumber+turnNumber）、`_turnNumber`
- **行动 Action**：单人一次操作 → `ActionStarted/EndedEvent`（含 playerId）、`_actionActive`
- 顺序：`ActionEnded` →（若绕回队首）`TurnEnded` → `TurnStarted` → `ActionStarted`；TurnEnded 由 `NextTurn()` 靠 `_turnActive` 统一广播一次
### 技能体系（2026-11-02 用户定义，三层并列，不可混用）
- **主动技能**：玩家在**自己回合内主动释放**的效果（消耗一次行动前置，每回合 1 次）→ 代码 `SkillConfig` / `SkillType` / `SkillSystem`。
  **⚠️ 现阶段暂不设计，只留框架。**
- **被动技能**：**常时生效、满足某条件即触发**的效果（例：点燃＝笔碰到其它笔就给对方挂燃烧层）→ 代码 `PenConfig.passiveSkills` / `PassiveEffectType` / `PassiveSkill` 钩子
- **效果（Status）**：**独立于技能层**的可挂载状态，主动技能和被动技能都可以给别人挂上（燃烧、冰冻）。
  → 代码 `StatusEffect` / `StatusEffectManager.AddEffect<T>()` / `Status/AllStatus/`
  → 所以「效果」不是技能的一种，**来源与效果解耦**：`BurnEffect` 既可被 `SkillType.Burn`（主动）施加，也可被 `IgnitePassive`（被动）施加
- **强化**：**对局中以卡牌选择形式**进行的成长（**含开局强化 + 每小局败者强化**，用户已确认"对局中"包含开局），分通用/专属 → `UpgradePhaseManager` + `UpgradeSystem`
  - **通用强化**：所有笔都能选到（改物理参数等）→ `UpgradePool.asset` / `UpgradeEffectType`
  - **专属强化**：只有某支/某几支笔能选到（例：火焰笔的燃烧类强化）→ `PenConfig.exclusiveUpgrades`（已有字段，抽取时与通用池合并）
  - **参数型 vs 行为型**（2026-11-09 打通）：`UpgradeEffectType.Passive` = 行为型，挂一个 `PassiveSkill` 组件，参数在 `UpgradeEntry.passive`（复用 `PassiveSkillEntry`）；其余都是参数型，只改修饰量。
    行为组件打 `fromUpgrade` 标记，`UpgradeSystem.ApplyAll` 每小局「先 RestoreBaseValues → 再 Detach 旧组件 → 再重建」，不会叠层。
    ⚠️ Detach 必须 `fromUpgrade=false` → `enabled=false`（立刻退订事件）→ `Destroy`（帧末），只 Destroy 会双倍触发。
    ⚠️ 子类**不要**在 OnDisable/OnDestroy 里撤销 ApplyStaticEffect 的修饰量（会减成负数）。
    `UpgradeEntry.Clone()` 是深拷贝，`UpgradePhaseManager.Select` 必须用它（曾漏拷 operationType 导致加算退化成乘算）。
  - 专属条目偏薄（每支笔 1~2 条），用户会自己补设计
- ⚠️ **命名雷区**：`PassiveEffectType` 指的是"被动**能力**枚举"（荆棘/势能/冲击波…），`StatusEffect` 指的是"可挂载**状态**"（燃烧/冰冻）。两者都叫 effect，写文档时注明是哪个。

## 架构速查
- `EventBus`：自研单例 PenBattleEventBus（命名空间 PenBattleGame.Core），GameEvent 基类。事件字段一律小写驼峰。
- `GameManager`：状态机 Idle→Action→Resolving→RoundEnd→MatchEnd，管淘汰/积分/结算。
- `InputManager`：选笔→笔身按下蓄力→松开发射。`CanOperate(pen)` 判"笔是我的 && 轮到我的回合"，用 `LocalPlayerId()` 而非 `GetCurrentPlayerId()`（**"该谁动"和"我是谁"是两个概念**）。
- `PenController`：Rigidbody 物理、耐久 100、出界判定、碰撞事件。`Pen/PenPhysicsParams.cs` 是早期纯逻辑残留，未接通。

## 已锁定决策
- 网络：NGO（2022.3 只能 1.x）+ UnityTransport，Host 权威。
- 技能绑定在笔上（`PenConfig.activeSkills`），`SkillTargetMode` 5 种；技能=回合内免费前置动作，每回合限 1 次，用完仍需弹笔/放置结束回合。
- 被动内嵌 `PenConfig.passiveSkills`（`List<PassiveSkillEntry>`），已实现 Thorns/Momentum/Tough/Slippery/冲击波。
- 败者强化不区分排名，所有人同等级同数量。
- 耐久每小局重置满、燃烧每回合扣 1 → 燃烧杀不死人。**用户决定保持现状**，数值阶段 3 统一调。

## 进度
6 步计划：①多人回合制 ②放置 ③选笔/技能UI ④开局强化 ⑤败者强化 ⑥网络层。
阶段 1（技能绑定+手感）、阶段 2（被动）已完成；阶段 3（强化）、阶段 6（UI）进行中。剩余 10 个被动设计在 `Docs/被动效果设计.md`，暂停实现。

## 用户约定
- **默认贴代码给他自己粘**，只有明说"直接改/直接写入"才动文件。
- 报错要定位根因；他粘代码易重复/截断（曾致方法重复定义、.asset 的 m_Script 变 fileID:0）。

## ⚠️ 物理坑
`DynamicsManager` 的 `m_AutoSyncTransforms: 0`（关闭）→ **移动/旋转带 Rigidbody 的物体必须写 `Rigidbody.position/rotation` 或 `MovePosition`**，只写 transform 会被物理同步覆盖回旧位姿（表现：正确位置出现一帧后弹回）。已修 TeleportTo/ResetPen/PlayerPlacePen/ResetObstacle。别开 AutoSyncTransforms 兜底。
- `linearDrag` 与 `friction` 必须分开；`RebuildPhysicMaterial` 要给所有 Collider 赋材质。

## 联机
- **进度（2026-11-04）：M4 完成（输入上报 + 归属校验 + 越权拒绝），M5 骨架完成但附属状态缺失** —— 详见 `2026-11-04.md`。
  已修的坑：每小局重抽地图只有房主换 → `StartNewRound` 里补 `NetworkMatchFlow.HostApplyMap()`。
  未同步：障碍物破坏、技能剩余次数 / hasPlaced、客户端 `PlayerData.upgrades`（`NetworkUpgradeBridge` 只传文案不传数据）。
- **2026-11-09 补的通道**：`PenNetworkBridge` 加 `_maxDurability`（血条分母，0.15s 轮询）。
  `_durability` 初值改 **-1 哨兵**，`PenHealthBar` 判 `>= 0f`（原 bug：0 血时回退读本地满血值→血条弹满）。
  **体型不需要通道**：`笔_1.prefab` 的 NetworkTransform 已开 `SyncScaleX/Y/Z: 1`，localScale 自动同步，别再手写 `_scale`。
- **铁律：客户端永远不跑 `ApplyAll`/`AttachTo`**。`PassiveSkill.OnEnable` 订 9 个事件，客户端挂真组件会真的执行玩法逻辑（点燃扣血/冲击波推刚体/粘液改摩擦）。客户端只有 `visualOnly` 状态镜像 + 纯数据通道。
- 还没做：**瞬时表现**（冲击波波纹/荆棘火花/点燃火焰）主机 `PlayFeedbacks` 客户端收不到，要 `ClientRpc(penId, 效果类型, 位置)`。
- `NetworkVariable<Vector3>` NGO 1.15.1 可用（unmanaged → ILPP 自动生成），不用裹 `ForceNetworkSerializeByMemcpy`。
- 方案文档 `Docs/联机改造方案.md`（A~J 模块 + M1~M8 里程碑，§2.4 记录 451 事件）。
- **已装**：NGO **1.15.1** + transport **1.5.0**（packages-lock 确认，走 packages.unity.cn）。
- **已完成**：`Assets/Scripts/Net/` 下 NetworkLauncher（Host/Client 直连 + IMGUI）、NetworkMatchFlow、NetworkMatchSync、NetworkUpgradeBridge、PenNetworkBridge；`UI/ConnectPanelUI.cs` 接正式面板。
- 三铁律：客户端不产生权威状态 / 不执行玩法逻辑 / UI 表现层可本地跑。
### ⚠️ 互联网房间已放弃（Unity Relay 被地区限制）
- **Unity 自 2026-06-30 起停止向中国大陆/港澳组织提供全球 Gaming Services**（Relay / Lobby / Matchmaker / Vivox / Cloud Code / Cloud Save / CCD…），只保留 **Ads / IAP / Authentication**。请求返回 **HTTP 451 "Unavailable in your region"（错误码 53）**，按**组织注册地**判定，与玩家位置无关。
- 匿名登录能成功、`CreateAllocationAsync` 才 451 → 很容易误判成"就差一步"。**改代码解决不了。**
- **2026-10-23 已全部回滚**：删 `Net/RelayRoom.cs`(+meta)；manifest 移除 `com.unity.services.relay` 与 `cn.unity.uos.launcher`；删 `Assets/UOSLauncherEncrypt/`、`Assets/Resources/UOSSettings.asset`；`NetworkLauncher` / `ConnectPanelUI` 恢复纯局域网版（`NetMode`/`CreateRoom`/`JoinRoom`/密码审批全部移除）。

### 将来要接互联网，可选路线（都不理想）
- **UOS Sync Relay**（unity.cn，官方国区替代）：用 `Unity.Sync.Relay.Transport.Netcode.RelayTransportNetcode` 替代 UnityTransport，**NGO 上层逻辑不用改**；`LobbyService.AsyncCreateRoom(CreateRoomRequest{ Visibility=Private, JoinCode=密码, OwnerId 必填 })` → 加入走 `AsyncQueryRoomByRoomCode` → `SetJoinCode` → `SetRoomData` → `StartClient()`；需 UOS App + **Room Profile UUID**，SDK 由 **UOS Launcher** 装（不是 UPM 包名）。计费：微型房 0.20 元/峰值·天 + 下行 1.20 元/GB，试用 50 房间。
- **ZeroTier / Tailscale 虚拟局域网**：永久免费（ZT Personal 10 设备/1 网络；TS Personal 6 用户/无限设备），玩家装客户端 → **现有局域网代码零改动**。
- **公网 IPv6 直连**：免费，需双方放行入站；走 `SetConnectionData(NetworkEndpoint)`（string 重载参数名叫 ipv4Address，只能 v4）。
- **自建房间服务 + Dedicated Server / frp**：云主机 10~40 元/月 + 运维。
- 结论：**"零配置 + 永久免费 + 6 位房间号"三者不可兼得** —— 这个位置本来是 Unity Relay 的，已关停。
- 笔预制体必须先拍平（Rigidbody+PenController+Collider 移到根，删根上烘焙偏移），NetworkObject 必须在根。

## 其他系统
- **地图**：`Assets/Scripts/Map/` MapConfig/MapPool(SO) + MapManager + SpawnPointMarker + MapBoundsMarker + MapCameraFit。每小局重抽、不连续重复、先做 3 张。`StartNewRound()` 开头 `SelectAndApply()`；`GetSpawnPositionFor` 三级优先（地图出生点→场景 spawnPoints→圆形）。
- **相机**：Main Camera 透视 fov60；Cinemachine VCam 也 fov60、静态无 Follow；`CinemachineVirtualCamera.m_Lens` 是 public 可脚本改。
- **Feel v3.12**（2022.3 对应版本，别装 v6）。主类 `MMF_Player`，最有用 `PlayFeedbacks(Vector3 pos, float intensity)`。接入方式：FeelFeedbackBridge 订阅 EventBus → (pos,intensity)，参数留在 Inspector。表现层只在本地播，联机别放权威逻辑。
- PostProcessing(PPv2) 未装，URP 用 Volume / MMF_ShaderGlobal 替代。

## 策划案与宣传
- 策划案权威版在飞书《斗笔游戏（暂名）策划文档》：wiki `SP1Ywp1UAiAo3Rk9uQGcmMl3nmh`，doc token `ONAcdWEPCocdXGx9XdMcDsW5nVh`。本地 md 已废弃只读。文档缺 5 处，用户要求暂不补。
- lark-cli 改法：fetch 取 block id → update（str_replace/block_replace/block_insert_after/block_delete）→ 再 fetch 验证。未授权时 `--device-code` 会阻塞，别干等。
- 招募图 v2：`Docs/Recruit/recruit-series-v2.html` → `招募系列-v2/*.png`，卡通明亮风 6 页，CTA 统一「欢迎私信交流」，已插飞书附录（宽 600 居中）。标题描边靠 JS 克隆 `span.stroke` + `-webkit-text-stroke`。
- 美术参考 `Docs/Screenshots/`（AI 概念图，非实机）。
- 子文档结构（2026-11-08 重整）：**「资产需求清单」doc `DYSndhd9Bo5riwx0pz9c8iCpngc`**（原燃尽图改名，只放资产需求，8 节，工作量共 49.6~75.2 人天），其下两个子节点：
  - **「11.16前」** doc `KSUMdLtSWo0Dltx99jVcFqljnUe`
  - **「燃尽图」电子表格** `FeSKstNxMhcRVntoKXkcFT43ngc` —— 飞书**甘特图模板**，5 个子表：项目管理 / 项目甘特图(bitable `Gei1bnDOiaBNSMscNSkcUueun8c` / `tbl9G7rjW0MrYIcX`) / 项目进展分析 / 使用指南 / 节假日。已填 4 个父任务 + 22 条子任务，排期 2026-11-09 → 2027-03-05。
- **五支笔实际设定**（代码已定，别再当"待设计"）：火焰笔(被动点燃3层) / 冰霜笔(bounciness 0.07 最不弹，被动冰冻) / 荆棘笔(反伤) / **重笔(mass 2)** / 冲击波笔。每支 2 条专属强化，通用 6 条 → 卡面共 16 条。
- **三张地图是同一主题**（标准课桌 A/B/C，仅布局不同）→ 美术只需 1 套课桌视觉 + 3 张布局图。
- **写 XML 表格**：`docs +update --command overwrite --doc-format xml --content "@./x.xml"`。`<table><colgroup><thead><tr><th>` 能正常渲染，但 **`background-color` 和 `<col width>` 会被丢弃**，表头着色/列宽控制不生效。
- **`<td rowspan>` / `<colspan>` 不被支持**：报 5002，合并单元格失效后续行变空白 → **表格每行都写全内容**；改错用 `block_replace --block-id <表格id>` 整表替换。
- `<callout emoji background-color border-color>` 可用，能正常渲染。

## 飞书电子表格（sheet）实操要点
- 链接：`https://jcngqxev3ksd.feishu.cn/sheets/FeSKstNxMhcRVntoKXkcFT43ngc`
- **日期必须写 `=DATE(y,m,d)` 公式**。`+csv-put` 写 "2026/11/9" 会存成**文本**，导致父任务 MIN/MAX 公式算出 **1899/12/30**。
- **父任务行的汇总列是公式，别覆盖**；只填子任务行，父行自动汇总。
- 整块文本用 `+csv-put --start-cell`；零散多区域用 `+cells-set --writes`（一次批量）。
- `项目甘特图` 是 **bitable 子表**，grid 命令读不了（900015206）→ 先 `+workbook-info` 拿 `bitable_app_token` / `bitable_table_id`。
- `base +record-delete` 用 `--json '{"record_id_list":[...]}' --yes`（`--record-id` 是 stringArray，逗号串不行）。
- `base +record-list` 的 `data.data` 是按字段位置排的**数组**，要用返回的 `fields` 顺序映射。
- ⚠️ **甘特图模板的时间轴固定 8 周（J:BM）**，`H2`「显示第 N 周」是**滑动窗口（1~8）不是延长**，最远只到约第 15 周。**插入列 + 复制表头块会整块 `#VALUE!`**（表头公式硬绑 1~8 索引），不要试图延长。
  任务跨度超 8 周时，改用 `项目甘特图` 多维表的**原生 gantt 视图**（`vewSdLK5oE`，无周数限制）。
- **权限割裂**：bot 缺 wiki scope（`wiki +node-list` 报 99991672），但 `wiki +node-get` 能通；列子节点必须 `--as user`。**user token 约 1 个月就过期**（10-20 授，11-08 已 expire）。

## 文档产出索引
- `Docs/开题报告大纲.md` —— 研究口径权威版（设计实践型 / 三层时序 / 三条创新点 / 不夸大）
- `Docs/毕业设计任务书.md` —— 精炼版三段：背景意义、主要内容含 14 项指标、进度安排
- `Docs/小红书招募文案.md` —— 主推版 + 短版 + 9 图顺序 + 发布贴士
- 写论文/PPT/任务书一律沿用开题大纲口径，别另起一套说法。

## 飞书甘特图（燃尽图）表结构（2026-11-08 已重构，以代码为准）
- 时间轴现在是 **J:DX = 119 列 = 17 周**（2026/11/9 → 2027/3/7），**固定不滑动**。
- `J4 = =C2-WEEKDAY(C2,1)+2`（锚点，别再塞 H2 偏移）；`K4:DX4` = 前一日+1；`J5:DX5` = `CHOOSE(WEEKDAY(...))`。
- 周标签（行2）/月标签（行3）只在 17 个组首列（J/Q/X/AE/AL/AS/AZ/BG/BN/BU/CB/CI/CP/CW/DD/DK/DR），每块合并 7 列。
- `H2` 已改成 `=(DX4-J4+1)/7` 只做显示（值 17），下拉已删。**要再改跨度就改 DX→新末列并重算这三行。**
- 蓝条 `#245BDB` = `AND(ISNUMBER($D6),ISNUMBER($E6),$D6<=J$4,$E6>=J$4)`，range J6:DX35
- 灰条 `#8F959E` = 同上 + `ISNUMBER($G6)` + `ROUNDDOWN(($E6-$D6+1)*$G6,0)+$D6-1>=J$4`
- 🔴 **甘特区 J:BM 原本有模板 demo 的硬填色**（直接写在单元格上的背景色，不是条件格式）。已刷白，父任务行 6/14/20/27 刷回 `#D8D8D8`。**以后若又冒出色块，先 `+cells-get --include style` 读单元格自身 background_color。**
- 父任务 D/E/G/H 全部 IFERROR 包裹（见 2026-11-08.md），支持"待定"。

## 飞书 CLI 易错点补充
- 插入列后**条件格式 range 会自动扩**（J6:BM35 → J6:DX35），不用手改。
- `+cells-set --range K4 --cells ... --copy-to-range "L4:DX4"` 可一键填充相对公式，比 `range-fill` 稳。
- `+styles-put` 一次可做 merges + number_format + 边框（`cell_merges` / `cell_styles`），比逐条 merge 高效。
- `+cells-clear` / `+dim-delete` 需 `--yes`；`--output-path` 有时不落盘，直接用 shell 重定向。
- `base +record-list` 用 `--base-token`（不是 `--app-token`），且不支持 `--jq`。
