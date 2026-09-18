# 围棋 · Go Game

用团结引擎（Tuanjie，兼容 Unity 2022.3 LTS）开发的单机围棋。棋盘、棋子与界面全部程序化生成，无需美术资源。

## 功能

- **打谱**：固定 19 路摆谱；手数 / 坐标 / 形势（Bouzy 估目）；退出主界面时自动保存进度
- **人机对弈**：开局前选定 9 / 13 / 19 路、执黑 / 执白 / 猜先、贴目与让子、电脑对手；局内可开关手数 / 坐标 / 形势（与打谱相同）
- **保存棋谱**：终局弹窗可填写对局名称、双方昵称与段位、描述；时间与结果由系统填入
- **历史棋谱**：主界面可搜索本地棋谱库，点开后在棋盘上复盘（上一手 / 下一手 / 终局）
- 完整规则：提子、打劫（Zobrist 全局同形禁着）、自杀拦截、双停数子、死子标记
- 悔棋、停一手、认输、清空棋盘

## 电脑对手

人机开局可选三种对手：

### KataGo（推荐）

调用本机 [KataGo](https://github.com/lightvector/KataGo) GTP 引擎（10 路神经网络，g170e-b10c128）。Windows 发布包已附带引擎与权重；从源码运行时把文件放到工程根目录 `Tools/KataGo/`：

- `katago.exe`（优先 OpenCL / GPU）
- `katago-eigen.exe`（CPU AVX2 备用）
- `default_model.bin.gz`
- `gtp.cfg`（每手约 400 visits / 最多 4 秒；本仓库提供）

引擎二进制不进 Git。首次用 OpenCL 时可能自动调优，第一手会稍慢。

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

- **直接玩**：到 [Releases](../../releases) 下载 Windows 打包版，解压运行 `GoGame.exe`
- **编辑器**：用团结引擎 2022.3.62t14（或 Unity 2022.3 LTS）打开本工程，按 Play 进入主界面（`Assets/Editor/AutoPlay.cs` 控制自动运行，不需要可删）

## 操作

鼠标单击交叉点落子。主界面：**打谱** / **人机对弈** / **历史棋谱** / **退出**。

对局右侧：落子记录、悔棋、停一手、认输、清空、数目、返回。打谱与人机左下角可开关手数 / 坐标 / 形势。复盘时用上一手、下一手、终局浏览棋谱。

## 打包

编辑器菜单 `Build → Windows EXE`，或命令行：

```powershell
Tuanjie.exe -batchmode -quit -projectPath . -executeMethod BuildScript.BuildWindows -logFile build.log
```

产物在 `Builds/Windows/`。

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
