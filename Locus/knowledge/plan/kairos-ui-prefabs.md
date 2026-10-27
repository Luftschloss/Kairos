---
id: kd_af2b18ca-0697-476c-9b2e-3f422f7914e4
injectMode: inherit
injectAgents:
- unity
aiEditMode: inherit
---

# KAIROS UI Prefab迁移执行记录

目标：所有页面、子面板、弹窗有可定位的Prefab；布局由Prefab控制，代码仅绑定数据/交互，页面/弹窗统一管理。

## 拆分

- UIRoot：Canvas、1600×900 Screen、PageLayer、ModalLayer、EffectsLayer、UiFramework。
- Pages：Home、Collection、DeckPicker、WorkshopSlots、KeeperPicker、Profile、Receipts、Battle、Campaign、RemoveCard、LoadFailure。
- Panels：Header、BattleSidebar、TutorialSidebar、Timeline、ActionWindow、Workshop、PlayerInfo、Board、Hand、Mulligan、PrivacyCurtain等。
- Modals：TutorialWelcome/Intro/Complete、Rules、Leave/TutorialLeave、Choice、Result、Craft、CardDetails、UnsavedWorkshop、DeleteDeck、Abandon、SaveError。
- Widgets：保留原CardFace/HandCard/BoardUnit/CollectionTile/KeeperCard/KeeperBadge/CollectionFilter，并提供列表/文字/按钮槽。

## 迁移原则

现有布局将通过命名绑定槽提取为编辑期Prefab模板，覆盖全部条件分支。运行期只查找命名槽并绑定文本/事件/启用状态，不重新赋值静态RectTransform、字号、字体、颜色。动态列表数量与时轴物件/拖拽几何仍由游戏数据驱动。

现有用户修改和Prefab GUID保留；只创建缺失模板，新框架不能运行时自动重写资源。验证包括Prefab编辑能在刷新后保留、全部页面与弹窗、热座隐私、Dropdown遮挡和拖拽。

状态：已完成12页面、14弹窗、主要子面板与7控件的Prefab绑定，提供UIRoot框架、UiPrefabCatalog与KAIROS/UI/Navigator。静态Prefab改动刷新后保留验证通过；动态列表/时轴仍由数据驱动，旧Rect参数仅作为绑定槽身份。

验证：.NET/Unity 86/86；覆盖导航、七类筛选、弹窗遮挡/返回原页、嵌套卡牌拖拽、热座无卡面、旅程五阶段、9条动态账单和后段时轴。完整定位说明：Assets/Kairos/UI/README.md。未重建独立播放器。
