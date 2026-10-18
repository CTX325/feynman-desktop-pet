# 费曼 AI 桌面宠物

一个面向 Windows 的复古像素风理查德·费曼桌宠。项目在原有透明悬浮窗、动画和拖拽交互之上，加入了 Windows 本地中英文语音合成、Windows 本地语音识别、DeepSeek 对话和紧凑型 AI 对话面板。

![费曼桌宠动画预览](assets/feynman/preview_sheet.png)

> 本项目是非官方、个人学习性质的同人作品，与 Richard Feynman 本人及相关机构无关联。角色与声音均为风格化表达，不声称复制费曼本人的真实声音。

## 功能

### 桌面宠物

- 逐像素透明、无边框、可置顶的 WPF 悬浮窗口。
- 透明区域支持鼠标穿透，启动时不抢占键盘焦点。
- 默认尺寸 64 × 96 像素，并支持 48 × 72、96 × 144 原生像素尺寸。
- 待机、眨眼、行走、跳跃、睡觉、思考、讲解物理、点击回应和摸头回应。
- 拖拽位置、尺寸、置顶和自动漫步状态会自动保存。
- 系统托盘支持显示、隐藏、尺寸、置顶和退出。
- 隐藏到托盘时暂停动画、自动行为和语音任务，降低资源占用。

### AI 语音对话

- 点击桌宠旁边的麦克风快捷按钮开始录音，再次点击结束并识别。
- 独立 AI 对话面板提供开始录音、结束识别、取消录音、重新录音、编辑文本、提交问题、取消等待、停止朗读和清除对话。
- 使用 Windows 本地语音识别；识别文本不会自动发送，用户可先修改。
- 使用 DeepSeek `/chat/completions` 生成回答，默认模型为 `deepseek-chat`，可在设置中修改。
- AI 回答会显示完整文字，并可按设置自动朗读。
- 语音识别语言和回答语言是独立设置：回答可固定为简体中文、英文，或跟随当前问题的主要语言。
- 每次发送请求都会把当前回答语言作为最高优先级系统指令注入，不受旧对话语言影响。
- 对话历史只保存在当前进程内，最多保留最近 8 条消息。
- 请求超时、断网、HTTP 错误、限流、余额不足、无效响应和取消操作均有独立错误处理。
- 回答生成和语音播报均可取消；旧请求的回答不会覆盖新的问题。
- 没有 `DEEPSEEK_API_KEY` 时会显示配置提示，不影响桌宠继续运行。

### 中英文声音

- 使用 Windows SAPI 本地 TTS，不依赖网络或付费服务。
- 可选 eSpeak NG 本地男声适配器，直接调用已安装的 `espeak-ng.exe`，不需要注册 SAPI5。
- 自动检查本机声音，并优先选择中文成年男声；如果系统没有男声，会使用实际可用的中文声音。
- 中文和 English 声音分别保存、分别选择，不把单语言声音伪装成另一种语言。
- SAPI 与 eSpeak NG 声音不会混在一起显示；设置页只显示当前提供程序实际可用的声音。
- 支持语速调整、停止播报和测试中英语音。
- 朗读前会自动清理 Markdown、代码围栏、链接、路径和易被误读的符号；界面文字保持原样。
- 项目不会上传、保存或克隆任何费曼历史录音。

## 环境要求

- Windows 10 或 Windows 11
- .NET Framework 4.8
- Windows PowerShell 5.1
- Python 3.9 或更高版本，仅用于重新生成素材和运行完整测试

运行时不依赖 NuGet、npm 或外部语音 SDK。语音识别和 TTS 使用 Windows 自带组件。

eSpeak NG 是可选的本地方案。默认安装路径为：

```text
C:\Program Files\eSpeak NG\espeak-ng.exe
```

安装后无需注册 SAPI5，桌宠会直接调用它。当前版本可自动发现其中的中文男声 `cmn` 和英文男声 `en-us`。

## 启动

