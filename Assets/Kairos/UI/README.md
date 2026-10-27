# KAIROS UI 框架与 Prefab 定位

## 入口

- 场景仍为 `Assets/Kairos/Scenes/KairosDemo.unity`，GameController负责游戏状态和交互绑定。
- UI根：`Assets/Kairos/UI/Resources/KairosUI/UIRoot.prefab`。
- 资源目录：`Assets/Kairos/UI/Resources/KairosUI/`。
- Inspector汇总：`Assets/Kairos/UI/UiPrefabCatalog.asset`。
- 编辑器菜单 **KAIROS → UI → Navigator**：按页面/面板名搜索，定位或打开Prefab；显示运行中的当前页面与弹窗状态。

## 整体结构

```text
UIRoot (Canvas / CanvasScaler / GraphicRaycaster / UiFramework)
└─ Screen (1600×900逻辑区域)
   ├─ PageLayer   一个当前页面
   ├─ ModalLayer  一个顶层弹窗，遮挡底层输入
   └─ EffectsLayer 拖拽提示、指向连线（弹窗打开时放到底层）
```

页面/弹窗切换统一由 `UiFramework` 管理。打开规则不会再把收藏页跳回菜单；关闭后回到原页。战斗帮助/离开仍沿用暂停AI和抢拍计时规则。

正常运行只实例化Prefab、按命名槽绑定数据；**不会自动生成或写回Prefab**。缺失槽报具体templateId与key，避免偷偷降级成另一套运行布局。

## 页面索引（12个）

|目录Pages/|用途|
|---|---|
|Home.prefab|局外主页、模式入口、选择配方、继续进度|
|Collection.prefab|收藏、筛选、选中卡详情入口＋工坊|
|DeckPicker.prefab|玩家/对手牌组选择|
|WorkshopSlots.prefab|12槽、新建、复制、删除|
|KeeperPicker.prefab|钟主选择|
|Profile.prefab|钟主熟练度与背包说明|
|Receipts.prefab|最近永久账单|
|Battle.prefab|战斗与教学共用画面|
|PrivacyCurtain.prefab|热座隐私交接，完全不生成手牌/卡面|
|Campaign.prefab|旅程地图、奖励、休整、通关/失败|
|RemoveCard.prefab|旅程删牌|
|LoadFailure.prefab|档案加载失败与重试|

页面包含所有当前条件分支的槽，未使用节点会关闭。不要把inactive槽当垃圾删除；例如主页的“继续进度”仅存在保存对局时启用。

## 常用子面板（Panels/）

- `Header.prefab`：共用游戏标题与副标题。
- `CollectionFilters.prefab`：搜索和七类Dropdown。
- `CollectionGrid.prefab`：9张卡列表。
- `CollectionFooter.prefab`：分页、选中牌、加牌/合成入口。
- `Workshop.prefab`：右侧牌组编辑。
- `ActionWindow.prefab`：行动权/抢拍/等待状态。
- `Timeline.prefab`：时轴、局部范围、支付预览与确认。
- `BattleSidebar.prefab`：时器、蓄势、静候、卡牌详情、砂、预言、确认、历史。
- `FutureObjects.prefab`：侧栏未来物件滚动区。
- `TutorialSidebar.prefab`＋`TutorialTask.prefab`：步骤说明与当前任务。
- `PlayerInfo0.prefab` / `PlayerInfo1.prefab`：双方钟主、命烛/时标/刻印。
- `Board0.prefab` / `Board1.prefab`：双方侍从行。
- `Hand.prefab`：手牌行。
- `Mulligan.prefab`：起手调度提示与确认。
- `BattleHint.prefab`：手牌上方操作提示。
- `CardDetailsScroll.prefab` / `CraftLines.prefab` / `ChoiceCards.prefab`：长文字/合成/选择滚动区。
- `Board0_*` / `Board1_*`：各侍从空槽；`WorkshopSlots_*`：各配方槽子面板。名称中的坐标仅是定位编号，运行显示位置由Prefab保存。
- `Assets/Kairos/UI/Archive/`保留10个迁移过程中不再引用的旧空场模板，不在Resources或活动目录中加载；当前修改Board0/Board1即可。

