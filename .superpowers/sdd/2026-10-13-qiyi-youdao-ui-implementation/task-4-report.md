# Task 4 report: 抽离棋盘皮肤和棋盘侧边工具栏

## Status

DONE_WITH_CONCERNS

Task 4 已在指定隔离工作树完成。

Commit: `af65a48e7b720fa09ee96cee1c77db40137d97d1`

## Implemented

- 新建 `Assets/Scripts/UI/GoBoardSkin.cs`，提供 brief 要求的 `Build`、`Refresh`、`SetToolbarState`、`Clear` 和 `GridToLocal` 公共接口。
- `GoBoardSkin` 集中负责木纹棋盘、网格、9/13/19 路星位、黑白棋子、死子标记、最后一手标记、手数 TextMesh、坐标标签和形势领地覆盖层。
- `GridToLocal` 统一返回 `(x * cellSize, y * cellSize)`，9/13/19 路共用同一换算函数。
- `GoGame.BuildBoard` 改为创建并委托给 `GoBoardSkin`；旧的 `boardRoot`、ghost、最后一手、标记渲染和分析覆盖层引用保留为兼容别名/入口，规则、AI、历史、存档代码未重写。
- 落点输入通过 `GoBoardSkin.NotifyPointClick` 转发回 GoGame，保留正式对局和数子阶段的落子/死子标记行为。
- 左侧创建“手数 / 坐标 / 形势”三个纵向工具按钮，按钮状态通过回调同步到 GoGame；形势显示仍调用 `GoRules.EstimateSituation`，只改变可视化，不修改规则盘面。
- `GoIconCatalog` 增加棋盘工具图标键，并保留运行时图标回退。
- 添加 `Assets/Tests/Editor/GoBoardSkinTests.cs`：坐标换算、空盘计数、9/13/19 路棋子/手数计数，以及工具状态和落点回调测试。
- 添加计划要求的固定资源路径：
  - `Assets/Art/Board/board_wood_1024.png`
  - `Assets/Art/Board/stone_black_256.png`
  - `Assets/Art/Board/stone_white_256.png`
  - `Assets/Art/Board/stone_shadow_256.png`
  - `Assets/Art/Board/marker_last_move_128.png`
  - `Assets/Art/Board/marker_ai_best_128.png`
  - `Assets/Art/UI/Panels/panel_paper_9slice_512.png`
  - `Assets/Art/UI/Panels/panel_ink_9slice_512.png`
  - `Assets/Art/UI/Buttons/button_icon_96.png`

## Verification

### Tuanjie batch compile and existing TestRunner

Command:

```text
Tuanjie.exe -batchmode -nographics -quit -projectPath C:\Users\luyus\.codex\worktrees\qiyi-youdao-ui\GoGame -executeMethod TestRunner.RunAllTests -logFile Logs/task4-compile-2.log
```

Result: PASS, process exit `0`.

- 脚本编译通过。
- `GoRulesTests`、`GoSaveTests`、`GoAiTests` 均通过。
- 日志包含 `ALL TESTS PASSED SUCCESSFULLY!`。
- 本轮新增的资源已被 Unity 生成对应 `.meta` 文件。

### Static and diff checks

- 源码和测试的 `git diff --check` 无问题；包含 Unity 自动生成 `.meta` 的完整 staged diff 会报告这些 `.meta` 内空 YAML 字段的尾随空格（`userData:`、`assetBundleName:` 等），这是导入器生成格式，不是源码空白错误。
- 检查确认 GoGame 不再保留基础棋盘的 `SpriteRenderer[,]`、手数数组和旧木纹/棋子/死子/网格生成函数。
- 检查确认 `GoBoardSkin` 暴露 brief 要求的接口，固定资源路径全部存在。
- 未修改计划文件；计划目录中的既有未跟踪 `docs/superpowers/plans/` 保持原样，未纳入提交。

### Managed EditMode limitation

Command:

