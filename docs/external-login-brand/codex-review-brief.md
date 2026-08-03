# Codex Code Review Brief — 外部登录品牌化 + GitHub/微信 + 配置 Tab

> **用途**:交给 Codex（或其它 reviewer）做实现审查的**唯一入口**。  
> **规格正文**（决策/台账，勿与本文件混）: [decisions.md](./decisions.md) · [ledger.md](./ledger.md)  
> **评审轨迹**（设计期 Codex 意见）: [review-findings.md](./review-findings.md)  
> **日期**:2026-08-01 · **分支状态**:工作区未提交（见文末文件清单）

---

## 1. 请 Codex 做什么

请对**本批实现代码**做 adversarial review，重点抓：

1. **正确性 / 安全**（身份键、OAuth 交换、密钥不进日志、启用开关与 authorize 一致性）  
2. **DI / 可替换性**（卫星包注册、不破坏 `IExternalAuthProvider` 与 TryAdd 成法）  
3. **前后端契约**（providers vs providers/all、enabled 键、双模板对等、零共享）  
4. **测试是否测到真路径**（尤其 Setup 注册、禁用过滤、unionid/id 映射）  
5. **回归与遗漏**（种子 Id 冲突、菜单权限、OtherConfig 排除分组、openapi/client 类型捷径）

**输出格式建议**（便于作者修）:

| 字段 | 说明 |
|------|------|
| Severity | Blocker / High / Medium / Low / Nit |
| Location | `path:line` 或符号名 |
| Finding | 一句话问题 |
| Why it matters | 用户/安全/可维护后果 |
| Suggested fix | 可操作建议 |
| Evidence | 你读到/跑到的依据 |

请区分：**已证实 bug** vs **推测风险**。不要重复 [review-findings.md](./review-findings.md) 里已定案且已回填的设计争论，除非实现与决策矛盾。

---

## 2. 产品/架构背景（30 秒）

TenonAdmin 是可分发 **Admin 内核**。批次 D 已有外部登录骨架（OIDC + WeCom/DingTalk 卫星包、`sys_user_external`、默认未绑定拒绝、`GET providers`）。

本批在 **不推翻 ADR-0002** 前提下增加：

| 刀 | 内容 |
|----|------|
| D1-① | 登录/绑定 **Gitee 风品牌圆钮**（双模板 Vue + React） |
| D1-④ | 系统配置独立 Tab **「第三方登录」** 动态显隐 |
| D1-② | 卫星包 `TenonAdmin.Auth.GitHub` |
| D1-③ | 卫星包 `TenonAdmin.Auth.WeChat`（个人微信开放平台，非企微） |

权威决策:[decisions.md](./decisions.md)（含 review 回填 §2.1、§7–§15）。

---

## 3. 已冻结的关键决策（实现必须对齐）

| ID | 决策 | 实现锚点 |
|----|------|----------|
| S-A | 微信 Subject **仅 unionid**，禁止降级 openid | `WeChatExternalAuthProvider.ExchangeAsync` |
| E1 | GitHub scope **仅 `read:user`**，不调 emails | `GitHubExternalAuthProvider` authorize + exchange |
| K1 | Code **硬固定** `github` / `wechat`，options 无 Code | `FixedCode` + Setup |
| H1 | 构造注入 `HttpClient`（经 IHttpClientFactory） | GitHub/WeChat Setup + Provider ctor |
| U2 | 未绑定默认拒绝；JIT 只文档/`sys_config`，MinimalHost 不 seed provision | 沿用现网 `IsEnabled`/`provisioning` |
| I-A | icon fallback **仅 Iconify 名**，拒绝 URL | `oauthBrand.ts` `isIconifyName` / `resolveProviderIcon` |
| O-A | 登录按钮序 = **API 序**；N=4 溢出 | `splitLoginProviders` |
| B-A | 绑定页 = 启用 providers ∪ 已绑定停用项 | `mergeBindingRows` |
| B 方案 | 配置中心 **独立 Tab**，不进安全策略 | `ExternalAuthConfig` + `config/index` |
| DI 坑 | `TryAddEnumerable` 必须 `Singleton<IExternalAuthProvider, TImpl>(factory)` | GitHubSetup / WeChatSetup（曾错用仅接口工厂 → ArgumentException，已修） |

