# 外部登录品牌化 — 功能测试清单

> 给**人**做验收用（含真机第三方联调）。自动化单测通过 ≠ 功能完成；下列 **「真机必过」** 项必须真实走一遍 OAuth。  
> 规格：`decisions.md` / `ledger.md`。  
> 日期：2026-08-01

---

## 0. 测前准备

### 0.1 本地跑起来

| # | 步骤 | 期望 |
|---|------|------|
| 0.1.1 | 后端：`dotnet run --project backend/samples/MinimalHost`（或你们惯用 host） | 启动成功；控制台有库初始化/种子信息 |
| 0.1.2 | 前端 Vue：`cd web && npm run dev`（:5173）和/或 React：`cd web-react && npm run dev`（:5174） | 代理到后端 API 正常 |
| 0.1.3 | 浏览器打开登录页 | 能账密登录超管（种子账号见控制台 / appsettings） |

### 0.2 回调与域名（真机 OAuth 关键）

前后端分离时必须一致：

| 配置项 | 含义 | 本地示例（按实际改） |
|--------|------|----------------------|
| `TenonAdmin:ExternalAuth:CallbackBaseUrl` | 后端公网/可被 IdP 访问的基址（**厂商回调打到后端**） | 常用 `https://xxx.ngrok.io` 或内网穿透；仅 localhost 时多数厂商要求登记该地址 |
| `TenonAdmin:ExternalAuth:FrontendResultPath` | 后端换票后 302 回前端的路径 | 开发常写成完整前端 URL，如 `http://localhost:5173/oauth/callback` |

**注意**：

- 回调 URL 形态一般是：`{CallbackBaseUrl}/api/v1/auth/external/{provider}/callback`
- GitHub OAuth App / 微信开放平台里登记的 **Authorization callback URL** 必须与上述一致（含 http/https、端口、路径）
- 微信还要求网站应用、开放平台账号绑定（否则 **没有 unionid**，本实现会故意失败）

### 0.3 厂商侧准备（真机）

#### GitHub（相对好测，建议先测）

