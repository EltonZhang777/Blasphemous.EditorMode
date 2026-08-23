# Architect for Blasphemous 迁移规格

状态：需求已确认；本文件是本地规格文档，不发布到 issue tracker。

## Problem Statement

Architect-Silksong 是一个为 Silksong 构建的带 UI 的运行时关卡编辑器。它已经提供对象目录、放置、选择、变换、属性、事件、脚本、Prefab、Workshop、本地存储、场景修改、预加载、在线分享和联机等能力，但其实现深度绑定 Silksong 的程序集、Unity 版本、场景结构、对象生命周期、输入/UI 系统和在线服务。

目标用户希望将这套编辑体验迁移到 Blasphemous 1，并且：

- 完整记录当前 Architect-Silksong 版本的全部用户功能；
- 以当前房间的 Room Overlay 作为首版目标；
- 通过 Blasphemous.Framework.Levels 保存和应用兼容的关卡 JSON；
- 支持本地单机 JSON 导入、编辑、导出；
- 只允许首版编辑 Architect 自己创建的对象，原版对象只读；
- 不在首版实现在线分享、网络 Workshop 或联机；
- 保留可扩展的事件/脚本方向，但只执行已经验证的 Blasphemous 事件子集；
- 将 Tilemap、多房间、整张地图、Standalone Level 和完整脚本系统列为需要额外验证的后续能力；
- 不把 Blasphemous.LevelFrameworkExtended 作为默认依赖；
- 本会话只生成规格和 handoff，不承担具体代码实现。

当前的主要问题不是“把几个类名替换为 Blasphemous 类名”，而是需要在不同的运行时生命周期和数据契约之间重新建立边界：编辑会话负责实时用户操作，Level Framework 负责关卡 JSON 的加载和应用，Blasphemous 适配层负责场景与输入/UI 状态。

## Solution

创建一个独立的 Architect-Blasphemous mod 工程，遵循 BepInEx 和 Blasphemous.ModdingAPI 的项目惯例，并把 Architect-Silksong 作为功能基线而不是共享运行时依赖。

核心方案：

1. 以 LevelEditSession 作为最高层用户行为接缝，承载当前 Room Identity、Architect-owned Placement、编辑命令和 undo/redo。
2. 以 BlasphemousLifecycleBridge 连接 LevelManager 的关卡生命周期，在关卡加载后启动编辑会话，在离开关卡时清理运行时对象。
3. 以 RuntimePlacementManager 提供当前房间的实时预览和对象操作，不把基础 Level Framework 的预加载流程误当作实时编辑 API。
4. 以 LevelFrameworkAdapter 读写基础 Framework 兼容的 canonical JSON；不修改 Framework 源码。
5. 以 curated Object Catalog 暴露已经验证的原生对象；首版不依赖内部 creator registry 的自动枚举。
6. 以 UI、输入、属性编辑、错误报告组成游戏内编辑器界面；首版支持键鼠并通过动作抽象保留手柄扩展点。
7. 以 capability registry 管理事件/脚本/Tilemap等不确定能力；未验证能力只能导入、保留或报告，不得静默执行。
8. 将 Extended 的 creator、公开元数据、JSON codec、实时 apply 等能力作为可选扩展建议，不放进 Architect 默认依赖链。

## User Stories

### 编辑会话与入口

1. As an editor developer, I want to enable a protected editor mode, so that normal players do not accidentally modify a room.
2. As an editor developer, I want to enter the editor with an in-game hotkey, so that I can inspect the current room without leaving gameplay.
3. As an editor developer, I want the editor to identify the current room and its scene role, so that exported content is applied to the intended room.
4. As an editor developer, I want to exit editor mode safely, so that player control, time scale, input blockers, HUD state, and pause state are restored.
5. As an editor developer, I want the editor to reject activation outside a compatible gameplay room, so that menu, loading, cinematic, and transition states are not corrupted.
6. As an editor developer, I want the editor to show whether the current session is clean or dirty, so that I know whether an export is required.
7. As an editor developer, I want the editor to show the active room identity, so that I do not export a layout to the wrong level.
8. As an editor developer, I want the editor to recover to a stable room state after an import failure, so that one bad file does not leave the game unusable.