在项目根目录打开 PowerShell：

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
powershell -ExecutionPolicy Bypass -File .\run.ps1
```

构建产物位于：

```text
bin/FeynmanPet.exe
```

右键角色或托盘图标，选择 **AI 语音对话** 可打开对话面板。也可以直接点击角色旁边的麦克风快捷按钮。

## DeepSeek API Key 配置

程序只从环境变量 `DEEPSEEK_API_KEY` 读取密钥，不把密钥写入源码、设置文件、日志或仓库。

当前 PowerShell 会话临时设置：

```powershell
$env:DEEPSEEK_API_KEY = "<your-deepseek-api-key>"
.\bin\FeynmanPet.exe
```

如需为新启动的终端保留，请使用 Windows 的“系统属性 → 环境变量”，或执行 `setx DEEPSEEK_API_KEY` 后重新打开应用。不要把真实密钥写进 `.env.example`、INI、README 或截图。

DeepSeek 接口：

- 基础地址：`https://api.deepseek.com`
- 对话接口：`/chat/completions`
- 默认模型：`deepseek-chat`
- 模型名称可在对话面板的“语音与 AI 设置”中修改

DeepSeek 是付费 API，实际费用以 DeepSeek 官方账户和计费规则为准。项目不会自动发送问题，只有用户点击“提问”后才请求网络。

## 语音识别与声音配置

### 语音识别

对话面板和素材前会使用 Windows 本地识别器。可先运行：

```powershell
.\bin\FeynmanPet.exe --voice-diagnostics
```

报告写入 `logs/voice-diagnostics.txt`，不会包含音频或 API Key。

如果缺少语言包，请打开：

**设置 → 时间和语言 → 语言和区域 → 对应语言 → 语言选项 → 语音识别**

当前实现中：

- “自动”表示自动选择 Windows 已安装的识别器，不是对一段混合中英录音做语言识别。
- 已安装中文识别器时，可以选择中文录音。
- 如果没有英文识别语言包，英文录音会明确提示安装，不会返回伪造文本。
- 原始录音不保存到磁盘。

### 本地 TTS

Windows SAPI 声音的可用性取决于系统安装了哪些声音。打开对话面板中的“语音与 AI 设置”可查看当前识别器、声音和 API Key 状态。播放需要系统存在可用的默认音频输出设备。

中文和 English 声音分别保存为独立选项。设置页只列出当前 TTS 适配器真正能访问的声音；只注册在 OneCore 中、但不能被当前适配器稳定播放的声音不会被标为可用。

回答语言与语音识别语言分开设置。TTS 会根据最终回答文本选择对应语言的语音，并先清理 Markdown、URL、代码块、路径和装饰符号；清理结果不会替换界面中显示的原始回答。

如果想使用成年男声，请在 Windows 中安装并启用相应语言的男声；程序不会把女声或合成结果宣传为费曼本人的真实声音。

如果已安装 eSpeak NG：

1. 打开 **AI 语音对话 → 语音与 AI 设置**。
2. 将语音服务切换为 **eSpeak NG（本地男声）**。
3. 中文声音选择包含 `cmn` 的男声，English 声音选择包含 `en-us` 的男声。
4. 点击 **测试中英声音**。

如果要安装更多声音，请使用 Windows 的“设置 → 时间和语言 → 语音”添加对应语言的声音包。

## 操作说明

- 左键拖拽角色：移动桌宠，松开后保存位置。
- 单击头部：摸头回应。
- 单击身体：点击回应。
- 双击角色：开始讲解物理。
- 右键角色：动作、尺寸、置顶、自动漫步、AI 对话、隐藏和退出菜单。
- 麦克风快捷按钮：打开 AI 面板并开始/结束录音。
- 托盘图标：双击恢复桌宠；右键可显示、隐藏、调整设置或退出。

