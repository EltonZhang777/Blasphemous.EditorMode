# Architect → Blasphemous 技术概述

状态：需求已确认，本文档仅定义移植架构、功能映射、实施步骤与验证方案，不创建 Blasphemous 移植代码。

目标：将当前 Architect-Silksong（版本 3.33.10）的运行时关卡编辑能力，重新设计为面向 Blasphemous 1 PC 版的独立 BepInEx/Blasphemous.ModdingAPI mod。当前仓库保留为 Silksong 功能基线；实际代码将在另建仓库实现。

## 1. 已确认的产品边界

| 项目 | 决策 |
|---|---|
| 目标游戏 | Blasphemous 1，PC 版；目标运行时以本地源码、实际部署 DLL 和 ModdingAPI 版本为准 |
| 目标 Unity | 本地 modding 指引记录为 Unity 2017.4.40f1；实现前仍须用实际游戏程序集确认 |
| 入口 | 游戏内热键；默认受开发者/调试开关保护 |
| 第一空间范围 | 当前激活房间的 Room Overlay |
| 后续空间范围 | 多房间、整张地图、Standalone Level |
| 持久化 | 基础 Blasphemous.Framework.Levels 为必需外部依赖，使用其兼容的关卡 JSON |
| Extended 关系 | Blasphemous.LevelFrameworkExtended 不作为默认依赖；只有确认基础框架缺少绝对必要能力时，才在扩展建议中提出功能 |
| 首版联网 | 不实现在线分享、Workshop 网络服务或联机；只做本地单机 JSON 导入/编辑/导出 |
| 对象边界 | 首版新增、移动、删除 Architect 自己创建的对象；原版对象只读 |
| 脚本边界 | 统一为 Blasphemous 事件抽象，保留数据兼容；只实现已验证的目标游戏事件子集 |
| Tilemap/地图结构 | 完整盘点；只有目标 API 和回滚策略验证后才可列为 P0 |
| 输入 | 首版键鼠；动作必须抽象，手柄后续接入 |
| 交付物 | 中文技术概述、迁移规格/任务清单、临时 handoff 文档 |
| 本会话范围 | 文档设计，不修改外部 Framework 工程，不创建具体移植代码 |

## 2. 核心结论

Architect-Silksong 不是一个可直接替换程序集引用的通用 Unity 编辑器。它的入口、场景生命周期、对象创建、UI 状态、角色控制、资源加载、事件系统和在线服务都直接绑定 Silksong。

Blasphemous.Framework.Levels 可以承担“按关卡生命周期加载 JSON，并执行新增/修改/删除”的持久化与运行时应用职责，但它本身不是完整的游戏内编辑器。它当前还存在两个影响 Architect 设计的 API 边界：

1. ObjectCreator 注册表的枚举和 loader/modifier 查询是内部能力；外部 mod 可以注册 creator，但不能稳定地通过公开 API 枚举全部 creator。
2. LevelEdit、LoadModEdits、Apply additions/modifications/deletions 等关键能力部分为内部实现；外部编辑器不能把基础框架当作完整的实时编辑服务。

因此 Blasphemous 版本必须把“编辑会话/实时预览”和“Framework JSON 持久化”拆成两个协作层：

    用户输入
      ↓
    LevelEditSession（主测试接缝）
      ├─ Command/Undo 栈
      ├─ Object Catalog
      ├─ Runtime Placement Manager（当前房间实时预览）
      └─ LevelFramework Adapter（导入/导出 canonical JSON）
            ↓
      Blasphemous.Framework.Levels
            ↓
      LevelManager.OnLevelPreLoaded / OnLevelLoaded

这也是为什么本次规格不会建议把 Extended 直接加入依赖链：它可以作为能力扩展参考，但不能替代 Architect 自身的领域层和错误处理。

## 3. 事实基线

### 3.1 Architect-Silksong

当前工程是 SDK-style C# mod 工程，目标 netstandard2.1，直接引用 Silksong 游戏库、Unity 6000.0.50、BepInEx、Silksong DataManager/ModMenu/AssetHelper/Prepatcher/SSMP 以及 Patchwork、QuickWarp、FsmMaster 等外部 DLL。

插件入口在 ArchitectPlugin 中依次初始化场景、Prefab、存储、设置、修复器、对象目录、事件、联机、预加载和 UI。当前功能的运行时边界因此不是单一 EditorUI，而是一组共同依赖 Silksong 类型的子系统。

关键基线证据：

- 版本和编译依赖：C:\Users\28090\Documents\GitHub\Architect-Silksong\Architect.csproj:23、30、32、34、37
- 插件初始化：C:\Users\28090\Documents\GitHub\Architect-Silksong\ArchitectPlugin.cs:50、59、92
- UI 组合：C:\Users\28090\Documents\GitHub\Architect-Silksong\Editor\EditorUI.cs:80、94、144
- 关卡数据：C:\Users\28090\Documents\GitHub\Architect-Silksong\Placements\LevelData.cs:11、46
- 对象注册和实例化：C:\Users\28090\Documents\GitHub\Architect-Silksong\Objects\Placeable\PlaceableObject.cs:20、86、181、209
- 关卡应用：C:\Users\28090\Documents\GitHub\Architect-Silksong\Placements\PlacementManager.cs:48、84、189
- 存储目录：C:\Users\28090\Documents\GitHub\Architect-Silksong\Storage\StorageManager.cs:39、80

### 3.2 Blasphemous 运行时

本地 Assembly-CSharp 解包表明 Blasphemous 的关卡生命周期由 Framework.Managers.LevelManager 组织：

