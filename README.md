# 棋弈有道

用团结引擎（Tuanjie，兼容 Unity 2022.3 LTS）开发的 Windows 单机围棋。运行时从空场景自举，产品名为“棋弈有道”。

## 功能

- **打谱**：固定 19 路摆谱；手数 / 坐标 / 形势（Bouzy 估目）；退出主界面时自动保存进度
- **人机对弈**：开局前选定 9 / 13 / 19 路、执黑 / 执白 / 猜先、贴目与让子、电脑对手；局内可开关手数 / 坐标 / 形势（与打谱相同）
- **保存棋谱**：终局弹窗可填写对局名称、双方昵称与段位、描述；时间与结果由系统填入
- **历史棋谱与 KataGo 复盘**：
  - 棋谱搜索与步进浏览（上一手 / 下一手 / 终局）
  - **全局胜率分析**：调用 KataGo 生成全盘胜率走势折线图与目数差曲线，标记恶手（红色）与疑问手（橙色）；支持点击曲线直接跳转对应手数，分析结果自动缓存至本地棋谱库
  - **变化推演（PV）**：当前局面展示 KataGo 前 5 推荐选点（标有序号、胜率及目数差），悬停自动推演后续变化图（带手数序列）；支持点击棋盘进入自由试下推演模式，并可一键「恢复实战」
- 完整规则：提子、打劫（Zobrist 全局同形禁着）、自杀拦截、双停数子、死子标记
- 悔棋、停一手、认输、清空棋盘

## 电脑对手

人机开局可选三种对手：

### KataGo（可选）

