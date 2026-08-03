# 外部登录品牌化 + GitHub / 微信 · 执行台账

> **来源**:2026-07-31 grilling。方向决策见同目录 [decisions.md](./decisions.md)（ADR-0007）与下文 §1；**执行期不回炉**。  
> **前置**:[[ADR-0002]] 外部登录骨架已落地（provider 契约、票据、绑定表、默认拒绝、WeCom/DingTalk）。  
> **驱动方式**:仿 `docs/refinement-ledger.md` / `docs/excel-ledger.md` —— 按 D1 三刀推进，每刀可独立英文 conventional commit，可断点续跑。

---

## 0. 给执行者的话

1. **先读 [decisions.md](./decisions.md)**，再动代码。本文件是施工勾选与验收，不是重新设计入口。  
2. **一次只推进一刀**（D1-① → ② → ③）。遇决策/本台账未写的取舍，停下来问维护者。  
3. **禁止**:给 Core 加厂商 SDK；在 `web/` 与 `web-react/` 之间抽共享层；把品牌 SVG 打进后端 NuGet；默认打开 JIT 开户；本批做 QQ/Gitee **后端包**。  
4. **验证 = 跑出来的证据**（typecheck/lint/相关测试/手动点 providers 条）。

**术语**:「brand map」= 按 provider `code` 映射精修 SVG 的前端表；「圆钮」= 登录区纯图标圆形按钮（无并列文案）。

---

## 1. 决策全表（grilling 钉死）

| # | 题 | 裁定 |
|---|----|------|
| 1 | 交付边界 | **B** 前端呈现 + 新卫星包 |
| 1b | 运营开关 UI | 系统配置 **独立 Tab「第三方登录」**（方案 B，D1-④） |
| 2 | 首批 provider | **GitHub + 个人微信**；QQ 第二批 |
| 3 | UI | **P1** 纯图标 + **O1** 溢出 **N=4** + **I3** 混合图标 |
| 4 | 模板 | **F1** Vue + React 对等 |
| 5 | 未绑定 | **U2** 代码默认拒绝；JIT 仅文档写 `sys_config` 键路径，MinimalHost 不 seed 打开 |
| 6 | 微信 | **W1** 开放平台网站 `qrconnect` + **S-A 仅 unionid**（无则拒绝，不降级 openid）+ 包 `TenonAdmin.Auth.WeChat`（`code=wechat`） |
| 7 | GitHub | **G1** OAuth App + **T1** 数字 id + scope **仅 `read:user`（E1）** + 包 `TenonAdmin.Auth.GitHub`（`code=github`） |
| 8 | 表面 | **B2** 登录 + 个人中心绑定；绑定页不套溢出 |
| 9 | 视觉 | **V1** 中性圆底 + 彩色品牌标 |
| 10 | 节奏 | **D1** 三刀：UI → GitHub → WeChat |
| 11 | Brand map | **M2** `github` `wechat` `wecom` `dingtalk` `gitee` `qq` |

---

## 2. 当前状态

| 刀 | 内容 | 状态 |
|----|------|------|
| D1-① | 双模板登录圆钮 + 溢出 + brand map + 绑定页 brand | ✅ 完成 |
| D1-② | `TenonAdmin.Auth.GitHub` | ✅ 完成 |
| D1-③ | `TenonAdmin.Auth.WeChat` | ✅ 完成 |
| D1-④ | 系统配置 Tab「第三方登录」+ 管理端 provider 列表 API + 种子 | ✅ 完成 |

---

## 3. D1-① 双模板 UI（优先）

### 目标

现有 `providers` API **零协议变更**（或仅文档说明 `icon` 可空）；登录页与个人中心绑定变成 Gitee 风品牌条。

### 建议触点（实现时以仓内现状为准）

**Vue `web/`**

- `views/login/LoginForm.vue`（及皮肤若内联了 SSO 区）
- 个人中心绑定页（bindings）
- 新增如 `components/oauth/BrandIcon.vue` / `utils/oauthBrand.ts`（名称可调，保持自包含）
- 静态 SVG：`assets/oauth/` 或等价目录（`github` `wechat` `wecom` `dingtalk` `gitee` `qq`）
- i18n：溢出「更多」等键（中英）

