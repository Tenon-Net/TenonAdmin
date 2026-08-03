# 外部登录品牌化 + GitHub / 微信实现审查

> 审查日期：2026-08-01  
> 审查依据：[`codex-review-brief.md`](./codex-review-brief.md)、[`decisions.md`](./decisions.md)、[`ledger.md`](./ledger.md)  
> 审查范围：brief §4、§6、§9 列出的后端内核改动、GitHub/WeChat 卫星包、MinimalHost、双模板登录/绑定/配置 Tab、相关测试与权限种子。  
> 当前结论：**APPROVE**。原审查的 4 条 findings、复审新增的 GitHub `User-Agent` finding，以及后续发现的登录 callback cancellation 传播缺口，均已由当前代码和回归测试验证关闭。
> **作者跟进 2026-08-01**：4 条原 findings 均已修复，见「Remediation」；本轮复审已完成。

## Findings

| Severity | Location | Finding | Why it matters | Suggested fix | Evidence |
|---|---|---|---|---|---|
| High | `backend/src/TenonAdmin.Auth.GitHub/GitHubExternalAuthProvider.cs:76-97,107-137`; `backend/src/TenonAdmin.Auth.WeChat/WeChatExternalAuthProvider.cs:59-85`; `backend/src/TenonAdmin.AspNetCore/Controllers/ExternalAuthController.cs:108-143` | GitHub/WeChat 的网络、超时、非法 JSON 或字段类型异常没有统一转换为 OAuth 交换失败。 | GitHub 的 `SendAsync`/`ReadAsStringAsync`/`JsonDocument.Parse`/`GetString`/`GetInt64`，以及微信的 `GetAsync`/`JsonDocument.Parse`/`GetInt32`/`GetString` 都可能直接抛 `HttpRequestException`、超时 `TaskCanceledException`、`JsonException` 或 `InvalidOperationException`。回调只捕获 `AdminException`；`AdminExceptionFilter` 对其它异常明确放行，因此这些情况会变成 500，前端收不到约定的 OAuth 错误跳转。ledger 要求 malformed/non-2xx/missing fields/timeout 进入交换失败；取消才可传播。 | 在两 provider 的 token/user 交换边界统一包装非调用方取消的 HTTP、超时、JSON/字段解析异常为 `AdminException(ErrorCode.OAuthExchangeFailed)`；保留调用方已请求的 cancellation 传播。不要为授权码交换增加重试。补 fake-handler 回归测试：异常、超时、非法 JSON、错误字段类型。 | `ExternalAuthController.Callback` 仅 `catch (AdminException)`（:128-143）；`AdminExceptionFilter` 仅处理 `AdminException`（`backend/src/TenonAdmin.AspNetCore/Filters/AdminExceptionFilter.cs:14-24`）。契约见 `ledger.md:154-158`、`ledger.md:211-214`。现有目标测试 24/24 通过，但没有覆盖这些失败路径。 |
| High | `backend/src/TenonAdmin.Auth.GitHub/GitHubExternalAuthProvider.cs:121-125` | GitHub API 请求没有设置有效的 `User-Agent`。 | 冻结台账要求 GitHub token/user 请求带有效 `User-Agent`；当前 provider 只设置 `Authorization` 和 `Accept`，而 `HttpClient` 默认不会替应用提供应用级 UA。GitHub REST API 可拒绝没有有效 UA 的请求，导致 GitHub 登录在拿到 token 后调用 `/user` 失败。 | 在命名 `HttpClient` 或每个 GitHub request 上设置稳定、可识别且不含密钥的 `User-Agent`（例如 `TenonAdmin/<version>` 或包常量），并在 fake-handler 测试断言 `/user` 请求存在该 header。不要把 client secret 或 access token 放入 UA。 | 当前代码 `GitHubExternalAuthProvider.cs:121-123` 仅设置 `Authorization`/`Accept`，没有 `User-Agent`；`ledger.md:132,151` 明确要求有效 UA；现有 `GitHubWeChatAuthProviderTests` 没有 UA 断言。 |
| Medium | `backend/src/TenonAdmin.Auth.GitHub/GitHubExternalAuthProvider.cs:117-131` | GitHub 的字符串 `id` 分支接受任意非空字符串，没有验证其为数字，也没有规范化数字字符串。 | 冻结契约要求 GitHub 用户数字 `id`（数字或数字字符串）转成十进制字符串；当前响应 `{"id":"not-a-number"}` 会成功生成外部身份键 `not-a-number`，而 `{"id":"00123"}` 会保留非规范形式。上游响应异常时没有 fail closed，可能把无效身份写入绑定链。 | 对字符串 ID 使用 invariant numeric parsing，成功后输出规范十进制表示；拒绝非数字、溢出及不符合 GitHub ID 约束的值。增加非数字字符串、前导零/溢出等测试。 | 当前代码在 `ValueKind.Number` 之外只做 `GetString()` 和 `IsNullOrWhiteSpace` 检查（:123-131）；契约见 `ledger.md:151-157`。现有测试只覆盖 JSON 数字 `12345`（`GitHubWeChatAuthProviderTests.cs:50-71`）。 |
| Medium | `backend/src/TenonAdmin.Services/Config/ConfigService.cs:81-102`; `backend/src/TenonAdmin.Services/Entities/SysConfig.cs:11-17` | `SaveValuesAsync` 对未种子的 `sys.externalauth.*` 键采用“先查后插”，并发首配会撞唯一索引。 | 两个配置 Tab 保存请求可同时查不到同一个动态 provider 键，然后都执行 `InsertAsync`；`SysConfig.ConfigKey` 有唯一索引，后到请求会抛数据库唯一键异常，配置页面得到 500。多管理员或多副本部署首次配置未种子 provider 时可复现。 | 使用数据库原生 insert-ignore/upsert；或捕获唯一键冲突后重新读取行并执行更新。增加两个并发 `SaveValuesAsync`/HTTP 保存同一新 provider 键的回归测试，并确认缓存失效只在最终写入后执行。 | 查询和插入是独立操作（`ConfigService.cs:81-96`）；唯一索引定义在 `SysConfig.cs:12`。当前 `ExternalAuthTests` 只测单次自动建键（`ExternalAuthTests.cs:175-181`），没有并发覆盖。 |
| Low | `web/src/views/login/LoginForm.vue:129-133,440-453`; `web-react/src/views/login/LoginForm.tsx:446-465` | 登录页溢出菜单项只包含 provider 名称，没有渲染小品牌图标。 | 登录按钮冻结决策要求第 5 个起进入“…”菜单，菜单项包含“小圆标 + 名称”；当前 Vue `NDropdown` 的 option 和 React `Dropdown` item 都只有 `key`/`label`。功能可用，但 overflow 与平铺按钮的品牌识别不一致，违反已冻结 UI 契约。 | 使用两模板各自 UI 库支持的自定义 option/menu item 渲染 `BrandIcon` 与 `displayName`，同时保留 API 顺序和现有点击行为。 | `decisions.md:68-70` 明确“菜单内小圆标 + 名称”；当前实现只构造 `{ key, label }`（Vue）和 `{ key, label, onClick }`（React）。相关自动化测试只断言按钮数量/可访问名，未断言菜单项内容。 |

