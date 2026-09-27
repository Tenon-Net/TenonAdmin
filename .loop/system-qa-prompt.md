你在 /loop 里跑。每轮同一份提示、上下文是空的，全部状态只存在 `.loop/system-qa.md`。本轮只做一件事，做完即停。

GOAL: 以高级测试工程师身份，对 TenonAdmin 做全面探测（后端内核 + web/ Vue + web-react/ React：代码审查、现有测试、API/UI），找出 bug 与潜在风险；极小且确定的 bug 当轮直接修；存疑项记入 `docs/qa/findings.md`。
DONE-CONDITION: `.loop/system-qa.md` 的 ## Tasks 全部 `[x]`，且该文件顶部有 `sweep: clear`，且 `docs/qa/findings.md` 存在。终轮「P0/P1 清扫」只有在本轮 0 个新 P0/P1 时才能勾选；若清扫又发现 P0/P1，保持该任务未勾选并写入 NEXT。

每轮按顺序：
1. 读 `.loop/system-qa.md`。若不存在，按约定骨架创建后继续。
2. GUARD：若 DONE-CONDITION 已满足，或 `round >= max`，宣布「✅ DONE」，不再干活，停，不要再 arm 下一轮 wake。
3. 否则取 ## Tasks 里第一个未勾选任务（若上轮 log 有 `NEXT:`，以 NEXT 为准）。
4. 只做这一个功能面：先读已有测试与关键实现（后端 + web/ + web-react/，禁止抽公共层）；能跑则跑相关测试；服务已起则做 API/UI 探测。极小且确定的 bug 当轮修掉。存疑追加到 `docs/qa/findings.md`。禁止编写 exploit / PoC payload。面太大就拆成新的未勾选 Tasks，本轮只做其中一片。
5. 更新 `.loop/system-qa.md`：完成则 `[x]`；失败保持 `[ ]` 并记 blocker。终轮清扫仅当本轮 0 个新 P0/P1 时勾选并设 `sweep: clear`。Round log 追加 `### Round {n} — ... NEXT: ...`。`round:` +1。
6. 停。下一轮只靠 ledger 续跑。

不要依赖上一轮记忆。ledger + `docs/qa/findings.md` 是唯一真相。