**React `web-react/`**

- 登录表单 SSO 区 + bindings 页对等改动
- 自有 brand map / 组件（**禁止** import `web/`）

### 行为规格

| 规则 | 说明 |
|------|------|
| 无 provider | 整段「其他方式登录」不渲染（保持现状） |
| 1–4 个 | 登录页横向平铺圆钮（**严格 API 序**，O-A） |
| ≥5 个 | 下标 0..3 平铺 + 「…」装 4..；菜单项顺序同 API 后缀；前端不重排 |
| 点击 | 仍 `authorizeUrl(code)` 顶层跳转（login）；绑定流保持现有 bind 模式 |
| tooltip | 圆钮 `title`/`Tooltip` = `displayName` |
| 图标解析 | `brandMap[code]` → 否则 **仅 Iconify 名称** `icon` → 否则首字母/通用标；**不支持 URL**（I-A） |
| 绑定页 | 启用 providers ∪ 已有 binding（含已停用）：brand 小标 + 名称 + 绑/解绑；已停用可解绑不可再绑（B-A）；**不** N=4 截断 |
| 无障碍 | 按钮有可访问名称（至少 `displayName`） |
| 暗色 | 只调圆底 token，不换第二套 SVG |

### 验收（T2）

**自动化（双模板 vitest，合入门禁）**

- [ ] `web` / `web-react` typecheck + lint + **相关 vitest** 绿  
- [ ] 0 provider：SSO 段不渲染  
- [ ] 1、4 个：全平铺、无「…」；序 = API  
- [ ] 5+ 个：前 4 = API[0..3]，溢出 = API[4..]；前端不重排  
- [ ] icon：known brand / 合法 Iconify / 空 / 未知名 / 误填 URL → I-A  
- [ ] 绑定页：不 N=4 截断；B-A 合并（已停用可解绑、不可再绑）  
- [ ] 圆钮具备可访问名称（aria-label 或 title = displayName）  
- [ ] 未在两模板间抽共享包  

**手测抽样（不阻塞逻辑合入，做完勾选）**

- [ ] 溢出菜单可点；Esc/失焦关闭行为可接受  
- [ ] 启用 wecom/dingtalk 时 brand 标正确（若本地有配置）  

### 提交建议

`feat(web): brand icon SSO buttons on login and bindings`  
`feat(web-react): brand icon SSO buttons on login and bindings`  
（可按模板拆两个 commit）

---

## 4. D1-② `TenonAdmin.Auth.GitHub`

### 目标

可选卫星包；注册后 `code=github` 出现在 providers；走现有 authorize/callback/ticket/exchange。

### 规格要点

| 项 | 值 |
|----|-----|
| 包名 | `TenonAdmin.Auth.GitHub` |
| 注册 | `AddTenonAdminGitHubAuth`（命名对齐 WeCom/DingTalk） |
| 配置节 | `TenonAdmin:ExternalAuth:GitHub`（ClientId / ClientSecret / **Provider** DisplayName 可选 / Icon 可选；启用对齐现网运营键） |
| 应用类型 | GitHub **OAuth App** |
| Authorize | `https://github.com/login/oauth/authorize` |
| Token | `https://github.com/login/oauth/access_token` |
| User | `https://api.github.com/user`（**不**调 `/user/emails`） |
| Scope | **仅** `read:user`（E1） |
| Subject | 用户数字 `id` 字符串 |
| Provider DisplayName | 默认 `GitHub`；空配置回退默认（N1） |
| Identity DisplayName | `login` → `name` → `null`（N1） |
| 依赖 | 仅 Core + Microsoft.\*；**注入** `HttpClient`（H1），裸 HTTP |
| 默认未绑定 | 拒绝（U2）；不写死 JIT |
| Code | **硬固定** `github`（K1；options 不暴露 Code） |

### 接线

- 加入 `backend/TenonAdmin.slnx`；目录布局对齐 `TenonAdmin.Auth.WeCom`，但 **HTTP 接缝用 H1**（注入 `HttpClient`），不抄 static client  
- MinimalHost：**ProjectReference 可选** + appsettings **注释示例**（无真密钥）  
- 测试：包内 `HttpMessageHandler` fake；exchange 成功 mapping、缺 id 失败、厂商错误、敏感信息不进日志  
- 文档：本刀最小 = 包 README + MinimalHost 注释；CHANGELOG 随发版  

