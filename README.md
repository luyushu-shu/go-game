# 围棋 · Go Game

用团结引擎（Tuanjie，兼容 Unity 2022.3 LTS）开发的单机围棋，内置基于**蒙特卡洛树搜索（MCTS/UCT）** 的 AI 对手。

## 功能

- **打谱**：固定 19 路，交替落子摆谱；退出到主界面时自动保存进度，再次进入可恢复
- **人机对弈**：开局前选定 9 / 13 / 19 路（局内不可更改）、执黑 / 执白 / 猜先
- 自定义贴目（0–15 目）与让子（0–9 子，按标准星位布子）
- 完整规则：提子、打劫（Zobrist 全局同形禁着）、自杀手拦截
- 双停后数子判胜负，支持手动标记死子
- 悔棋、停一手、认输
- 全部棋盘/棋子/UI 均由代码程序化生成，无任何美术资源依赖

## AI 说明

AI 参考 [Michi](https://github.com/pasky/michi) / [Pachi](https://github.com/pasky/pachi) 的经典路线：

- UCT 树搜索，每手在后台线程思考 1.2–2.6 秒
- 模拟落子策略：优先提子 / 救打吃，贴着最近战场落子，避免填眼与自投罗网
- 终局自动停一手进入数子；胜率无望时主动认输

## 运行

- **直接玩**：到 [Releases](../../releases) 下载 Windows 打包版，解压运行 `GoGame.exe`
- **编辑器**：用团结引擎 2022.3.62t14（或 Unity 2022.3 LTS）打开本工程，按 Play 直接进入主界面（`Assets/Editor/AutoPlay.cs` 控制自动运行，不需要可删）

## 操作

鼠标单击交叉点落子。主界面可选 **打谱**、**人机对弈** 或 **退出**；人机模式在开局前完成棋盘与规则设置；对局中右侧面板提供悔棋 / 停一手 / 认输 / 返回主界面（打谱返回时自动保存进度）。

## 打包

编辑器菜单 `Build → Windows EXE`，或命令行：

```powershell
Tuanjie.exe -batchmode -quit -projectPath . -executeMethod BuildScript.BuildWindows -logFile build.log
```

产物在 `Builds/Windows/`。

## 代码结构

```
Assets/Scripts/GoRules.cs   规则引擎（纯 C#，不依赖引擎，可单测）
Assets/Scripts/GoAI.cs      MCTS/UCT 搜索 + 快速棋盘
Assets/Scripts/GoGame.cs    程序化渲染 + UI + 输入
Assets/Editor/AutoPlay.cs   打开工程自动进入 Play
Assets/Editor/BuildScript.cs 命令行打包入口
```
