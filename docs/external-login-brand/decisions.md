# ADR 0007 — 外部登录品牌化 UI + GitHub / 个人微信卫星包

- 状态:**已采纳**(2026-07-31)
- 相关:[[ADR-0002]]（外部登录 / SSO 三处取舍，本 ADR **不推翻**）；同目录 [ledger.md](./ledger.md)（施工台账）
- 来源:2026-07-31 grilling（参考 Gitee 登录页第三方图标条）
- 正文位置:`docs/external-login-brand/decisions.md`（本文件为权威正文）

## 背景

批次 D（[[ADR-0002]]）已落地 `IExternalAuthProvider`、OIDC 内置、企微 / 钉钉卫星包、`GET providers` 驱动登录按钮、个人中心绑定，以及默认未绑定拒绝 + per-provider JIT。

现状不足：

1. **呈现偏功能按钮**（图标 + 文案），不像 Gitee 等产品站的「其他方式登录」纯品牌圆钮 + 溢出「…」。
2. **常用互联网身份源未装箱**：GitHub、个人微信（开放平台网站扫码）与已有 **企业微信 / 钉钉** 不是同一产品线，需独立卫星包。
3. 消费者仍可通过自注册 `IExternalAuthProvider` 扩展，但官方模板与首批包应给出 **可复制成法**。

本 ADR 冻结 UI 语言、首批 provider 与交付切分；协议骨架、票据交换、绑定表、默认拒绝策略 **全部沿用 0002**。

## 决策

### 1. 交付形态 = 前端呈现升级 + 新卫星包（不进 Core 重依赖）

- 不把 QQ / 微信 / GitHub 厂商 SDK 或品牌资源打进内核四包。
- 新身份源按 `TenonAdmin.Auth.WeCom` / `.DingTalk` 成法：独立 NuGet、仅 Core + Microsoft.\*、裸 `HttpClient`、`AddTenonAdminXxxAuth` 前置注册。
- 登录按钮仍只渲染 `GET /api/v1/auth/external/providers` 返回的 **已启用** 项。

### 2. 首批身份源 = GitHub + 个人微信；QQ 第二批

| 首批 | 包 | code | 说明 |
|------|-----|------|------|
| GitHub | `TenonAdmin.Auth.GitHub` | `github` | OAuth App；Subject = 数字 `id`（字符串化）；scope = **仅 `read:user`**（E1，见 §13） |
| 个人微信 | `TenonAdmin.Auth.WeChat` | `wechat` | 开放平台 **网站应用** `qrconnect`；Subject = **仅 `unionid`**（见决策 2.1） |

- **企业微信** 继续 `TenonAdmin.Auth.WeCom` / `wecom`，与个人微信 **图标与 code 绝不混用**。
- **钉钉** 继续现有包；本批只跟 UI 对齐。
- **QQ / Gitee 后端包不做本批**；前端 brand map 可先占位图标（见决策 5）。
- 不按邮箱自动并号（避免账号夺取）；本批 GitHub **不**申请邮箱 scope（§13）。

#### 2.1 微信 Subject = 仅 `unionid`（修订原 S1；2026-08-01 review P1-1）

原「unionid 优先，否则 openid」在现有 `(Provider, Subject)` 唯一绑定下会导致**身份漂移**：先以 openid 落库，后改用 unionid 则找不到绑定，且同用户同 provider 无法再绑。

**定案（S-A）**：

- `ExternalIdentity.Subject` **只**取 `unionid`（非空字符串）。
- 响应中无 `unionid` / 为空 → **交换失败**（映射现网 OAuth 交换失败错误码），**不得**降级为 openid。
- 文档与包 README 写明：网站应用须挂在微信开放平台账号下并确保返回 unionid；配置不当视为部署错误，不靠运行时迁就。
- **本批不做** openid→unionid 迁移、不做 alias 表。
- 必测：仅 openid → 失败；有 unionid → Subject 为 unionid；禁止「先后两种 subject 静默变两个身份」。

### 3. 未绑定策略 = 沿用 0002 默认拒绝；文档示范 JIT（U2）

- 代码默认：**未绑定 → `OAuthAccountNotBound`**，须本地账号 + 个人中心绑定。
- per-provider `sys.externalauth.{code}.provisioning` 等运营键 **不变**。
- **U2 示范路径（2026-08-01 review P2-4 收紧）**：JIT **不是** appsettings 项，而是 `sys_config` 运营键。  
  - MinimalHost：**只**注释 appsettings 连接项（ClientId/Secret 等），**默认不**打开 JIT、不 seed 打开 provisioning。  
  - 文档（包 README / 本目录 / site 择一）写清真实操作：在配置中心或通过现有配置 API 设置  
    - `sys.externalauth.github.provisioning=provision`（或 `wechat`）  
    - 可选 `sys.externalauth.{code}.defaultRoleIds`、`defaultOrgId`  
    - 撤回：改回 `reject` 或删除键（缺省 reject）  
  - **禁止**把「改 appsettings 即 JIT」写成示例。