- currentLevel 和 lastLevel 表示当前/上一关卡；
- OnBeforeLevelLoad 在切换前触发；
- OnLevelPreLoaded 在关卡场景激活、玩家生成前后附近的初始化阶段触发；
- OnLevelLoaded 在关卡完成加载、输入恢复和存档层回调之后触发；
- LevelManager 以关卡名管理多个可加载/激活/停用的 Unity 场景；
- Tools.Level.Layout.LevelInitializer 负责当前关卡的初始化、时间缩放、关卡效果、玩家生成和调试状态。

关键证据：

- C:\Users\28090\Desktop\Games\Documents\渎神\渎神1\渎神 源码解包 仅Assembly-CSharp\Assembly-CSharp\Framework\Managers\LevelManager.cs:31、45、54、63、72、81
- 同文件：LevelManager.ChangeLevel、ChangeLevelCorrutine、InitializeLevel、LoadAndActivateLevel
- C:\Users\28090\Desktop\Games\Documents\渎神\渎神1\渎神 源码解包 仅Assembly-CSharp\Assembly-CSharp\Framework\FrameworkCore\Level.cs
- C:\Users\28090\Desktop\Games\Documents\渎神\渎神1\渎神 源码解包 仅Assembly-CSharp\Assembly-CSharp\Framework\FrameworkCore\LevelScene.cs
- C:\Users\28090\Desktop\Games\Documents\渎神\渎神1\渎神 源码解包 仅Assembly-CSharp\Assembly-CSharp\Tools\Level\Layout\LevelInitializer.cs:22、101、150、218、227

Blasphemous 的关卡通常按同一关卡前缀拆成 _DECO、_LAYOUT、_LOGIC 等场景。Level Framework 的删除/修改逻辑正是依据关卡名和这些 scene suffix 定位对象，因此 Room Identity 不能只保存 Unity active scene 名称，还必须记录 Level Framework 使用的关卡键和目标 scene 类型。

### 3.3 基础 Level Framework

本地工程：

    C:\Users\28090\Documents\GitHub\Blasphemous.Framework.Levels

工程信息：

- 目标框架：net35
- 本地 csproj 版本：0.1.4
- 包依赖：Blasphemous.ModdingAPI 2.4.1、Blasphemous.GameLibs 4.0.67
- 插件主类：Blasphemous.Framework.Levels.Main
- 运行服务：Blasphemous.Framework.Levels.LevelFramework

启动时，LevelFramework 从 Modding/levels 读取每个已加载 mod 的目录，并把每个文件名映射为一个关卡键。每份 JSON 的顶层模型是：

- additions：新增对象
- modifications：修改现有场景对象
- deletions：停用现有场景对象

ObjectData 的字段是：

- scene：修改/删除时定位 _DECO、_LAYOUT、_LOGIC 等目标 scene
- path：修改/删除时从 scene root 沿 Transform 路径定位对象
- type：creator 类型标识
- id：对象唯一标识或物品/配置标识
- position、rotation、scale：新增/修改对象的变换
- condition：条件表达式
- properties：creator/modifier 自定义属性列表

运行流程：

1. LevelFramework.OnInitialize 读取所有已加载 mod 的 levels 文件。
2. 将每个关卡的 additions、modifications、deletions 合并到内存字典。
3. 在 OnLevelPreloaded 中按新关卡键应用三类变更。
4. additions 通过注册的 ObjectCreator 加载一个基础 GameObject，执行 BaseModifier 和 creator modifier。
5. modifications 通过 scene + path 定位原版 GameObject 后执行 modifier。
6. deletions 按 scene 分组，通过路径找到对象并 SetActive(false)。
7. condition 当前支持 flag、penitence、gamemode 三类条件。

关键证据：

- C:\Users\28090\Documents\GitHub\Blasphemous.Framework.Levels\Blasphemous.Framework.Levels\LevelFramework.cs:20、51、60、79、133、149、167、275
- C:\Users\28090\Documents\GitHub\Blasphemous.Framework.Levels\Blasphemous.Framework.Levels\ObjectData.cs:16
- C:\Users\28090\Documents\GitHub\Blasphemous.Framework.Levels\Blasphemous.Framework.Levels\ObjectCreator.cs:9
- C:\Users\28090\Documents\GitHub\Blasphemous.Framework.Levels\Blasphemous.Framework.Levels\LevelRegister.cs:29
- C:\Users\28090\Documents\GitHub\Blasphemous.Framework.Levels\Blasphemous.Framework.Levels\Loaders\ILoader.cs:9
- C:\Users\28090\Documents\GitHub\Blasphemous.Framework.Levels\Blasphemous.Framework.Levels\Loaders\SceneLoader.cs:12
- C:\Users\28090\Documents\GitHub\Blasphemous.Framework.Levels\Blasphemous.Framework.Levels\LevelExtensions.cs:10、39

基础框架当前在 OnRegisterServices 注册的 creator 包括：

- item-ground、item-shrine
- chest-iron、chest-gold、chest-relic
- platform-wood、platform-blood
- spikes-wasteland
- lantern、bell-iron

这些条目构成不依赖 Extended 的第一版原生对象目录候选集，但是否全部作为 P0 仍须在实际游戏环境逐项验证加载路径和 modifier 行为。

### 3.4 LevelFrameworkExtended

本地工程：

    C:\Users\28090\Documents\GitHub\Blasphemous.LevelFrameworkExtended

它是附属 mod，不进入 Architect 默认依赖链。当前工程信息：