---

## 4. 实现地图（按审查顺序）

### 4.1 后端 — 内核改动

| 文件 | 改什么 |
|------|--------|
| `backend/src/TenonAdmin.AspNetCore/Controllers/ExternalAuthController.cs` | 新增 `GET providers/all`（`[RolePermission]`），返回全部已注册 + `enabled` |
| `backend/src/TenonAdmin.AspNetCore/ExternalAuth/ExternalAuthModels.cs` | `ExternalProviderAdminItem` |
| `backend/src/TenonAdmin.Services/Config/ConfigService.cs` | `SaveValuesAsync`：对 `sys.externalauth.*` **缺失键自动 Insert**（其它键仍 ignore） |
| `backend/src/TenonAdmin.Services/Seed/ConfigSeed.cs` | Id 31–34：`wecom/dingtalk/github/wechat.enabled`，GroupCode=`externalauth` |
| `backend/src/TenonAdmin.Services/Seed/DefaultMenuSeed.cs` | Id 157：权限 `GET:/api/v1/auth/external/providers/all` |
| `backend/Directory.Packages.props` | `Microsoft.Extensions.Http` 10.0.0 |
| `backend/TenonAdmin.slnx` | 加入 Auth.GitHub / Auth.WeChat |

**未改但被依赖的现网行为**（请对照，防回归）:

- 公开 `GET .../providers` 仍只返回 `IsEnabledAsync==true`
- authorize 仍 `ResolveEnabledProviderAsync` → 禁用抛 `OAuthProviderDisabled`
- `sys.externalauth.{code}.enabled` 缺省 true（`SysUserExternalService.IsEnabledAsync`）

### 4.2 后端 — 新卫星包

```
backend/src/TenonAdmin.Auth.GitHub/
  TenonAdmin.Auth.GitHub.csproj
  GitHubAuthOptions.cs
  GitHubExternalAuthProvider.cs
  GitHubSetup.cs          # AddTenonAdminGitHubAuth

backend/src/TenonAdmin.Auth.WeChat/
  TenonAdmin.Auth.WeChat.csproj
  WeChatAuthOptions.cs
  WeChatExternalAuthProvider.cs
  WeChatSetup.cs          # AddTenonAdminWeChatAuth
```

| 包 | Code | Subject | 交换要点 |
|----|------|---------|----------|
| GitHub | `github` | 数字 `id` 字符串 | POST form token + Accept json；Bearer 调 `/user`；DisplayName=`login`→`name`→null |
| WeChat | `wechat` | **仅 unionid** | qrconnect + `#wechat_redirect`；token GET（secret 在 query 为厂商契约）；**不调 userinfo**；缺 unionid 失败 |

**DI 注册（审查重点）**:

```csharp
// 正确（已落地）
services.TryAddEnumerable(
  ServiceDescriptor.Singleton<IExternalAuthProvider, GitHubExternalAuthProvider>(sp => { ... }));

// 错误（曾写出，会 ArgumentException，已修）
// ServiceDescriptor.Singleton<IExternalAuthProvider>(factory)
```

### 4.3 MinimalHost

| 文件 | 改什么 |
|------|--------|
| `Program.cs` | `AddTenonAdminGitHubAuth` / `AddTenonAdminWeChatAuth`（在 `AddTenonAdmin` 前） |
| `MinimalHost.csproj` | ProjectReference 两包 |
| `appsettings.Development.json.example` | GitHub/WeChat 注释示例 + JIT 走 sys_config 说明 |

### 4.4 测试

| 文件 | 覆盖 |
|------|------|
| `backend/tests/.../ExternalAuthTests.cs` | **新增** `Public_providers_omits_disabled_while_admin_list_includes_it`（公开列表 vs all + SaveValues 关 enabled） |
| `backend/tests/.../GitHubWeChatAuthProviderTests.cs` | **新建** fake handler：mapping / 缺 id / 缺 unionid / errcode / HTTP 错 / authorize URL；**Setup DI 注册**（options + configuration 路径） |
| `TenonAdmin.Tests.csproj` | 引用 Auth.GitHub / Auth.WeChat |