## Explicitly Checked

- **DI：通过，未报 bug。** `GitHubSetup.cs:31-38` 与 `WeChatSetup.cs:30-37` 均使用 `ServiceDescriptor.Singleton<IExternalAuthProvider, ConcreteProvider>(factory)` 放入 `TryAddEnumerable`，并通过 `IHttpClientFactory` 创建命名 `HttpClient`。目标测试已验证 GitHub/WeChat 均能解析 provider；没有重复报告 brief §8 已修复的“仅接口工厂导致 ArgumentException”问题。
- **身份与开关：当前目标测试覆盖通过。** WeChat 仅 unionid、openid-only 失败；GitHub 数字 id、`read:user` scope；公开 providers 的禁用过滤与管理列表保留禁用项均通过。
- **双模板及配置隔离：当前静态/测试检查通过。** Vue/React typecheck、相关 Vitest、`OtherConfig` 排除 `externalauth`、权限码一致性测试均通过；没有把 OpenAPI 路径类型断言（brief §7 已知非目标）重复作为问题。
- **敏感信息：未确认新的日志泄露 bug。** WeChat token URL 按厂商契约把 secret 放在 query；provider 自身没有记录完整 URL，也没有在本次检查中证明默认 HTTP client logging 的实际输出包含 secret。应在后续测试中用捕获 logger 验证，但本报告不把未复现风险升级为 confirmed finding。