### HTTP 契约（C1 · GitHub）— 实现必须遵守

| 项 | 规格 |
|----|------|
| Authorize URL | `GET https://github.com/login/oauth/authorize`；query：`client_id`、`redirect_uri`、`scope=read:user`、`state`；`response_type` 非必须（GitHub 默认 code） |
| Token | `POST https://github.com/login/oauth/access_token`；**body = application/x-www-form-urlencoded**（`client_id`、`client_secret`、`code`、`redirect_uri`）；请求头 **`Accept: application/json`**（否则可能返回 form）；**必须**设合理 **`User-Agent`**（GitHub API 要求） |
| Access token 使用 | 调 API 时 **仅** `Authorization: Bearer <token>`（或 `token <token>` 文档允许的一种，包内固定一种并测）；**禁止**把 access_token / client_secret 放进 query 或日志 |
| User | `GET https://api.github.com/user`；建议 `Accept: application/vnd.github+json`；可选 `X-GitHub-Api-Version`（若固定版本则写入包常量） |
| Subject | JSON 数字/数字字符串字段 **`id`** → `Subject` 十进制字符串 |
| Identity DisplayName | `login` → `name` → null（N1） |
| Provider DisplayName | 默认 `GitHub`；空白回退 |
| 成功判定 | HTTP 2xx 且 JSON 可解析且 `id` 有效；否则交换失败 |
| 非 2xx / 业务错 | 统一 `AdminException(OAuthExchangeFailed)` 或现网等价；日志只记 status + 脱敏后的错误摘要，**不**记 body 中的 token |
| 缺字段 / 空 id | 交换失败 |
| 超时 / 取消 | 传播取消；超时视为交换失败；**不对 token 交换重试**（授权码单次消费） |
| 重试 | **默认不重试** token 与 user 请求 |

### 验收

- [ ] solution 级 `dotnet build` / 相关 test 含新包  
- [ ] 包内 fake handler 单测绿（成功 mapping、缺 id、非 2xx、无重试约定）  
- [ ] `Code` 恒为 `github`（options 无 Code 配置项）  
- [ ] providers 项 DisplayName 默认 `GitHub`；空配置不返回空串  
- [ ] identity DisplayName：`login` 优先，否则 `name`，否则 null  
- [ ] 配好密钥后 providers 含 `github`，登录页显示 GitHub 圆钮  
- [ ] 未绑定账号登录 → 40016（或现网等价）  
- [ ] 已绑定 → 正常发令牌  
- [ ] 令牌不进 redirect URL（沿用 ticket）  
- [ ] 日志/异常消息不含 client_secret、access_token  

### 提交建议

`feat(auth): add TenonAdmin.Auth.GitHub satellite package`

---

## 5. D1-③ `TenonAdmin.Auth.WeChat`

### 目标

个人微信开放平台网站应用扫码登录；**不得**与 `wecom` 配置/图标混淆。

### 规格要点

| 项 | 值 |
|----|-----|
| 包名 | `TenonAdmin.Auth.WeChat` |
| 注册 | `AddTenonAdminWeChatAuth` |
| 配置节 | `TenonAdmin:ExternalAuth:WeChat`（AppId / AppSecret / **Provider** DisplayName 可选 / Icon 可选） |
| 形态 | 网站应用 **`qrconnect`**（PC 扫码） |
| Subject | **仅 `unionid`**（S-A；修订原 S1） |
| 无 unionid / 空 | 交换失败；**禁止**降级 openid（防身份漂移，见 decisions §2.1） |
| Provider DisplayName | 默认 **`微信`**；空配置回退默认（N1） |
| Identity DisplayName | 本批恒 **`null`**（W-n2：不调 userinfo） |
| 依赖 | 仅 Core + Microsoft.\*；**注入** `HttpClient`（H1），裸 HTTP |
| 默认未绑定 | 拒绝（U2） |
| Code | **硬固定** `wechat`（K1；options 不暴露 Code） |