### 对象目录与预览

9. As an author, I want to browse a catalog of verified Blasphemous placeable objects, so that I can build content without memorizing scene paths.
10. As an author, I want objects grouped by meaningful categories, so that hazards, platforms, items, chests, and utilities are easy to find.
11. As an author, I want to search the object catalog, so that large catalogs remain usable.
12. As an author, I want each catalog entry to display its capability status, so that I know whether it supports preview, properties, transform, or runtime placement.
13. As an author, I want to preview an object before placing it, so that I can judge its appearance and approximate collision footprint.
14. As an author, I want preview instances isolated from gameplay, so that inspecting an object does not activate its gameplay behavior.
15. As an author, I want unsupported creators to be hidden or clearly marked, so that I do not create content the target runtime cannot load.
16. As an author, I want catalog entries to report their source and compatibility, so that an optional extension is not mistaken for a core dependency.
17. As an author, I want to preserve the possibility of favourites and saved objects, so that frequently used content can later be reused efficiently.

### 放置、选择与变换

18. As an author, I want to place a verified object in the current room, so that I can author a new gameplay arrangement.
19. As an author, I want every placed object to receive a stable unique id, so that it can be selected, copied, removed, and serialized.
20. As an author, I want to select one placed object, so that I can inspect and change it.
21. As an author, I want to select multiple placed objects, so that I can perform batch operations efficiently.
22. As an author, I want to drag selected objects, so that positioning does not require repeated numeric entry.
23. As an author, I want to enter exact position values, so that repeated layouts can be aligned precisely.
24. As an author, I want to rotate a placed object, so that orientation can match the intended scene.
25. As an author, I want to scale a placed object when the creator supports scaling, so that the layout can be tuned without changing the source asset.
26. As an author, I want to flip a placed object when the creator supports flipping, so that directional objects can be reused.
27. As an author, I want to control depth or sorting values when the target object supports it, so that 2D layering remains predictable.
28. As an author, I want to copy and paste placed objects, so that repeated structures can be authored quickly.
29. As an author, I want copied objects to receive non-conflicting ids, so that references and exports remain valid.
30. As an author, I want a pick tool to copy an existing Architect-owned placement into the cursor, so that I can reuse its properties.
31. As an author, I want a cursor tool to inspect an Architect-owned placement, so that I can understand its identity and transform.
32. As an author, I want to lock an Architect-owned placement, so that accidental edits do not change a finished part of a layout.
33. As an author, I want to delete an Architect-owned placement, so that I can revise the layout.
34. As an author, I want the editor to prevent deletion of original game objects, so that authoring remains a non-destructive overlay.
35. As an author, I want the runtime to distinguish original objects from Architect-owned objects, so that ownership controls are enforced outside the UI as well.

### 属性与对象行为

36. As an author, I want an inspector for creator-supported properties, so that I can configure objects without editing JSON manually.
37. As an author, I want each property to declare its type and valid values, so that invalid data is rejected before runtime application.
38. As an author, I want property changes to update the preview when safe, so that the inspector reflects the final object.
39. As an author, I want a failed property change to leave the previous value intact, so that editing is reversible.
40. As an author, I want object-specific properties represented as Framework properties, so that exported JSON remains compatible with the persistence backend.
41. As an author, I want unsupported properties preserved or reported rather than silently discarded, so that an imported layout can be repaired.
42. As an author, I want event-capable objects to expose only verified event options, so that a script cannot invoke unknown Blasphemous behavior.
43. As an author, I want event data to remain separate from Unity component implementation details, so that future adapters can support additional target events.

### 撤销、重做与恢复

44. As an author, I want to undo placement, movement, transform, property, lock, copy, and deletion operations, so that experimentation is safe.
45. As an author, I want to redo an undone operation, so that I can compare alternative layouts.
46. As an author, I want undo/redo to operate on domain commands rather than fragile scene references, so that scene reload does not corrupt the history model.
47. As an author, I want to reset the current editing session without changing the original save, so that I can discard experimental work.
48. As an author, I want a failed command to produce a visible report, so that the editor never pretends an operation succeeded.
49. As an author, I want a backup before destructive export or reset, so that the previous layout can be recovered.