## Verification

| Check | Result |
|---|---|
| `dotnet test backend/TenonAdmin.slnx -c Release --filter "FullyQualifiedName~GitHubWeChatAuthProviderTests&#124;FullyQualifiedName~ExternalAuthTests"` | **39 passed, 0 failed** |
| `dotnet test backend/TenonAdmin.slnx -c Release --filter "FullyQualifiedName~PermissionCodeConsistencyTests"` | **2 passed, 0 failed** |
| `web`: `npm run typecheck` | **passed** |
| `web`: `npm run test -- --run src/utils/oauthBrand.spec.ts` | **1 file, 9 passed** |
| `web`: `npm run lint` | **passed** |
| `web-react`: `npm run typecheck` | **passed** |
| `web-react`: related Vitest files from brief §6 | **3 files, 45 passed** |
| `web-react`: `npm run lint` | **passed** |
| `git diff --check` | **clean** |

第一次并行启动两个 .NET 测试任务时发生了测试项目生成 `MvcTestingAppManifest.json` 的文件锁冲突；随后改为串行执行，目标测试和权限测试均通过。该工具冲突不计入代码 findings。

## Handoff Order

1. ~~GitHub `User-Agent`~~ **已修**（请求级 + Setup 默认头 + 测试断言）。
2. 原 4 条 findings **已关闭**。
3. 登录 `Callback` → `LoginByExternalAsync` 的 cancellation token 传播 **已修复并补测试**。

---

## Remediation（作者 2026-08-01 已落地）

| Finding | 修复摘要 | 回归测试 |
|---|---|---|
| **High** 交换异常未映射 | `GitHubExternalAuthProvider` / `WeChatExternalAuthProvider` 的 `ExchangeAsync` 捕获 HTTP/超时/JSON/字段异常 → `OAuthExchangeFailed`；调用方 `CancellationToken` 取消仍传播 | malformed JSON、`HttpRequestException`、caller cancel 传播 |
| **Medium** GitHub 字符串 id | `ParseGitHubUserId`：仅接受正数 long；字符串规范化为十进制（`00123`→`123`）；拒绝非数字/≤0 | invalid id Theory + 规范化用例 |
| **Medium** 配置并发首插 | `SaveValuesAsync` 对 `sys.externalauth.*` 插入撞唯一键 → 重读并 Update；`LooksLikeUniqueKeyViolation` 启发式 | `Concurrent_SaveValues_first_write_of_externalauth_key_does_not_throw` |
| **Low** 溢出菜单无图标 | Vue `NDropdown` label render + React `Dropdown` label 均 `BrandIcon` + 名称 | 手测/既有 5 provider 数量断言；图标为 render 侧 |

**验证（作者机）**: `dotnet test … GitHubWeChatAuthProviderTests|ExternalAuthTests` → **39 passed / 0 failed**（含 cancellation propagation 回归测试）。

