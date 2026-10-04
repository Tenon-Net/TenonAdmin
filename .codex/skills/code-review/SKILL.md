---
name: code-review
description: "[OMX] 审查指定代码或 diff；明确要求合并就绪审查或活动流程要求时，运行代码与架构双代理门禁。"
---

# Code Review Task Card

用于有明确范围的代码审查。通用执行与权限规则见 [AGENTS.md](../../../AGENTS.md)。

## When to use

- The user asks for a code review or quality/security assessment.
- 用户明确要求合并就绪审查，或已授权的活动流程要求独立审查门禁。
- Do not activate this card for implementation, broad planning, or automatic cleanup.

## Inputs

- Scope: the requested files, commit, PR, or whole diff.
- Requirements/specification, acceptance criteria, and relevant test/CI evidence.
- Existing review artifacts and known risks, if any.
- If the user says `continue`, advance the current verified review step rather than restarting discovery.

先按用户范围采集证据。“当前未提交修改”包括 staged、unstaged 和未跟踪文件：

```sh
git status --short
git diff HEAD --stat -- <scope>
git diff HEAD -- <scope>
git ls-files --others --exclude-standard -- <scope>
```

单独读取范围内未跟踪文件的相关内容。无初始提交时分别使用 `git diff --cached` 与 `git diff`；用户指定分支、提交或 PR 时改用相应基准，不能用工作树 diff 代替。审查期间范围内发生新改动时，更新受影响部分的证据。

## 选择审查深度

普通审查：检查用户指定的文件或 diff，按严重程度报告有依据的问题与验证缺口；只有独立视角能补充证据时才委派。缺少规格时先审查可判断的部分，说明覆盖限制。普通审查不输出合并就绪认证，也不因未启动双代理而判定代码有问题。

[双代理执行、状态和合并门禁](references/merge-ready.md)仅用于明确请求的合并就绪审查，或 Autopilot 等活动流程要求的审查阶段。单独调用 `$code-review` 默认普通审查；`$code-review --merge-ready` 明确选择双代理门禁。活动流程的必需门禁不能降级为普通审查。

## 完成条件

普通审查完成指定范围的检查后，报告有文件与行号依据的问题、严重程度、影响、建议和验证缺口；没有发现时如实说明。仅在选择合并就绪分支时读取上面的参考并满足双代理门禁。审查本身保持只读，修复交回已授权的实现流程。
