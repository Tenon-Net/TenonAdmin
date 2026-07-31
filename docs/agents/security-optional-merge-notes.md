# 可选安全 — 合入前收口说明

- 日期：2026-07-30
- 分支：`feature/mlps-level3-phase1`
- 决策：[ADR 0006](../adr/0006-general-admin-optional-security.md)
- 配置键：[security-optional-config.md](security-optional-config.md)

## 合入门禁（本轮）

| 项 | 状态 |
|----|------|
| 默认路径无 Level3 fail-closed / 生产合规告警 | 已做 |
| 独立键 `Totp` / `Session:CookieMode` | 已定稿并接线 |
| 自助绑定 + 清除 MFA；无邀请/InitGrant 产品路径 | 已做 |
| 双前端绑定 UI 自助化 | 已做 |
| 相关后端测试（见下） | **须绿** |
| 不宣称完整三级 / 无 phase2–3 路线图承诺 | ADR + 计划已废止 |
| OpenAPI `gen:api` | **合入后**有跑后端时再刷（invite 已删） |

## 本轮验证命令

```bash
dotnet test backend/TenonAdmin.slnx -c Release --filter "FullyQualifiedName~MfaEnrollmentTests|FullyQualifiedName~Level3SessionCsrfTests|FullyQualifiedName~Level3PolicyFloorTests|FullyQualifiedName~Level3PrecheckTests"
```

期望：上述过滤器全部通过。  
（`Level3_startup_with_missing_redis_auth_*` 已改为**不**拒绝启动，符合 ADR 0006。）

前端（可选）：

```bash
cd web-react && npx vitest run src/views/mfa/BindPage.spec.tsx
```

## 合入后可跟（不挡本 PR）

1. ~~CookieMode 双前端~~ — 客户端已具备 credentials + CSRF + 静默刷新（见 web/web-react `api/client.ts`）  
2. ~~登录页 / 个人中心链到 `/mfa/bind`~~ — `/personal/security` + 登录「设置身份验证器」  
3. 物理删除历史 Level3 注册位、DeployGrant/Invite 实体、闲置账号 Job  
4. 重命名/精简仍带 `Level3*` 的测试类名  
5. 正式 `gen:api`（当前 schema 已手工对齐 clear/自助 bind，下次 gen 会覆盖）  

## P2 跟进（Codex review 后已修）

- [x] Reauth TTL → `ResolveReauthWindowMinutes`
- [x] `MfaBindInvalid` 语义 + i18n（40021 兼容）
- [x] 测试洞：Totp off / RequireForSuperAdmin / clear 拒绝 / HTTP clear
- [x] baseline/precheck 菜单默认禁用 + 控制器文档降级
- [x] 启动清理：禁用已拆除 invite/reset 菜单权限行
- [x] schema.d.ts：invite → clear；bind 入参自助化

## 建议 commit 主题（英文）

```
feat(security): optional TOTP/Cookie instead of Level3 profile

- Adopt ADR 0006: general admin kernel; drop MLPS phase 2/3 roadmap
- Independent Security:Totp and Session:CookieMode flags (default off)
- No fail-closed Level3 startup; ready probe no longer gates on precheck
- Self-service TOTP bind (account+password); admin clear MFA; remove invites
- Vue/React bind pages and user admin UI updated; related tests adjusted
```

## 明确不对消费者说

- 「已通过等保三级」  
- 「完整三级应用安全基线」  
- 「请配置 Profile=Level3 才能安全上线」  
