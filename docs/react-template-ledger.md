# React 模板台账（web-react/）

> 来源:2026-07-20 维护者裁定推翻 `web-shared/` 共享层方向,改为**两个自包含前端模板**。前身 `docs/react-port-ledger.md` 连同 A1–A3/B1–B4 的实现与两份 review 全部留在本地 `archive/web-shared-extract` 分支(未推远端),本台账取代它。
> 驱动方式:仿 `docs/refinement-ledger.md` ——逐条执行、每条独立英文 conventional commit、可断点续跑。
> 执行协议:**每次只做一条**;开工前有设计取舍/命名/行为边界疑问先向维护者确认;做完跑验证、勾选本文件、单独提交。**每条完成后另起 review lane**(`code-reviewer` / `verifier`),不在同一上下文自审。
> 验证纪律:每个模板各自跑四件套——`npm run lint` / `npm test` / `npx tsc --noEmit`(Vue 侧 `vue-tsc`) / `npm run build`。**本机内存紧张,一次只跑一个重进程,绝不与 `dotnet test` 并发。**涉端点跑 MinimalHost 实打 + `npm run gen:api`;体验件 `npm run dev` 实点。
> 判据纪律:**跑绿的用例什么都不证明,直到它被变异证伪。**每条改动过的断言都要证明它还会红:预期失败集合**先写后跑**,预测与实测不符要记进轮次日志(这是最有价值的一类记录)。
> 横切纪律:两个模板**零共享、各自自包含**。`web-react/` 里不得出现 `@shared`、`web-shared`、`../web` 任何形式的跨模板引用。

## 为什么推翻共享层（防反复,别再抽一次）

共享层把成本转嫁给了消费者,收益只归维护者。四条证据:

1. **一个 UI 库的需求漏进另一个的地基**。为修 antd 暗色阴影,`--color-shadow` 被写进**共享的** `tokens.css`(即 Vue 模板的唯一真源),而且值是错的:antd 把 `colorShadow` 的 alpha **乘进每一层派生阴影**(`alias.js:31-33`),`rgba(20,27,45,0.16)` 让亮色下 `boxShadowDrawerLeft` 从 `0.08/0.12/0.05` 塌到 `0.0128/0.0192/0.008`——**为修暗色引入了亮色回归**,而守它的测试只查色相不查量级,坏与好两种状态下都绿。
2. **`web/` 不再能单独拷走**。`CLAUDE.md` 被迫写上"打包 `web/` 必须带上 `web-shared/`",degit 上手流程从一条命令变成两条。
3. **`server.fs.allow` 的双条目陷阱**,只因共享层在项目根之外;已引发过一次 `GET /` 全站 403,而 lint/typecheck/build 一个都发现不了。
4. **`openapi-fetch` 要在四处重复配置**(两个模板各 vite alias + tsconfig paths),还得专设 CI 闸门把依赖数锁死在 1。

接受的代价(明确的、不再讨论):**文案与设计令牌今后要改两遍。**不建同步脚本,不建"两边键必须一致"的 CI 闸门——两个模板本就该能有意分叉(antd 与 naive 的组件文案不同)。

## 参考项目

