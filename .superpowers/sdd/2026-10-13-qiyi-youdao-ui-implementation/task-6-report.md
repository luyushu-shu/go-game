# Task 6 报告：计时视图、正式对局、打谱和终局弹窗

## 完成内容

- 新建 `Assets/Scripts/UI/GoClockView.cs`（及 Unity `.meta`）：支持 `Bind`、`Render`、`SetWarning`；无限制显示 `∞`，本时显示 `M:SS`/`H:MM:SS`，读秒显示 `读秒 N/M · MM:SS`；读秒剩余 10 秒或更少时使用朱砂色并播放 `clock_warning`，同一读秒段只提醒一次。
- `GoClockController` 增加读秒总段数与读秒段标识，供视图准确显示及区分每个读秒段的提醒。
- `GoGame` 正式 AI 对局创建黑方首手计时器；在 HumanPlay、HumanPass、AI 落子/停一手后切换行棋方；`Update` 使用 `Time.unscaledDeltaTime` Tick；复盘、数子、认输/保存等阻断式弹窗、终局状态统一停表。
- 棋盘右侧操作区改为固定同一行的四按钮：`悔棋 / 停一手 / 数目 / 认输`；正式对局底部状态只显示当前手数与黑方/白方行棋，不显示 AI 思考状态。
- 认输改为动态确认弹窗，按当前行棋方使用 `GoResultFormatter.FormatResign`；确认调用 `rules.ResignBy(rules.Turn)`，停表、保存打谱草稿并进入终局操作。
- 数目改为两次停一手进入 `GoPhase.Scoring` 后显示黑白分数、贴目和差值，确认通过 `rules.ConfirmEnd` 记录规范的一位小数结果。
- 超时回调调用 `rules.FinishByTimeout(loserColor)`，显示规范超时结果并停止计时；三类终局继续复用已有棋谱保存字段和结果格式化器。
- 打谱复用同一操作栏，隐藏 AI/复盘导航，并显示 `自动保存：已保存` 或 `自动保存：保存失败`。
- 补充计时测试：终局只触发一次并停表、无限制不扣时、读秒警告及同一读秒段单次提醒；已有结果格式测试覆盖认输、数目、超时和终局保护。

## 验证

- Tuanjie batch compile：第二次完整编译通过；新脚本已导入并完成 Assembly-CSharp 编译，无 C# 编译错误。
- 现有 `TestRunner.RunAll`：通过；规则、存档、AI 回归均显示 `All ... PASSED`。
- managed EditMode：已尝试运行，但本工程环境返回 `Scan Filter Count: 0`，未生成 XML 结果，不能据此宣称 NUnit focused tests 已由 managed Test Runner 执行。
- Play Mode：未运行；当前收敛要求下保留为环境限制，分辨率布局、弹窗点击链和音频实际播放仍需在可交互编辑器/构建中验收。

## 范围与兼容性

- 未修改任务计划。
- 复用 Task 1 已存在的 `GoRules.FinishByTimeout`、`GoResultFormatter` 语义，以及前置任务已存在的 `GoBoardSkin` 和 `GoAudioManager` 能力；未引入第二套结果或时间语义。
- 未触及 Task 7 的历史/SGF 数据结构与复盘实现。

## 环境限制与 concerns

- Unity/Tuanjie 命令行日志包含许可证客户端不可用提示，但脚本程序集随后成功编译，且现有 TestRunner 通过。
- managed EditMode XML/测试发现受当前环境限制；Play Mode 及 1920×1080、1600×900、1366×768 的实际布局验收未完成。
- 报告与实现一起提交；最终 commit hash 以任务回复为准。
