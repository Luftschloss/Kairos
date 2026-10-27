# KAIROS 图片生成与编辑

供 Codex 在当前工程中制作美术资源。`imagegen.ps1` / `imagegen.py` 加载本机配置，再调用已安装的 Codex `imagegen` 技能 CLI；没有在 Unity 或游戏客户端中保存密钥，也没有修改技能脚本。

本次写实卡牌系列位于 `ArtSource/Kairos/RealisticCards/`，包含 56 份独立场景设计、提示词、原图、可筛选的新旧对照图库和构图焦点。使用说明见该目录 `README.md`；本地整备/缩图/校验由 `realistic_cards.py` 完成，所需 Pillow 可使用已有 Codex Python 环境中的版本。

## 本机配置

项目根目录 `.env.imagegen.local` 已加入 Git 忽略。首次使用只需在本机编辑该文件，填写 `OPENAI_API_KEY=` 后的值。不要把完整 Key 发到聊天或写进可提交文件。

```dotenv
OPENAI_BASE_URL=https://tc-paperhub.diezhi.net/v1
OPENAI_API_KEY=在本机填写
OPENAI_IMAGE_MODEL=gpt-image-2
```

新机器先将 `.env.imagegen.example` 复制为 `.env.imagegen.local`。配置值为字面量，可用单引号或双引号包围；不执行变量展开，不支持行尾注释。文件不会由 OpenAI SDK 自动加载，由项目入口显式读取。

- 生成：`https://tc-paperhub.diezhi.net/v1/images/generations`
- 编辑：`https://tc-paperhub.diezhi.net/v1/images/edits`
- 可选模型：`gpt-image-2`、`gpt-image-2.5-flare`、`gpt-image-2.5-sunburst`。默认 `gpt-image-2`，用 `--model` 切换；不会因失败自动切换模型或服务。
- Key 仅取自项目配置，不回退到进程中其他服务的 `OPENAI_API_KEY`。执行时只给子进程设置 URL 和 Key，不更改用户全局配置；不传递继承的 OpenAI Organization / Project ID。

检查本机状态（不联网、不输出 Key）：

```powershell
& ./Tools/Kairos.Art/imagegen.ps1 status
```

## 环境安装

本机使用项目内 `.venv-imagegen`。在新机器上，从项目根目录执行：

```powershell
uv venv .venv-imagegen --python 3.12
uv pip install --python .venv-imagegen/Scripts/python.exe -r Tools/Kairos.Art/requirements-imagegen.txt
```

也可使用自己的 Python 环境安装上述依赖，然后直接运行 `python Tools/Kairos.Art/imagegen.py ...`。需已安装 Codex `imagegen` 技能，入口为 `$CODEX_HOME/skills/.system/imagegen/scripts/image_gen.py`，未设置 `CODEX_HOME` 时使用用户目录的 `.codex`。可选的 CLI 缩图参数需要另装 Pillow；本项目默认不缩图。

## 生成

从项目根目录执行；所有相对路径都以项目根目录为基准。首次先加 `--dry-run` 检查参数；去掉该参数后才会向配置服务发送请求并产生费用。

```powershell
& ./Tools/Kairos.Art/imagegen.ps1 generate --model gpt-image-2.5-flare --prompt 'KAIROS 卡牌插画：黄铜钟楼与时砂，暗色版画风，无文字和边框。' --size 1024x1536 --quality medium --out ArtSource/Kairos/GptImage/clocktower-v1.png --dry-run
```

长提示词可换为 `--prompt-file ArtSource/Kairos/GptImage/prompts/clocktower.txt`，使用 UTF-8 文件。入口默认不自动扩写提示词；需要技能的提示词结构化功能可添加 `--augment`。

## 编辑、参考图与蒙版

```powershell
& ./Tools/Kairos.Art/imagegen.ps1 edit --model gpt-image-2.5-sunburst --image ArtSource/Kairos/GptImage/clocktower-v1.png --prompt '只把天空改成暮色，保留钟楼轮廓、构图和版画质感。' --out ArtSource/Kairos/GptImage/clocktower-v2.png --dry-run
```

重复 `--image` 可输入多张参考图，提示词中说明每张图的作用；`--mask path/to/mask.png` 指定局部编辑区域。蒙版使用带 Alpha 通道的 PNG，与第一张原图具有相同尺寸，透明区域为待编辑区域。蒙版是模型参考，不保证区域外逐像素不变。

当前安装的技能 CLI 对两款 `gpt-image-2.5-*` 接受 `auto`、`1024x1024`、`1024x1536`、`1536x1024`；这是本机 CLI 的参数限制，不代表服务只支持这些尺寸。需要其他规格时应先检查技能支持情况。完整选项用 `generate --help` 或 `edit --help` 查看。

## 批量与 Unity 导入

`generate-batch --input <jobs.jsonl> --out-dir ArtSource/Kairos/GptImage/<batch-name>` 可使用技能的批量生成；先用 `--dry-run` 检查，任务的模型与参数可在 JSONL 中指定。

原图及编辑版本保存在 `ArtSource/Kairos/GptImage/`，使用描述性文件名和版本号。选定后按现有美术流程裁切/缩放到 384×512，再替换 `Assets/Kairos/Art/Resources/KairosArt/Portraits/<CardKey>.png` 的内容；保留原 `.meta` 和 key。此入口只生成文件，不自动替换运行资源或提交 Git。

默认不覆盖已有输出。技能 CLI 可能在收到 API 结果后才检查文件是否存在，因此调用前应选择新的版本文件名，避免重复付费。AI 输出是位图，不自动获得现有 SVG/ORA 的可编辑图层。

原有程序化版画工具仍为 `generate.py` / `render.ps1`，使用说明见 `ArtSource/Kairos/EtchedCards/README.md`。

## 验证与排错

离线自动验证：`.venv-imagegen/Scripts/python.exe Tools/Kairos.Art/test_imagegen.py -v`。测试使用 HTTP 模拟响应，覆盖三个模型的生成与多参考图/蒙版编辑、Base64 文件保存、密钥隔离与错误日志脱敏，不连接远端服务。

`status` 和 `--dry-run` 都不联网，不能证明远端 Key、模型权限或编辑协议可用。真实生成通常返回 `data[].b64_json`；若代理只返回图片 URL，需要其提供兼容的 Base64 响应。401/403 检查 Key 和权限，404 检查 Base URL 和路由，400 检查模型和参数。网络受限时由 Codex 请求对应网络访问权限，不要把 Key 加进命令行参数。

官方协议参考：<https://developers.openai.com/api/docs/guides/image-generation>