### 4.5 前端 — Vue (`web/`)

| 文件 | 改什么 |
|------|--------|
| `src/utils/oauthBrand.ts` + `.spec.ts` | 纯逻辑：split N=4、I-A icon、B-A merge |
| `src/components/oauth/BrandIcon.vue` | brand SVG map → Iconify → 首字母 |
| `src/views/login/LoginForm.vue` | 圆钮 + NDropdown 溢出 |
| `src/views/personal/bindings.vue` | 合并列表 + 已停用标签 + 解绑 |
| `src/views/system/config/components/ExternalAuthConfig.vue` | 新 Tab 内容 |
| `src/views/system/config/index.vue` | Tab「第三方登录」 |
| `src/views/system/config/components/OtherConfig.vue` | 排除 `externalauth` 分组 |
| `src/api/index.ts` | `providersAll`、`ExternalProviderAdmin` |
| `src/locales/zh-CN.ts` / `en-US.ts` | login.moreMethods、oauth.disabled、config.tab.externalAuth 等 |

### 4.6 前端 — React (`web-react/`)

与 Vue **对等、零 import `web/`**（故意双份）:

| 文件 | 对应 |
|------|------|
| `src/utils/oauthBrand.ts` + `.spec.ts` | 同 Vue |
| `src/components/oauth/BrandIcon.tsx` | 同 Vue |
| `src/views/login/LoginForm.tsx` + `loginform.css` | 圆钮 + Dropdown 溢出 |
| `src/views/login/LoginPage.spec.tsx` | aria-label + 5 provider 溢出断言 |
| `src/views/personal/bindings.tsx` | B-A |
| `src/views/system/config/ExternalAuthConfig.tsx` + `index.tsx` | 新 Tab |
| `src/views/system/config/configForm.ts` + `OtherConfig.spec.tsx` | STRUCTURED_GROUPS 含 externalauth |
| `src/api/index.ts`、locales | 同 Vue |

---

## 5. 建议审查检查单（请逐项勾）

### 安全 / 身份

- [ ] 微信：仅 openid 的 token 响应 **必须**失败，不得落库 openid  
- [ ] GitHub：Subject 稳定为 id 字符串，不依赖 login  
- [ ] 日志/异常不出现 client_secret、access_token、带 secret 的完整 token URL（微信 GET secret 在 query 时尤其检查 logger 参数）  
- [ ] 禁用 provider：公开 providers 无此项；authorize 仍拒绝；配置 Tab 仍可见  

### DI / 包边界

- [ ] 有 ClientId/AppId 时 `AddTenonAdmin*Auth` **可成功 BuildServiceProvider 并解析** `IEnumerable<IExternalAuthProvider>`  
- [ ] 空配置节为空操作，不抛  
- [ ] 不引入厂商 SDK；Core 不反向引用卫星包  
- [ ] 与 WeCom/DingTalk 并存、按 Code 选型  

### 配置 / 运营

- [ ] `sys.externalauth.{code}.enabled` 与 `IsEnabledAsync` 一致（false/0 关，缺省开）  
- [ ] `SaveValuesAsync` 自动建键是否过宽/是否有竞态或权限问题  
- [ ] ConfigSeed Id 31–34、Menu Id 157 与现网种子无冲突  
- [ ] SuperAdmin 以外角色：`providers/all` 权限是否合理；超级管理员是否仍全放行  

### 前端

- [ ] 0/1/4/5 provider 平铺与溢出（序不重排）  
- [ ] 误配 `https://...` icon → 首字母，不 `<img>`  
- [ ] 绑定页 ≥5 项不截断；已停用仅可解绑  
- [ ] `providersAll` 类型捷径：`GET '.../all' as '.../providers'` 是否可接受（未 gen:api）  
- [ ] Vue/React 行为是否漂移  

### 测试缺口（请找漏）

- [ ] 是否缺「双重注册 TryAddEnumerable 幂等」  
- [ ] 是否缺「日志不含量词」的结构化断言  
- [ ] 是否缺配置 Tab 前端组件测试  
- [ ] 是否缺 authorize 禁用集成测（现网是否已有覆盖）  