- 版本：0.1.0
- 目标框架：net35
- 声明依赖：Blasphemous.Framework.Levels 0.2.0、Blasphemous.ModdingAPI 3.0.1
- 主要扩展：LevelObject Creators、对象 modifier、.jsonc 和 $prev 继承加载

LevelFrameworkExtended 的 LevelObject Creators 区域提供了 spikes、trap、ladder、droppable platform、solid platform、shelf 等对象定义；每个 LevelObject 将 id、SceneLoader 和 IModifier 绑定后通过 RegisterObjectCreator 注册。

它还通过 Harmony patch 拦截 LevelFramework.LoadModEdits，增加：

- .jsonc 支持
- JSON 注释忽略
- .json 优先于同名 .jsonc
- 文件级失败隔离和日志
- $prev 条目继承、向量字段合并、唯一 ID 生成

这些能力写入本文档的“可实现在 LevelFrameworkExtended 中的功能”章节，仅作为可选建议，不成为 Architect 的硬依赖。

关键证据：

- C:\Users\28090\Documents\GitHub\Blasphemous.LevelFrameworkExtended\Blasphemous.LevelFrameworkExtended\LevelFrameworkExtended.cs:15、20、51、58、68、78、121、134、139
- C:\Users\28090\Documents\GitHub\Blasphemous.LevelFrameworkExtended\Blasphemous.LevelFrameworkExtended\Components\LevelObject.cs:7、21
- C:\Users\28090\Documents\GitHub\Blasphemous.LevelFrameworkExtended\Blasphemous.LevelFrameworkExtended\Patches\LevelEditLoaderPatch.cs:10、13、20
- C:\Users\28090\Documents\GitHub\Blasphemous.LevelFrameworkExtended\Blasphemous.LevelFrameworkExtended\Patches\LevelEditLoader.cs:12、37、45、98、128

## 4. 建议的目标工程架构

实现时新建独立工程/仓库，例如 Architect-Blasphemous。以下是逻辑模块，不是本次会话创建的目录：

    Architect.Blps
    ├── Bootstrap
    │   ├── Plugin composition root
    │   ├── dependency/version gate
    │   └── startup diagnostics
    ├── Domain
    │   ├── LevelEditSession
    │   ├── RoomIdentity
    │   ├── PlacementRecord
    │   ├── ObjectCatalogEntry
    │   ├── LevelDocument DTO
    │   ├── ApplyReport
    │   └── command/undo model
    ├── Runtime
    │   ├── BlasphemousLifecycleBridge
    │   ├── RuntimePlacementManager
    │   ├── ObjectOwnership
    │   ├── Transform/selection/raycast services
    │   └── pause/input-state guard
    ├── Catalog
    │   ├── base-framework catalog
    │   ├── curated object metadata
    │   ├── creator capability checks
    │   └── preview factory
    ├── Persistence
    │   ├── LevelFrameworkAdapter
    │   ├── canonical JSON codec
    │   ├── import/export validation
    │   ├── backup/recovery
    │   └── optional editor metadata sidecar
    ├── UI
    │   ├── editor canvas
    │   ├── object browser
    │   ├── inspector
    │   ├── toolbar/hotbar
    │   ├── import/export dialog
    │   └── error/report panel
    ├── Events
    │   ├── supported event abstraction
    │   ├── legacy compatibility reader
    │   └── capability registry
    ├── Diagnostics
    │   ├── object/scene validator
    │   ├── dependency report
    │   ├── import result report
    │   └── safe failure logging
    └── Tests
        ├── pure JSON/domain tests
        ├── command/session tests
        └── Blasphemous profile smoke tests

### 4.1 Bootstrap 与依赖门禁

职责：

- 注册 BepInEx/Blasphemous.ModdingAPI 插件。
- 声明并检测基础 Blasphemous.Framework.Levels。
- 记录实际加载的 ModdingAPI、Framework、GameLibs 和 Unity 版本。
- 检测可选 Extended，但默认不引用、不要求、不启用其 API。
- 若版本不匹配，禁用编辑器而不是让游戏继续进入半初始化状态。
- 初始化服务顺序固定为：配置 → 生命周期桥 → JSON/诊断 → 目录 → 编辑会话 → UI/输入。

关键原则：

- 不复制 Architect-Silksong 的 Silksong 专属依赖。
- 不把当前 Architect 的 netstandard2.1 和 Unity 6000 引用直接带入目标工程。
- 使用实际 Blasphemous ModdingAPI 项目惯例和实际部署 DLL 决定目标编译框架。

### 4.2 LevelEditSession：核心领域层与主测试接缝

LevelEditSession 是用户行为的最高层边界。它不直接依赖 Canvas、Unity scene 或 BepInEx 配置，负责：

- 当前 Room Identity 和导入文档的关联。
- 当前会话中的 Architect-owned placements。
- 新增、移动、删除、变换、锁定、属性编辑、复制粘贴等命令。
- undo/redo。
- 从 canonical Level JSON 建立会话。
- 将会话导出为基础 Framework 可加载的 additions/modifications/deletions。
- 汇总 ApplyReport。

建议使用不可变快照或可逆命令，而不是把 Unity GameObject 作为唯一状态源。Unity GameObject 是运行时投影；LevelEditSession 才是编辑状态的来源。

### 4.3 BlasphemousLifecycleBridge

职责：

- 监听 LevelManager 的关卡切换事件。
- 将 currentLevel、LevelName、scene suffix 和 LevelInitializer 映射为 RoomIdentity。
- 在 OnLevelPreLoaded 后确认 Framework 已完成或正在完成 canonical JSON 应用。
- 在 OnLevelLoaded 后创建/恢复编辑会话和 UI。
- 在房间离开前销毁或冻结当前 Architect-owned runtime objects。
- 不在场景加载竞态期间强行修改玩家生成、摄像机、LevelInitializer 或存档。

