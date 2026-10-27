# 写实版卡牌资源

依据世界观、角色档案、卡牌名称/机制/背景文案，为 46 张卡牌与 10 名钟主分别设计叙事插画。统一方向为写实暗色奇幻绘画，详见 [美术规范](ART_DIRECTION.md)。

本轮已完成全部 56 张，使用 `gpt-image-2.5-sunburst`、高质量模式，原图 1024×1536。运行版 PNG 已替换，旧版 PNG 与 GUID 已保存和校验。Unity 正在打开工程时，需要让编辑器刷新资源/完成脚本编译后查看；本次未重建独立运行包。

打开 [审阅图库](gallery.html) 可按学派、卡牌/钟主和名称筛选；点击卡片查看完整原图、背景描述与提示词，并切换新旧版本。全套总览为 [contact-all.jpg](contact-all.jpg)。

原图在 `originals/`；实际提示词在 `prompts/`，`manifest.json` 记录模型、质量、尺寸与来源文件哈希。`scenes.json` 保存每张图的独立场景设计。所有图片通过项目配置服务的 `imagegen` 技能 API/CLI 生成。

运行插画目标路径：`Assets/Kairos/Art/Resources/KairosArt/Portraits/<Key>.png`。本套导入版为 768×1024，默认纹理导入上限为 1024；保留每张原 `.meta` 的 GUID。`integration.json` 仅在整套图片齐全且导入完成后生成，记录实际运行文件的哈希。`previous-runtime/` 保存替换前的插画和 meta。

卡名、数值、规则和学派边框由现有 Unity Prefab 单独绘制。原版几何画的 SVG/ORA 分层源仍保存在 `../EtchedCards/`；新写实图是位图原稿，不宣称具有不存在的可编辑图层。

`KairosArt/portrait-framing.json` 保存逐卡纵向构图焦点，`focusY` 从图片顶部 0 到底部 1。`CardVisual` 根据插画区域比例裁切时读取该值，避免横向卡面把人物头部裁掉；`ui-crop-proof.jpg` 是按 CardFace 实际插画比例生成的布局模拟校验图，不是 Unity 游戏截图。

## Unity UI适配与选中反馈

- 56张运行图已在Unity中验证，保持原PNG路径与GUID，不重复复制或重新创建资源。
- 手牌Prefab高度调整为188，插画显示区从107增至129；详情CardFace采用300×400，插画区280×330，减少原横向区域对纵向主体的裁切。
- 保留逐卡`portrait-framing.json`焦点；不拉伸原图，按显示区域裁切。
- 六种卡牌Prefab新增独立`SelectionHighlight`与`ActionHintHighlight`。选中为明亮金色牌框；可行动为较细的提示框。选中时隐藏普通提示框，插画和底色不改色。
- 六学派Frames统一九宫格：Sprite Border为左/下/右/上各72像素，FullRect、PPU100；Prefab的`SchoolFrame`使用Sliced、FillCenter关闭、PixelsPerUnitMultiplier=4。边角按18个逻辑单位显示，中段可伸缩；不再整张拉伸牌框。可在Sprite Editor和Prefab Image组件调整。
- 适用于手牌、场上来源/目标、收藏条目、效果选择和当前钟主；高光关闭raycastTarget，不拦截点击/拖拽。
- 字体继续使用MiSans；卡面不烧入规则文字，详情右侧显示完整文案。
- 验证：56插画×6组件共336次绑定与UV范围检查；实测选中/取消颜色不变、点击和拖拽正常；源图哈希/原GUID测试通过。尚未重新打包Windows版本。

## 本地维护

使用带 Pillow 的 Python，从工程根目录运行：

```powershell
python Tools/Kairos.Art/realistic_cards.py status
python Tools/Kairos.Art/realistic_cards.py prepare
python Tools/Kairos.Art/realistic_cards.py contact
python Tools/Kairos.Art/realistic_cards.py gallery
```

`prepare` 校验 56 个 Key 并更新未完成任务清单，不联网且不覆盖已有提示词。若修改提示词，需自行保存新的图片版本；不要把旧的完整图片误认为新提示词的生成结果。任务文件通过既有 `imagegen.ps1 generate-batch` 执行；重新调用付费接口之前先核对清单确实只包含待生成项目。

`integrate` 会检查全套文件完整性，然后备份并替换运行版 PNG，修改默认纹理尺寸上限并记录 GUID。只有需要正式替换整套运行资源时才运行此命令。此入口不构建独立播放器。
