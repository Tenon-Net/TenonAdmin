# 可选安全第一刀瘦身计划（执行稿）

- 状态：**第一刀合入门槛已就绪**（2026-07-30）— 见 [security-optional-merge-notes.md](security-optional-merge-notes.md)
- 决策源：[ADR 0006](../adr/0006-general-admin-optional-security.md)
- 分支：`feature/mlps-level3-phase1`（**先瘦身再合入**，禁止原样合完整 Level3）

## 目标（第一刀完成定义）

1. 默认行为 ≈ 普通后台（无 Level3 fail-closed、无生产合规告警、无强制 MFA 全集）。
2. 独立开关：可选 TOTP（自助绑定 + 恢复码）、可选 Cookie/CSRF、可配置密码/会话策略。
3. 双前端：默认路径与 **TOTP 自助绑定** 可走通（Cookie 前端对齐可后置）。
4. 文档不再承诺三级基线 / 二/三期交付。

## 已完成

### 文档 / 决策

- [x] ADR 0006；ADR 0005 部分取代
- [x] `mlps-level3-kernel-plan` 废止为建设路线图
- [x] `CONTEXT.md` → 可选应用安全
- [x] 配置键定稿 `security-optional-config.md`
- [x] Cookie 部署说明改独立键 `mlps-level3-cookie-csrf-deploy.md`
- [x] 合入说明 `security-optional-merge-notes.md`

### 代码

- [x] 无 fail-closed 启动 / 生产合规告警 / ready 恒 Healthy（历史探针名保留）
- [x] `AdminSecurityOptions` helpers + 产品路径接线
- [x] 自助绑定 + `sys/mfa/clear`；拆除邀请/InitGrant/紧急恢复端点
- [x] 双前端绑定页与用户页 UI
- [x] MFA 测试改自助；启动 fail-closed 测试改为「不阻断」

## 合入后（不挡 PR）

- [ ] 物理删除历史 Level3 类型/注册位/预检测评话术
- [ ] CookieMode 双前端完整对齐（内存 access + CSRF）
- [ ] 闲置账号 Job 确认默认不跑
- [ ] `Level3*` 测试类重命名
- [ ] OpenAPI `gen:api`

## 明确不做

- 原二/三期整包
- 新的「Hardened Profile」总档

## 历史施工稿

`docs/agents/mlps-level3-phase1-*` 仅作历史；**不要**按其「继续二期」执行。
