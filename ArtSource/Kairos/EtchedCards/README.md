# KAIROS 分层版画卡牌与 Prefab UI

> 旧版几何美术归档：当前 56 张运行插画已替换为 [RealisticCards 写实系列](../RealisticCards/README.md)，运行尺寸为 768×1024。下文的分层源与 384×512 说明属于旧版；重新运行本目录记录的 `generate.py` / `render.ps1` 会覆盖写实运行插画。六套原学派边框仍沿用。

## 交付范围

- **46种卡牌＋10名钟主**，围绕刻律之城、黄铜钟楼、腕钟、时砂与五时学派制作原创暗色版画。
- 用户选择本地版画风；这是几何剪影／刻线风格的程序化原创插画，**不是写实AI绘画，不复用昆特牌原画或角色**。
- 6套学派边框：中立、晨、昼、暮、夜、永；稀有度标记由UI独立绘制。
- 卡名、数值、规则、状态不烧进图；可继续调整平衡和中文文案。
- 完整源图在Unity之外，运行版图像与UI Prefab在Assets内。

## 源文件与分层

`ArtSource/Kairos/EtchedCards/<CardKey>/`：

|文件|用途|
|---|---|
|layered.svg|600×800矢量源；命名分组，在支持SVG图层的工具中编辑|
|editable.ora|OpenRaster分层源，600×800；可用Krita/GIMP等打开；**不是伪装扩展名的PSD**|
|01_atmosphere.png|底色、天空、远处时间光盘|
|02_clock_city.png|黄铜城市、钟塔、桥梁与透视刻线|
|03_subject.png|角色、器物、树木或时间异象主体|
|04_foreground.png|前景石台、齿轮、草叶、火星或星光|
|05_engraving.png|刻线、纸纹与边缘暗角（透明叠加）|
|merged.png|五层合成预览，不含文字/边框|
|thumbnail.png|OpenRaster内部缩略图及外部小预览|

`ArtSource/Kairos/EtchedCards/contact-sheet.png` 为56张总览；`manifest.json`记录每图key、学派、主体意象、固定生成种子与分辨率。

源图使用共同视觉语言与部分人物基础轮廓；道具、服饰、学派色彩及时间异象区分角色。可直接替换某个主体层，不必重做其他层。不要把程序化基础美术当成最终商业级手绘。

## Unity运行资源

- 插画：`Assets/Kairos/Art/Resources/KairosArt/Portraits/<CardKey>.png`，384×512。
- 边框：`Assets/Kairos/Art/Resources/KairosArt/Frames/<School>.png`，384×512，透明。
- 导入：不生成mipmap，Clamp/Bilinear、sRGB、Uncompressed、最大512；边框为Sprite，插画为Texture2D（RawImage使用）。
- 完整分层源不在Resources中，不随播放器加载；运行时仅加载合成小图。
- `CardVisual`按key缓存图像；绑定时按插画区域比例裁切，避免纵图被横向拉伸。
- 不应改文件key或删除meta重新创建；替换PNG内容即可保持绑定和GUID。

## Prefab拆分与编辑

全部位于 `Assets/Kairos/UI/Resources/KairosUI/`：

|Prefab|实际用途|
|---|---|
|CardFace.prefab|收藏详情的完整纵卡面|
|HandCard.prefab|战斗手牌：插画、费用／基础体型、较大卡名、可用状态；规则通过侧栏查看|
|BoardUnit.prefab|场上侍从：插画、当前攻击/生命/节奏、关键词、疲惫/就绪状态|
|CollectionTile.prefab|收藏列表与预见/召回选择：插画、类别、拥有/选择状态；完整规则在详情中|
|KeeperCard.prefab|钟主选择与档案：肖像、时器费用/冷却、能力、权限/熟练度|
|KeeperBadge.prefab|战斗双方钟主：肖像、名称和当前命烛|

打开Prefab Mode调整`Illustration`、`SchoolFrame`、`Title`、`Cost`、`Stats`、`Rules`、`Metadata`、`Status`。锚点按占比设置，实例仅设置所在位置与足迹大小，不重建内部布局。

根组件 `CardVisual`通过序列化引用绑定上述部件；根Button处理点击。美术/文字子节点raycastTarget关闭，避免挡住拖拽。新增UI子层应同样关闭raycastTarget，除非它确实需要独立操作。

**页面外层仍由现有代码组织**：菜单、模态框、时轴、配方按钮未全部Prefab化。卡牌相关表现已切换Prefab；这次没有把整个客户端重写。

`KAIROS → Art → Create Missing Card Prefabs`只创建缺失Prefab，不覆盖已存在的手工布局。后续改布局以Prefab为准，不要删Prefab后重建来丢弃用户修改。

## MiSans字体替换

用户新增字体位于 `Assets/Kairos/Art/Resources/Fonts/`：

- **MiSans-Regular**：正文、数值和说明。
- **MiSans-Bold**：卡名、主要标题、手牌状态/关键数值。
- **MiSans-Normal**：保留，当前不作为默认正文（Regular更适合小字号）。

三个字体导入为Dynamic＋HintedSmooth，基准导入字号24。`UiFonts`加载项目内字体，不再依赖Microsoft YaHei等本机字体。

- 六Prefab都已持久引用MiSans；CardVisual只替代空/内置Latin字体，不覆盖用户后来指定的其他字体。
- 页面生成文字通过Label统一使用MiSans（23px及以上标题用Bold）。Canvas开启pixelPerfect。
- 原手牌规则会缩到9px，现不在小卡面强塞完整规则；卡名17px（长名最低14）、状态14、主要数值13。悬停/选中手牌后在右侧17px正文区域查看效果。
- 大卡与钟主条目仍有有限Best Fit处理长文案，字体/最小字号/区域均可在Prefab调整。
- 当前1600×900逻辑画布整体等比缩放；Game窗口很小（例如实测高496像素）时所有文字仍会变小。MiSans和像素对齐不能补足不足的实际像素。建议16:9、至少1280×720，阅读完整牌文用详情。
- 字体授权文件未由本任务提供；正式分发前请核对用户字体来源与其附带许可。

## 重新生成与验证

从项目根执行：

```sh
python Tools/Kairos.Art/generate.py
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Tools/Kairos.Art/render.ps1
python Tools/Kairos.Art/generate.py --package
python -m unittest discover -s Tools/Kairos.Art -v
```

生成器仅用Python标准库；渲染使用Windows System.Drawing，无需Pillow或在线图像服务。重新生成会覆盖本套源SVG/PNG/ORA，**如已手工修改先备份或另存新版本**。Unity运行图像内容更新后刷新导入，不改meta。

验证记录：

- 4项美术测试：56张key完整且PNG不同、五层SVG/PNG与ORA栈一致、六套边框、无烧入文字/外部原图。
- .NET规则测试85/85；Unity EditMode85/85（规则未变）。
- 实际检查教学、收藏、钟主选择、完整详情；确认页面使用MiSans、收藏正文无截断告警。
- 分帧拖拽验证Prefab子图像不阻挡部署目标；点击射线命中卡牌根Button；没有提交正式存档收益。
- 最终源文件和Prefab已持久化；Unity已返回Edit Mode。**尚未生成新的Windows独立运行包。**