| **High** GitHub 缺 User-Agent（复审新增） | 请求级 `User-Agent: TenonAdmin-GitHubAuth` + Setup/ctor 默认头；测试断言 token 与 `/user` 均带 UA | `AssertGitHubUserAgent` on both requests |
| **Follow-up** 登录 callback cancellation 未传入 provider（复审后续项） | `Callback`、`IAuthService.LoginByExternalAsync`、`AuthService.ResolveExternalIdentityAsync` 现在贯通同一个 `CancellationToken`；新增 service-level propagation 测试 | `External_login_propagates_cancellation_token_to_provider` |

## Codex Re-review（2026-08-01）

### Result

本轮直接复查当前工作树后，确认复审新增的 GitHub `User-Agent` finding 已修复。4 条原 findings 和该复审 finding 的修复不是只改文档：对应代码路径、回归测试和双模板静态检查均已实际验证。

| Original finding | Re-review result | Current evidence |
|---|---|---|
| High：OAuth 网络/超时/非法响应变成 500 | **Closed** | GitHub `ExchangeAsync:57-76`、WeChat `ExchangeAsync:51-70` 分别把非调用方取消的 HTTP/超时/JSON/字段异常映射为 `OAuthExchangeFailed`；调用方取消单独传播。新增 GitHub/WeChat malformed JSON、HTTP exception、caller cancellation 测试通过。 |
| Medium：GitHub 字符串 `id` 不严格 | **Closed** | `GitHubExternalAuthProvider.ParseGitHubUserId:152-171` 只接受正数 `long`，并将 `00123` 规范化为 `123`；invalid shape theory 和规范化测试通过。 |
| Medium：配置首插并发撞唯一键 | **Closed** | `ConfigService.SaveValuesAsync:88-108` 捕获跨数据库唯一键冲突后重读并更新；并发首写测试和唯一键消息识别测试通过。 |
| Low：overflow 菜单缺少品牌图标 | **Closed** | Vue `LoginForm.vue:129-143` 与 React `LoginForm.tsx:446-460` 均自定义渲染 `BrandIcon + displayName`；两模板 typecheck/lint 和相关 Vitest 通过。 |
| 新增 High：GitHub 请求缺少 `User-Agent` | **Closed** | 每条 token/user 请求 `ApplyRequestUserAgent`；命名 HttpClient + ctor 默认头双保险；`UserAgentValue` 常量；测试断言两请求均含 UA 且无 secret/token。 |

### Previously Noted Follow-up (Closed)

`ExternalAuthController.Callback:116-123` 的登录分支现在把 callback 的 `cancellationToken` 传给 `auth.LoginByExternalAsync(...)`；`AuthService.LoginByExternalAsync` 和 `ResolveExternalIdentityAsync` 继续将同一 token 传给 provider。绑定分支 `HandleBindCallbackAsync:242-248` 原本就会传递 token。

客户端中断登录回调时，登录模式现在也会将请求取消传到 GitHub/WeChat provider 的 HTTP 请求。新增 `External_login_propagates_cancellation_token_to_provider` 回归测试验证 service 到 provider 的传播。

### Current Verification

| Check | Result |
|---|---|
| `dotnet test backend/TenonAdmin.slnx -c Release --filter "FullyQualifiedName~GitHubWeChatAuthProviderTests&#124;ExternalAuthTests"` | **39 passed, 0 failed** |
| `dotnet test backend/TenonAdmin.slnx -c Release --filter "FullyQualifiedName~PermissionCodeConsistencyTests"` | **2 passed, 0 failed** |
| `web`: typecheck + `oxlint` + `oauthBrand.spec.ts` | **passed; 9 tests** |
| `web-react`: typecheck + `oxlint` + §6 related Vitest files | **passed; 3 files, 45 tests** |
| `git diff --check` | **clean** |

**复审结论（取消链路修复后）**：原 4 条 + 复审 High（User-Agent）+ cancellation follow-up 均已关闭。建议状态：**APPROVE**。本轮串行复跑 `GitHubWeChatAuthProviderTests|ExternalAuthTests`：**39 passed, 0 failed**。