### JSON 导入、导出与 Level Framework

50. As an author, I want to import a Framework-compatible level JSON, so that I can continue editing an existing layout.
51. As an author, I want to export a Framework-compatible level JSON, so that the Level Framework can apply my layout when the room loads.
52. As an author, I want import and export to round-trip additions, modifications, and deletions, so that editing does not lose supported data.
53. As an author, I want exports scoped to the current Room Identity, so that one room does not overwrite another room's content.
54. As an author, I want the canonical JSON to remain separate from editor-only metadata, so that the Level Framework is not given unsupported fields.
55. As an author, I want a failed import to preserve the original file, so that I can repair it manually or retry with another runtime.
56. As an author, I want import to validate creator type, room key, transform values, ids, conditions, and properties, so that invalid content is found before application.
57. As an author, I want unknown creators skipped individually with a report, so that one unavailable object does not erase the rest of a layout.
58. As an author, I want duplicate ids detected before export, so that the Framework receives unambiguous object data.
59. As an author, I want export writes to be atomic and backed up, so that an interrupted write cannot destroy a working layout.
60. As an author, I want the editor to work when LevelFrameworkExtended is absent, so that the core mod has a stable minimum dependency set.
61. As an editor developer, I want the editor to state which capabilities are supplied by the base framework and which are only suggestions for Extended, so that installation requirements stay honest.
62. As an editor developer, I want the mod to detect Framework version mismatches, so that an incompatible JSON contract does not silently corrupt a level.
63. As an editor developer, I want the editor to respect base Framework conditions such as flags, penitence, and gamemode, so that authored content follows the target runtime's conditional semantics.

### 场景生命周期与运行时安全

64. As an editor developer, I want the editor to observe Blasphemous level transitions, so that a session is created only for a loaded room.
65. As an editor developer, I want the editor to apply runtime previews after the room is ready, so that object creation does not race LevelInitializer or player spawning.
66. As an editor developer, I want all Architect-owned runtime objects removed or reconciled when leaving a room, so that objects do not leak into the next room.
67. As an editor developer, I want repeated level-load callbacks to be idempotent, so that the same placement is not spawned twice.
68. As an editor developer, I want the editor to avoid writing the original save, so that authored layouts remain independent from player progression.
69. As an editor developer, I want a room reload to reconstruct the same observable layout from JSON, so that runtime edits are persistent without mutating packaged scenes.
70. As an editor developer, I want the editor to stop applying changes during loading, fading, and cinematics, so that game lifecycle invariants are preserved.
71. As an editor developer, I want a complete diagnostic report when a scene path cannot be found, so that an object definition can be corrected without guesswork.

### UI、输入与诊断

72. As an editor developer, I want a clear editor canvas with browser, inspector, toolbar, import/export controls, and status report, so that the workflow is discoverable.
73. As an input user, I want all P0 actions mapped through named actions, so that physical key bindings can change without changing domain logic.
74. As an editor developer, I want the editor to use the target ModdingAPI configuration and keybinding conventions, so that it behaves consistently with other Blasphemous mods.
75. As an editor developer, I want the UI to show current room, dirty state, selected object, dependency status, and last apply result, so that debugging is possible without reading logs.
76. As an editor developer, I want errors and warnings to be both logged and summarized in the UI, so that non-fatal degradation is visible.
77. As an editor developer, I want the editor disabled with an actionable explanation when dependencies are missing, so that normal gameplay can continue.
78. As an editor developer, I want the interface to reserve space for gamepad navigation and localization, so that later UI expansion does not require replacing the domain layer.

### 脚本、Tilemap、Prefab 与后续能力