### 4. UI 语言 = Gitee 风纯图标圆钮

| 项 | 裁定 |
|----|------|
| 呈现 | **P1** 纯图标圆钮；`title` / tooltip = `displayName` |
| 溢出 | **O1** 登录页最多平铺 **N=4**，第 5 个起进「…」菜单（菜单内小圆标 + 名称） |
| 图标源 | **I3** 已知 `code` → 前端精修 SVG；否则回退后端 `icon`（**仅 Iconify 名称**，见 §10）；再否则首字母 / 通用 SSO 标 |
| 视觉 | **V1** 中性圆底 + 彩色品牌 glyph；「…」中性同尺寸；悬停统一反馈，无厂商定制动画 |
| 表面 | **B2** 登录页 + **个人中心绑定** 共用 brand map；绑定页 **全量平铺**（不套 N=4） |
| 模板 | **F1** `web/` 与 `web-react/` 对等实现；**零共享**，brand map / 组件各维护一份 |

OIDC 多实例的 `code` 由配置自定，不为每个 IdP 画品牌标。

### 5. Brand map 预置 code（M2）

前端第一批预置精修标：

`github` · `wechat` · `wecom` · `dingtalk` · `gitee` · `qq`

其中 `gitee` / `qq` **仅图标占位**，无本批后端承诺。

### 6. 落地节奏 = 四刀（D1）

1. **双模板 UI**（圆钮 / 溢出 / brand map / 绑定页）— 现有 wecom/dingtalk/oidc 立刻受益  
2. **`TenonAdmin.Auth.GitHub`**  
3. **`TenonAdmin.Auth.WeChat`**  
4. **系统配置 Tab「第三方登录」**（§15）— 可与 ① 后或并行，不依赖 ②③  

每刀可独立合并；微信开放平台申请不堵 UI 与 GitHub。真机联调不挡合并（对齐企微/钉钉先例）。

施工细项、验收与勾选见 [ledger.md](./ledger.md)。

### 7. 卫星包 HTTP 接缝与契约深度（2026-08-01 review P1-2）

- **H1**：GitHub / WeChat provider **构造注入 `HttpClient`**（生产用 `IHttpClientFactory` 命名客户端或等价注册；测试传入带 `HttpMessageHandler` 的 client）。**禁止**新包再采用 WeCom 式 static `HttpClient` 作为唯一路径。
- **C1**：token / userinfo 的请求形态、必选 header、字段路径、错误映射、敏感信息脱敏、**授权码交换不重试** 等写进 [ledger.md](./ledger.md) 附录，达到可执行/可单测，不靠「对照官方文档自行发挥」。
- 内核 `ExternalAuthTests` 仍可整替 `IExternalAuthProvider` 测登录尾链；**包内**必须有 handler 级测试覆盖 mapping 与脱敏。

### 8. 官方 provider Code 硬固定（2026-08-01 review P1-3 · K1）

- `TenonAdmin.Auth.GitHub`：`IExternalAuthProvider.Code` **恒为** `github`；options **不暴露** Code。
- `TenonAdmin.Auth.WeChat`：`Code` **恒为** `wechat`；options **不暴露** Code。
- 绑定、`ExternalIdentity.Provider`、运营键 `sys.externalauth.{code}.*`、前端 brand map、验收断言 **全部**使用上述固定值。
- 需要第二套同厂商应用时：消费者自写 `IExternalAuthProvider`，不改编官方包。
- **不**在本批收紧历史 WeCom/DingTalk 的可配置 Code（兼容保留）。
- 包测 / 集成测断言 `Code == "github"|"wechat"`。

### 9. 两种 DisplayName 分离（2026-08-01 review P1-4 · N1 + W-n2）

| 概念 | 字段 | 规则 |
|------|------|------|
| **Provider 显示名** | `IExternalAuthProvider.DisplayName` → API providers 项 | 登录圆钮 tooltip / 列表用。GitHub 默认 **`GitHub`**；微信默认 **`微信`**。options 可覆盖；**null/空白回退默认，禁止返回空串**。 |
| **外部用户显示名** | `ExternalIdentity.DisplayName` → 绑定表 | 「绑的是谁」。GitHub：`login` → 否则 `name` → 否则 `null`。微信本批 **不调 userinfo**，恒 **`null`**（Subject 仅靠 token 的 unionid）。 |

- 测试分别断言：providers 接口的 DisplayName 非空默认；identity mapping 的 DisplayName 规则。
- 绑定页 UI 在用户 DisplayName 为空时，仍可靠 brand + provider 名 + 绑定状态表达。

### 10. icon fallback 仅 Iconify 名（2026-08-01 review P2-1 · I-A）

解析顺序（登录圆钮与绑定页共用）：

1. 前端 brand map 命中 `code` → 精修 SVG  
2. 否则 `icon` 为**非空 Iconify 名称**（如 `mdi:github`）→ 离线 Iconify 渲染（与现 `OfflineIcon` / `@iconify/react/offline` 一致）  
3. 否则 → 首字母或通用 SSO 标  

