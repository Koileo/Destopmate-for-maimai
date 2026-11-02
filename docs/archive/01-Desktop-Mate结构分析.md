> 历史设计与验收归档；当前行为和配置以 README 及默认配置为准。

# Desktop-Mate 结构、动画与待机逻辑分析

> 对象：`C:\Users\Koileo\Downloads\Desktop-Mate\Desktop Mate\`
> 方法：读取构建产物、`global-metadata.dat` 类型名堆、StreamingAssets 资源清单（该版本为 IL2CPP，无源码）

## 1. 实测构成

| 项 | 值 |
|---|---|
| 引擎 | Unity **6000.3.8f1**（Unity 6），IL2CPP |
| 进程文件 | `DesktopMate.exe` (652 KB) + `GameAssembly.dll` (145 MB) + `UnityPlayer.dll` (34 MB) |
| 图形 | 自带 `D3D12/` 目录，`boot.config` 里 `force-d3d11-bitblt-model=`、`gfx-threading-mode=6` |
| 角色资源 | `DesktopMate_Data/StreamingAssets/AssetBundle/*.cb`，共 **46 个**（miku、SnowMiku2026、GUMI、KizunaAI、Nier_2B、sanrio_hellokitty…） |
| 资源索引 | `StreamingAssets/database.info`（UnityFS，Bundle 头显示 **6000.2.14f1**） |
| 加密 | 类型表里存在 `DesktopMate.Crypto.Runtime.DMCryptoStream`，`.cb` 非明文 UnityFS |
| 第三方 | `VRoidSDK`、`UniVRM`（含 `VRM10`）、`MagicaCloth2`、`UniTask`、`R3`、`NativeWebSocket`、`RootMotion` |

`.cb` 是 Unity 6000.2 构建的加密 AssetBundle，不能直接交给舞萌的 Unity 2018.4 加载。但几何、贴图、骨骼和表情数据可离线转换。本机 `miku.cb` 已导出为独立 `character.dmc`：247 个节点、4 个网格、30,280 个顶点、47,272 个三角形、23 个表情形变，由旧版 Unity API 在运行时重建。原始文件不修改。

## 2. 四层结构（从类型名还原）

```
┌──────────────────────────────────────────────┐
│ 宿主层  Windows/Mac 集成                      │
│  WindowsOsSupports / WindowsRectExtensions    │
│  WindowClickThroughView  窗口穿透与点击直通    │
│  SlotSettingPlayerPrefsRepository  槽位持久化  │
│  WebSocketApiServer / HotKeyMessage / Alarm   │
├──────────────────────────────────────────────┤
│ 交互层  App.Views.Interaction                 │
│  PickView(抓起) DraggingView(拖动)            │
│  CharacterInteractionView / NadeNadeEffect(摸头)│
│  ICursorCharacterSlotIdDetector(命中哪只)      │
│  RateOverDistanceCompensator(远近速率补偿)     │
├──────────────────────────────────────────────┤
│ 表现层  App.Domain + Main.Scripts.Rendering    │
│  CharacterSlot / CharacterRenderOrder          │
│  CharacterSizeUseCase / ICharacterSizeSetting  │
│  CharacterLayerSwapper(渲染层交换合成)         │
│  Main.Scripts.Rendering.CharacterLayerSwapper  │
├──────────────────────────────────────────────┤
│ 内容层  DesktopMate.Sdk.Character（数据驱动）  │
│  CharacterManifest → CharaData → 状态机/动画轴  │
└──────────────────────────────────────────────┘
```

MVVM 分层很清楚：`App.Domain`（规则）/ `App.Application`（用例）/ `App.Infrastructure`（IO、资源、平台）/ `App.ViewModels` / `App.Views`。移植时这套分层可以直接照搬命名，只是把「用例」换成机台状态。

## 3. 人物内容层：一套数据驱动的角色描述（最值得复用的部分）

`DesktopMate.Sdk.Character` 下的类型，等价于一份**角色包规范**：

| 类型 | 作用 | 对机台的意义 |
|---|---|---|
| `CharacterManifest` / `CharaData` | 角色总清单：模型、动作库、表情、语音、壁纸、粒子 | 直接对应我们的 `character.ab + character.json` |
| `CharaStateMachineData` | 待机状态机：状态、迁移条件、动作序列 | **直接照搬成 `IdleBrain`** |
| `MotionManifest` / `MotionTypeDatabase` / `AnimData` | 动作清单与分类（Idle/Walk/…） | 对应 Animator State 名表 |
| `FacialData` / `BlinkData` / `MouthAnimationData` | 表情、眨眼间隔、口型 | 对应 `Blink` Trigger + `PlayFace` |
| `LookAtSoftClip` | 视线软钳制（限制头部转动范围） | 机台没鼠标，改成「视线漂移 + 钳制」 |
| `SwayParameter` | 头发/裙摆弹簧参数 | 用 MagicaCloth 预烘焙进预制体，运行时不调 |
| 动作原语 `SetBool` / `SetFloat` / `SetInt` / `SetTrigger` / `SetRandomBool` / `SetRandomFloat` / `AddRandomFloat` / `PlayFace` / `SetBoolOnStateActive` / `SetSignalOnStateActive` | 状态迁移时对 Animator 施加的原子操作 | 抽象成 `ICharacterDriver` 的 8 个方法 |

这份规范最有价值的启示：**角色 = 模型 + 状态机 + 时间抖动 + 表情轨**，四者都是数据，代码只做解释器。所以换角色不需要改代码。

## 4. 待机逻辑：三条并行轨道

```
时间轴 ────────────────────────────────────────────────►
主轨(状态机)  Idle ──6~14s──► Peek/Stretch/Wave ──► Idle
抖动轨       每段时长 × (1 ± TimeJitter)          ← AddRandomFloat
表情轨       眨眼每 2.2~5.8s 一次 / 视线每 2.5~7s 漂移一次 ← BlinkData + LookAtSoftClip
```

实现要点：

1. **不要用固定节拍**。`AddRandomFloat` 的存在说明官方也在刻意制造不规则感：`duration = base * (1 + rand(-jitter, jitter))`。
2. **状态迁移用 Trigger 而不是 Bool 组合**，避免 Bool 组合爆炸导致状态卡死。
3. **眨眼和视线必须独立于主状态机**，否则做动作时会「忘记眨眼」。
4. **视线是软钳制**：头部最多转 ±35° 左右，超出范围只转眼球（`LookAtSoftClip`）。
5. **交互反馈走事件**：摸头 → `NadeNadeEffectEmitter` + `PlayFace("Smile")`，与主状态机解耦。

## 5. 交互层设计（可选择性移植）

| 能力 | 类型依据 | 机台可行性 |
|---|---|---|
| 抓起 / 拖动 | `PickView` / `DraggingView` / `DraggingViewModel` | ✗ 无鼠标；可替换为「触摸板点按 → 摸头反应」 |
| 摸头特效 | `NadeNadeEffectEmitter` | ✓ 触摸板有 34×34 压力/接触数据，可做 |
| 窗口点击直通 | `WindowClickThroughView` | ✗ 机台不需要（我们画在游戏内部） |
| 命中检测（点到哪只） | `ICursorCharacterSlotIdDetector` | 简化成单角色，不需要 |
| 多角色槽位 | `CharacterSlot` / `SlotSettingPlayerPrefsRepository` | 可保留配置项，机台上限 1~2 只 |
| 大小 / 透明度 | `CharacterSizeUseCase` / `ICharacterSizeSettingRepository` | ✓ 直接对应 `Scale` / `Opacity` |
| 渲染顺序 | `CharacterRenderOrder` / `CharacterRenderPriority` | ✓ 对应 `SortingOrder` |
| 外部控制 | `WebSocketApiServer` | 可选，用于调试面板联动 |
| 热键 | `HotKeyMessage` | ✓ `HotKeyToggle` |

## 6. 明确不能搬的东西

| 项 | 原因 |
|---|---|
| 原始 `.cb` / Unity 6 Bundle 的直接加载 | 序列化版本不兼容；当前采用离线转换几何/骨骼/贴图的路径，不能直接加载整包 |
| URP / RenderGraph / `CharacterLayerSwapper` 的 SRP 部分 | 舞萌是 built-in RP，2018.4 无 SRP |
| MagicaCloth2 | 依赖 Unity 2021+ Job/Burst；2018.4 用 MagicaCloth1 或 UniVRM 自带 SpringBone |
| `R3` / `UniTask` / `NativeWebSocket` | 与机台无关，且会引入新依赖 |
| Windows 透明窗口 / D3D12 | 我们直接画在游戏帧缓冲里，不需要透明窗口 |

## 7. 移植映射一览

| Desktop-Mate | 机台上对应物 |
|---|---|
| 桌面窗口（置顶透明） | 舞萌主屏 uGUI `ScreenSpaceOverlay` 画布 |
| `CharacterLayerSwapper` | 专用 Layer(30) + 独立相机 + RenderTexture |
| `SlotSettingPlayerPrefsRepository` | `Mods/DesktopMate/DesktopMate.toml` |
| `CharacterManifest` | `Characters/<id>/character.dmc` + PNG 贴图，或 2018.4 的 `character.ab` |
| `CharaStateMachineData` | `IdleBrain`（同构重写，见 `src/Core/IdleBrain.cs`） |
| 鼠标位置 → LookAt | 固定「玩家位置」+ 漂移 + 软钳制 |
| 桌面空闲判定（无输入 N 秒） | 机台进程状态（广告循环 / 待刷卡的 Entry 流程） |