### HTTP 契约（C1 · 微信开放平台网站应用）— 实现必须遵守

| 项 | 规格 |
|----|------|
| Authorize | `https://open.weixin.qq.com/connect/qrconnect`；query 必含：`appid`、`redirect_uri`（URL 编码）、`response_type=code`、`scope=snsapi_login`、`state`；授权 URL **以 `#wechat_redirect` fragment 结尾** |
| Token | `GET https://api.weixin.qq.com/sns/oauth2/access_token`；query：`appid`、`secret`、`code`、`grant_type=authorization_code`（secret 在 query 为厂商约定；**日志禁止完整 URL**） |
| Userinfo | **本批不调用**（W-n2）；token 响应已含 unionid 即可完成 identity |
| Subject | token JSON **`unionid`**；缺失/空 → 失败，不用 `openid` 顶替 |
| openid 字段 | 可忽略；**不**写入 `ExternalIdentity.Subject` |
| Identity DisplayName | 恒 `null` |
| 成功判定 | HTTP 2xx、JSON 无业务 `errcode`（或 0）、`unionid` 非空 |
| 业务错误 | `errcode` ≠ 0 → 交换失败；日志记 errcode/errmsg 摘要 |
| 非 2xx / 缺字段 / 超时 / 取消 | 失败或传播取消；**token 交换不重试** |
| 敏感信息 | secret、access_token、refresh_token 不得进日志 / 异常 Message / 浏览器可见正文 |

### 验收

- [ ] 与 GitHub 刀对称的 solution build/测试/providers 点亮  
- [ ] `Code` 恒为 `wechat`（options 无 Code 配置项）；前端绿标（非企微标）  
- [ ] providers 项 DisplayName 默认 `微信`；空配置不返回空串  
- [ ] identity DisplayName 恒 null；**无** userinfo HTTP 调用  
- [ ] 文档明确：企业微信用 WeCom，个人微信用本包；须能返回 unionid  
- [ ] MinimalHost 注释示例  
- [ ] 仅 openid、无 unionid → 交换失败  
- [ ] 有 unionid → Subject 为该 unionid；绑定/登录按 `(wechat, unionid)`  
- [ ] 不得以 openid 成功落库后静默改用 unionid  
- [ ] 日志不含 secret / access_token / 完整带 secret 的 token URL  
- [ ] fake handler 覆盖成功、缺 unionid、errcode、非 2xx  

### 提交建议

`feat(auth): add TenonAdmin.Auth.WeChat open-platform satellite package`

---

## 5.1 D1-④ 系统配置 · 第三方登录 Tab（方案 B）

### 目标

管理员在 **系统配置** 中开关各第三方在**登录页**的显示，无需手填 `sys.externalauth.*.enabled`，无需重启。

### 后端

| 项 | 规格 |
|----|------|
| 公开列表 | `GET /api/v1/auth/external/providers` **不变**（仅 `IsEnabledAsync==true`） |
| 管理列表 | 新增需登录+权限的接口，例如 `GET /api/v1/auth/external/providers/all`（路径以实现时 OpenAPI 为准）：返回 **全部已注册** `IExternalAuthProvider`，每项含 `code`、`displayName`、`icon`、`enabled`（当前运营开关） |
| 权限 | 权限码 = 路由规范化（与仓内一致）；建议与系统配置读写同级或挂在 ExternalAuth 模块下，菜单/按钮种子按现网取号规则追加 |
| 写开关 | 优先复用现有 **配置写入 API**（`IConfigService` / ConfigController）更新 `sys.externalauth.{code}.enabled`；若封装专用 `PUT .../providers/{code}/enabled` 亦可，内部仍写同一键 |
| 缺省 | 键不存在 = 启用（与 `IsEnabledAsync` 一致） |
| 种子 | `ConfigSeed` 为官方 code 预置行（至少 `wecom`/`dingtalk`；`github`/`wechat` 可一并预置）：`ConfigKey=sys.externalauth.{code}.enabled`，`ConfigValue=true`，`Name` 中文说明，`GroupCode=externalauth`，`Remark` 写明「关闭后登录页不显示该按钮」；**新 Id 不与现有 ConfigSeed Id 冲突** |
| 缓存 | 走现有配置缓存穿透；改完后登录页再拉 providers 即生效（若有统一失效，跟其它 sys_config 一致） |