建议的状态：

    Disabled
      → ReadyInMainMenu
      → RoomLoaded
      → Editing
      → Importing/Exporting
      → Applying
      → Faulted

状态转换必须可恢复。任何导入失败都回到 Editing 或 RoomLoaded，而不是把游戏留在暂停、无输入或半激活状态。

### 4.4 LevelFrameworkAdapter 与 canonical JSON

LevelFrameworkAdapter 是唯一负责 Framework JSON 的边界。它不修改 Framework 源码，使用自己的公开 DTO 映射基础字段：

- additions、modifications、deletions
- scene、path、type、id
- position、rotation、scale
- condition、properties

导出文件必须保持基础框架可识别的顶层结构。编辑器专属数据，例如 UI 收藏、未应用的命令历史、目录显示名或原始错误 token，不应塞入 canonical JSON；如确有需要，使用旁车 metadata 文件，并明确它不会被 Level Framework 读取。

导入策略：

1. 读取原始文本并保留原文件副本。
2. 解析 canonical JSON。
3. 校验顶层数组、字段类型、room key、creator type、ID 唯一性和变换值。
4. 对未知字段采取保留/忽略策略，但不擅自转写。
5. 对已知但不兼容的对象生成 ApplyReport 错误。
6. 成功时建立 LevelEditSession；失败时保留原始 JSON，不覆盖。

导出策略：

1. 仅导出当前 Room Identity 对应的文件。
2. 只写 Architect-owned 新增对象和允许的修改记录。
3. 对已删除的 Architect-owned placement 不再输出 additions。
4. 不写入玩家原版存档。
5. 使用临时文件写入、校验后替换，并保留上一版本备份。

### 4.5 Object Catalog 与原生对象

Object Catalog 是面向用户的对象目录，不等同于 Framework 内部 creator 字典。每个目录条目应包含：

- 稳定 type key
- 显示名和分类
- 来源：基础 Framework、Architect 自有适配、可选扩展建议
- 是否可新增
- 是否可预览
- 支持的变换
- 支持的 properties
- 依赖的 scene/path
- 失败时的诊断信息

第一版目录从基础 Framework 的已验证 creator 候选开始。由于 LevelRegister 的 creator 集合是 internal，不能仅依赖公开 API 自动生成完整目录。目标实现必须在以下方案中选择一个：

- Architect 自己维护经过验证的目录清单和运行时 creator 描述；
- 基础框架未来提供公开只读 creator 元数据；
- 在不成为默认依赖的前提下，由 LevelFrameworkExtended 或另一个 companion mod 提供公开目录能力。

本次规格选择第一种作为默认方案，第二/第三种只列为扩展建议。

原生对象的操作边界：

- Architect 新建的对象由 ObjectOwnership 标记为可编辑。
- 原版已有对象在首版只读，不进入拖拽/删除/属性写入流程。
- Framework 的 modifications/deletions 能力仍然在文档中保留，但首版不把它暴露为任意原版对象编辑器。
- 任意 creator 缺失时跳过该条目并显示原因。

### 4.6 RuntimePlacementManager

职责：

- 在当前房间把 LevelEditSession 的 placement 投影为 GameObject。
- 缓存 creator/scene loader 结果，避免每次移动都重新加载源场景。
- 为光标预览创建隔离的 preview instance。
- 处理移动、旋转、缩放、翻转和删除。
- 记录 ownership，防止误删原版对象。
- 在退出编辑模式或房间切换时清理临时对象。

不要把基础 Framework 的 OnLevelPreloaded 当作实时编辑 API。它适合在房间加载时应用持久化文件；当前房间内的拖拽和撤销必须由 RuntimePlacementManager 直接完成，导出后由 Framework 在下一次关卡预加载时复现。

### 4.7 UI 模块

首版 UI 采用运行时创建的 Canvas/屏幕空间界面，但要遵守 Blasphemous 自身的 UI 与输入状态管理：

- Editor root：编辑模式开关、当前房间、dirty 状态、错误状态。
- Object Browser：分页、搜索、分类、收藏预留。
- Inspector：position、rotation、scale、properties、ownership。
- Toolbar：选择、放置、拖拽、删除、复制、锁定、撤销、重做。
- Import/Export：选择当前房间文件、导入结果、备份、冲突提示。
- Report Panel：未知 creator、路径失败、条件不满足、版本不匹配、跳过数量。

进入/退出编辑模式时必须保存并恢复：

- Time.timeScale
- Core.Input blocker 状态
- UIController.Paused 状态
- 玩家控制/伤害/碰撞的临时修改
- HUD 和过场状态

不能只设置 Time.timeScale=0 后退出；所有被修改的状态都必须用 guard/transaction 方式恢复。

首版只保证键鼠。所有 UI 命令通过 Action 名称进入，不在领域层直接读取 KeyCode；未来可接入 Blasphemous.ModdingAPI 的 keybindings 约定。ModdingAPI 官方 README 说明配置位于 Modding/config，键位位于 Modding/keybindings，应优先遵循这一约定。

### 4.8 Event/Script 模块

Architect-Silksong 的 Legacy broadcaster/receiver 和 ScriptBlock 图形化系统不能直接移植。Blasphemous 版本定义：

- Domain Event：编辑器可持久化、可验证的事件描述。
- Runtime Event Adapter：把描述映射为目标游戏实际可用的事件/FSM/组件操作。
- Capability Registry：列出当前安装环境支持的事件节点。
- Unsupported Node：保留数据、禁止运行，并在报告中提示。