---

## 6. 如何本地复现 / 跑测

```bash
# 后端（核心）
dotnet test backend/TenonAdmin.slnx -c Release --filter "FullyQualifiedName~GitHubWeChatAuthProviderTests|FullyQualifiedName~ExternalAuthTests"

# 前端
cd web && npm run typecheck && npm run test -- --run src/utils/oauthBrand.spec.ts
cd web-react && npm run typecheck && npm run test -- --run src/utils/oauthBrand.spec.ts src/views/login/LoginPage.spec.tsx src/views/system/config/OtherConfig.spec.tsx
```

作者侧曾观测（实现机，非 CI）:

- `GitHubWeChatAuthProviderTests` + `ExternalAuthTests`：**24 passed**（含 DI 修复后）  
- 双模板 typecheck 绿；oauthBrand + LoginPage/OtherConfig 相关 vitest 绿  

**请 Codex 独立再跑一遍**，勿仅信本 brief。

---

## 7. 已知限制 / 非目标（不要当 bug 开）

- 无真实 GitHub/微信密钥 E2E；假 Http 即验收  
- QQ/Gitee **仅图标占位**，无后端包  
- 默认不 JIT；邮箱不并号  
- 双模板不共享组件库  
- 配置中心不排序按钮、不存 ClientSecret  
- 品牌 SVG 为简化 glyph，非官方全量 brand kit  
- OpenAPI `schema.d.ts` **未** `gen:api`；`providersAll` 用路径类型断言  

---

## 8. 实现期已修问题（避免重复报）

| 问题 | 状态 |
|------|------|
| GitHub/WeChat `TryAddEnumerable(Singleton<IExternalAuthProvider>(factory))` → `ArgumentException`，装包后 0 provider | **已修**为 `Singleton<IExternalAuthProvider, TImpl>(factory)` + DI 单测 |
| 设计期 P1/P2（unionid 漂移、HTTP 契约、固定 code、DisplayName 混用、Iconify-only 等） | **设计已定案**并回填 decisions/ledger；请查实现是否遵守 |

---

## 9. 工作区文件清单（便于 diff）

### 新增（untracked 时）

- `backend/src/TenonAdmin.Auth.GitHub/**`
- `backend/src/TenonAdmin.Auth.WeChat/**`
- `backend/tests/TenonAdmin.Tests/GitHubWeChatAuthProviderTests.cs`
- `docs/external-login-brand/**`（含本文件）
- `docs/adr/0007-external-login-brand-ui-and-providers.md`（指针）
- `web/src/utils/oauthBrand.ts` + `.spec.ts` + `components/oauth/BrandIcon.vue` + `ExternalAuthConfig.vue`
- `web-react/src/utils/oauthBrand.ts` + `.spec.ts` + `components/oauth/BrandIcon.tsx` + `ExternalAuthConfig.tsx`

### 修改（节选）

- ExternalAuthController / Models / ConfigService / ConfigSeed / DefaultMenuSeed  
- MinimalHost Program/csproj/example  
- slnx / Directory.Packages.props / Tests.csproj  
- LoginForm + bindings + config index（双模板）  
- ExternalAuthTests  

无关噪声：`web-react/package-lock.json` 可能有本批无关 diff，**可忽略**除非 reviewer 发现与功能耦合。

---

## 10. 给 Codex 的开场 prompt（可复制）

```text
Review the external-login brand + GitHub/WeChat satellite + config tab implementation
in this repo using docs/external-login-brand/codex-review-brief.md as the brief.

Read decisions.md + ledger.md for frozen requirements. Diff the files listed in §4 and §9.
Run the tests in §6 if you can. Report findings in the table format from §1.
Focus on real bugs and contract breaks vs decisions; skip re-litigating closed design choices
unless the code violates them. Especially re-check DI registration for Auth.GitHub and Auth.WeChat.
```

---

## 11. 作者联系点

- 规格争议 → 回 `decisions.md` / 维护者，不擅自改范围  
- 实现修 bug → 优先对应刀：UI / 配置 Tab / 卫星包 / 测试  
