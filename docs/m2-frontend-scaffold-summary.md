# M2 前端脚手架 · 交付总结(供查阅)

> 完成日期:2026-07-07 · 分支:`dev`(未推送)
> 提交:`627c2a6 feat(web)`(脚手架 + 品牌)、`b70be0d fix(module)`(顺带的后端 M1.5 收尾)、`aa14231 chore`(gitignore .omx/)
> 相关文档:设计规范 `web/DESIGN.md` · tokens 单源 `web/src/styles/tokens.css` · 进度总账 `docs/dev-plan.md` §4「M2 · 工程脚手架首版」

---

## 1. 一句话

在 `web/` 落地了一套精简、地道的 **Vue 3.5 + Naive UI** 企业后台脚手架,端到端打通「**登录 → 多应用门户 → 菜单驱动动态路由 → tokens 换肤**」闭环,并接入品牌图标。当前是**可运行、已浏览器验证**的第一版骨架,非完整业务系统。

## 2. 技术栈(已锁定,见 `rebuild-design.md` §7.2/§13.6)

| 关注点 | 选型 | 说明 |
|---|---|---|
| 构建 | **Vite 6** + TypeScript | `vue-tsc --noEmit` 类型门槛 |
| 框架/UI | **Vue 3.5 + Naive UI 单套** | 不做适配层,写法地道;视觉靠 tokens |
| 状态 | **Pinia** + persistedstate | user/app 持久化,auth 易失 |
| 路由 | **vue-router**,菜单驱动动态注册 | |
| API 层 | **openapi-typescript + openapi-fetch** | 弃 axios;类型从后端 OpenAPI 生成,零手写 DTO |
| 多语言 | **vue-i18n**(zh-CN / en-US) | error.* 键 = 后端 msgKey |
| 图标 | **@iconify/vue**(运行时)+ 品牌内联 SVG | 弃 unplugin(菜单图标是动态服务端字符串) |
| 依赖纪律 | 显式 import,不引 unplugin-auto-import/components | typecheck 洁净、不依赖生成 dts |

## 3. 目录结构(`web/src/`)

```
api/         client.ts(openapi-fetch + 中间件)· index.ts(unwrap/端点)· schema.d.ts(gen 产物)
composables/ useTheme · useModule · useAuthMenu · useTable   ← 逻辑单源,不含 Naive 专有类型
components/   TenonLogo.vue(品牌徽标,明暗切换)
directives/   auth.ts(v-auth,当前 fail-open)
layouts/      default.vue(侧栏+顶栏+内容)· AppHeader.vue(主题/主色/密度/语言/切应用/用户)
locales/      index · zh-CN · en-US
router/       index.ts(守卫/resetRouter)· routes.ts(静态白名单)
stores/       user(令牌·持久化)· auth(菜单/权限·易失)· app(UI偏好·持久化)
theme/        mix.ts(派生规则+自检)· accents.ts(6主色)· naive-theme.ts(tokens→overrides)
types/        menu.ts · api.ts
styles/       tokens.css(设计单源,勿手改)· index.css(密度变量)
utils/        error.ts(msgKey→i18n)
views/        login · dashboard/workbench · module · system/user · personal/{profile,password} · error/404
```

## 4. 核心机制(实现要点)

### 4.1 请求层(`api/`)
- `client.ts`:openapi-fetch 客户端(`baseUrl:''`,路径已含 `/api/v1`)+ 两个中间件:
  - **认证**:请求时读 user store 塞 `Authorization: Bearer`;
  - **401 刷新**:并发 401 合流到**同一次刷新**(共享 Promise),成功则重放原请求,失败则清会话跳登录;刷新用独立 `bare` 客户端防递归。
- `index.ts` `unwrap<T>()`:**同时容忍两种响应形状** —— `Result<T>` 信封(`code!==0` 抛 `ApiError`)与 ProblemDetails(400 校验/500,无 code)。业务错误经 `translateError` → i18n 文案。
- `npm run gen:api`:从 `http://localhost:5000/openapi/v1.json` 生成 `schema.d.ts`。

### 4.2 菜单驱动动态路由(`useAuthMenu` + `router`)
- `import.meta.glob('/src/views/**/*.vue')` 预登记所有页面;菜单树叶子(`type===2`)的 `component` 字符串映射到对应文件 → `router.addRoute('layout', …)`。
- **刷新白屏守卫**:动态路由只活在内存,auth store 不持久化;F5/深链时 `routesReady=false` → 守卫重新拉菜单、重建路由、重解析当前 URL。
  - ⚠ **已修 bug**:守卫原按 `to.meta.public` 短路,未注册的深链会先命中 public 的 404 → 错显 404;改为按**登录态 + routesReady** 判定。

### 4.3 多应用门户(`useModule`,M1.5)
- 登录后拉 `/personal/modules`:单应用自动进 / 有默认且可访问进默认 / 否则弹选择器;顶栏可切换应用(重建路由 + 跳落点)。