首版只实现已在 Assembly-CSharp 和目标 mod 环境确认过的事件子集。完整 Script Editor、变量端口、跨对象图、全局脚本和 Legacy 兼容属于 P1/P2，除非另有能力验证。

### 4.9 预加载、修复器和外部资源

Silksong 的 PreloadManager、AssetHelper、Fixers、RuntimeDetour 和外部 PNG/WAV/MOV 下载不能作为 Blasphemous 端默认依赖。迁移策略：

- 只为已验证的原生对象实现必要的源场景加载和缓存。
- 每种 creator 自己声明资源/场景依赖。
- 不把 Silksong 的全局 Fixer 体系整体复制。
- 不在首版允许任意远程资源下载。
- 需要修复器时，以单个 Blasphemous 对象适配为单位加入，并为每个 Hook 提供回滚/禁用开关。

## 5. 当前 Architect 全功能清单与迁移分级

分级含义：

- P0：首版编辑闭环需要；必须进入迁移实现规格。
- P1：目标平台适配后实现；需要额外 API 或验证。
- P2：后续产品阶段；不阻塞本地单机 JSON 编辑器。
- OUT：当前明确不实现，但必须在功能盘点中记录原因。

| # | Architect-Silksong 当前能力 | 主要实现证据 | Blasphemous 分级 | 迁移说明 |
|---:|---|---|---|---|
| 1 | 插件加载、依赖声明、模块初始化 | ArchitectPlugin.cs:50、59、92 | P0 | 重写为 BepInEx/ModdingAPI composition root |
| 2 | 主菜单 Level Sharer 入口 | Sharer/SharerManager.cs:41、74 | OUT | 首版不做在线分享 |
| 3 | 快捷键和编辑器选项配置 | Storage/Settings.cs:57、120、253、338 | P0 | 改用目标 keybindings/config 约定 |
| 4 | Map Editor、Script Editor、Workshop 模式切换 | Editor/EditorUI.cs:144、146、159 | P0/P1/OUT | Map P0；Script P1；Workshop 网络内容 OUT |
| 5 | 进入/退出编辑模式、自由移动 | Editor/EditManager.cs:246、253、264、498 | P0 | 改接 Penitent、Core.Input、UIController |
| 6 | 层切换、层可见性、重生点显示 | Editor/EditorUI.cs:1183、1243；RespawnMarkerManager.cs:57 | P1 | 目标层和重生点 API 未完全验证 |
| 7 | 对象目录、分类、分页、搜索 | Categories.cs:7、33；EditorUI.cs:215、1047 | P0 | 从基础 creator 候选建立 curated catalog |
| 8 | 收藏、Saved Object、Prefab 分类 | EditorUI.cs:257、866；StorageManager.cs:170、195 | P1 | 先做目录，保存对象与 Prefab 后置 |
| 9 | 鼠标悬停预览 | CursorManager.cs:19、69、103 | P0 | 需要 Blasphemous Sprite/scene loader preview |
| 10 | 单选、多选、框选、拖拽移动 | DragObject.cs:19；EditManager.cs:646、673、710 | P0 | 以 ownership placement 为操作对象 |
| 11 | 复制、粘贴、ID 重映射 | EditManager.cs:374、384；Settings.cs:141 | P0 | ID 必须在 session 和导出中保持唯一 |
| 12 | Cursor/Pick Tool 获取对象和属性 | CursorObject.cs:21、30；PickObject.cs:18、25 | P0 | Pick 只读取 Architect-owned placement |
| 13 | Eraser Tool 删除对象 | EraserObject.cs:19、29；ActionManager.cs:203 | P0 | 禁止删除原版对象 |
| 14 | 锁定/解锁对象 | LockObject.cs:17、25；ActionManager.cs:175 | P0 | 锁定只影响编辑器，不改变游戏对象语义 |
| 15 | 旋转、翻转、缩放、精确变换 | Settings.cs:64、78；EditorUI.cs:1072；EditManager.cs:464 | P0 | 需确认 Blasphemous Z 排序和精灵朝向 |
| 16 | 九槽 Hotbar 保存编辑状态 | EditManager.cs:53；EditorUI.cs:1162 | P1 | 可在核心编辑闭环稳定后加入 |
| 17 | 对象配置面板、配置类型、预览应用 | EditorUI.cs:571、608；ConfigType.cs:20 | P0 | properties 以 creator 能力表限制 |
| 18 | Legacy Receiver/Broadcaster 配置 | EditorUI.cs:677、761；ObjectPlacement.cs:216 | P1/P2 | 统一到目标事件抽象，不照搬组件 |
| 19 | 放置对象并保存完整 placement 生命周期 | PlaceableObject.cs:181、209；ObjectPlacement.cs:216；PlacementManager.cs:120 | P0 | 迁移核心对象抽象 |
| 20 | 覆盖对象、抓取 ID、放置时自动锁定/加入脚本 | PlaceableObject.cs:185、201、204 | P1 | 原版覆盖受 B 决策限制；脚本自动加入后置 |
| 21 | 撤销/重做、重置房间、重载场景 | ActionManager.cs:52、65；ResetObject.cs:31；EditManager.cs:580 | P0 | 脚本专用 Execute 空实现需避免复现 |
| 22 | Tilemap 添加/移除 tile | TileChangerObject.cs:30、45；LevelData.cs:21 | P1/P2 | 目标 tile 技术和回滚尚未确认 |
| 23 | Local/Global Script 编辑器 | ScriptEditorUI.cs:209、295、503；ScriptManager.cs:320 | P1/P2 | 先做事件数据模型和可执行子集 |
| 24 | 事件端口、变量端口连接 | ScriptManager.cs:102、225；ScriptBlock.cs:255 | P1/P2 | 需要目标事件能力表 |
| 25 | Script Comment 创建、编辑、保存 | ScriptEditorUI.cs:527、590、802 | P1 | UI/数据可独立移植 |
| 26 | Prefab 编辑器、展开 Prefab | EditorUI.cs:324；PrefabManager.cs:42；PrefabObject.cs:77、122 | P2 | 需要独立对象容器和 ID 重映射 |
| 27 | Workshop 内容创建/搜索/保存/删除 | WorkshopUI.cs:91、117、281、450；WorkshopManager.cs:29、65、83 | OUT/P2 | 首版无 Workshop 服务；本地 reusable content 可后置 |
| 28 | 自定义场景和地图接入 | CustomScene.cs:42；SceneUtils.cs:139、474、571 | P2 | 依赖 Blasphemous 自定义关卡和地图拓扑 |
| 29 | 本地关卡/Prefab/Workshop/全局数据保存 | StorageManager.cs:41、80、103；LevelData.cs:42 | P0 | 关卡 canonical JSON 改由 Level Framework 承担 |
| 30 | 项目管理、命名备份、恢复、删除 | ProjectManager.cs:10、50、58、65 | P1 | 首版先保证单文件原子写和备份 |
| 31 | 通过 API/其他 mod 导入地图 | Api/MapLoader.cs:11、24、59 | P1 | 首版可只支持 Framework JSON 文件导入 |
| 32 | 在线登录、上传、搜索、点赞、下载 | RequestManager.cs:25、133、313、440、478 | OUT | 明确不实现 |
| 33 | SSMP 联机同步编辑 | CoopManager.cs:8、20；SsmpManager.cs:89、147 | OUT | 明确不实现 |
| 34 | 预加载对象和显示进度 | PreloadManager.cs:36、91、110；PreloadObject.cs:22 | P1 | 只做 creator 所需资源缓存 |
| 35 | 外部 PNG/WAV/MOV 下载与缓存 | CustomAssetManager.cs:79、113、179 | OUT/P2 | 首版禁止远程资源执行 |
| 36 | Fixers、Hook、FSM、运行时行为修复 | ArchitectPlugin.cs:59、60；HookUtils.cs:13、34 | P1 | 只按目标对象和验证结果逐项重建 |

