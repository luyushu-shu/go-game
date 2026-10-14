# Final review fix report

Status: COMPLETE

This fix wave addresses all three Important findings from review `2c0bb5b..74cc0bb`.

## Implemented fixes

### 1. KataGo request timeout and shutdown safety

- Replaced synchronous `StandardOutput.ReadLine` polling with `GoKataGoResponseReader`, which consumes `OutputDataReceived` lines and waits on a signal with a real deadline.
- `GenMoveAsync` now has a hard 120-second request deadline. On timeout it terminates and disposes the current process, waits briefly for the worker to unwind, and completes with `GoMoveResult.Fail("KataGo 请求超时")` so `GoAiController` can use the existing local-MCTS fallback.
- `SendBlock` also terminates the process on response timeout or communication failure.
- `Shutdown` interrupts the current process before taking the serialized request gate, then takes the gate and performs a second cleanup pass. This prevents a hung stdout read from blocking shutdown indefinitely and does not create a second KataGo process.
- The response-reader seam is deterministic and does not require KataGo to be installed.

### 2. Local MCTS and AI busy-state recovery

- `GoAI.SearchAsync` now catches invalid-input/search exceptions, produces `GoMoveResult.Fail`, sets the search completion state in `finally`, and invokes its callback exactly once, including superseded searches.
- `GoAiController` now guards the local wait with an interlocked completion flag and uses `finally` to clear `IsBusy` and deliver one completion result.
- `GoGame.AiTurn` now clears `aiBusy` and refreshes the UI from a context-checked `finally` path, including coroutine/search failures and early invalidation.

### 3. Kifu board-size validation

- `KifuDatabase.ValidateRecord` now accepts only 9, 13, and 19.
- Catalog migration validates legacy records after migration and removes unsupported records before they can appear in history or reach replay/rules construction.
- `BuildSgf` safely falls back to 19 for an invalid direct-call record, and `GoGame.OpenKifuReview` rejects invalid records before use.
- Regression coverage includes board sizes 1, 9, 13, 19, 20, and 25 plus legacy catalog filtering.

## Verification

All commands below were run in `C:\Users\luyus\.codex\worktrees\qiyi-youdao-ui\GoGame`.

1. Editor compilation:

```powershell
$editor = 'C:\Program Files\Tuanjie\Hub\Editor\2022.3.62t14\Editor\Tuanjie.exe'
$log = 'C:\Users\luyus\.codex\visualizations\2026\10\12\01a13071-1cfb-70e3-b18e-3ae882b75e51\qiyi-final-fix-compile.log'
$p = Start-Process -FilePath $editor -ArgumentList @('-batchmode','-nographics','-quit','-projectPath','C:\Users\luyus\.codex\worktrees\qiyi-youdao-ui\GoGame','-logFile',$log) -Wait -PassThru -WindowStyle Hidden
$p.ExitCode
Select-String -Path $log -Pattern 'error CS|Tundra build|Compilation failed|Scripts have compiler errors|Exiting'
```

Result: `EXIT_CODE=0`; `Tundra build success`; no compiler-error or compilation-failed lines; batch mode exited successfully.

2. Repository custom regression suite:

```powershell
$log = 'C:\Users\luyus\.codex\visualizations\2026\10\12\01a13071-1cfb-70e3-b18e-3ae882b75e51\qiyi-final-fix-tests.log'
$p = Start-Process -FilePath 'C:\Program Files\Tuanjie\Hub\Editor\2022.3.62t14\Editor\Tuanjie.exe' -ArgumentList @('-batchmode','-nographics','-projectPath','C:\Users\luyus\.codex\worktrees\qiyi-youdao-ui\GoGame','-executeMethod','TestRunner.RunAllTests','-logFile',$log) -Wait -PassThru -WindowStyle Hidden
$p.ExitCode
Select-String -Path $log -Pattern 'GoSaveTests|GoAiTests|ALL TESTS|FAILED|Assertion Failed|error CS|Tundra build'
```

Result: `EXIT_CODE=0`; `Tundra build success`; `[GoSaveTests] All persistence tests passed successfully!`; `[GoAiTests] All AI tests passed successfully!`; `ALL TESTS PASSED SUCCESSFULLY!`.

3. Whitespace validation:

```powershell
git diff --check
```

Result: no output; exit code 0.

## Remaining concerns

- No real KataGo executable was available in the worktree, so process-kill behavior was verified through the deterministic response-reader seam and compile/custom tests rather than a live engine integration test. The production path uses the same reader and process cleanup code.
- The pre-existing untracked `docs/superpowers/plans/` directory was intentionally left untouched.