## 构建与测试

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
powershell -ExecutionPolicy Bypass -File .\tests\test.ps1
```

测试覆盖：

- 156 张 RGBA 动画帧的尺寸、透明通道和动画状态。
- 9 种原桌宠状态与透明/置顶/不抢焦点窗口行为。
- 三档实际窗口像素尺寸。
- 透明区域鼠标穿透、角色点击和右键菜单。
- 尺寸、位置和用户设置持久化。
- Windows 识别器、SAPI 声音、麦克风可用性诊断。
- 缺失 DeepSeek Key 时的安全错误提示。
- 仓库密钥模式扫描和 `.gitignore` 检查。
- AI 对话面板和麦克风快捷窗口能够创建。

额外的离线诊断：

```powershell
.\bin\FeynmanPet.exe --tts-text-test
.\bin\FeynmanPet.exe --espeak-playback-test
```

前者验证 Markdown/标点/公式清理与语言指令；后者实际调用已安装的 eSpeak NG 中文和英文男声，并验证停止播放。

如果当前环境没有麦克风、英文识别语言包或音频输出设备，测试报告会把它标记为不可用，而不会伪造录音、识别或播报成功。

## 素材生成

```powershell
python .\tools\generate_sprites.py
```

该脚本只使用 Python 标准库，生成三档原生尺寸透明 PNG、动画清单、预览图和托盘图标。

## 项目结构

```text
.
├─ assets/
│  └─ feynman/                 动画帧、预览、图标和 manifest
├─ config/
│  └─ assistant.example.ini    非敏感配置示例
├─ src/
│  ├─ FeynmanPet.cs            桌宠窗口、动画、托盘与程序入口
│  └─ Ai/
│     ├─ AssistantWindow.cs    对话面板和交互状态机
│     ├─ AssistantSettings.cs  本地非敏感设置
│     ├─ DeepSeekChatService.cs DeepSeek 异步对话
│     ├─ ESpeakTtsService.cs   独立 eSpeak NG 本地男声适配器
│     ├─ LanguageTools.cs      回答/朗读语言解析
│     ├─ TtsTextPreprocessor.cs 朗读专用文本清理
│     ├─ MicrophoneButtonWindow.cs 桌宠旁快捷麦克风按钮
│     ├─ VoiceService.cs       SAPI TTS、云端预留接口与诊断
│     └─ WindowsSpeechRecognitionService.cs Windows 本地识别
├─ tests/
│  ├─ test.ps1
│  ├─ test_assets.py
│  └─ test_repository.py
├─ tools/
│  └─ generate_sprites.py
├─ build.ps1
├─ run.ps1
├─ .env.example
├─ .gitignore
└─ README.md
```

运行后会生成以下本地文件，它们已被 `.gitignore` 排除：

```text
bin/                         构建产物
logs/runtime.log             运行日志（不记录对话和密钥）
logs/voice-diagnostics.txt   本地语音能力报告
data/pet-settings.ini        桌宠位置和尺寸
data/assistant-settings.ini  AI、语言和声音偏好
```

## 隐私与安全

- 不硬编码 API Key；`DEEPSEEK_API_KEY` 和 `ELEVENLABS_API_KEY` 只从环境变量读取。
- 不保存原始录音，不自动上传录音。
- 不把识别文本、AI 回答或 API Key 写入日志。
- `data/`、`logs/`、`bin/`、音频临时文件和构建缓存均已排除。
- DeepSeek 请求启用超时和取消，并限制上下文历史长度。
- 桌宠是本地客户端应用，无法把 API Key 完全隐藏于本机进程；不要把含密钥的环境变量或配置文件上传到公开仓库。

## 云端语音与合规说明

`CloudTtsService` 目前是预留适配层，默认禁用，也不会自动发起付费请求。若未来启用 ElevenLabs 或其他云端 TTS，必须先确认官方接口、费用、模型的中英文能力、声音使用授权和账号配额。

项目不会下载或上传费曼历史录音，不会尝试声音克隆，也不会绕过服务商的声音验证。即使将来加入独立的授权声音，也不应宣传为费曼本人的真实录音。

## 已知限制

- Windows 本地识别一次使用一个已安装语言；混合中英文连续语音的自动语言判定受系统语言包限制。
- 当前实现没有独立的“说话”动画，朗读时复用“讲解物理”动画；没有可用声音时恢复待机。
- 云端 TTS 尚未启用，只有本地 TTS 可用。
- 本项目是客户端直连 DeepSeek；正式分发时必须避免把密钥写入客户端或仓库。

## 许可证

仓库当前未附带开源许可证。如果计划允许他人复制、修改或分发代码与素材，请在公开发布前补充合适的许可证文件。