- **本批不支持** `http(s):` URL、data URL、远程 SVG；误配 URL 视为非法，走步骤 3。  
- 不把任意配置 URL 交给 `<img>` 或内联 SVG（安全/CSP）。  
- D1-① 至少覆盖：known brand、合法 Iconify、空 icon、未知 Iconify 名、非法 URL 形态。

### 11. 登录页 provider 顺序 = API 序（2026-08-01 review P2-2 · O-A）

- 前端对 `GET providers` 结果 **严格保序**，不重排、不做 brand 优先插队。
- 溢出规则：下标 `0..3` 平铺，`4..` 进「…」（N=4）；顺序变化只来自后端返回变化。
- 后端本批 **不新增** Order/Sort 字段；展示序 = DI / `IEnumerable<IExternalAuthProvider>` 枚举序（注册顺序）。文档与包 README 写明：要调整按钮顺序，调整注册顺序或 providers 组装顺序。
- 绑定页启用项按 providers API 序；**仅因 binding 补入**的已停用项接在后面（或按 binding 列表序追加，实现选定一种并测），**不做** N=4 截断。

### 13. GitHub scope 仅 `read:user`（2026-08-01 review P2-5 · E1）

- 授权 scope：**只** `read:user`；**不**申请 `user:email`。
- **不**调用 `GET /user/emails`；若 `ExternalIdentity` 有 Email 字段则保持 null。
- 与「不按邮箱并号」一致；减少授权页权限勾选。
- 日后若要邮箱资料回填，另开需求再加 scope（用户需重新授权）。

### 14. D1-① 测试策略 T2（2026-08-01 review P2-6）

- **必须自动化**（双模板 vitest，逻辑/组件测）：  
  0/1/4/5 provider 平铺与溢出切分（API 序）；icon 解析 I-A；绑定页合并 B-A（含已停用可解绑）；绑定页不 N=4 截断；圆钮可访问名（aria-label/title）在组件 props 层可断言。  
- **可手测抽样**：溢出菜单键盘/Esc/失焦（各 UI 库差异大，不阻塞合入，但 ledger 保留 checklist）。  
- D1-② / D1-③：包内 fake handler 测 URL/mapping/缺 subject/厂商错误/敏感信息不进日志；solution 级 build/test。

### 15. 系统配置独立 Tab「第三方登录」（2026-08-01 · 方案 B）

运营要在后台**动态控制登录页显示哪些第三方按钮**，不塞进「安全策略」长表，也不只靠「其他配置」手填 key。

**定案：**

- 系统配置中心新增 **并列 Tab**（与「安全策略」同级），中文名 **「第三方登录」**（i18n 可英译 External login）。
- **不**并入 `SecurityConfig`；键空间保持 `sys.externalauth.{code}.*`，**GroupCode** 建议 `externalauth`（与 `security` 分组隔离，OtherConfig 不重复展示本 Tab 已托管键）。
- 本批 Tab **主能力**：「登录页显示」开关 → 读写 `sys.externalauth.{code}.enabled`（`true`/`false`；与现网 `IsEnabledAsync` 一致，缺省 true）。
- **本批不做**（可二期叠在同一 Tab）：后台拖拽排序、在配置中心改 ClientSecret、JIT/默认角色的完整表单（JIT 仍按 §3 文档路径；若开关 UI 极便宜可加 `provisioning` 下拉，**非门禁**）。
- 密钥与连接仍只在 **appsettings**；配置中心只碰运营开关。
- 公开 `GET .../providers` 行为不变（仅已启用）；管理端另需 **已注册全量列表 + enabled**（见 ledger D1-④）。
- 双模板对等（`web/` + `web-react/` 配置中心各一页组件）。

### 12. 绑定页：已禁用但仍绑定（2026-08-01 review P2-3 · B-A）

- 列表数据 = **已启用 providers** ∪ **当前用户已有 binding 的 provider**（即便运营已关、providers 接口不再返回）。
- 已停用行：展示 brand（map / 回退）、名称（binding 侧 displayName 或 code）、**「已停用」** 状态；**允许解绑**；**禁止**再点绑定/授权。
- 后端 unbind **不**要求 provider 仍启用（现网已如此）；前端必须露出入口。
- 本批 **不做** 禁用时级联删除绑定。
## 后果

- 登录 / 绑定的信息架构与 0002 一致，仅呈现与官方 provider 集合扩展。
- 消费者装包 + 配 appsettings 即亮对应圆钮；未装包 / 未启用则不出现。
- 商标与品牌 SVG 留在 **前端模板**，不进后端 NuGet 分发面。
- 第二批（记账，非承诺排期）：QQ 互联包；若有需求再补 Gitee 包。

## 非目标（本批）

- 微信公众号网页授权、小程序登录  
- 默认 JIT 开户、按邮箱 link 账号  
- 两前端抽共享 UI 包  
- 内核引入厂商官方 SDK  
- 一锅端 Google / Microsoft / Apple 图标库  