79. As an author, I want a unified event abstraction to represent verified Blasphemous events, so that script content is not tied to Silksong-only components.
80. As an author, I want unsupported script nodes preserved as unsupported data, so that importing a layout does not destroy author intent.
81. As an editor developer, I want a capability registry for event nodes, so that the UI exposes only runtime-supported operations.
82. As an author, I want Tilemap or equivalent layout edits only when the target runtime supports safe rollback, so that P0 object editing is not destabilized by uncertain tile behavior.
83. As an author, I want multi-room identity reserved in the document model, so that later map-wide editing does not invalidate current-room files.
84. As an author, I want a future Standalone Level mode to be distinct from Room Overlay, so that new scenes and original room patches are not conflated.
85. As an author, I want future Prefab support to preserve nested ownership and id remapping, so that reusable content does not produce duplicate references.
86. As an editor developer, I want online sharing, network Workshop, and multiplayer kept out of the first release, so that local persistence can be stabilized first.

## Implementation Decisions

### 工程边界

- Blasphemous 版本使用独立工程/仓库；当前 Architect-Silksong 只作为功能基线和证据来源。
- 不共享 Silksong 的 Assembly-CSharp、DataManager、ModMenu、AssetHelper、SSMP、Patchwork 或 QuickWarp 运行时依赖。
- 使用 BepInEx 和 Blasphemous.ModdingAPI 的目标游戏惯例；目标编译框架以实际 ModdingAPI/GameLibs 依赖为准，不沿用当前 netstandard2.1 假设。
- 当前会话不创建移植代码。

### 依赖与能力

- Blasphemous.Framework.Levels 是首版必需外部依赖。
- Architect 不修改基础 Framework 源码。
- Blasphemous.LevelFrameworkExtended 不作为默认依赖，也不提供默认兼容分支。
- 如果基础 Framework 缺少绝对必要能力，只在单独的扩展建议中描述可实现于 Extended 的能力。
- 依赖门禁必须记录实际加载版本并拒绝未知不兼容版本。

### 领域模块

- LevelEditSession：用户编辑会话、Room Identity、owned placements、命令和结果。
- PlacementRecord：对象 type、id、transform、properties、ownership、状态。
- ObjectCatalog：面向用户的对象元数据、分类、能力和兼容性。
- RuntimePlacementManager：当前房间 GameObject 投影、预览、清理和幂等。
- LevelFrameworkAdapter：canonical JSON 导入/导出、校验、备份和报告。
- BlasphemousLifecycleBridge：LevelManager 与 LevelInitializer 生命周期。
- UI：Canvas、浏览器、检查器、工具栏、导入导出和诊断面板。
- Input：命名动作、键鼠首版、手柄扩展接缝。
- Events：统一事件抽象、能力注册和不支持节点保留。
- Diagnostics：依赖门禁、逐项错误、导入摘要、日志和恢复。

### 关卡与对象语义

- Level 首版以当前 Room Overlay 为目标，不改写打包 Unity 场景。
- 原版对象只读；Architect-owned 对象可以新增、移动、变换、修改属性、锁定、复制和删除。
- 原版对象的修改/删除数据结构保留在兼容模型中，但不作为首版任意编辑 UI。
- Level Framework additions/modifications/deletions 是 canonical JSON 的持久化结构。
- Room Identity 包含 Framework level key、目标 scene suffix、active scene name 和导出文件身份。
- editor-only metadata 不混入 Framework canonical JSON。

### 错误与恢复

- 单个对象失败时尽量继续，逐项记录错误，并生成导入结果摘要。
- 原始 JSON 在导入前备份；导出采用临时文件、校验、替换和上一版本备份。
- 未知 creator、scene/path、property、condition 或版本不匹配不会静默执行。
- UI、日志和 ApplyReport 必须同时可见失败原因。
- 任何暂停、输入、HUD、玩家控制状态都通过可恢复 guard 管理。

### 功能优先级

- P0：编辑模式、对象目录、预览、放置、选择、移动、删除、变换、属性、ownership、undo/redo、Framework JSON round-trip、错误报告。
- P1：收藏、Hotbar、Prefab 基础复用、事件子集、预加载进度、单对象 fixer、更多属性和目录。
- P2：完整 Script Editor、Tilemap、Prefab 编辑器、多房间、整张地图、Standalone Level、Workshop 本地内容。
- OUT：在线登录、上传、搜索、下载、网络 Workshop、SSMP 联机、远程外部资源下载。

