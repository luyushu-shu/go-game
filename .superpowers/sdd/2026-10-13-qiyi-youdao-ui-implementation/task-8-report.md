# Task 8 实现与发布验收报告

## 发布候选

- 目标发布提交：`05b81267976895ce937cf50dbd9147acae79c2d7`（`release: finish qiyi youdao ui migration`）。
- 当前 worktree HEAD：`2e8a8dd41b1799b81f0662efdabccf8cdcb0e030`，同名发布提交；与 `05b8126` 的唯一差异是 `GoKifuSgfTests.cs.meta` 空 YAML 字段的尾随空格格式，不涉及代码或资源语义。
- 工作树核对时仅剩既有未跟踪计划目录 `docs/superpowers/plans/2026-10-13-qiyi-youdao-ui-implementation/`，未纳入发布提交；未再修改代码。

## Task 8 交付

- 运行时 Canvas 统一使用 `ScaleWithScreenSize`、参考分辨率 `1920×1080`、`MatchWidthOrHeight`、`match=0.5`。
- 菜单安全区使用左右 `120`、上下 `70`；游戏侧栏按屏幕宽度响应，`1366×768` 为 `390`，高分辨率收敛到 `360`，棋盘摄像机让出侧栏并优先保留完整棋盘点击区域。
- 历史、落子记录、复盘内容使用垂直滚动；正文/批注入口最低字号统一为 `13`；正式对局四按钮继续使用同一行 `悔棋 / 停一手 / 数目 / 认输`。
- `BuildScript` 产品名为“棋弈有道”，保留空场景自动生成、Build Settings 写入和 `Builds/Windows/GoGame.exe` 输出；成功日志包含 `BUILD OK`。
- 补齐并交付 `Assets/Tests/Editor/GoKifuSgfTests.cs.meta`；未加入计划目录。
- README 已补充启动入口、主菜单四入口、时间/读秒范围、棋谱位置、EditMode/自定义测试入口、Windows 构建入口和当前环境限制。
- 未改动 Task 5–7 业务逻辑；旧 JSON 缺失 `mode`、`result`、`stateJson` 的兼容路径保持不变。

## 可执行回归结果

| 项目 | 结果 | 证据 |
|---|---|---|
| 脚本编译 | PASS | `Logs/task8-compile.log`：退出码 0、Tundra build success、正常 batchmode 结束；构建后 `task8-compile-postbuild.log` 同样通过。 |
| 项目 TestRunner | PASS | `Logs/task8-custom-tests.log` 与 `task8-custom-tests-postbuild.log`：GoRules 11 项、GoSave 6 项、GoAI 通过，含 `ALL TESTS PASSED SUCCESSFULLY!`。 |
| 静态矩阵 | PASS | 产品名、构建路径、`BUILD OK`、空场景、Build Settings、Canvas scaler、1920×1080、match 0.5、安全区、侧栏边界、正文最低字号、滚动区域、单行操作栏、旧 JSON README 说明、主场景、测试 `.meta` 均通过。 |
| Windows 构建 | PASS（以最终成功结果计） | `Logs/task8-build.log`：`Build Finished, Result: Success.`、`BUILD OK -> Builds/Windows/GoGame.exe`、退出码 0；产物存在于 `Builds/Windows/GoGame.exe`。 |

构建日志中还保留一条构建期间脚本重新编译前的瞬态 `GoGame.cs(113)` / `GoClockView` CS0246 诊断；随后 Tundra 重新运行成功，最终构建成功，且构建后编译日志无该错误。该日志事实作为验收 caveat 保留，不将其抹除或伪称不存在。

## 未完成的环境项

- 标准 EditMode CLI 未产生可验证 XML；`Logs/task8-editmode.log` 仅显示 `Scan Filter Count: 0`。因此不能宣称 Unity Test Framework/EditMode NUnit 已执行通过；项目自定义 TestRunner 的结果已单独通过。
- 未执行 Play Mode 或 1920×1080、1600×900、1366×768 的交互式 Game 视图验收，不能据 headless 编译结果宣称按钮点击、输入不溢出、滚动、棋盘落子和音频交互通过。
- 未在无 Unity 编辑器的干净 Windows 环境启动 `GoGame.exe`，未完成旧 `kifu_db.json` 替换后的真实启动/打开验证；该项仍需真实 Windows 环境补验。

## 结论

Task 8 的代码、构建脚本、README 和测试资源交付已在发布提交中完成；编译、项目自定义回归、静态矩阵和 Windows 构建具备日志证据。Play Mode、标准 EditMode XML 和干净 Windows 旧棋谱验收受当前环境限制，明确保留为未完成项。

## Task 8 新鲜修复（2e8a8dd 后）

