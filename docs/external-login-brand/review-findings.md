# 外部登录品牌化 + GitHub / 个人微信 · 实现前评审

> 评审日期: 2026-08-01  
> 评审范围: `decisions.md`、`ledger.md`，以及当前外部登录、双模板前端、卫星包和 MinimalHost 实现  
> 状态: **✅ 评审项已全部定案**（2026-08-01 grilling 回填完毕）  
> 说明: 实现前风险已收敛进 `decisions.md` / `ledger.md`。其他 agent 开工前读本目录三份正文；新取舍仍须维护者确认。

## 结论

ADR-0007 方向成立；Codex 指出的 P1/P2 已全部定案并回填。文档现可作为 D1 实现契约使用（以 `decisions.md` + `ledger.md` 为准，本文为评审轨迹）。

## P1

### P1-1 微信 Subject 会发生身份漂移 — ✅ 已定案（S-A）

**定案（2026-08-01 grilling）**: **仅 `unionid`**，拿不到 / 空字符串 → 交换失败，**禁止**降级 `openid`；本批不做迁移与 alias。

已回填：`decisions.md` §2.1、`ledger.md` §1 行 6 与 D1-③ 规格/验收。

### P1-2 厂商 HTTP 契约与 fake HTTP 接缝 — ✅ 已定案（H1 + C1）

**定案（2026-08-01 grilling）**:

- **H1**：构造注入 `HttpClient`；测试用 fake `HttpMessageHandler`；新包禁止以 WeCom static client 为唯一路径。
- **C1**：GitHub / 微信完整 HTTP 契约写入 `ledger.md` D1-② / D1-③（含 header、字段、错误、脱敏、不重试）。

已回填：`decisions.md` §7、`ledger.md` 两刀的 HTTP 契约表与验收项。

### P1-3 官方 provider 的固定 code 与可配置 Code — ✅ 已定案（K1）

**定案（2026-08-01 grilling）**: `github` / `wechat` **硬固定**；options **不暴露** Code；不在本批收紧 WeCom/DingTalk 历史行为。

已回填：`decisions.md` §8、`ledger.md` 两刀规格与验收。

### P1-4 Provider DisplayName 与外部用户 DisplayName — ✅ 已定案（N1 + W-n2）

**定案（2026-08-01 grilling）**:

- Provider：GitHub 默认 `GitHub`，微信默认 `微信`；空配置回退默认。
- Identity：GitHub `login`→`name`→null；微信本批 **不调 userinfo**，DisplayName 恒 null。

已回填：`decisions.md` §9、`ledger.md` 两刀规格/HTTP/验收。

## P2

### P2-1 icon 的 URL fallback — ✅ 已定案（I-A 仅 Iconify）

**定案（2026-08-01 grilling）**: brand map → Iconify 名 → 首字母/通用标；**不支持 URL**。

已回填：`decisions.md` §10、`ledger.md` D1-① 行为与验收。

### P2-2 溢出菜单的 provider 顺序 — ✅ 已定案（O-A API 序）

**定案（2026-08-01 grilling）**: 前端严格保序；平铺/溢出按下标切；本批不加 Order 字段。

已回填：`decisions.md` §11、`ledger.md` D1-①。

### P2-3 已绑定但 provider 被禁用 — ✅ 已定案（B-A 合并展示）

**定案（2026-08-01 grilling）**: 启用 ∪ 已绑定；已停用可解绑、不可再绑；不级联删绑定。

已回填：`decisions.md` §12、`ledger.md` D1-①。

### P2-4 MinimalHost 的 JIT 示例 — ✅ 已定案（文档写 sys_config；样例默认不打开）

**定案（2026-08-01 grilling）**: MinimalHost 只注释连接密钥；JIT 键路径写在文档；不 seed `provisioning=provision`。

已回填：`decisions.md` §3（U2 收紧）、`ledger.md` §1 行 5。维护者 2026-08-01 确认。

### P2-5 GitHub `user:email` scope — ✅ 已定案（E1）

**定案（2026-08-01 grilling）**: scope **仅** `read:user`；不调 `/user/emails`；不填 Email。

已回填：`decisions.md` §13、`ledger.md` GitHub 规格与 HTTP 契约。

### P2-6 D1-① 测试矩阵 — ✅ 已定案（T2）

**定案（2026-08-01 grilling）**: 逻辑/切分/icon/绑定合并 **vitest 门禁**；溢出键盘/Esc **手测抽样**。

已回填：`decisions.md` §14、`ledger.md` D1-① 验收分栏。

## 建议执行顺序（定案后）

1. ~~冻结 P1/P2~~ **已完成** → 以 `decisions.md` + `ledger.md` 施工。  
2. **D1-①** UI + T2 自动化。  
3. **D1-② / D1-③** 先 H1 接缝再写 provider；入 `TenonAdmin.slnx`；solution build/test。  

## 评审证据路径

- 内核契约: `backend/src/TenonAdmin.Core/Security/IExternalAuthProvider.cs`
- 外部身份绑定: `backend/src/TenonAdmin.Services/ExternalAuth/SysUserExternalService.cs`
- 外部登录控制器: `backend/src/TenonAdmin.AspNetCore/Controllers/ExternalAuthController.cs`
- 现有卫星包样板: `backend/src/TenonAdmin.Auth.WeCom/`、`backend/src/TenonAdmin.Auth.DingTalk/`
- Vue 图标/绑定: `web/src/components/AppIcon.vue`、`web/src/views/personal/bindings.vue`
- React 图标/绑定: `web-react/src/components/AppIcon.tsx`、`web-react/src/views/personal/bindings.tsx`
- Solution: `backend/TenonAdmin.slnx`