### 前端（双模板）

| 项 | 规格 |
|----|------|
| 入口 | `views/system/config/index` 增加 Tab「第三方登录」；组件如 `ExternalAuthConfig.vue` / `.tsx` |
| 列表 | 调管理列表 API；行：brand 小标（同 brand map）+ 显示名 + code + **开关「登录页显示」** |
| 空态 | 无已注册 provider：提示需在部署中注册/配置卫星包或 OIDC，**不是**业务错误 |
| 保存 | 切换开关即保存或底部统一保存（对齐 SecurityConfig 既有交互习惯，两模板一致） |
| 其它 Tab | `OtherConfig` **排除** `GroupCode=externalauth`（或排除 `sys.externalauth.` 前缀），避免与结构化 Tab 双入口 |
| i18n | 中英：`config.tab.externalAuth` 等 |

### 验收

- [ ] 关 `enabled` → 匿名 `GET providers` 不再含该 code；登录页圆钮消失  
- [ ] 开 `enabled` 且已注册 → providers 含该 code；登录页出现  
- [ ] 关后 authorize 仍拒绝（现网 `OAuthProviderDisabled`）  
- [ ] 管理列表含 **已禁用** 的已注册项（与公开 providers 不同）  
- [ ] 种子可幂等；双模板 typecheck/lint；相关测试或手测 checklist  
- [ ] 密钥未出现在配置中心  

### 建议顺序

可在 D1-① 之后、或与 ① 并行（依赖 brand map 仅 UI）；**不**依赖 GitHub/微信包——有 wecom/dingtalk/oidc 即可验收。

### 提交建议

`feat(config): external auth providers tab for login visibility`  
（可拆 backend 管理 API + seed / web / web-react）

---

## 6. 非目标 / 第二批记账

**本批明确不做**

- QQ 互联 / Gitee 后端包  
- 公众号 H5、小程序登录  
- 默认 JIT、邮箱自动并号  
- 双前端共享组件库  
- Google/Microsoft/Apple 大图标库  
- 配置中心拖拽排序第三方按钮、在 UI 里填 ClientSecret  
- 把第三方开关塞进「安全策略」Tab（已否决，用独立 Tab）  

**第二批（仅记账，无排期）**

- [ ] `TenonAdmin.Auth.QQ`（前端 `qq` 图标已占位）  
- [ ] 若有需求：`TenonAdmin.Auth.Gitee`（前端 `gitee` 已占位）  

---

## 7. 轮次日志

| 日期 | 刀 | 摘要 | 提交 |
|------|----|------|------|
| 2026-07-31 | — | grilling 定稿；文档落入 `docs/external-login-brand/` | （docs） |
| 2026-08-01 | — | Codex review grilling：P1/P2 全定并回填 decisions/ledger | （docs） |
| 2026-08-01 | — | 运营 UI：方案 B 独立 Tab「第三方登录」→ D1-④ | （docs） |
| 2026-08-01 | ①④②③ | 全刀落地：品牌 UI、配置 Tab、GitHub/WeChat 包 + 测试 | （impl） |

---

## 8. 相关路径速查（探查锚点，实现前再核对）

| 区域 | 路径提示 |
|------|----------|
| Provider 契约 | `backend/src/TenonAdmin.Core/Security/IExternalAuthProvider.cs` |
| 选项 | `backend/src/TenonAdmin.Core/Options/AdminExternalAuthOptions.cs` |
| 企微/钉钉样板 | `backend/src/TenonAdmin.Auth.WeCom/` · `Auth.DingTalk/` |
| API | `backend/src/TenonAdmin.AspNetCore/ExternalAuth/` |
| ADR 0002 | `docs/adr/0002-batch-d-external-login-sso.md` |
| 本主题决策 | `docs/external-login-brand/decisions.md` |
| Vue 登录 SSO | `web/src/views/login/LoginForm.vue` |
| React 绑定等 | `web-react/` 个人中心 bindings + 登录表单（以仓内检索为准） |
