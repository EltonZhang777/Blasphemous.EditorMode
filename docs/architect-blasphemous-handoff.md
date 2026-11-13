# Architect → Blasphemous 续接摘要

## 目标

用户要把 Architect-Silksong（当前版本 3.33.10）的运行时关卡编辑器移植到 Blasphemous 1。当前会话只生成技术文档，不创建移植代码；实际实现将另建仓库。

## 已确认决策

- 目标：Blasphemous 1 PC；Unity 2017.4.40f1 是当前指引基线，实际实现前要用部署 DLL 验证。
- 入口：游戏内热键，受开发者/调试开关保护。
- 首版范围：当前房间 Room Overlay。
- 持久化：Blasphemous.Framework.Levels 是必需外部依赖，使用其 additions/modifications/deletions JSON。
- Extended：Blasphemous.LevelFrameworkExtended 不作为默认依赖；只有基础框架缺少绝对必要能力时，才在扩展建议中提出能力。
- 首版对象：新增、移动、变换、属性编辑、删除 Editable Overlay Placement；原版对象只读。
- JSON：导入 + 编辑 + 导出，错误逐项跳过并报告，原文件备份。
- 事件：统一目标事件抽象，只执行已验证事件子集。
- Tilemap/多房间/Standalone Level：完整盘点，能力验证后再分级。
- 首版只做本地单机；在线分享、网络 Workshop、联机 OUT。
- 首版键鼠；输入动作抽象。
- 规格主测试接缝：LevelEditSession 的导入 → 编辑 → undo/redo → 导出 → 重载 round-trip。

## 已生成产物

- 技术概述：architect-blasphemous-technical-overview.md
- 迁移规格：architect-blasphemous-migration-spec.md
- 领域术语：CONTEXT.md

这些文件包含完整功能矩阵、目标架构、Framework JSON 契约、Blasphemous 生命周期映射、风险、扩展建议、用户故事、测试和阶段路线。不要在续接会话中重复生成；先读取并在其基础上工作。

## 已验证事实入口

- Architect-Silksong：C:\Users\28090\Documents\GitHub\Architect-Silksong
- 基础 Framework：C:\Users\28090\Documents\GitHub\Blasphemous.Framework.Levels
- Extended：C:\Users\28090\Documents\GitHub\Blasphemous.LevelFrameworkExtended
- Blasphemous lightweight Assembly-CSharp：C:\Users\28090\Desktop\Games\Documents\渎神\渎神1\渎神 源码解包 仅Assembly-CSharp

基础 Framework 的关键事实：

- LevelFramework 在关卡预加载阶段应用 additions、modifications、deletions。
- ObjectData 包含 scene、path、type、id、position、rotation、scale、condition、properties。
- ObjectCreator 由 ILoader + IModifier 组成；基础 creator registry 的枚举和查询有 internal 边界。
- 基础工程声明 0.1.4，但 Extended 的包引用为 0.2.0；ModdingAPI 版本也不同，必须先核对实际部署 DLL。
- Extended 的主要额外能力是原生 LevelObject creators、对象 modifiers、.jsonc/$prev 加载增强。

## 下一步

1. 新会话先读取三个本地产物。
2. 按 Phase 0 建立实际 ModdingAPI/Framework/GameLibs/Unity 版本矩阵。
3. 验证基础 creator 的实际加载、房间 identity 和 LevelManager 事件顺序。
4. 设计独立 Architect-Blasphemous 工程的 LevelEditSession、LevelFrameworkAdapter、RuntimePlacementManager 和 UI/Input 边界。
5. 以主测试接缝完成 P0 JSON round-trip 和房间重载烟测，再推进 P1。

## Suggested skills

- blasphemous-modding-helper：分析目标游戏源码、ModdingAPI 约定和运行时日志。
- domain-modeling：继续维护领域术语和边界。
- migration：实现与外部 Level Framework 的兼容、安全、可回滚迁移。
- tdd：为 JSON codec、LevelEditSession 和 command undo/redo 建立行为测试。
- to-spec：若用户重新要求发布 issue tracker 规格，先确认 tracker 配置再使用。
- handoff：再次切换会话时生成续接摘要。

## 当前限制

本会话没有创建代码、修改两个外部 Framework 工程、发布 issue 或实现在线功能。