当前 Architect 还有两个必须在移植规格中注明的基线问题：

- ActionManager 中若干脚本动作的 Execute 为空，不能把 Silksong 当前脚本撤销行为当作已验证正确。
- Tilemap 越界、备份失败和部分资源失败存在静默吞异常；Blasphemous 版本应把失败纳入 ApplyReport 和日志，而不是复制静默行为。

## 6. Blasphemous 适配矩阵

| Silksong 概念 | Blasphemous 目标接缝 | 处理 |
|---|---|---|
| ArchitectPlugin | BepInEx plugin + Blasphemous.ModdingAPI BlasMod/service registration | 重新设计 |
| SceneUtils/GameMap | Framework.Managers.LevelManager、Level、LevelScene、Unity SceneManager | 使用 LevelManager 事件和 Level Framework 关卡键 |
| Scene load/apply | LevelFramework.OnLevelPreloaded | canonical JSON 由外部框架应用，实时编辑由 Architect 管理 |
| current scene object holder | Tools.Level.Layout.LevelInitializer.transform | 运行时新增对象的默认父节点候选，需烟测 |
| LevelData placements | ObjectData additions/modifications/deletions | DTO 映射，保持 Framework JSON |
| PlaceableObject | ObjectCatalogEntry + runtime object factory | 不依赖内部 creator 枚举 |
| PlaceableObject prefab | LevelFramework ILoader/SceneLoader 或 Architect 自有缓存 loader | 首版仅使用已验证源场景 |
| ConfigType | properties + creator-specific property editor | 不复制 Silksong 组件配置 |
| HeroController/EditManager movement | Penitent、Core.Logic、Core.Input、LevelInitializer.TimeScale | 只在开发者编辑状态修改并恢复 |
| UIManager.uiState | UIController、Core.Input blockers、目标 Canvas hierarchy | 需实际游戏烟测 |
| tk2dTileMap | Blasphemous 的实际 Tile/Collider/scene 结构 | 未验证，P1/P2 |
| Legacy EventManager/ScriptBlock | Blasphemous event capability adapter | 只实现已验证子集 |
| AssetHelper/PreloadManager | creator-specific SceneLoader/cache | 不迁移 Silksong AssetHelper |
| SSMP/Sharer | 无首版等价接缝 | OUT |

## 7. JSON 设计与示例语义

### 7.1 Canonical JSON

canonical JSON 必须以基础 Framework 能反序列化的三数组为核心：

    {
      additions: [ ... ],
      modifications: [ ... ],
      deletions: [ ... ]
    }

文档中的字段名使用小写，以匹配基础 LevelEdit/ObjectData 的 JsonProperty 定义。具体文件不应假设自己的版本字段会被基础框架读取。

新增对象至少需要：

- type
- id
- position
- rotation
- scale
- properties（没有属性时为空数组）
- condition（无条件时为空字符串）

修改/删除原版对象还需要：

- scene
- path

首版只允许 Architect-owned 新对象进入编辑命令栈；原版对象 modifications/deletions 只保留为未来能力和兼容读取边界，不能由默认 UI 任意操作。

