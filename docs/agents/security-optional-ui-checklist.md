# 可选安全 — 网页自审功能清单

- 日期：2026-07-31
- 分支：`feature/mlps-level3-phase1`（未要求 push；可本地自审）
- 决策：[ADR 0006](../adr/0006-general-admin-optional-security.md)
- 配置键：[security-optional-config.md](security-optional-config.md)
- 合入收口：[security-optional-merge-notes.md](security-optional-merge-notes.md)

本文只列**能在浏览器里看到、能点的**路径，供维护者对照验收与提问。  
勾选列给本地打印/复制用；不要求提交勾选结果。

---

## 1. 本地怎么起

| 端 | 地址 | 命令 |
|----|------|------|
| 后端 | http://localhost:5100 | 仓库根：`dotnet run --project backend/samples/MinimalHost` |
| Vue | http://localhost:5173 | `cd web && npm run dev` |
| React | http://localhost:5174 | `cd web-react && npm run dev` |

- 两套前端**契约相同、代码不共享**；至少在一端完整走通，有余力再对端 spot-check。
- 超管账号：种子一般为 `superAdmin`；**密码以首次启动控制台打印为准**（已有库则用你本地已知密码）。
- Vite 默认把 `/api` 代理到 `:5100`（可用 `TENON_API_TARGET` 覆盖）。

### 1.1 审查用配置（Development）

默认 **TOTP / CookieMode 全关**。要测绑定与挑战，在  
`backend/samples/MinimalHost/appsettings.Development.json`（gitignored；可从 `.example` 复制）增加：

```json
"TenonAdmin": {
  "Security": {
    "Totp": {
      "Enabled": true,
      "RequireForSuperAdmin": false,
      "Issuer": "TenonAdmin",
      "ChallengeTtlSeconds": 300,
      "ReauthWindowMinutes": 5
    },
    "Session": {
      "CookieMode": false
    }
  }
}
```

| 想测什么 | 额外配置 |
|----------|----------|
| 绑定 / 恢复 / 登录 TOTP | `Totp:Enabled=true` |
| 超管必须二因子 | 再加 `Totp:RequireForSuperAdmin=true` |
| 指定用户必须二因子 | 用户编辑里打开 **强制 TOTP**（`forceTotp`） |
| Cookie + CSRF 会话 | `Session:CookieMode=true`（本机同源 proxy 一般可过） |

改配置后**重启后端**再生效。

---

## 2. 功能地图（入口 → 页面）

| 能力 | 入口（UI） | 路由 / 页面 | 依赖配置 |
|------|------------|-------------|----------|
| 登录页链到绑定 | 登录表单「设置身份验证器」 | `/mfa/bind` | 关 Totp 时页可开，绑定会失败 |
| 个人安全中心 | 顶栏头像下拉 → 账号安全 | `/personal/security` | 无；链到 bind 才需 Totp |
| 自助绑定 / 恢复码 UI | 个人安全按钮或登录链接 | `/mfa/bind`（可 `?account=`） | `Totp:Enabled` |
| 强制某用户 MFA | 系统 → 用户 → 编辑 | 表单「强制 TOTP」 | 写用户权限 |
| 登录 TOTP 挑战 | 账密登录后 | 登录二步 / 信令 UI | Totp 开 + 用户已绑或被强制 |
| 管理员清除 MFA | 用户列表「清除二因子」 | 弹确认后调 clear API | Totp 开；用户已绑；有 clear 权限 |
| Cookie 会话 | 登录后看 DevTools | Cookie `tenon_rt` / `tenon_csrf` | `Session:CookieMode` |
| 高危再认证弹窗 | 触发需 reauth 的写操作 | 全局 Reauth 弹窗 | Totp 能力相关 |

**不应再出现（产品路径已拆）：** 绑定邀请、InitGrant、紧急授权、MFA reset 邀请发码等菜单/按钮。

---

## 3. 清单 A — 默认关开关（零配置对照）

目的：确认「普通后台」不被可选安全拖垮；入口可见但能力关闭时行为可预期。