## Testing Decisions

### 主测试原则

测试外部可观察行为，不测试具体私有实现。主接缝是 LevelEditSession/编辑会话：

导入 JSON → 创建当前房间会话 → 编辑 Architect-owned 对象 → undo/redo → 导出 JSON → 房间重载 → 再导入并比较结果。

### 必须覆盖的模块

- canonical JSON codec：字段、默认值、条件、properties、错误、round-trip。
- LevelEditSession：命令、ownership、dirty state、undo/redo、id。
- ObjectCatalog：能力过滤、缺失 creator、预览状态。
- RuntimePlacementManager：幂等创建、变换、清理、原版对象保护。
- LevelFrameworkAdapter：文件范围、备份、原子写入、版本门禁。
- BlasphemousLifecycleBridge：加载事件顺序、重复事件、房间切换。
- UI/Input：热键、状态恢复、错误摘要和动作映射。
- Events：能力注册、不支持节点和运行时子集。

### 游戏烟测

- 缺少基础 Framework。
- Framework 版本与 JSON 契约不匹配。
- Extended 安装与未安装两种环境。
- MainMenu、普通房间、过场、加载中、房间切换期间启用/禁用编辑器。
- 基础 creator 加载、预览、放置、变换、删除。
- JSON 导入、导出、房间重载。
- 原版对象只读和 Architect-owned 对象可编辑。
- TimeScale、Core.Input、UIController.Paused、玩家控制和 HUD 的完整恢复。
- 多次触发 OnLevelPreLoaded/OnLevelLoaded。

### 失败注入

- JSON 语法错误。
- 缺失三数组。
- 未知 type。
- 缺失 scene/path。
- 重复 id。
- 非有限变换。
- 不支持 property。
- 不支持 condition。
- 源 scene/path 不存在。
- 文件写入中断。
- 房间在导入/应用中切换。
- 目标 creator 在 Extended 存在但基础依赖不存在。

### 验收标准

P0 只有在主接缝、依赖缺失降级、房间重载一致性、错误恢复和原版存档安全都通过后才算完成。单纯 UI 可见或单次放置成功不构成完成。

## Out of Scope

- 本会话创建或修改具体 Blasphemous 移植代码。
- 修改 Blasphemous.Framework.Levels 源码。
- 将 Blasphemous.LevelFrameworkExtended 加入 Architect 默认依赖。
- 一次性实现全部 Architect-Silksong 功能。
- 在线登录、上传、点赞、搜索、下载和网络服务。
- SSMP 或其他多人联机同步。
- 远程图片、音频、视频下载与执行。
- 任意原版 GameObject/Component/Script 的开放式反射编辑。
- 首版整张地图连接、传送拓扑和 Standalone Level。
- 未经验证的 Tilemap 写入。
- 未经验证的完整可视化脚本、Legacy broadcaster/receiver 复制。
- 修改玩家原版存档或把布局写入存档槽。
- 提供 issue tracker 发布或自动创建 issue。

## Further Notes

1. 当前基础 Framework 工程的本地版本信息与 Extended 的包依赖存在差异：基础工程声明 0.1.4，Extended 引用 0.2.0；两者的 ModdingAPI 版本也不同。实现仓库必须先以实际部署 DLL 建立版本矩阵。
2. 基础 Framework 的 creator 注册更接近“其他 mod 注册关卡对象”的服务，而不是完整的编辑器目录 API。Object Catalog 需要自己的稳定元数据层。
3. Extended 的 .jsonc/$prev 逻辑适合作者工作流，但 canonical JSON 首版应先保持基础 Framework 独立可加载。
4. 当前 Architect-Silksong 功能矩阵和平台适配证据见配套技术概述；本规格只保留实现决策和用户可验证行为。
5. 未来如果需要把本规格拆为 agent 任务，应先按 Phase 0 → Phase 1 → Phase 2 顺序执行，并在每个阶段满足停止条件后再推进。
6. 本文档按用户要求保存在仓库 docs 目录；handoff 文档应位于操作系统临时目录，并引用本地规格和技术概述，而不是重复全文。