**ant-design-pro**(https://github.com/ant-design/ant-design-pro)—— React 19 + antd 6,与本模板同栈。两个用法:

1. **antd v6 正确用法的活语料**。v5 习惯是已知地雷(`Card.bordered`→`variant`、`bodyStyle`→`styles.body`、`Button.iconPosition`→`iconPlacement`),而这些改名 **`tsc` 一个都不会红**。拿不准的 API 先 `antd info <组件> --detail` 查离线 CLI,再对照它的实际写法——**CLI 给签名,它给惯用法**。
2. **页面结构与交互约定**:dashboard / form / list / profile / result / exception / account 这几类页的组织方式。

**边界**:它是 Umi Max 4 + Tailwind v4 + ProLayout,我们是 **Vite + antd 原生 `Layout` 自建 + 无 Tailwind**。参考页面结构与 v6 用法,**不是它的框架选型**——别把 Umi 的约定式路由、`useModel`、Tailwind 类名带进来。

按既有约定:提组件设计前把它与 soybean-admin / XiHan(`参考项目\admin\`)、SimpleAdmin 一并过一遍,别只照一家抄。

## 批次 R · 重置与自包含化

- [x] **R1 保存与重置**(2026-07-20,本文件首次提交) — `git branch -m feat/web-shared-extract archive/web-shared-extract`(留本地,**不推远端**);`git switch -c feat/web-react-template dev`。存档分支是后续所有"从旧分支取内容"的来源,也保存着两份 review 的结论与变异证据;新分支站稳前不删。验:`git ls-tree dev -- web-shared web-react` 为空(确认抽取从未进 dev),新分支 `git status` 干净。
- [ ] **R2 把唯一一条真 bug 修复带回** — `archive` 的 `f1f579e` 与抽取无关:`57bde5e` 给后端 site-info DTO 加了 `Logo` 但没重跑 `gen:api`,`schema.d.ts` 从那时起就缺这个字段,**而且没人发现**——`configApi.siteInfo()` 走手写 `unwrap<{...}>` 内联类型,`paths` 里少个字段不会让任何东西编译失败。`git show archive/web-shared-extract:web-shared/api/schema.d.ts > web/src/api/schema.d.ts`(生成物与路径无关)。验:**起 MinimalHost 真跑一次 `npm run gen:api`,确认 diff 为空**——不要相信这次搬运。单独 `fix(web):` 提交。**这是整个重构里唯一一处动 `web/`。**
- [ ] **R3 `web-react/` 脚手架(自包含)** — 从 `archive` 取 B1 的配置,**删光共享层接线**:`vite.config.ts` 去 `@shared` alias、去 `openapi-fetch` alias、`server.fs.allow` 整条删除(回默认);`tsconfig.json` 的 `paths` 只留 `@/*`、去 `../web-shared/**` 的 include;`package.json` 的 lint 去掉 `cd ..`、**补自己的 `gen:api`**(输出 `src/api/schema.d.ts`)。保留 `port: 5174`、proxy、`define.__APP_VERSION__`、`test.include: ['src/**/*.spec.{ts,tsx}']`(**`.tsx` 不能少**:React 组件测试必须带 JSX,漏掉时 vitest 不报错、CI 全绿、那些用例从来没执行过)、串行 pool 设置。**`types` 不加 `"node"`**——它是项目级的,会让 `process.env` 在浏览器源码里静默通过 typecheck,而 `web/` 那边没有这条,同一行代码会在**另一个模板**里炸;改为在需要 `node:fs` 的那一个 spec 顶部写 `/// <reference types="node" />`。验:四件套绿。
- [ ] **R4 框架无关文件落进 `web-react/src/`** — 从 `archive` 的 `web-shared/` 复制,导入一律改 `@/*`:`types/{api,menu}.ts`→`src/types/`;`locales/{zh-CN,en-US}.ts`→`src/locales/`;`styles/tokens.css`→`src/styles/`;`theme/{mix,accents}.ts`→`src/theme/`;`utils/{tree,url}.ts`→`src/utils/`;`api/{index,schema.d}.ts`→`src/api/`。`locales/ext.ts` **内联进 `src/locales/index.ts`**(只剩一个消费者,与 `dev` 上 `web/src/locales/index.ts` 形状一致)。**暂不搬** `utils/{ua,chunkUpload}.ts`(当前无调用方,等批次 C 用到它们的页面再搬)。验:`grep -rn '@shared\|web-shared' web-react/` 为空;四件套绿。

## 批次 B · React 模板阶段一

前四条是从 `archive` 搬运 + 落实两份 review 的结论。**不要把已知缺陷重新发一遍。**

- [ ] **B1 空壳能起** — 已含在 R3/R4。探针页(`App.tsx`)一并搬:它**故意不是 hello world**——只渲染文字的壳在下面任何一条假设坏掉时照样绿,所以把它们逐条渲染成肉眼可见的红字。到 B8 布局壳落地时删掉。
- [ ] **B2 主题桥 + review 处置** — 搬 `theme/{antd-theme,useAntdTheme}.ts` 与 `antd-theme.spec.ts`,并修:
  - **[HIGH] `--color-shadow` 必须是不透明色**:`#141B2D`(亮)/ `#000000`(暗)。antd 拿它当**基色**、把它自己的 alpha 乘进每一层。令牌旁注明"这是基色,antd 会按自己的档位乘 alpha,**别直接写进 `box-shadow`**"。
  - **[HIGH] 测试断量级,不断色相**:现有 `not.toMatch(/255,255,255/)` 在坏与好两种状态下都绿。改为断言明暗两色下派生阴影的 alpha 与 antd 默认档位一致。
  - **[HIGH] 删掉假注释**:`antd-theme.ts` 声称 `boxShadow`/`boxShadowSecondary` "已登记豁免",而 `EXEMPT` 里只有 `colorTextDisabled|colorTextQuaternary` 一条,且那对根本不是转发赋值、闸门从不评估它。
  - **[MEDIUM] 填充阶要成套**(已核对 `alias.js`:`colorBgContainerDisabled = colorFillTertiary`、`colorBgTextHover = colorFillSecondary`、`controlItemBgHover = colorFillTertiary`、`colorFillAlter = colorFillQuaternary`):`colorFillTertiary` 改回**静息**色 `--color-fill`(它驱动 Tag / filled Button / Slider / Empty 与**禁用输入框底色**,现在错用了 hover 色),`colorFillSecondary` 给 `--color-fill-hover`(让文字按钮 hover 与行 hover 同色)。`controlItemBgHover|colorFillTertiary` 是有意打破的恒等,**登记进 `EXEMPT` 带理由**——豁免必须显式,否则它和"忘了"长得一模一样。
  - **[MEDIUM] `MAP_PAIRS` 补 `colorBgContainer|colorBgElevated`**(亮色下 antd map 层是同一表达式;今天两边都是 `#FFFFFF`,无人钉住)。
  - **[LOW]** `afterAll` 复位 `data-theme`;`--color-shadow` 记进 `web-react/` 侧的设计文档。
  - 验:浏览器探针明暗 × 6 accent × 密度全绿零控制台错误;**变异**——把 `--color-shadow` 改回 `rgba(20,27,45,0.16)`,新的量级断言必须变红(旧的色相断言不会)。
- [ ] **B3 四个 Zustand store** — 搬 `user`/`auth`/`app`/`dict`(逻辑逐字对齐 Vue 侧:`hasPerm` 三条规则、`homePath` 阶梯、dict 的 typeCode 缓存 + 并发去重 + `invalidate` 竞态守卫;`usePreferredDark` 换裸 `matchMedia`;persist 白名单与 Vue 侧一致)。**决策记录**:`hasPerm`/`homePath`/`isDark` 一律写成**纯函数 + 细粒度 hook**,不做"返回闭包的选择器"——zustand 每次渲染都跑选择器并与上次结果 `Object.is` 比对,返回新建函数 = 每次都"变了" = 无限重渲染。补一条 review LOW:`auth-hooks.spec.tsx` 第三个用例名称声称"随 store 写入",而用例体里**根本没有 store 写入**,改名或补上写入。**tabs store 推迟到 D1**(它真正难的部分条条要导航,B3 时点既无路由也无容器);`auth.reset()` 里那句 `clearTabs()` 与 `useModule` 切应用那一处,两处都要在 D1 补回。验:单测 + 变异逐批证伪。
- [ ] **B4 i18n + review 处置** — 搬 react-i18next 接线,三处默认值改写(**坏了都不抛错**):`interpolation.prefix/suffix`(默认 `{{name}}`,文案是 `{name}`,不改则取字照常成功、页面上永远挂着花括号)、`escapeValue: false`(默认转义,React 本来就转义,再来一遍把 `&` 显示成 `&amp;`)、`nsSeparator: false`(默认 `:` 会把含冒号的键切成两半,**权限码 `GET:/api/v1/x` 正是这个形状**)。antd 自带文案是**另一套 locale**,ConfigProvider 接 `antd/locale/*` 一起切,否则是「中文界面 + No data」。落实 review:
  - **[MEDIUM]** 子树键上 `exists()` 与 `t()` 不一致(`exists('error.auth')` 为真而 `t()` 返回一句英文 debug 文本),与 Vue 侧 `te()` 行为**相反** → B5 的 `translateError` 会把 debug 文本弹给用户。**先写判别值再验证**,确认后加 `te()` 辅助 + 用例。
  - **[MEDIUM]** 模块级 `useAppStore.subscribe` 补 `import.meta.hot.dispose`(`stores/app.ts` 里 40 行外就有这个模式);`i18n.init()` 前加 `void`。
  - **[MEDIUM]** 副作用导入从 `App.tsx` 移到 `main.tsx`(与 `tokens.css` 并列,理由相同),删掉那条把顺序保证归因于 import 位置的**错注释**——真正的保证是模块求值早于渲染。
  - **[LOW]** `RESOURCES` 不导出(spec 改用 `i18n.getResourceBundle`);`fallbackLng` 的回声断言换成**行为**断言;删 `afterAll` 空操作;`ext/README.md` 补冒号键约束。
  - **已知判据缺口(如实记下,别假装堵上了)**:①深合并的保证**只靠一条用例**撑着——浅/深之别只在 ext 键与内置键**碰撞**时才可观测,"新增命名空间"两种实现结果相同;②`ext/` 目录按设计是空的,**没有常驻用例能证明那个 glob 打不打得中**(把路径写错,一条测试都不红)。已用临时 fixture 当场验过一次接缝确实生效,验完删除。
  - 验:中英实点 + **两次刷新,中文下也要刷一次**——EN 恰好等于 `fallbackLng`,只在 EN 下刷新的话把 `lng` 初值整句删掉照样绿。
- [ ] **B5 api client + 登录页** — `api/client.ts` **不搬 archive 版本**(那版为服务两个模板抽了 `ApiAdapter`/`createApiClient` 工厂,现在只有一个宿主,工厂没有存在理由),改以 `dev` 上 `web/src/api/client.ts` 为蓝本重写:宿主耦合直接写死(zustand + react-router)。中间件三个机制**逐字保留**——`WeakMap` 请求克隆重放(Request 的 body 是一次性流,首次 fetch 就被消费,令牌恰好在一次 POST 时过期就会丢请求体)、`refreshOnce` 并发 401 合流、`bare` 客户端不挂刷新中间件(避免刷新自身 401 递归)。**这是唯一一处重复有真实技术风险的代码**,值得单独一轮变异。登录页先做一套皮肤 + 后端可配 logo(`useSite` 换成 zustand)。验:登录拿到令牌对、**token 过期自动刷新重放**(要真造过期,不是 mock)。
- [ ] **B6 动态路由** — `useRoutes(routes)`,routes 从 `auth.menuTree` 派生的普通数组。**Vue 版 `router/index.ts` 那个 `return to.fullPath` 重解析 trick 不需要存在**。`buildRoutesForModule` 逻辑逐条搬:flatten → 跳过非 Menu/无 path → 跳过 `isHttpUrl(path)` 外链 → `isHttpUrl(component)` 走 iframe 视图 + `meta.iframeSrc` → 否则查 `import.meta.glob('/src/pages/**/*.tsx')`,缺失 `console.warn` 跳过;`viewComponentPaths` 由同一张 glob 表反推(**防手敲错致菜单静默消失,原样保留**)。`<RequireAuth>` 三条守卫按 Vue 版顺序:未登录→`/login`、`mustChangePassword`→锁死改密页、`!routesReady`→loading + `enterInitial()`。验:F5 深链能重建路由、无权限路径 404。
- [ ] **B7 模块选择页 + useModule** — 决策阶梯逐字搬(0 模块→选择器 / 记住的仍有效→进 / 单个→进 / 有默认→进 / 否则选择器);`switchModule` 清 tabs + 跳 `homePath`;`setDefault` 同步。验:多应用切换 + 设默认后仍能从右上角九宫格回选择器。
- [ ] **B8 布局壳** — antd 原生 `Layout` 自建(**不用 ProLayout**):侧边栏 236/76 + header 62 + blur(12px),菜单树由 `menuTree` 派生,外链走 `window.open`。暂不带 tabs。`[data-gray]`/`[data-density]` 那批样式此时搬进 `web-react/src/styles/`。验:折叠/展开、明暗、菜单选中态跟随路由。
- [ ] **B9 权限 + 消息 + 确认基建** — `<Can code="VERB:/path">`(替代 `v-auth`);`App.useApp().message` 承接 Vue 侧 74 处 `useMessage`;`useConfirm` 三 API 用 `Modal.useModal()` 重写(`modal.confirm({onOk:async})` 返 promise 时按钮自动 loading 且不关窗,正是现有语义,**代码会比 93 行更短**)。验:无权限按钮不渲染;确认框执行中不可重复点/不可 Esc 关。
- [ ] **B10 `<DataTable>` 薄封装** — 隔离 `pro-components` beta,16 个 CRUD 页只依赖它。含 `toProTable()` 适配器(`toPage()` 的 `{items,total}` → `{data,success,total}`)、排序映射到后端 `SortField`/`SortOrder`、`columnsState` 持久化沿用 `protable:{module}-{page}` 命名、labels 由 i18n 驱动。**`proTable` 那批 UI 键此时才定**:现有的 8 个键是 Naive 那个 ProTable 的文案,antd 的 ProTable 自带 locale,要不要加、加什么在这一条决定,别提前猜。验:契约单测 + 一页实跑。
- [ ] **B11 system/user 页** — 标准列表原型(搜索 + 工具栏 + 表格 + 分页 + 服务端排序 + 列设置)。验:增删改查全通、列设置刷新后保留。
- [ ] **B12 阶段一验收** — 手动对照 Vue 版走 10 条链路:登录 / token 过期刷新重放 / 强制改密 / 多应用切换 / F5 深链重建路由 / user 页增删改查 / 搜索排序分页 / 列设置持久化 / 明暗+中英切换 / 无权限按钮不渲染。

## 批次 C · 共享组件层 + 剩余 22 页

先补组件再批量做页。**`web/COMPONENTS.md` 记录的每个坑要在 antd 侧逐条重验,不能假设同样成立**(静态模式无客户端筛选、受控 `expandedRowKeys` 与 `defaultExpandAll` 互斥、过滤树浅拷贝写回不生效必须 reload)。

- [ ] **C0 从存档搬运剩余条目** — 批次 C 及之后的细目在 `archive/web-shared-extract` 的 `docs/react-port-ledger.md` 里,逐条搬进本文件(结构不变,去掉 `@shared` 相关措辞)。**先做这一条**,别凭记忆重写。
- [ ] **C1 字典三件套 + 表单容器** — `DictSelect`/`DictTag`/`useDictOptions`(数据基座是 B3 的 dict store)、`FormContainer`(Modal+Drawer 双形态,`onConfirm` owns loading+close)、`StatusSwitch`(悲观 + 自动回滚,建在 useConfirm 的 `ask` 上)。
- [ ] **C2 选择器族** — `ApiSelect`(远程分页 + 防抖 + 竞态守卫)、`UserSelect`、`OrgTreeSelect`(扁平→树用 `utils/tree`,含子树排除)、`UserPicker` 弹窗。

## 批次 D · 容器与标签页

- [ ] **D1 tabs store + 标签栏容器** — B3 推迟的那件。`removeTab` 的邻居选择、`_ensureActive`、`cachedNames` 依赖 `hasRoute`,都要有路由和容器才写得了。落地时**必须**把 `auth.reset()` 与 `useModule` 切应用两处的 `clearTabs()` 补回。

## 批次 E · 工程化

- [ ] **E1 `web-react/Dockerfile` + compose 服务** — 照 `web/Dockerfile`,但**构建上下文可以是 `./web-react` 自己**(不再需要仓库根,这正是自包含买到的)。
- [ ] **E2 `web-react-ci.yml`** — lint → test → build → dev server 冒烟(5175,`--strictPort`,**断言内容而非状态码**——未知路径命中 SPA fallback 返 200 + index.html,只查状态码的检查会在什么都没证明的情况下通过;并断言仓库根 403)。**不带**任何共享层闸门与 `/@fs` 断言。paths 只有 `web-react/**`。
- [ ] **E3 `dev.bat`/`dev.sh` 带上 web-react** — 目前只起 backend + web。
- [ ] **E4 文档** — 根 `CLAUDE.md` 加 `web-react/` 段落,写明两个模板各自自包含、零共享,**且这是刻意选择**;**不要**出现任何"必须一起带上"的措辞。site/ 加一页 React 模板上手(degit 一条命令)。

## 不做清单（有依据,防反复）

- [~] **`web-shared/` 共享层** — 2026-07-20 推翻,理由见本文件开头四条证据。不要再抽第二次。
- [~] **npm workspaces / 发布共享包** — 与"消费者 fork 后自己拥有模板"的产品模型冲突:一个装出来、改不动的依赖,对 fork-and-own 模板是负资产。
- [~] **"两边 locale 键必须一致"的 CI 闸门** — 两个模板本就该能有意分叉。
- [~] **ProLayout** — 布局壳自建(B8)。参考 ant-design-pro 的页面结构,不引它的框架选型。

## 轮次日志

（每轮追加:做了哪条、判据、变异结果、**预测与实测不符的地方**、下一条。）

### 2026-07-20 · R1 保存与重置

做了 R1。三条前置在动手前逐条查过,全部成立:

- `git ls-tree dev -- web-shared web-react` 输出为空 —— 抽取**从未进 `dev`**,所以这不是回滚,只是不再做第二次。
- `git ls-remote --heads origin feat/web-shared-extract` 为空 —— 分支从未推远端,改名不影响任何人。
- 工作树只有未跟踪的本文件,无待提交改动。

改名 `feat/web-shared-extract` → `archive/web-shared-extract`(仍指 `c59f76f`,B4 那次提交),从 `dev`(`71d660d`)开 `feat/web-react-template`。本文件作为未跟踪文件跨 `git switch` 带过来了,在新分支上首次提交。

**预测与实测不符**:预期 `git switch` 之后 `web-shared/` 与 `web-react/` 都消失,实测 `web-shared/` 消失而 **`web-react/` 还在** —— 它装了 `node_modules`(252M)、`dist/`、`.omc/`,全是 gitignore 产物,`switch` 只清跟踪文件。已确认无源码残留后整个删除。

删 `node_modules` 而不是留着省一次安装,是**有意的**:R3 会写一份新的 `package.json`,而旧 `node_modules` 里的包即使不再被声明也照样解析得到 —— 本地绿、CI 红,正是本台账反复防的那类静默失败。R3 反正要装一次。

`grep -rn '@shared\|web-shared' web/src web/vite.config.ts web/tsconfig.json` 为空,确认 `dev` 上的 `web/` 本就自包含,R2 之外无需再动它。

下一条:**R2**(把 `f1f579e` 那条真 bug 修复带回 `web/src/api/schema.d.ts`)。注意它要起 MinimalHost 真跑 `gen:api` 复核,**不与任何重进程并发**。