### 4.4 运行时主题(`theme/` + `useTheme`)
- `mix.ts` 实现 `DESIGN.md §7.1` 派生规则(纯函数 + `demo()` 自检)。
- **6 主色切换**:按派生规则从 accent 重算 `--color-primary*`(写到 `<html>`,裸 CSS 跟着变)+ 重建 Naive `themeOverrides`。
- **明暗**:`data-theme` 翻转 + `darkTheme`。**密度**:`data-density` 切 `--pad-page/--gap-card/--row-h`。
- 三项均存 `app` store 持久化。渐变/发光(`btnGrad`/`glowSh`)仅登录页/工作台英雄区,不满屏。

### 4.5 权限(`v-auth`)
- 指令已脚手架,但**后端暂无「返回按钮权限码」接口** → `permissionCodes` 恒空 → **fail-open**(不隐藏);服务端 403/`code 41001` 兜底。待后端补 `GET /personal/permissions` 后自动生效。

### 4.6 品牌(`TenonLogo.vue` + `public/`)
- 「透榫」徽标内联 SVG,**明暗切换**(亮 `#646CFF` 底白榫 / 暗 `#16181D` 底 `#7A81FF` 榫),固定品牌色不随 accent 变。
- favicon / apple-touch / PWA manifest / theme-color 已接入 `index.html`,资源在 `web/public/`。

## 5. 如何运行(本地)

```bash
# 1) 后端(仓根)—— dev 账号已固定 superAdmin / Tenon@2026
TenonAdmin__Seed__AdminPassword='Tenon@2026' dotnet run --project backend/samples/MinimalHost
#   → http://localhost:5000  (零配置 SQLite 自建库 + 种子)

# 2) 前端(首次需生成类型,后端须起着)
cd web && npm install && npm run gen:api

# 3) 前端 dev
npm run dev            # → http://localhost:5173(Vite proxy 反代 /api、/openapi 到 :5000)
```

> **注**:后端 CORS 默认 deny-all,故必须走 Vite dev proxy;浏览器只与 :5173 通信。
> 重置库(换密码/重新种子):删 `backend/samples/MinimalHost/data/admin.db` 后重启。
> 常用脚本:`npm run dev|build|preview|typecheck|gen:api`。

## 6. 关键设计决策(及理由)

| 决策 | 选择 | 理由 |
|---|---|---|
| 请求客户端 | openapi-fetch(非 axios) | §13.6 明确、依赖最轻、端到端类型化 |
| 图标系统 | @iconify/vue 运行时(弃 unplugin-icons) | 菜单图标是动态服务端字符串,编译时方案解析不了 |
| 自动导入 | 不用(显式 import) | typecheck 洁净、不依赖生成 dts、少魔法 |
| 圆角 | 采用新原型(更圆:6/10/12/16) | 对齐 `design_handoff_rbac_admin` 视觉 |
| 视觉特效 | 渐变/发光仅登录页+英雄区 | 应用内保持 Naive 平面主色,不满屏渲染 |
| 运行时开关 | 主色切换 + 密度(fx 动画档后置) | 兑现原型「可调主题」卖点,控制脚手架体量 |
| 逻辑/视图分离 | composables 不返回 Naive 专有类型 | 将来补第二皮肤退化为「加一层视图」 |

## 7. 验证结果(浏览器实跑,非推断)

- ✅ `vue-tsc --noEmit` 0 错 + `vite build` 通过(按路由分包)
- ✅ 登录(`superAdmin`/`Tenon@2026`)→ 单应用自动进 → 工作台;侧栏按菜单树渲染(空目录自动过滤)
- ✅ 深链 `/system/user` 表格真拉后端数据(`useTable` + `n-data-table` + PagedList 解包)
- ✅ **F5 深链无白屏**(守卫重建,并修掉 public-404 短路 bug)
- ✅ 明暗切换 `data-theme` 翻转、`--color-primary` 按 accent 派生(绿 `#10B981` → 暗档 `#3bc698`)
- ✅ accent / 密度 / 语言持久化并在刷新后恢复;i18n 中英切换生效
- ✅ stale token → 401 → 刷新失败 → 自动登出回登录页
- ✅ 品牌徽标随主题切换、favicon/manifest 均 200

## 8. 后续待办(不阻塞,按刀推进)

**后端**
- [ ] `GET /api/v1/personal/permissions` 暴露已有 `GetPermissionCodesAsync` 扁平列表 → 前端 `v-auth` 真正生效
- [ ] 完整页面菜单种子(目前仅播了「用户管理」一个页面节点供动态路由落点)

**前端**
- [ ] §7.3 其余业务页:角色(菜单授权/数据范围授权面板)、机构、职位、菜单管理、字典、系统配置、操作/登录日志、在线用户
- [ ] M1.5 门户前端补全:模块管理页、菜单表单「所属应用」选择器
- [ ] fx 特效档(炫酷/沉稳环境动画)+ 完整双布局 Dashboard(纯 SVG/CSS,不加图表库)
- [ ] 提交规范(commitlint/husky/lint-staged)+ ESLint/Prettier
- [ ] 前端 CI(lint + typecheck + build)接 `.github/workflows`

**未入库的本地产物**(留待处理):`.claude/`(含 `launch.json`)、`docs/backend-review-loop-prompt*.md`、`docs/review-runs/`、`web/design-mockups/*.zip`