| # | 页面 / 操作 | 预期 | 勾选 |
|---|-------------|------|------|
| A1 | 打开登录页 | 账密可登录；有「设置身份验证器」链接 | ☐ |
| A2 | 点「设置身份验证器」 | 进入 `/mfa/bind` 整页 | ☐ |
| A3 | 在 bind 页尝试绑定（Totp 关） | 失败/无权限，**不**白屏、不卡死 | ☐ |
| A4 | 登录后顶栏用户下拉 | 有 **账号安全**（中/英随语言） | ☐ |
| A5 | 进入账号安全 | `/personal/security`：标题、说明、两按钮 | ☐ |
| A6 | 从个人安全点「设置身份验证器」 | 跳到 `/mfa/bind`（宜带当前 account query） | ☐ |
| A7 | 系统 → 用户管理 | 列表正常；编辑有 **强制 TOTP** 开关 | ☐ |
| A8 | 用户列表「清除二因子」 | 仅当行上用户已绑 TOTP 时出现；关 Totp 时 clear 应失败 | ☐ |

---

## 4. 清单 B — `Totp:Enabled=true`（主路径）

先改配置并重启后端。建议用 Authenticator 应用（或临时 OTP 工具）。

### 4.1 登录页自助绑定

| # | 操作 | 预期 | 勾选 |
|---|------|------|------|
| B1.1 | 登录页 → 设置身份验证器 | `/mfa/bind` | ☐ |
| B1.2 | 账号 + 当前密码 → 开始 | 出密钥 / otpauth（及前端展示的二维码若有） | ☐ |
| B1.3 | 填正确 6 位码完成 | 成功；**一次性展示恢复码**（立刻抄下） | ☐ |
| B1.4 | 同一账号再 start | 拒绝（已绑定） | ☐ |
| B1.5 | 错误密码 start | 失败；不泄露「账号是否存在」的差异话术（尽量） | ☐ |
| B1.6 | 错误 TOTP complete | 失败，挑战可重试或按产品提示重开 | ☐ |

### 4.2 个人中心入口

| # | 操作 | 预期 | 勾选 |
|---|------|------|------|
| B2.1 | 顶栏 → 账号安全 | 文案说明依赖服务端 Totp；按钮可用 | ☐ |
| B2.2 | 「设置身份验证器」 | 跳转 bind，query 尽量带 account | ☐ |
| B2.3 | 「使用恢复码」 | 进入可走恢复的绑定/恢复 UI（与 bind 页同一能力面） | ☐ |

### 4.3 登录二次因子

| # | 操作 | 预期 | 勾选 |
|---|------|------|------|
| B3.1 | 编辑测试用户，打开强制 TOTP 并保存 | 列表/详情反映强制 | ☐ |
| B3.2 | 该用户已绑 TOTP 后账密登录 | 密码过后进入 TOTP 步（或等价挑战 UI） | ☐ |
| B3.3 | 正确 TOTP | 进入布局壳 / 首页 | ☐ |
| B3.4 | 错误 TOTP | 失败可重试 | ☐ |
| B3.5 | （可选）`RequireForSuperAdmin=true` 且超管未绑 | 引导自助绑定，**不是**部署 InitGrant | ☐ |
| B3.6 | 用户未绑且未强制 | 账密直通（与「仅开 Totp 能力」一致） | ☐ |

### 4.4 恢复码

| # | 操作 | 预期 | 勾选 |
|---|------|------|------|
| B4.1 | 用绑定下发的恢复码完成恢复流程 | MFA 清除；会话吊销；需重新绑定 | ☐ |
| B4.2 | 同一恢复码再用 | 无效 | ☐ |
| B4.3 | 关 `Totp:Enabled` 后调恢复 | 拒绝（门闸） | ☐ |

### 4.5 管理员清除 MFA

| # | 操作 | 预期 | 勾选 |
|---|------|------|------|
| B5.1 | 用户列表对已绑用户点「清除二因子」 | **先确认对话框** | ☐ |
| B5.2 | 取消 | 不清除 | ☐ |
| B5.3 | 确认 | 成功提示；该用户 `totpEnabled` 消失 | ☐ |
| B5.4 | 被清用户再登录 | 无二因子（若仍 forceTotp 则须重绑才能过登录） | ☐ |
| B5.5 | Vue 与 React 各走一遍 B5.1–B5.3 | 行为一致 | ☐ |

### 4.6 高敏权限 / 再认证（可选）