- 响应式尺寸已改为按 CanvasScaler 实际屏幕比例换算：`GoUiTheme` 提供物理像素/Canvas 单位互换、侧栏宽度、安全区和正文最小字号 helper，并带有可静态调用的 `AssertResponsiveTargets`。`1366×768`、`1600×900`、`1920×1080` 的目标侧栏保持约 `360px`、正文实际字号不低于 `13px`；`GoClockView` 的直接 `11px` 文本已提升到主题最小字号。
- 资源交付已补齐并接入清单、工厂、图标目录和构建脚本：三套非空可区分的九宫格按钮 Sprite，以及设置、音量、静音、悔棋、停一手、数目、认输图标和对应 `.meta`。缺失资源现在产生明确错误和可辨识诊断图形，不再使用 `1×1` 空白 fallback；保留原有 `Assets/Art` 路径。
- README 已改为明确说明 KataGo 可选且需用户自行安装/配置；当前构建不随包附带可执行程序或模型。构建脚本只做明确的缺失提示，运行时不可用时使用本地引擎降级，并保留原构建输出和入口。
- 新增/更新响应式与资源静态检查；未改动 Task 1–7 的规则、AI、历史、SGF 和计时核心。

### 新鲜修复验证与限制

| 项目 | 结果 | 证据 |
|---|---|---|
| 修复后脚本编译 | PASS | `Logs/task8-repair-compile-final.log`：Tundra build success，未见 C# 编译错误。 |
| 项目自定义 TestRunner | PASS | `Logs/task8-repair-custom-tests.log`：GoRules 11 项、GoSave 6 项，最终 `ALL TESTS PASSED SUCCESSFULLY!`。 |
| 新响应式/资源检查 | 已补入代码 | `Assets/Tests/Editor/GoUiThemeTests.cs`；本轮未继续等待标准 EditMode XML 执行。 |
| 修复后 Windows build | 未完成 | 两次尝试均被同一工作树残留的 Tuanjie 实例阻塞；`Logs/task8-repair-build-final.log` 明确记录该阻塞。未将旧的 `Builds/Windows/GoGame.exe` 冒充为本次修复产物。 |

本轮已停止验证进程并清理临时锁文件与 `test_result.txt`。Play Mode、交互式三分辨率验收、标准 EditMode XML 和干净 Windows 启动仍未执行，不能宣称交互验收完成。

## Task 8 第二轮新鲜修复（80f9ff6 后）

### 修复内容

- `GoGame.StartAiGame` 在用户选择 KataGo、但 `GoKataGo.IsAvailable()` 为 false 时，不再提前返回；本局 `opponentChoice` 改为本地引擎并同步设置页选项，随后继续创建棋局、计时和既有 `GoAiController` 流程。开局提示明确包含“未找到 KataGo，已切换本地引擎”。KataGo 可用时保留原路径，降级分支不会启动 KataGo 进程。
- `GoKataGo.InstallHint` 补充精确的 `katago.exe`（或 `katago-eigen.exe`）、`default_model.bin.gz`、`gtp.cfg` 文件要求。
- `BuildScript.LogOptionalKataGoStatus` 改为逐项检查 `katago.exe`/`katago-eigen.exe`、`default_model.bin.gz`、`gtp.cfg`，删除 `*.bin*` 泛匹配，并在缺失时逐项输出明确日志；仍明确说明构建不打包 KataGo。
- README 现在按 `GoGame.exe` 同级目录精确说明 `Tools/KataGo/` 的运行时布局、编辑器目录映射、用户自行安装、不打包和不可用时自动使用本地 MCTS 的行为。
- 未修改 Task 1–7 的规则、计时、AI 搜索、历史、SGF 或复盘核心逻辑。

### 第二轮验证证据

| 项目 | 结果 | 证据 |
|---|---|---|
| 静态断言 | PASS | 断言降级分支无提前 `return`、`opponentChoice=0`、明确提示存在；BuildScript 包含三个精确文件检查且无 `*.bin*`/`Directory.GetFiles`；README 成品目录与不打包/降级说明存在。 |
| 修复后脚本编译 | PASS | `Logs/task8-round2-compile.log`：`Tundra build success`，无 `error CS`/Compilation failed，正常 batchmode 退出码 0。 |
| 项目自定义 TestRunner | PASS | `Logs/task8-round2-custom-tests.log`：GoRules 11 项、GoSave 6 项、GoAI 均通过，含 `ALL TESTS PASSED SUCCESSFULLY!`；随后已清理 `test_result.txt`。 |
| Windows build 尝试 | BUILD PASS，产物不计本轮新产物 | `Logs/task8-round2-build.log`：`Build Finished, Result: Success.`、`BUILD OK`，并输出精确 KataGo 缺失日志。构建前后 `Builds/Windows/GoGame.exe` 长度均为 464384、SHA-256 均为 `61263A2AAD52E1BE6961139F628D00EB2ABAF6799134F69F4062BC5E33178F1B`，写入时间也未变化，因此不将该旧 exe 作为本轮新产物证据。 |

### 保留的环境限制

- 按用户中止指令，本轮在上述验证后停止，不再运行新的测试或启动交互式编辑器。
- Play Mode、1920×1080 / 1600×900 / 1366×768 的人工点击验收、标准 EditMode XML 和无 Unity 编辑器的干净 Windows 启动仍未执行。
- 本轮 Windows build 日志报告成功，但最终 `GoGame.exe` 指纹未变化；发布验收仍需在可确认产物更新时间/指纹变化的环境重新生成并启动验证。
