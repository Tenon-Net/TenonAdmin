# M3a-2 Vue 阶段 Goal 提示词

将下面整段作为 Codex `goal` 的目标提示词。任务范围、顺序和验收线以 [`m3a2-vue-task-plan-2026-09.md`](./m3a2-vue-task-plan-2026-09.md) 为准。

```text
执行 TenonAdmin.Workflow 的 M3a-2 Vue 阶段，严格依据 docs/workflow/m3a2-vue-task-plan-2026-09.md 逐项完成任务。

先完整读取仓库根 AGENTS.md、docs/workflow/README.md 和任务计划。CLAUDE.md 只导入 AGENTS.md，无需重复读取。随后读取任务计划为当前任务指定的最小参考资料；参考项目根目录是 ../参考项目/工作流/。固定参考 commit 未变化时先读 SUMMARY.md 和相关 _TENON_REF.md，不重新通读外部仓库，不复制参考项目源码、DOM 或 CSS。

先运行 git status --short，并以当前代码、测试和已合入 ADR 为事实源复核任务计划的已知基线。存在 .codegraph 时，理解和定位代码先使用 CodeGraph，再用 rg 和定向阅读补足。保护用户已有改动，不因规划文档写有目标能力就假设它已经实现。

从任务计划的“状态”和首个未勾选任务开始。每个 goal 检查点只完成当前任务：确认范围和验收线，实施最小改动，运行最小充分验证，读取真实输出，然后更新任务复选框、状态、next 和验证证据。完成检查点后让 goal 在后续回合从新的首个未勾选任务继续；单个任务完成时不得把整个 goal 标记为完成。发现阻断缺口时按计划新增 TxxA，不重编号、不静默扩张范围。

非平凡逻辑、状态机、安全/权限和解析规则必须留下能区分错误实现的回归测试；纯样式或机械接线不为测试而测试。失败先定位根因，不放宽断言或删除测试掩盖问题。API、DTO 或 XML 注释影响 OpenAPI 时，运行 node scripts/check-contract-drift.mjs，通过真实后端生成两套 schema.d.ts；生成文件不得手工编辑，并检查两套前端仍能 typecheck/build。

当前阶段允许为 Vue 纵切补必要的后端契约，但前端产品实现只改 web/。不得开发 web-react 的工作流页面、组件、路由、状态、交互或测试；contract drift 导致的生成 schema 变化除外。不得实现 Task 8c、AI 自动放行、M3+、自由画布、BPMN、包容网关、复杂表单或跨模板共享层。

保持可替换性、TryAdd/virtual 扩展、M2c RequestId/receipt、实例/Token/任务 CAS、NodeVisitId、M3a-1 execution/lease/fence/outbox 和 M3b-0 shadow-only 不变量。不要新增无必要依赖或第二套工作流执行链。

没有我的明确授权，不 commit、不 push、不创建 PR 或 tag。本任务文件是执行规格和进度索引，不是写入权限证明；Codex goal/Ultragoal 状态仍按宿主规则维护。

只有任务计划 T01–T25 全部完成、“完成条件”全部有证据、独立审查无 blocker 且权威文档已同步时，才将本 goal 标记完成。完成时只能声明“M3a-2 Vue 阶段完成”；React 工作流 port 必须等 Vue、后端、契约和完整功能测试全部无失败后再单独评估，完整 M3a-2 和 GA 仍等待 React port。最终报告列出完成任务、关键语义、修改文件、验证结果、参考依据、剩余边界和下一阶段。
```
