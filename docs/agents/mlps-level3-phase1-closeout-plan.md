# 等保三级一期收口执行计划

- 状态：**历史收口记录** — 一期曾在分支完成；**产品方向已由 [ADR 0006](../adr/0006-general-admin-optional-security.md) 纠正**。后续执行见 [security-optional-slim-plan.md](security-optional-slim-plan.md)，**禁止**原样将完整 Level3 合入 main，**禁止**按二/三期继续建设。
- 收口日期：2026-07-30（一期实现）；方向纠正：2026-07-30
- T5 第一版～第十轮复审修复：2026-07-30
- 依据（历史）：`docs/mlps-level3-kernel-plan.md`、`docs/agents/mlps-level3-phase1-*.md`、`docs/agents/mlps-level3-cookie-csrf-deploy.md`
- 原目标：完成并验证等保三级应用安全内核第一期；不扩展到第二、三期，且不得宣称产品已通过等保三级测评。

## 范围与约束

### 本期范围

1. Level3 Profile、有效安全策略下限、启动/预检与 Redis TLS/认证 fail-closed。
2. 密钥提供方、秘密保护、TOTP、恢复码、绑定邀请、超管恢复与高敏权限 MFA。
3. 安全 Cookie 会话、双提交 CSRF、会话时限/并发、闲置账号治理、密码与验证码下限。
4. Vue、React 的 Level3 登录、TOTP、邀请码绑定、恢复码和再次认证交互。
5. 安全基线页面/接口与 CI 可读取预检结果。

### 明确不做

- 第二期：审计 180 天留存、Outbox/SIEM/WORM、审计哈希链、安全响应头、文件扫描、敏感导出/水印、第三方 HMAC 接入。
- 第三期：通用字段加密与轮换、国密档、SBOM/漏洞门禁/产物证明。
- 不回退或覆盖既有未提交改动；不手改 OpenAPI 生成文件；不提交代码。

## 任务分解与结果

### T1–T9 轮历史

此前轮次阻断均已关闭，见历史记录。

### 第十轮复审阻断项 — 已关闭（2026-07-30）

| 项 | 修复 | 回归 |
|----|------|------|
| first-seen TTL 用应用侧预读 `@cutoff` | 最终 UPDATE：`FirstSeenAt > (DB_UTC − TTL)`，方言 `DATEADD` / `DATE_SUB` / `datetime('now','-N minutes')` 等在**执行时**求值；Consume 不再用应用侧 cutoff 作最终谓词 | `Deploy_grant_consume_rejects_when_first_seen_ttl_expired_at_db_time`；`InitGrant_start_ok_then_complete_after_ttl_fails_without_binding`（改 FirstSeenAt 后 Complete 失败、不写 MFA） |

定向后端：**71/71**（MfaEnrollment + Level3Precheck + SessionCsrf + PolicyFloor）。

### 自审摘要（实现方复查）

| 结论 | 说明 |
|------|------|
| 第十轮高优先级已对症 | TTL 与绝对到期的**最终**比较均在单条 UPDATE 中用数据库 UTC 求值。 |
| 回归 | 行内 `FirstSeenAt` 改到过去、配置 NotAfter 仍未来 → Ensure 行存在可过、DB TTL 谓词拒绝；证明不是应用侧预读 cutoff。 |
| StartBind 早失败 | `EnsureWithinTtlAsync` 仍用应用时钟做 first-seen TTL 早失败（UX）；与最终消费闸门分离，消费只走 `EnsureRowExists` + DB 谓词。 |
| 残留 | 端口锁 TOCTOU、CI 未默认跑 e2e 等同前。 |

## Grok 交付要求

1. 先阅读本文件和全部 `docs/agents/mlps-level3-phase1-*.md`。
2. 评估复杂度；复杂时先给出依赖明确的细粒度子任务，再实现。
3. 保留无关改动，不编辑 `.omc/grill-dispatch` 账本。
4. 最终报告：复杂度、执行方式、改动文件、每项验证命令的结果、未完成项和风险。
5. 若外部运行时、依赖或测试阻断，停止在最小可复现证据处，不伪造通过结果。

## 完成定义

T1-T5 与全部复审高优先级项关闭、相应新增回归通过，方可将状态标为「第一期已完成」。完成一期不等于 TenonAdmin 或任何部署系统已通过等保三级。

## 残留风险与缺口

1. 管理端「点发邀请」UI 未单独 e2e。
2. 未跑全量后端套件。
3. 二/三期未做。
4. 部署须 Redis TLS/认证、DataProtection、InitGrant+NotAfter、HTTPS。
5. 跨源须 CookieDomain + AllowCredentials + HTTPS。
6. 非 CI 本地并行 e2e 端口隔离依赖锁文件；CI 应显式分配端口。