```text
Tuanjie.exe -batchmode -nographics -quit -projectPath C:\Users\luyus\.codex\worktrees\qiyi-youdao-ui\GoGame -runTests -testPlatform editmode -testResults Logs/task4-editmode-results.xml -logFile Logs/task4-editmode.log
```

Result: process exit `0`，但没有生成 XML；日志报告 `Scan Filter Count: 0`。因此 `GoBoardSkinTests` 已完成编译级检查，但无法在当前 managed EditMode 入口获得实际 NUnit 执行结果。这与前序 Task 3 记录的环境限制一致。

### Play Mode limitation

按用户要求未继续等待交互式验证。当前可用环境是 headless `-batchmode -nographics`，因此 9/13/19 路的可视点击、工具按钮实际屏幕位置和复盘交互未执行 Play Mode 手工验收；代码路径、公开接口、编译和现有业务回归已完成验证。

## Concerns

- 新增棋盘位图已按固定路径提交，但 `GoBoardSkin` 保留程序化 fallback；由于资源不在 `Resources` 目录且 Task 4 未扩展 Task 3 的场景资源清单，当前 headless 验证不确认 Windows Player 是否会收集这些新棋盘资源。缺失时仍会安全回退到程序化国风棋盘/棋子/标记。
- 形势文字摘要仍由 GoGame 的既有 UI 维护，领地颜色和坐标/手数实际绘制已由 GoBoardSkin 统一负责。
- 交互式 Play Mode 和 managed EditMode 执行结果需要在可用 Unity UI/Test Runner 环境补验。

## Fresh repair follow-up

### 修复范围

本轮只处理上一轮审查指出的 Task 4 问题，未修改 Task 5+，也未回退已有实现：

1. 扩展 `GoRuntimeAssetManifest`、`BuildScript` 与启动场景序列化引用，覆盖棋盘、黑白棋子、死子阴影、最后一手、AI 标记、panel/button 以及三枚棋盘工具图标。`GoBoardSkin` 和 `GoIconCatalog` 均优先从 manifest 读取，`Resources` 仅保留兼容性 fallback。
2. 新增并导入 `icon_board_moves_64.png`、`icon_board_coordinates_64.png`、`icon_board_situation_64.png`，三者为实际存在、内容可区分的 `Sprite/Single` 资源；左侧手数/坐标/形势按钮分别使用各自资源，不再引用不存在的 `BoardTool`。
3. 最后一手统一使用朱砂细环；`Clear` 清空落点回调，清空后 `NotifyPointClick` 不再触发旧回调；补充 Clear、最后一手/死子状态测试，并保留坐标与显示状态测试。
4. 将 Task 4 固定棋盘和 UI 图片的 importer 设置明确为 `Sprite/Single`，使 `AssetDatabase.LoadAssetAtPath<Sprite>` 与 Windows 构建收集机制一致。

### 验证结果

- 批量编译：通过；修复后的脚本可完成编译。
- Windows 构建：`BuildScript.BuildWindows` 日志包含 `BUILD OK -> Builds/Windows/GoGame.exe`。
- 自定义 `TestRunner.RunAllTests`：通过；`GoRulesTests`、`GoSaveTests`、`GoAiTests` 全部通过，并输出 `ALL TESTS PASSED SUCCESSFULLY!`。
- managed EditMode：进程退出码为 0，但当前环境仍返回 `Scan Filter Count: 0` 且不生成 XML；因此新增 NUnit 测试完成编译级检查，未获得实际 Test Runner 执行计数。
- Play Mode：当前环境为 headless `-batchmode -nographics`，未执行交互式棋盘点击、按钮位置与复盘手工验收。
- 静态检查：固定资源路径、manifest 字段、场景引用、三枚图标文件和 `Sprite/Single` importer 配置均存在；未发现 `BoardTool` 引用。

### 环境限制

managed EditMode 的发现/执行入口和交互式 Play Mode 仍受当前托管 headless 环境限制；这不影响脚本编译、manifest 序列化收集、Windows 构建日志和既有 TestRunner 回归结果。