### 7.2 Room Identity

Room Identity 至少由以下值组成：

- Framework level key，例如 DxxZxxSxx 类型的关卡键
- 当前目标 scene suffix，例如 _DECO、_LAYOUT、_LOGIC
- active Unity scene name
- source/export file key
- optional game build/framework version

导出文件名必须由 Room Identity 稳定生成，不使用当前玩家存档名作为唯一身份。

### 7.3 条件和 properties

基础 Framework 的条件语义支持 flag、penitence、gamemode。Architect 首版只允许目录条目声明已知条件，未知条件导入时报告错误并跳过，不执行任意表达式。

properties 不是通用反射写入接口。每个 creator 必须提供：

- 可接受 property key/type
- 默认值
- 解析失败行为
- 预览行为
- 运行时应用行为

## 8. 可实现在 LevelFrameworkExtended 中的功能

本节只提出扩展建议，不把 Extended 作为 Architect 依赖。

### 8.1 公开只读 creator 元数据

基础 LevelRegister 的 creator dictionary 和 TryGetLoader/TryGetModifier 是 internal。若 Architect 需要自动构建对象目录，Extended 可以提供只读服务：

- creator type/id
- display name/category
- source scene/path
- supported transform/property capabilities
- preview availability
- version/compatibility metadata

建议只公开元数据和能力查询，不公开内部 mutable dictionary。

### 8.2 公开 Level JSON DTO/codec

LevelEdit 当前是内部类型。Extended 可以提供稳定的 public DTO 或纯 JSON codec，避免外部工具使用反射获取内部类型。codec 应支持 additions/modifications/deletions、严格校验和逐项错误报告。

### 8.3 可选 .jsonc/$prev

Extended 当前的 .jsonc、注释、.json 优先级、$prev 继承和唯一 ID 生成适合作者工作流，但不是首版 canonical JSON 的必要条件。若后续加入，应由导出器显式选择，并确保最终生成的 JSON 仍可被基础 Framework 单独加载。

### 8.4 可选实时 apply/remove 服务

基础框架主要在关卡预加载时应用文件。若未来需要不重载房间就实时刷新，Extended 可以提供：

- apply additions
- remove Architect-owned additions
- reapply current room
- apply report
- lifecycle-safe cleanup

这类 API 必须定义对象 ownership 和重复应用语义，不能只暴露一个无状态的 Apply 方法。

### 8.5 更多原生 creators

Extended 现有 LevelObject Creators 可以作为 spikes、ladders、platforms、shelves 等对象的参考实现和可选 companion 内容。Architect 文档只记录其能力和依赖，不在首版假设安装了 Extended。

## 9. P0 编辑闭环

P0 的最低可验收闭环：

1. 用户在开发者开关开启时按热键进入编辑模式。
2. 游戏进入安全暂停/输入隔离状态，退出时完全恢复。
3. 当前房间建立 Room Identity。
4. Object Catalog 显示至少一个已验证的基础 Framework creator。
5. 用户可以预览、放置、选中、移动、旋转、缩放、删除自己创建的对象。
6. 用户可以编辑支持的 properties。
7. 用户可以撤销和重做这些动作。
8. 用户可以导入当前房间的 Framework JSON。
9. 用户可以导出 Framework-compatible JSON，并自动生成备份。
10. 离开并重新进入房间后，基础 Framework 能再次应用文件。
11. 未知 creator、坏路径、坏 properties、版本不匹配不会让游戏卡在编辑状态。
12. 原版存档不被写入，原版对象不被默认修改/删除。

## 10. 验证方案

### 10.1 主测试接缝

按已确认的 Q22=A，主测试接缝是 LevelEditSession/编辑会话：

    import JSON
      → create current-room session
      → place/move/delete owned objects
      → undo/redo
      → export JSON
      → reload room
      → re-import and compare observable result

UI、输入、对象目录、RuntimePlacementManager 和 LevelFrameworkAdapter 都作为该接缝的协作者；规格不绑定未来仓库的具体类名。

### 10.2 纯逻辑测试

- JSON 三数组解析和导出 round-trip。
- position/rotation/scale 的默认值和有限值校验。
- ID 生成、复制粘贴重命名和引用完整性。
- command undo/redo。
- unknown type/path/property 的逐项错误报告。
- conditions 的白名单校验。
- canonical JSON 与 editor sidecar 的边界。
- 原始 JSON 备份和原子替换策略。

### 10.3 游戏集成烟测

- 插件加载/依赖缺失时的降级。
- MainMenu → 房间 → 编辑模式 → 退出编辑模式。
- OnLevelPreLoaded、OnLevelLoaded 顺序和重复触发。
- 基础 creator 从源 scene 加载并 clone。
- 当前房间对象父节点、位置、旋转、缩放和 layer。
- 进入/退出编辑模式后 TimeScale、Input blocker、UIController.Paused、玩家控制全部恢复。
- 房间切换后旧对象没有残留。
- Framework 重载后 canonical JSON 结果一致。
- 仅有原版对象的房间不会被默认修改。

### 10.4 失败注入

- 删除或改名 creator source scene/path。
- 删除 Framework 或加载错误版本。
- JSON 缺少 additions/modifications/deletions。
- properties 类型错误。
- 重复 id。
- 未知 condition。
- 场景在导入过程中切换。
- 文件写入中断。
- Extended 不安装。

## 11. 风险登记