调用用户自行安装并配置的本机 [KataGo](https://github.com/lightvector/KataGo) GTP 引擎（10 路神经网络，g170e-b10c128）。当前仓库和 Windows 构建输出**不附带** KataGo 可执行程序、模型或配置；用户需自行取得并放到 `GoGame.exe` 同级目录的 `Tools/KataGo/`：

```text
<GoGame.exe 所在目录>/
  GoGame.exe
  Tools/KataGo/
    katago.exe
    default_model.bin.gz
    gtp.cfg
```

- `katago.exe`（优先使用；按 `GoKataGo.ResolvePaths` 规则，也可用同目录的 `katago-eigen.exe` 作为 CPU 备用）
- `default_model.bin.gz`（精确文件名，不能用任意其他 `.bin` 文件代替）
- `gtp.cfg`（精确文件名；每手约 400 visits / 最多 4 秒，本仓库提供示例配置）

编辑器运行时的对应目录是项目根目录 `Tools/KataGo/`；Windows 成品运行时则是 `GoGame.exe` 同级的 `Tools/KataGo/`。构建脚本不会打包或复制 KataGo 文件，只会检查项目目录中上述精确文件是否存在并输出明确日志。首次用 OpenCL 时可能自动调优，第一手会稍慢。若程序、`default_model.bin.gz` 或 `gtp.cfg` 任一不可用，选择 KataGo 开局时会提示“未找到 KataGo，已切换本地引擎”，然后正常使用本地 MCTS；不会阻塞普通对局。

### 本地引擎（MCTS）

参考 [Michi](https://github.com/pasky/michi) / [Pachi](https://github.com/pasky/pachi)：

- UCT + RAVE 树搜索，每手约 2–4 秒
- 先验：提子、3x3 定式、靠近最近战场、避开低线空旷与自打吃
- 模拟：优先打吃 / 逃吃，匹配 3x3 邻域，拒绝填眼

适合离线游玩。棋力有限，不是神经网络引擎。

### 大模型

将当前棋盘发给 **xAI / OpenAI 兼容** Chat Completions 接口，由模型返回一个坐标（如 `Q16`）再落下。非法着点会重试一次，仍失败则退回本地引擎。

配置方式（任选其一）：

1. 人机设置页「大模型密钥」输入框（存在本机 PlayerPrefs）
2. 项目根目录或 `persistentDataPath` 下的 `llm_key.txt`
3. 环境变量 `XAI_API_KEY` 或 `OPENAI_API_KEY`

默认接口 `https://api.x.ai/v1/chat/completions`，默认模型 `grok-4-fast`。密钥请到 [xAI Console](https://console.x.ai) 自行申请，不要提交进 Git。

## 运行

- **Windows 构建版**：运行 `Builds/Windows/GoGame.exe`。
- **编辑器启动入口**：用 Tuanjie 2022.3.62t14（或 Unity 2022.3 LTS）打开工程，打开 `Assets/Scenes/Main.unity` 后按 Play；空场景会自动生成完整运行时界面。
- `Assets/Editor/AutoPlay.cs` 仅用于编辑器自动运行，可按需保留或移除。

## 主菜单入口

主菜单固定提供四个入口：**打谱**、**人机对弈**、**历史棋谱**、**退出**。右上角进入设置，右下角提供主音量/静音快捷控制。

## 时间与读秒

- 本时选项：无限制、5 分钟、1 小时、2 小时、自定义；自定义范围为 **1–600 分钟**，0 和 601 分钟拒绝保存。
- 读秒选项：无、3×30 秒、5×30 秒、5×60 秒、自定义；自定义为 **1–20 段、每段 5–600 秒**。
- 对局右侧四按钮保持单行：悔棋、停一手、数目、认输；复盘页另提供上一手、下一手、回到终局。

## 棋谱位置与兼容性

- 对局草稿：Unity `Application.persistentDataPath` 下的 `study_progress.json`。
- 历史棋谱库：`Application.persistentDataPath` 下的 `kifu_db.json`；导出的 SGF 位于同目录的 `KifuExports/`。
- 读取旧 JSON 时，缺失的 `mode`、`result`、`stateJson` 等可选字段保持兼容，不会因缺字段导致启动异常。

## 测试

### EditMode 测试

测试资产位于 `Assets/Tests/Editor/`，包括时间、计时、结果、棋盘主题、SGF 和 UI token 测试。使用编辑器菜单 `Window → General → Test Runner` 选择 EditMode；也可使用命令行：

```powershell
Tuanjie.exe -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults Logs/editmode-results.xml -logFile Logs/editmode.log -quit
```

本轮发布候选验收结果：标准 EditMode 共 42 项，42 项通过，0 项失败，0 项跳过；项目自定义规则、存档和 AI 回归也全部通过。

### 项目自定义回归

规则、存档和 AI 的既有自定义入口为 `TestRunner.RunAllTests`：

```powershell
Tuanjie.exe -batchmode -nographics -projectPath . -executeMethod TestRunner.RunAllTests -logFile Logs/custom-tests.log
```

## 操作

鼠标单击交叉点落子。主界面：**打谱** / **人机对弈** / **历史棋谱** / **退出**。

对局右侧：落子记录、悔棋、停一手、认输、清空、数目、返回。打谱与人机左下角可开关手数 / 坐标 / 形势。复盘时用上一手、下一手、终局浏览棋谱，可开启「AI推演」查看推荐选点与变化图，或点击「全局分析」生成整盘胜率走势折线图。

## Windows 构建

编辑器菜单 `Build → Windows EXE`，或命令行：

```powershell
Tuanjie.exe -batchmode -quit -projectPath . -executeMethod BuildScript.BuildWindows -logFile build.log
```

产物固定为 `Builds/Windows/GoGame.exe`。构建脚本会自动生成并保存 `Assets/Scenes/Main.unity`、写入 Build Settings，并在成功日志输出 `BUILD OK`。

## 当前环境限制

本轮已完成脚本编译、标准 EditMode 42/42、项目自定义回归、Windows 构建，以及 1920×1080、1600×900、1366×768 三种分辨率的玩家启动烟测。仍需在具备真实 KataGo 程序与模型的环境中补做引擎联调；人工点击流程、视觉细节、真实音频播放和独立干净 Windows 机器验收仍建议在发布前由 QA 完成。

## 代码结构

```
Assets/Scripts/GoRules.cs        规则引擎（纯 C#）
Assets/Scripts/GoAI.cs           本地 MCTS + RAVE
Assets/Scripts/GoKataGo.cs       KataGo GTP 对接
Assets/Scripts/GoLlmAi.cs        大模型选点（xAI / OpenAI 兼容）
Assets/Scripts/KifuDatabase.cs   本地 JSON 棋谱库
Assets/Scripts/GoGame.cs         渲染、UI、存档与复盘
Assets/Editor/AutoPlay.cs        打开工程自动 Play
Assets/Editor/BuildScript.cs     命令行打包入口
```