页面里的子面板使用真实嵌套Prefab引用。已验证修改Header子Prefab的位置会传到Collection，重新刷新也不被代码覆盖。

## 弹窗索引（14个，Modals/）

`TutorialWelcome`、`TutorialIntro`、`TutorialComplete`、`TutorialLeave`、`Rules`、`Leave`、`Choice`、`Result`、`Craft`、`CardDetails`、`UnsavedWorkshop`、`DeleteDeck`、`Abandon`、`SaveError`。

每个有独立Prefab和Body；长文/选择滚动区为嵌套子面板。打开新的弹窗关闭旧顶层弹窗，避免多层遮罩残留。底层页不接收点击。

## 控件与特效

原有七控件保留：`CardFace`、`HandCard`、`BoardUnit`、`CollectionTile`、`KeeperCard`、`KeeperBadge`、`CollectionFilter`。

Effects/：`ActionLine`、`DragTip`、`TargetDot`。动态连线位置/长度与拖拽游标仍由运行时计算，但外观来自Prefab。

卡面继续用MiSans、写实插画、九宫格学派框与独立选中高光，不改卡牌规则/收藏数据。

## 如何修改与绑定

1. 用Navigator打开目标Prefab，修改RectTransform、Image颜色、Text字体/字号、图层顺序。
2. **不要改 `UiElementSlot.key`**；它是稳定绑定标识，代码传入的原始坐标只是查找旧布局槽的ID，不再给静态节点赋值。
3. `UiElementSlot.sourceRect`是原始定位信息，不是运行布局。`dynamicLayout=false`时修改RectTransform即可永久生效。
4. `bindColor=true`表示该颜色由状态绑定（例如合法/非法预览、行动状态）；其他节点颜色由Prefab保持。
5. `dynamicLayout=true`仅用于数据变化的列表/时轴/滚动文字；此类节点的数量和位置受数据驱动。手牌/侍从行使用 `UiRepeaterLayout` 的cellSize、spacing、columns、origin，方便整体调密度。
6. 动态卡牌内容、按钮是否可用、Dropdown选项、输入值、当前状态仍由代码绑定；不要把样例文字当成正式内容。
7. 场景运行实例临时修改不会保存，永久修改在Edit Mode的Prefab Mode完成。

## 边界

- 本轮是**现有UI布局的Prefab化和统一层级管理**，并未把规则/局外服务合并到UI框架。
- 控制器仍有旧的Rect参数和条件构建方法用于定位槽、绑定动作；不是每个页面一个独立Presenter。静态外观来自资源，动态数量/位置还需数据逻辑。
- 首次编辑期迁移通过模板捕获覆盖条件分支，不是运行期截图保存。`UiPrefabMigration`是本轮编辑工具，**不应在用户已有手工编辑后重新捕获覆盖**；正常UI不会调用它。
- 未提交Git，未重新打包Windows播放器。所有Prefab/脚本已持久化，原GUID、图像、字体和用户其他改动保留。

## 验证

- .NET与Unity EditMode：86/86，新增全部页面/弹窗/关键子面板存在性检查。
- 模板绑定：145个含嵌套实例的面板索引检查，无重复key。
- 实际检查Home、Collection、七类Dropdown、规则返回原页、钟主/档案/账单/配方、详情/合成、Battle/抢拍/效果选择、旅程五状态与删牌。
- 验证模板编辑保留、嵌套卡牌拖拽、热座无卡面、弹窗射线遮挡、动态9条账单和后段时轴。
- 一次存档备份回归遇到Windows瞬时文件替换失败，单测及完整重跑通过；本轮未修改存档实现。