| 风险 | 严重性 | 处理 |
|---|---|---|
| Unity 2017.4 与 Architect Unity 6000 API/行为差异 | 极高 | 独立目标工程；每个 Unity API 建立实际运行时验证 |
| 当前 Architect 强耦合 Silksong 类型 | 极高 | 不共享 Silksong 运行时层，只迁移领域语义 |
| Framework 版本不一致 | 极高 | 实现前锁定实际 deployed DLL；当前基础 csproj 为 0.1.4，Extended 包引用为 0.2.0 |
| ModdingAPI 版本不一致 | 高 | 当前基础工程 2.4.1，Extended 3.0.1；不能混用未验证 API |
| Framework creator registry 非公开 | 高 | Architect 自有 curated catalog；提出可选公共元数据扩展 |
| Framework 没有完整实时 apply API | 高 | session/runtime manager 与 Framework adapter 分离 |
| SceneLoader 加载临时场景会触发 LevelInitializer | 高 | 使用已有 LevelInit patch 语义作为参考；实现时验证初始化抑制和清理 |
| 原版对象 path 不稳定 | 高 | P0 不允许任意修改原版对象；导入逐项报告 |
| Tilemap 技术未知 | 高 | P1/P2；先完成对象 additions 闭环 |
| 角色暂停/输入状态恢复失败 | 高 | state guard、退出异常 finally、烟测覆盖 |
| Extended 被误当成硬依赖 | 中高 | 依赖门禁和文档明确 optional |
| 复制 Silksong online/SSMP | 高 | 首版 OUT，不复制协议或服务端 |
| 静默吞异常 | 中高 | ApplyReport、ModLog、UI 摘要和原始 JSON 保留 |

## 12. 分阶段迁移路线

### Phase 0：事实与版本门禁

- 确认 Blasphemous 实际游戏 DLL、Unity 版本、ModdingAPI、Framework.Levels 版本。
- 比对本地 Framework 源码版本与实际 deployed DLL。
- 建立基础 creator 可用性表。
- 记录每个关卡的 Level key、scene suffix、源对象 path。
- 验证目标测试 profile 的安装/启动/日志路径。

完成条件：版本矩阵和最小 creator 清单可以在目标 profile 中复现。

### Phase 1：独立插件与生命周期

- 创建独立 Blasphemous 工程。
- 接入 BepInEx/ModdingAPI plugin entry。
- 依赖缺失/版本不匹配时安全禁用。
- 接入 LevelManager 生命周期桥。
- 建立 Room Identity。
- 建立键鼠动作和开发者开关。

完成条件：插件可进入/退出空编辑会话，不破坏正常游戏和房间切换。

### Phase 2：P0 对象编辑闭环

- 建立 curated Object Catalog。
- 通过已验证源 scene/creator 实现 clone 和 preview。
- 实现 PlacementRecord 和 ownership。
- 实现选择、放置、移动、变换、删除、属性编辑。
- 实现 command undo/redo。
- 实现安全暂停和状态恢复。

完成条件：当前房间可编辑 Architect-owned 对象，原版对象保持只读。

### Phase 3：Framework JSON round-trip

- 实现 LevelFrameworkAdapter。
- 导入基础三数组 JSON。
- 导出当前 room canonical JSON。
- 原子写入、备份、失败保留原文。
- 退出/重进房间验证 Framework 再应用结果。

完成条件：主测试接缝完整通过。

### Phase 4：P1 能力

- 收藏、Hotbar、Prefab 基础复用。
- 更丰富的属性类型。
- Blasphemous 事件抽象和已验证事件节点。
- 预加载进度和更完整诊断。
- 必要的单对象 fixer。

### Phase 5：P2/扩展建议

- 多房间/整张地图。
- Tilemap 或等价布局修改。
- 完整 Script Editor、Global Script、变量端口。
- Prefab 编辑器和本地 reusable content。
- Framework/Extended 公共元数据、codec、实时 apply 建议。

在线分享、联网 Workshop 和 SSMP 联机不在当前路线内。

## 13. 实施停止条件

未来实际编码时，出现以下任一条件应停在当前阶段并记录阻塞，而不是继续堆功能：

- 实际 Framework 版本与 JSON/creator 契约不一致。
- 基础 creator 无法在目标 profile 稳定加载。
- 退出编辑模式无法完整恢复输入、TimeScale 或玩家状态。
- JSON 导入/导出 round-trip 不稳定。
- 当前房间 reload 会产生重复对象、残留对象或修改原版存档。
- 目标游戏没有可回滚的 Tile/场景结构，却试图把 Tilemap 作为 P0。

## 14. 参考资料

本地主要证据：

- Architect-Silksong：C:\Users\28090\Documents\GitHub\Architect-Silksong
- Blasphemous Framework：C:\Users\28090\Documents\GitHub\Blasphemous.Framework.Levels
- Framework Extended：C:\Users\28090\Documents\GitHub\Blasphemous.LevelFrameworkExtended
- Blasphemous lightweight source：C:\Users\28090\Desktop\Games\Documents\渎神\渎神1\渎神 源码解包 仅Assembly-CSharp

官方参考：

- Blasphemous ModdingAPI：https://github.com/BrandenEK/Blasphemous.ModdingAPI
- Blasphemous ModdingAPI docs：https://github.com/BrandenEK/Blasphemous.ModdingAPI/tree/main/docs
- Unity 2017.4 SceneManager.sceneLoaded：https://docs.unity3d.com/2017.4/Documentation/ScriptReference/SceneManagement.SceneManager-sceneLoaded.html
- Unity 2017.4 SceneManager.LoadSceneAsync：https://docs.unity3d.com/2017.4/Documentation/ScriptReference/SceneManagement.SceneManager.LoadSceneAsync.html

最后更新：2026-08-23