| # | 操作 | 预期 | 勾选 |
|---|------|------|------|
| B6.1 | 系统配置中高敏权限追加/删除（若菜单有权限） | 写操作可能弹出再认证 | ☐ |
| B6.2 | 弹窗输 TOTP 或密码 | 通过后原请求成功 | ☐ |

侧栏若看不到「安全诊断 / baseline」：菜单种子默认禁用，**可跳过**，不算本清单必过项。

---

## 5. 清单 C — `Session:CookieMode=true`

改配置并重启；用浏览器 DevTools → Application / Network。

| # | 操作 | 预期 | 勾选 |
|---|------|------|------|
| C1 | 登录成功 | refresh 在 **HttpOnly** Cookie（如 `tenon_rt`）；可读 CSRF Cookie（如 `tenon_csrf`）；勿依赖 localStorage 持久 refresh | ☐ |
| C2 | 硬刷新或深链打开业务页 | 能静默恢复（内存 access + Cookie refresh） | ☐ |
| C3 | 任意写操作（改资料、保存用户） | 请求头带 `X-Tenon-CSRF`（与 Cookie 双提交） | ☐ |
| C4 | 登出 | Cookie 清理；再访问业务回登录 | ☐ |
| C5 | 对照：`CookieMode=false` 再登录 | body/localStorage 兼容模式（历史行为） | ☐ |

跨源（非 Vite 同源 proxy）须额外配 `Session:CookieDomain` 与 CORS 凭证；本机默认审查一般不测跨源。

---

## 6. 清单 D — 负面（不应出现）

| # | 检查 | 预期 | 勾选 |
|---|------|------|------|
| D1 | 登录 / 个人安全 / 用户管理 | 无「邀请绑定 / InitGrant / 紧急授权」入口 | ☐ |
| D2 | 用户操作列 | 无旧版「发 MFA 邀请」类按钮 | ☐ |
| D3 | Network | 无 `/api/v1/sys/mfa/reset`、invite 类路径 | ☐ |
| D4 | 启动控制台 | 无「生产未开 Level3 即不合规」类强制产品告警 | ☐ |

---

## 7. 推荐审查顺序（约 30–45 分钟）

1. **A** 默认关开关（入口与零配置）  
2. 打开 `Totp:Enabled` → **B1 绑定 → B2 个人中心 → B3 登录挑战**  
3. **B4 恢复码 → B5 清除 MFA（含确认）**  
4. （可选）**C CookieMode**  
5. **D 负面清单**  
6. 有余力：另一前端模板只做 B1 + B5  

记录问题时建议写清：**Vue 还是 React、步骤编号（如 B5.1）、账号、接口 status/code、是否开 Totp/CookieMode**。

---

## 8. 本清单**不覆盖**（非网页主路径）

| 项 | 说明 |
|----|------|
| 预检 API / `SecurityBaseline*` 重命名 | 多为运维/诊断；菜单常禁用 |
| 死代码删除与测试类重命名 | 代码/测试层 |
| gen:api 生成物正确性 | 已由分支内 schema 提交覆盖；页面不直接展示 |
| 密码策略地板 / 遗留 Profile=Level3 钳位 | 配置与读策略层；非独立菜单 |
| 登录锁与自助 bind 的交叉爆破 | 需专门安全测试 |

合入门禁命令仍见 [security-optional-merge-notes.md](security-optional-merge-notes.md)。

---

## 9. 问题记录模板（可选）

复制填写，便于对照修：

```text
模板: Vue 5173 / React 5174
配置: Totp=  CookieMode=  RequireForSuperAdmin=
步骤编号:
操作:
预期:
实际:
接口: method path → status / envelope code
截图或 Network 备注:
```

---

## 10. 相关提交（便于对照 diff）

自 `origin/main` 合入基线起，本分支安全相关大致包括：

- 可选 TOTP/Cookie（ADR 0006）与 P2 修复  
- 个人安全页 + 登录入口  
- 删除 InitGrant/Invite/闲置 Job  
- 预检类型重命名  
- 审查 P2：TTL / 预检文案 / recovery 门闸 / 清 MFA 确认  
- 双前端 `gen:api`  

完整 log：`git log origin/main..HEAD --oneline`（或本地 merge-base 等价范围）。