1. [GitHub Developer Settings → OAuth Apps](https://github.com/settings/developers) 新建 OAuth App  
2. Homepage URL：任意（如 `http://localhost:5173`）  
3. Authorization callback URL：`{CallbackBaseUrl}/api/v1/auth/external/github/callback`  
4. 拿到 Client ID / Client Secret  

`appsettings.Development.json`（勿提交真密钥）示例：

```json
"TenonAdmin": {
  "ExternalAuth": {
    "CallbackBaseUrl": "https://你的穿透域名",
    "FrontendResultPath": "http://localhost:5173/oauth/callback",
    "GitHub": {
      "ClientId": "...",
      "ClientSecret": "...",
      "DisplayName": "GitHub"
    }
  }
}
```

重启后端；`Program.cs` 已调用 `AddTenonAdminGitHubAuth`。

#### OIDC（**GitHub 之后优先真机**，门槛低于微信）

标准 OIDC 授权码 + PKCE，内核内置（`TenonAdmin:ExternalAuth:Oidc`），**无需新 NuGet**。可选：

1. **Auth0** 免费租户 / **Keycloak** Docker / 公司现有 IdP  
2. 回调 URL：`{CallbackBaseUrl}/api/v1/auth/external/{code}/callback`（`code` = 配置里的 `Code`，如 `keycloak`）  
3. 多数 IdP 允许 `http://localhost:5100` 开发回调（比微信开放平台省事）  

```json
"Oidc": [
  {
    "Code": "keycloak",
    "DisplayName": "Keycloak",
    "Icon": "ph:key-duotone",
    "Authority": "https://your-idp/realms/demo",
    "ClientId": "tenon-admin",
    "ClientSecret": "...",
    "Scopes": "openid profile email",
    "UsePkce": true
  }
]
```

共享能力（与 GitHub 联调已修）：未绑定 → pending-link + **同浏览器 binder** + 登录后**确认绑定**；解绑软删。

#### 钉钉 / 企业微信（有企业应用再测）

- **钉钉**：`AppKey` + `AppSecret`；Subject=`unionId`（有 nick 可进 pending 确认框）  
- **企微**：`CorpId` + `AgentId` + `CorpSecret`；Subject=`userid`  
- 实现已对齐 GitHub：`IHttpClientFactory`、网络异常 → 40015、取消传播  

假占位密钥勿写入 Development（非空 CorpId/AppKey 会注册并点亮按钮）。

#### 个人微信（门槛高，可后置）

1. 微信开放平台 → 网站应用 → AppId / AppSecret  
2. 授权回调域 = `CallbackBaseUrl` **域名**；多半要 HTTPS 穿透  
3. 必须返回 **unionid**（无则交换失败）  

```json
"WeChat": {
  "AppId": "...",
  "AppSecret": "...",
  "DisplayName": "微信"
}
```

### 0.4 测试账号策略（U2：默认拒绝未绑定）

| 场景 | 做法 |
|------|------|
| **默认（推荐先测）** | 先用超管账密登录 → 个人中心 **账号绑定** 绑 GitHub/微信 → 退出 → 再用第三方登录 |
| **JIT 演示** | 配置中心或 `sys_config` 设 `sys.externalauth.github.provisioning=provision`（及可选 defaultRoleIds/defaultOrgId）→ 未绑账号也可首次登录建号。**生产慎用** |

---

## 1. 登录页 UI（可不接真厂商）

> 可用多个 OIDC 假配置 / 已启用 provider 做密度测试；或临时注册多个 provider。

| # | 步骤 | 期望 | 真机/可模拟 |
|---|------|------|-------------|
| 1.1 | 未配置任何外部登录 | 无「其他登录方式」整段 | 可模拟 |
| 1.2 | 仅 1 个 provider | 一个圆形图标按钮；悬停/读屏有显示名 | 可模拟 |
| 1.3 | 4 个 provider | 四个圆钮横排，无「…」 | 可模拟 |
| 1.4 | ≥5 个 provider | 前 4 平铺 + 「…」；菜单内 **小图标+名称**；顺序与接口一致 | 可模拟 |
| 1.5 | 点圆钮 | 浏览器顶层跳转 IdP（或授权页） | 真机 |
| 1.6 | Vue(:5173) 与 React(:5174) | 行为/观感对等 | 两边都测 |

**API 自检**（浏览器或 curl，匿名即可）:

```http
GET /api/v1/auth/external/providers
```

只应返回 **enabled=true** 且已注册的项。

---

## 2. 系统配置「第三方登录」Tab

| # | 步骤 | 期望 | 真机 |
|---|------|------|------|
| 2.1 | 超管 → 系统配置 → **第三方登录** | 列出**已注册** provider（含当前已关闭的） | ✅ |
| 2.2 | 关闭某 provider「显示」并保存 | `GET providers` 不再含该项；登录页圆钮消失 | ✅ |
| 2.3 | 未登录直接访问关闭后的 authorize URL | 业务失败/禁用（非裸 500） | ✅ |
| 2.4 | 再打开并保存 | 登录页按钮恢复 | ✅ |
| 2.5 | 未装包/未配密钥的 code | 列表可不出现；或出现但点授权会失败——以「未注册不出现」为准 | ✅ |
| 2.6 | 「其他配置」Tab | 不应重复刷一堆 `sys.externalauth.*` 结构化键（已排除 externalauth 组） | ✅ |

---

## 3. 个人中心绑定（真机推荐路径）

| # | 步骤 | 期望 |
|---|------|------|
| 3.1 | 账密登录 → 个人中心 **账号绑定** | 启用的 provider 列表有 brand 小标 |
| 3.2 | 点「绑定」GitHub | 跳转 GitHub 授权 → 回 `/oauth/callback` → 成功回绑定页 → 显示已绑定 |
| 3.3 | 再绑同一 GitHub 到**另一用户** | 应失败（已绑他人） |
| 3.4 | 解绑 | 列表变未绑定；之后第三方登录应回到「未绑定拒绝」（未开 JIT 时） |
| 3.5 | 运营关闭该 provider 后刷新绑定页 | **仍能看到已绑定行**，标「已停用」，**可解绑**，不可再绑 |

---

## 4. 真机：GitHub 登录全流程（**必过**）

前置：§0.3 GitHub 已配；§3.2 已用本地账号绑定过（默认策略）。

| # | 步骤 | 期望 | 通过 |
|---|------|------|------|
| 4.1 | 退出登录 | 登录页出现 GitHub 圆钮 | ☐ |
| 4.2 | 点 GitHub | 跳转 github.com 授权页；scope 仅基础用户读（无 email 勾选压力） | ☐ |
| 4.3 | 授权通过 | 经后端 callback → 前端 `/oauth/callback` 短暂处理 → **进入系统**（有 token/会话） | ☐ |
| 4.4 | 拒绝授权 / 取消 | 回到前端错误提示或登录页，**不 500 白屏** | ☐ |
| 4.5 | 未绑定账号点 GitHub 登录（未开 JIT） | 明确业务错误（未绑定），不建幽灵用户 | ☐ |
| 4.6 | 配置 Tab 关闭 github 后点登录/收藏 authorize | 按钮消失或授权拒绝 | ☐ |
| 4.7 | （可选）开 JIT 后新 GitHub 用户首次登录 | 自动建本地用户且可进系统；关 JIT 后行为恢复拒绝 | ☐ |

**失败排查捷径**：

- 回调 404 / 域名不符 → CallbackBaseUrl / 厂商回调 URL  
- 一直未绑定 → 是否先做 §3 绑定  
- 交换失败 → ClientSecret、时钟、code 二次使用  

---

## 5. 真机：个人微信扫码（**必过，若宣称支持微信**）

| # | 步骤 | 期望 | 通过 |
|---|------|------|------|
| 5.1 | 登录页微信圆钮 | 显示「微信」类名，与企微图标区分 | ☐ |
| 5.2 | 点击 → 微信二维码页 | 可扫码 | ☐ |
| 5.3 | 已绑定本地账号的微信扫码 | 登录成功进系统 | ☐ |
| 5.4 | 仅 openid、无 unionid 的应用配置错误 | **登录失败**（交换失败），不产生错误绑定键 | ☐ |
| 5.5 | 未绑定 + 默认拒绝 | 失败提示未绑定 | ☐ |
| 5.6 | 绑定页绑/解微信 | 与 GitHub 对称 | ☐ |

若 5.4 在「正确挂了开放平台仍无 unionid」——查开放平台账号绑定与网站应用状态，属部署问题。

---

## 6. 安全与负面用例（抽测）

| # | 步骤 | 期望 |
|---|------|------|
| 6.1 | 抓包或日志：登录过程 | 日志中无 client_secret、access_token 明文；微信 token URL 不要打完整 query |
| 6.2 | 伪造/重放旧 callback | 失败（state/票据一次性） |
| 6.3 | 禁用 provider 后旧收藏链接 | 拒绝 |
| 6.4 | 两浏览器同时首开配置保存（可选） | 不 500 |

---

## 7. 双模板覆盖矩阵

| 能力 | Vue `web` | React `web-react` |
|------|-----------|-------------------|
| 登录圆钮 / 溢出 | ☐ | ☐ |
| 配置 Tab 开关 | ☐ | ☐ |
| 个人中心绑定 | ☐ | ☐ |
| GitHub 真机登录 | ☐ | ☐（至少一侧完整；另一侧 UI 对等即可） |
| 微信真机登录 | ☐ | ☐ |

**最低完成线（建议写进验收）**：

1. **GitHub 真机**：绑定 → 登出 → 第三方登录进系统 → 解绑 → 再登被拒（默认策略）  
2. **配置 Tab**：关/开 github，登录页与 `GET providers` 同步变化  
3. **绑定页 B-A**：关闭 provider 后仍能解绑  
4. **Vue + React** 登录 UI 与配置 Tab 各走一遍  

微信：作为中国站能力则 **5.x 也必须过**；若本环境暂无开放平台，在清单注明「微信真机 blocked：缺 unionid/应用」，不与 GitHub 混为一谈。

---

## 8. 建议测试顺序（省时间）

```
1. 起服务 + 账密登录
2. 配置 Tab 列表是否出现 github（配好密钥后）
3. 个人中心绑定 GitHub  ← 真机
4. 登出 → 点 GitHub 登录进系统  ← 真机必过
5. 配置关 github → 按钮消失
6. 再开；解绑；未绑再登应失败
7. （可选）JIT 开/关
8. （可选）微信扫码全流程
9. 另一前端模板抽测 UI + 配置 Tab
```

---

## 9. 验收签字区

| 项 | 结果 | 测试人 | 日期 | 备注 |
|----|------|--------|------|------|
| GitHub 真机登录闭环 | ☐ 通过 / ☐ 失败 | | | |
| 配置 Tab 显隐 | ☐ 通过 / ☐ 失败 | | | |
| 绑定 / 解绑 / 已停用可解绑 | ☐ 通过 / ☐ 失败 | | | |
| Vue 登录 UI | ☐ 通过 / ☐ 失败 | | | |
| React 登录 UI | ☐ 通过 / ☐ 失败 | | | |
| 微信真机（若宣称） | ☐ 通过 / ☐ 跳过 / ☐ 失败 | | | |

**功能完成判定（建议）**：上表「GitHub 真机 + 配置显隐 + 绑定闭环」全通过，且至少一前端完整、另一前端 UI/配置无回归，即可认为本批**可交付**；微信按产品承诺决定是否 blocker。

---

## 10. 相关链接

- 决策：`docs/external-login-brand/decisions.md`  
- 台账：`docs/external-login-brand/ledger.md`  
- MinimalHost 示例：`backend/samples/MinimalHost/appsettings.Development.json.example`  
- 回调页：前端 `/oauth/callback`  
