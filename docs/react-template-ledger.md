# React 模板台账（web-react/）

> 来源:2026-07-20 维护者裁定推翻 `web-shared/` 共享层方向,改为**两个自包含前端模板**。前身 `docs/react-port-ledger.md` 连同 A1–A3/B1–B4 的实现与两份 review 全部留在本地 `archive/web-shared-extract` 分支(未推远端),本台账取代它。
> 驱动方式:仿 `docs/refinement-ledger.md` ——逐条执行、每条独立英文 conventional commit、可断点续跑。
> 执行协议:**每次只做一条**;开工前有设计取舍/命名/行为边界疑问先向维护者确认;做完跑验证、勾选本文件、单独提交。**每条完成后另起 review lane**(`code-reviewer` / `verifier`),不在同一上下文自审。
> 验证纪律:每个模板各自跑四件套——`npm run lint` / `npm test` / `npx tsc --noEmit`(Vue 侧 `vue-tsc`) / `npm run build`。**本机内存紧张,一次只跑一个重进程,绝不与 `dotnet test` 并发。**涉端点跑 MinimalHost 实打 + `npm run gen:api`;体验件 `npm run dev` 实点。
> 判据纪律:**跑绿的用例什么都不证明,直到它被变异证伪。**凡要写下"没有 X"/"唯一的 X"这类否定或全称断言,**先跑一条能打自己脸的枚举命令**(`git log --stat`、`git grep -l`…),把命令与结论一起记下来 —— 否则那只是印象。这条是被同一类错误坑了两次之后加的(R2 的"唯一一处动 web/"、R4 的"archive 上没测过")。每条改动过的断言都要证明它还会红:预期失败集合**先写后跑**,预测与实测不符要记进轮次日志(这是最有价值的一类记录)。
> **写每条断言时问一句:期望值是从哪来的?** 它若和实际值出自同一个变量,这条就是回声,恒真。B4 那句 `greeting === (i18n.language === 'en-US' ? … : …)` 正是如此 —— 删掉 `lng` 初值后 `i18n.language` 不是 undefined 而是被 `fallbackLng` 顶成 `'en-US'`,期望值跟着一起挪,中文下也照样绿。**期望值要取自被测链路之外**(那条改成比 store 就红了)。这是被"跑绿的用例什么都不证明"咬的第三次,而且咬的正是为防它而特意加的那一步。
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
- [x] **R2 把唯一一条真 bug 修复带回**(abd9d1e) — `archive` 的 `f1f579e` 与抽取无关:`57bde5e` 给后端 site-info DTO 加了 `Logo` 但没重跑 `gen:api`,`schema.d.ts` 从那时起就缺这个字段,**而且没人发现**——`configApi.siteInfo()` 走手写 `unwrap<{...}>` 内联类型,`paths` 里少个字段不会让任何东西编译失败。`git show archive/web-shared-extract:web-shared/api/schema.d.ts > web/src/api/schema.d.ts`(生成物与路径无关)。验:**起 MinimalHost 真跑一次 `npm run gen:api`,确认 diff 为空**——不要相信这次搬运。单独 `fix(web):` 提交。~~这是整个重构里唯一一处动 `web/`。~~ **这句断言已被 R1 review 证伪**(2026-07-20):`archive` 上另有 A5/A6 两个 commit 改了 `web/e2e/`,与共享层无关,见新增的 R2b。写这条时我是凭抽取的**意图**("抽取只该动 web-shared/")推断的,而不是跑 `git log --stat` 逐个 commit 看**实际**改了什么——意图与事实之间那道缝,正好够沉掉三个 commit。
- [x] **R2b 把 e2e 的两条测试质量修复带回**(8b52f71)(R1 review 补记) — `archive` 的 `f3e70ba`(A5)与 `4ea84ec`(A6)只碰 `web/e2e/*` 与 `web/playwright.config.ts`,**e2e 目录从未被抽取搬动过**,所以与共享层无关,不带回就是白丢。内容:①用例互相污染全局状态(默认 app)导致假红/假绿,各用例改为自建前置;`fullyParallel:false` 没达到注释声称的效果、`workers:1` 缺失;②菜单叶子按名字二次查找会命中**同名目录节点**("文件管理"目录 Id 30 vs 叶子 Id 78);③RBAC 那条 `<=1` 断言在零菜单场景下**恒真**——又一个假断言。已实测 `git cherry-pick -n f3e70ba` 五个 `web/` 文件全部干净落地,唯一冲突是新分支上不存在的旧台账 `docs/react-port-ledger.md`(丢弃即可)。**验:光 cherry-pick 不算数——e2e 要真跑一遍**(需后端 + web dev server),并对着 ③ 做变异:把 RBAC 断言恢复成 `<=1`,零菜单场景必须仍绿(证明它当初确实恒真),换成新断言后必须红。单独 `test(web):` 提交。
- [x] **R3 `web-react/` 脚手架(自包含)**(414a2e4) — 从 `archive` 取 B1 的配置,**删光共享层接线**:`vite.config.ts` 去 `@shared` alias、去 `openapi-fetch` alias、`server.fs.allow` 整条删除(回默认);`tsconfig.json` 的 `paths` 只留 `@/*`、去 `../web-shared/**` 的 include;`package.json` 的 lint 去掉 `cd ..`、**补自己的 `gen:api`**(输出 `src/api/schema.d.ts`)。保留 `port: 5174`、proxy、`define.__APP_VERSION__`、`test.include: ['src/**/*.spec.{ts,tsx}']`(**`.tsx` 不能少**:React 组件测试必须带 JSX,漏掉时 vitest 不报错、CI 全绿、那些用例从来没执行过)、串行 pool 设置。**`types` 不加 `"node"`**——它是项目级的,会让 `process.env` 在浏览器源码里静默通过 typecheck,而 `web/` 那边没有这条,同一行代码会在**另一个模板**里炸;改为在需要 `node:fs` 的那一个 spec 顶部写 `/// <reference types="node" />`。验:四件套绿。
- [x] **R4 框架无关文件落进 `web-react/src/`**(fbac63a) — 从 `archive` 的 `web-shared/` 复制(~~导入一律改 `@/*`~~ —— **实测一处都不用改**:`web-shared/{api,types,locales,…}` 与 `web-react/src/{api,types,locales,…}` 是 **1:1 目录映射**,所以连跨目录相对导入也免改。**B5 别再为此预留工作量**):`types/{api,menu}.ts`→`src/types/`;`locales/{zh-CN,en-US}.ts`→`src/locales/`;`styles/tokens.css`→`src/styles/`;`theme/{mix,accents}.ts`→`src/theme/`;`utils/{tree,url}.ts`→`src/utils/`;**只有** `api/schema.d.ts`→`src/api/`(`api/index.ts` 依赖 `./client`,随 B5 一起走,见轮次日志)。`locales/ext.ts` **内联进 `src/locales/index.ts`**(只剩一个消费者,与 `dev` 上 `web/src/locales/index.ts` 形状一致)。**暂不搬**(两者性质不同,别并成一条):`utils/ua.ts` 纯函数零依赖,只是当前无调用方,批次 C 随时可搬;`utils/chunkUpload.ts` 里调 `fileApi.chunkUpload`,与 `api/index.ts` 同一条依赖链,**必须等 B5 之后**。验:**只看 import,不看注释** —— `grep -rnE "from ['\"](@shared|\.\./web)" web-react/src` 为空(原判据 `grep '@shared\|web-shared' web-react/` 在 R3 落地时就已必然误报:配置文件里那几条"这里没有它"的说明注释会命中。一个必然误报的检查下次只会被划掉,不会被当真);四件套绿。

## 批次 B · React 模板阶段一

前四条是从 `archive` 搬运 + 落实两份 review 的结论。**不要把已知缺陷重新发一遍。**

- [ ] **B1 空壳能起** — 已含在 R3/R4。探针页(`App.tsx`)一并搬:它**故意不是 hello world**——只渲染文字的壳在下面任何一条假设坏掉时照样绿,所以把它们逐条渲染成肉眼可见的红字。到 B8 布局壳落地时删掉。
- [x] **B2 主题桥 + review 处置**(5bd7ada) — 搬 `theme/{antd-theme,useAntdTheme}.ts` 与 `antd-theme.spec.ts`,并修:
  - **[HIGH] `--color-shadow` 必须是不透明色**:`#141B2D`(亮)/ `#000000`(暗)。antd 拿它当**基色**、把它自己的 alpha 乘进每一层。令牌旁注明"这是基色,antd 会按自己的档位乘 alpha,**别直接写进 `box-shadow`**"。
  - **[HIGH] 测试断量级,不断色相**:现有 `not.toMatch(/255,255,255/)` 在坏与好两种状态下都绿。改为断言明暗两色下派生阴影的 alpha 与 antd 默认档位一致。
  - **[HIGH] 删掉假注释**:`antd-theme.ts` 声称 `boxShadow`/`boxShadowSecondary` "已登记豁免",而 `EXEMPT` 里只有 `colorTextDisabled|colorTextQuaternary` 一条,且那对根本不是转发赋值、闸门从不评估它。
  - **[MEDIUM] 填充阶要成套**(已核对 `alias.js`:`colorBgContainerDisabled = colorFillTertiary`、`colorBgTextHover = colorFillSecondary`、`controlItemBgHover = colorFillTertiary`、`colorFillAlter = colorFillQuaternary`):`colorFillTertiary` 改回**静息**色 `--color-fill`(它驱动 Tag / filled Button / Slider / Empty 与**禁用输入框底色**,现在错用了 hover 色),`colorFillSecondary` 给 `--color-fill-hover`(让文字按钮 hover 与行 hover 同色)。`controlItemBgHover|colorFillTertiary` 是有意打破的恒等,**登记进 `EXEMPT` 带理由**——豁免必须显式,否则它和"忘了"长得一模一样。
  - ~~**[MEDIUM] `MAP_PAIRS` 补 `colorBgContainer|colorBgElevated`**~~ —— **照字面写会红在非缺陷上**:这对**只有亮色**恒等(antd 亮色 `getSolidColor(bgBase, 0)` 两者相同,暗色是 8 与 12),而 `MAP_PAIRS` 原本不分明暗一起比。已改成按模式拆两组,该对只进 light-only。变异证实:放进 BOTH 则暗色红在 `colorBgContainer(#1F2229) ≠ colorBgElevated(#262A31)`。
  - **[LOW]** `afterAll` 复位 `data-theme`;`--color-shadow` 记进 `web-react/` 侧的设计文档。
  - 验:浏览器探针明暗 × 6 accent × 密度全绿零控制台错误;**变异**——把 `--color-shadow` 改回 `rgba(20,27,45,0.16)`,新的量级断言必须变红(旧的色相断言不会)。
- [x] **B3 三个 Zustand store**(8d0bf45;dict 随 B5,见轮次日志) — 搬 `user`/`auth`/`app`/`dict`(逻辑逐字对齐 Vue 侧:`hasPerm` 三条规则、`homePath` 阶梯、dict 的 typeCode 缓存 + 并发去重 + `invalidate` 竞态守卫;`usePreferredDark` 换裸 `matchMedia`;persist 白名单与 Vue 侧一致)。**决策记录**:`hasPerm`/`homePath`/`isDark` 一律写成**纯函数 + 细粒度 hook**,不做"返回闭包的选择器"——zustand 每次渲染都跑选择器并与上次结果 `Object.is` 比对,返回新建函数 = 每次都"变了" = 无限重渲染。补一条 review LOW:`auth-hooks.spec.tsx` 第三个用例名称声称"随 store 写入",而用例体里**根本没有 store 写入**,改名或补上写入。**tabs store 推迟到 D1**(它真正难的部分条条要导航,B3 时点既无路由也无容器);`auth.reset()` 里那句 `clearTabs()` 与 `useModule` 切应用那一处,两处都要在 D1 补回。验:单测 + 变异逐批证伪。
- [x] **B4 i18n + review 处置**(14b9f8f + review 处置 b4757d3) — 搬 react-i18next 接线,三处默认值改写(**坏了都不抛错**):`interpolation.prefix/suffix`(默认 `{{name}}`,文案是 `{name}`,不改则取字照常成功、页面上永远挂着花括号)、`escapeValue: false`(默认转义,React 本来就转义,再来一遍把 `&` 显示成 `&amp;`)、`nsSeparator: false`(默认 `:` 会把含冒号的键切成两半,**权限码 `GET:/api/v1/x` 正是这个形状**)。antd 自带文案是**另一套 locale**,ConfigProvider 接 `antd/locale/*` 一起切,否则是「中文界面 + No data」。落实 review:
  - **[MEDIUM]** 子树键上 `exists()` 与 `t()` 不一致(`exists('error.auth')` 为真而 `t()` 返回一句英文 debug 文本),与 Vue 侧 `te()` 行为**相反** → B5 的 `translateError` 会把 debug 文本弹给用户。**先写判别值再验证**,确认后加 `te()` 辅助 + 用例。
  - **[MEDIUM]** 模块级 `useAppStore.subscribe` 补 `import.meta.hot.dispose`(`stores/app.ts` 里 40 行外就有这个模式);`i18n.init()` 前加 `void`。
  - **[MEDIUM]** 副作用导入从 `App.tsx` 移到 `main.tsx`(与 `tokens.css` 并列,理由相同),删掉那条把顺序保证归因于 import 位置的**错注释**——真正的保证是模块求值早于渲染。
  - **[LOW]** `RESOURCES` 不导出(spec 改用 `i18n.getResourceBundle`);`fallbackLng` 的回声断言换成**行为**断言;删 `afterAll` 空操作;`ext/README.md` 补冒号键约束。
  - **已知判据缺口(如实记下,别假装堵上了)**:①深合并的保证**只靠一条用例**撑着——浅/深之别只在 ext 键与内置键**碰撞**时才可观测,"新增命名空间"两种实现结果相同;②`ext/` 目录按设计是空的,**没有常驻用例能证明那个 glob 打不打得中**(把路径写错,一条测试都不红)。已用临时 fixture 当场验过一次接缝确实生效,验完删除。
  - 验:中英实点 + **两次刷新,中文下也要刷一次**——EN 恰好等于 `fallbackLng`,只在 EN 下刷新的话把 `lng` 初值整句删掉照样绿。
- [x] **B5a api client + endpoint wrappers**(6989f1e) — 见轮次日志。`api/index.ts` 逐字复制;spec 跑 node 环境(happy-dom 没实现请求体一次性流);`bare` 与 URL 短路是**两道冗余闸**,已各自补判据。
- [x] **B5b 登录页 + dict store**(dict store 604955c / site+error 46feef6 / 登录页 2213a87) — 只做账号密码路径;SMS/MFA/SSO 暂缓。见轮次日志。
- [x] **B5 review lane** — **B5a + B5b 双 APPROVE 无阻断**(见轮次日志)。B5a 那条偶发红定性为跨进程污染、非文件内通道;B5b 三条 LOW 已处置。原 review-b5a lane 卡住已停,并入 review-b5b。 `api/client.ts` **不搬 archive 版本**(那版为服务两个模板抽了 `ApiAdapter`/`createApiClient` 工厂,现在只有一个宿主,工厂没有存在理由),改以 `dev` 上 `web/src/api/client.ts` 为蓝本重写:宿主耦合直接写死(zustand + react-router)。中间件三个机制**逐字保留**——`WeakMap` 请求克隆重放(Request 的 body 是一次性流,首次 fetch 就被消费,令牌恰好在一次 POST 时过期就会丢请求体)、`refreshOnce` 并发 401 合流、`bare` 客户端不挂刷新中间件(避免刷新自身 401 递归)。**这是唯一一处重复有真实技术风险的代码**,值得单独一轮变异。登录页先做一套皮肤 + 后端可配 logo(`useSite` 换成 zustand)。验:登录拿到令牌对、**token 过期自动刷新重放**(要真造过期,不是 mock)。
- [x] **B6 动态路由**(a 决策 3877df4 / b-1 materialization a417a70 / b-2 守卫+接线 5af2763) — F5 深链重建、无权限 404、chooser 落选择器均通;探针降为 /_probe(B8 删)。见轮次日志。 `useRoutes(routes)`,routes 从 `auth.menuTree` 派生的普通数组。**Vue 版 `router/index.ts` 那个 `return to.fullPath` 重解析 trick 不需要存在**。`buildRoutesForModule` 逻辑逐条搬:flatten → 跳过非 Menu/无 path → 跳过 `isHttpUrl(path)` 外链 → `isHttpUrl(component)` 走 iframe 视图 + `meta.iframeSrc` → 否则查 `import.meta.glob('/src/pages/**/*.tsx')`,缺失 `console.warn` 跳过;`viewComponentPaths` 由同一张 glob 表反推(**防手敲错致菜单静默消失,原样保留**)。`<RequireAuth>` 三条守卫按 Vue 版顺序:未登录→`/login`、`mustChangePassword`→锁死改密页、`!routesReady`→loading + `enterInitial()`。验:F5 深链能重建路由、无权限路径 404。
- [x] **B7 模块选择页 + useModule**(628d1c5) — switchModule/setDefault 补齐(4 变异全 kill)、/module 换真选择器;module/** 加入 glob 排除。见轮次日志。 决策阶梯逐字搬(0 模块→选择器 / 记住的仍有效→进 / 单个→进 / 有默认→进 / 否则选择器);`switchModule` 清 tabs + 跳 `homePath`;`setDefault` 同步。验:多应用切换 + 设默认后仍能从右上角九宫格回选择器。
- [x] **B8 布局壳**(cb62290 派生 / cac1efc 壳+接线+删探针) — antd 原生 Layout 自建、Sider 236/76、Header 62 blur、菜单树派生、选中/展开跟随路由、明暗、灰度/密度样式;探针删除。见轮次日志。 antd 原生 `Layout` 自建(**不用 ProLayout**):侧边栏 236/76 + header 62 + blur(12px),菜单树由 `menuTree` 派生,外链走 `window.open`。暂不带 tabs。`[data-gray]`/`[data-density]` 那批样式此时搬进 `web-react/src/styles/`。验:折叠/展开、明暗、菜单选中态跟随路由。
- [x] **B9 权限 + 消息 + 确认基建**(63e071f) — `<Can code>` 替 v-auth(判定收敛 hasPerm)、`useConfirm` 用 Modal.useModal 重写(busy 守卫内置);message 无需新建。见轮次日志。 `<Can code="VERB:/path">`(替代 `v-auth`);`App.useApp().message` 承接 Vue 侧 74 处 `useMessage`;`useConfirm` 三 API 用 `Modal.useModal()` 重写(`modal.confirm({onOk:async})` 返 promise 时按钮自动 loading 且不关窗,正是现有语义,**代码会比 93 行更短**)。验:无权限按钮不渲染;确认框执行中不可重复点/不可 Esc 关。
- [x] **B10 `<DataTable>` 薄封装**(eb3de2f) — toProTable 适配(9 变异)、隔离 pro-components、columnsState 持久化、reload 句柄;proTable UI 键**不加**(antd ProTable 自带 intl 跟 ConfigProvider)。见轮次日志。 隔离 `pro-components` beta,16 个 CRUD 页只依赖它。含 `toProTable()` 适配器(`toPage()` 的 `{items,total}` → `{data,success,total}`)、排序映射到后端 `SortField`/`SortOrder`、`columnsState` 持久化沿用 `protable:{module}-{page}` 命名、labels 由 i18n 驱动。**`proTable` 那批 UI 键此时才定**:现有的 8 个键是 Naive 那个 ProTable 的文案,antd 的 ProTable 自带 locale,要不要加、加什么在这一条决定,别提前猜。验:契约单测 + 一页实跑。
- [x] **B11 system/user 页** — 标准列表原型(搜索 + 工具栏 + 表格 + 分页 + 服务端排序 + 列设置)落地(`8eea775`)。写侧 antd 原生 Modal+Form;机构树筛选/头像/字典选择器/批量删除属批次 C,原型有意不带。**代码验四件套 + 变异全绿;但"增删改查全通、列设置刷新后保留"是浏览器行为,本 SSH 会话无浏览器可驱动 → 该项验收随 B12(手动)+ E2(Playwright 冒烟),未在此假称已验。**
- [ ] **B12 阶段一验收** — 手动对照 Vue 版走 10 条链路:登录 / token 过期刷新重放 / 强制改密 / 多应用切换 / F5 深链重建路由 / user 页增删改查 / 搜索排序分页 / 列设置持久化 / 明暗+中英切换 / 无权限按钮不渲染。

## 批次 C · 共享组件层 + 剩余 22 页

先补组件再批量做页。**`web/COMPONENTS.md` 记录的每个坑要在 antd 侧逐条重验,不能假设同样成立**(静态模式无客户端筛选、受控 `expandedRowKeys` 与 `defaultExpandAll` 互斥、过滤树浅拷贝写回不生效必须 reload)。

- [x] **C0 从存档搬运剩余条目** — 完成:C3–C12、D2–D5、E6 已从 `archive/web-shared-extract` 的 `docs/react-port-ledger.md` 搬入,**去掉共享层措辞**(`共享 utils/tree`→`utils/tree`;`共享 mix.ts/tokens.css`→本模板 `src/theme`/`src/styles`;`共享 chunkUpload.ts`→`src/utils/chunkUpload.ts`,该 util R4 推迟未搬,C5 时一并补)。**对账**:原 C-list 不含 `user`(=B11 原型)/`login`(=B5)/门户选择页(=B7);C10 的 `module` 是**管理页**(moduleApi CRUD),≠ B7 的门户选择器。已落地页(user)不再重复列。
- [x] **C1 字典三件套 + 表单容器** — 完成(`804b3db` 组件 + `b826df7` 页改造)。`DictSelect`/`DictTag`(数据基座 B5b 的 `useDictOptions`;antd Tag 用预设语义色串,naive 的 `info`→`processing`)、`FormContainer`(Modal/Drawer 双形态,`onConfirm` owns loading+close,返 false/抛错留窗)、`StatusSwitch`(悲观 + 自动回滚,建在 useConfirm 的 `ask` 上)。纯逻辑 `toDictOptions`/`resolveDictTag`/`shouldCloseAfterConfirm` 抽出变异钉死(**17 处全致死**;1 处空断言 M-SS5 存活→已修)。user 页手写 Modal+Form / 内联 EnabledSwitch / 性别 Select+文案 全部退成共享组件。**antd v6 改名坑**(`tsc` 不红):`maskClosable`→`mask.closable`、Drawer `width`→`size`、`destroyOnClose`→`destroyOnHidden`,已过 `antd lint` 零 deprecated。**推迟**:`web-react/COMPONENTS.md` 留到 E4 文档批(眼下组件靠文件头注自述,现建属过早脚手架);`FormContainer` 用 `destroyOnHidden` + 页里 `setFieldsValue` 先于开弹的时序(与 B11 同,若有 antd 未挂载告警留 B12 实点)。
- [x] **C2 选择器族** — 完成(C2a `71e0bc5`:ApiSelect/UserSelect/OrgTreeSelect;C2b `9798216`:UserPicker)。`ApiSelect`(远程分页 + 防抖 + 竞态守卫,逻辑抽 `useRemoteOptions` hook)、`UserSelect`、`OrgTreeSelect`(扁平→树用 `utils/tree`,含子树排除)、`UserPicker` 三面板弹窗(命令式 `open(ids)`)。变异门 C2a 13/13 + C2b 10/10 全致死;四件套 + `antd lint` 全绿。**有意裁剪**:UserPicker 只做每行「添加」,批量勾选待 DataTable 支持 rowSelection 时补。review lane 待开。
- [x] **C3 展示件** — 完成(C3a `98968b9`:图标基建 + AppIcon + TenonLogo;C3b `dc01107`:DetailPage + CodeBlock + PasswordStrength)。`AppIcon`(@iconify/react 薄封装 + 4 离线集合基建 `lib/icons.setupIcons`,菜单/模块页占位改真渲染)、`TenonLogo`(内联 SVG 明暗)、`DetailPage`(返回+标题+actions+body)、`CodeBlock`(highlight.js json,维护者裁定引;未注册语言 escapeHtml 降级防 XSS)、`PasswordStrength`(拉 `configApi.passwordPolicy` + 默认兜底)。纯逻辑 `iconName`/`logoColors`/`escapeHtml`/`highlightCode`/`computeChecks`/`computeStrength`/`buildRules` 抽出。**变异 C3a 6/6 + C3b 16/16 全致死**;四件套 + `antd lint` 全绿。**边界裁定**(维护者 Q&A):渲染器基建(@iconify/react + 4 集合)前移进 C3(AppIcon 必需,否则菜单只能占位);C4 缩为**轻量内联 IconPicker**(搜索+网格,不发包)+ 本地 svg glob。**顺带修**:module 页 B7 遗留的废弃 `Tag bordered`(lint 全部改动文件逮到,全库仅此一处)。review lane 待开。
- [x] **C4 IconPicker(轻量内联)** — 完成(`da7db2c`)。触发器 + 弹窗(搜索 + 集合 Tab + 本地 Tab + cap 300 网格 + "还有 N 个"提示),值契约 `prefix:name`/`local:name`。**去在线 tab**(气隙,对齐 C3 的 `/offline`;Vue 版有在线 iconify 名输入 tab,本版有意砍)。`lib/icons` 扩:`COLLECTIONS`(Tab 列表)、`loadIconNames`(懒加载+addCollection+排序名,按前缀缓存)、`sortedIconNames`/`svgToIcon` 纯逻辑、本地 svg 模块级注册(`src/assets/svg/*.svg` eager glob → `addIcon 'local:<name>'`,star.svg 从 web/ 拷来)。补 `common.clear`(zh+en)。**消费者是 C8 菜单 / C10 模块编辑表单(尚未建),组件先行**。**变异 14/14 全致死**;四件套 + `antd lint` 全绿,vitest 361/361。review lane 待开。
- [x] **C5 上传 + 富文本 + 图表** — 完成(C5a `3eb0feb`:chunkUpload + FileUpload;C5b `5822ae3`:MarkdownEditor/View + Chart)。`chunkUpload.ts`(R4 推迟的 util,框架无关**逐字搬**,`@/api`+`@/types` 1:1)、`FileUpload`(antd `Upload customRequest`;编排抽 `performUpload`)、`MarkdownEditor`/`MarkdownView`(md-editor-rt,存 Markdown 源文本 —— ~~无 XSS 面~~ **误,见 C5 review 处置:Markdown 天然放行 HTML,已补全局 XSSPlugin**)、`Chart`(**裸 echarts.init** 不加 wrapper;`lib/echarts.ts` = 按需注册 + `buildEChartsTheme` 逐字搬)。新依赖 md-editor-rt + echarts。**变异 C5a 11/11 + C5b 8/8 全致死**;四件套 + `antd lint` 全绿,vitest 376/376。**如实记**:Chart 命令式 canvas 渲染 happy-dom 不可单测,判据落 `buildEChartsTheme` 纯逻辑 + dev/B12 实点(同 B10 posture)。**review 处置 REQUEST-CHANGES → 已收口**(1 HIGH XSS 已修 + MED-1 + LOW-1;见轮次日志)。
- [x] **C6 标准列表页批**(全 7 页落完:position `ad5d849` / notice `5373528` / file `082db2c` / session `985f223` / recycle `9ddaf71` / cache `bdb0b76` / monitor `732d1fe`) — 照 B11 的 `<DataTable>` + `userForm.ts` 拆分范式(纯逻辑抽出变异钉、页面只接线)。**review lane → APPROVE**(独立上下文,六接缝对后端 seed 核过;4 LOW:LOW-1 recycle 判别力缺口已补测加固、其余 3 接受;见轮次日志)。收口,进 C7。
- [x] **C7 日志三页**(`169493a`) — `log/op` `log/login` `log/exception`,只读 DataTable + 列驱动搜索 + 时间范围 + 清空(硬删)。共享纯逻辑抽 `log/logFormat.ts`(operatorText/loginNameText/prettyParam/loginResultKey,变异钉),三页只接线。时间范围**没用**台账原设想的 `search.transform`,改 `valueType:'dateRange'` 直喂 `logApi.*Page` 的 `[start,end]` 元组(api 层 splitRange 已补 23:59:59,与 Vue 一致——API 早于本条落地,transform 反而多此一举)。**review lane → REQUEST-CHANGES(1 HIGH:op 操作人搜索 `formItemRender` 显式 onChange 被 pro-components cloneElement 末位展开盖掉、筛选死 no-op)→ 修复 `4c850c5` + 回归测试 + 复核 APPROVE**;顺带去一条 monitor waitFor flake(`0305bd1`)。见轮次日志。
- [x] **C8 树表原型**(**C8a `a5f9775` + C8b `b28817d`**) — `org` + `menu`(含 `ButtonManager` 子组件,写操作要 `hasPerm` 门 —— 沿用 dev 上 J4 的对齐)。**已 scope(读完 Vue 三源,别凭记忆重写)**:org ~250 行、menu ~530 行、ButtonManager ~360 行,全港最大项。**两点定案**:①**建独立 `TreeTable` 薄封装**(ProTable `dataSource`+`pagination:false`+`expandable:{expandedRowKeys,onExpandedRowKeysChange}`,续隔离 pro-components beta)——DataTable 是 fetcher 专用,树表是静态 data + 受控展开 + 客户端 `filterTree` + 无搜索表单,两模式塞一个组件会堆条件复杂度,故并列而非扩展;树页也不得直接碰 ProTable。②**拆 C8a(`TreeTable` + org)/ C8b(menu + ButtonManager)**,各自四件套+变异+原子提交(照 C2/C3/C5 a/b 先例)。**关键接缝**(移植时逐条对):受控 `expandedRowKeys` 默认全展开要自己播种(`expandableIds`)、搜索后重算(否则命中藏在折叠祖先里);`filterTree` 是浅拷贝故 StatusSwitch 成功后**重拉整树**而非写行;org 上级用 `OrgTreeSelect`(剪自身子树防成环)、menu 父级下拉排除自身子树;menu 按 `moduleId`(仅顶级)过滤 + `stripButtons`(按钮不进主树,只在 ButtonManager 里管)+ 关键字要搜到按钮权限码(`buttonInfoById`);menu 增删改后 `syncShell`(复用 B6 `buildRoutesForModule` 重建当前应用侧边栏+动态路由,失败静默);组件路径下拉取自 `viewComponentPaths`(glob 真实文件表);ButtonManager 权限码下拉取自 `menuApi.routes()` 实时路由表 + 按 `appPrefix` 软过滤 + 从路由批量添加。写操作全程 `useHasPerm` 门(服务端 [RolePermission] 兜底)。
- [x] **C9 主从分栏原型**(`d43cc47`) — `dict`(`activeRowKey` + 行点击,内联控件要 `stopPropagation`)。
- [x] **C10 角色 + 模块**(**C10b module `6f12b57` + C10a role `26609da`**) — `role`(含 `GrantMenuTable` 三级可勾选菜单树 + 数据范围单选 + 自定义组织多选)、`module`(管理页,moduleApi CRUD;≠ B7 门户选择器)。**C10a scope(已读 Vue 三源)**:role 页 = ProTable CRUD + 3 抽屉(授权菜单 GrantMenuTable / 数据范围 scopeType 单选+自定义组织多选 / 授权用户 UserPicker)+ 批删带持有人数警告(countUsers 复用 userApi.page roleId);GrantMenuTable = 自定义三列网格(目录|菜单|按钮)+ 三态复选(buildGroups/recomputeGroup/toggleCatalog|Menu|Button/collectChecked 是纯逻辑变异钉,抽 grantMenu.ts);roleApi 全套齐(getMenus/setMenus/getDataScope/setDataScope/getUsers/setUsers);**唯一缺口:`OrgTreeSelect` 无 `multiple`**(数据范围自定义组织多选需要)→ 按 DataTable 先例扩展可选 multiple。权限码:role add/PUT/DELETE/batch-delete + PUT role/menu、role/datascope、role/users。
- [ ] **C11 config 四标签页** — `SysBaseConfig`/`SecurityConfig`/`UploadConfig`/`OtherConfig`。
- [ ] **C12 dashboard + personal 七页** — `workbench`/`biz` + `profile`/`bindings`/`notice`/`password`/`sessions`(`password` 若早期已落占位,落地时核对)。

## 批次 D · 容器与标签页

- [ ] **D1 tabs store + 标签栏容器 + 页面缓存** — B3 推迟的那件,**整个移植第二难的东西**。①store:`removeTab` 的邻居选择、`_ensureActive`、`cachedNames` 依赖 `hasRoute`,都要有路由和容器才写得了;落地时**必须**把 `auth.reset()` 与 `useModule` 切应用两处的 `clearTabs()` 补回。②缓存:React 无 `keep-alive` 对等物 —— `<KeepAlive>` 容器维护 `Map<path, ReactNode>`,已开 tab 常驻挂载、非活动 `display:none`。`// ponytail: 不做 activated/deactivated 钩子,页面若需感知激活再加 useTabActive()`。代价是隐藏页的 effect/定时器仍在跑。验:切走切回状态保留、隐藏页定时器行为、内存随开 tab 是否线性增长。
- [ ] **D2 设置抽屉** — 6 accent / 密度两档 / 布局模式 / 水印 / 灰度。派生逻辑由 `src/theme/mix.ts` + `src/styles/tokens.css` 现成给出,成本在 `SettingsDrawer`(239) + `LayoutModeCards`(180) 的 UI 重写。
- [ ] **D3 SignalR 实时** — `@microsoft/signalr` 框架无关(需加此依赖),只重写 `useRealtime.ts`(68)外层包装:`force-logout` / `notice-changed` + 初次失败静默退回 30s 轮询。
- [ ] **D4 MenuSearch 全局命令面板** — 建在 `useMenuFlat` 的纯逻辑扫描上,外链项走 `window.open`。
- [ ] **D5 登录页另两套皮肤** — B5 已落一套;补 `AuroraGlass`(363)、`SplitPanel`(242)、`Spotlight`(107)+ 皮肤切换。**后两者在 Vue 版就是 Naive-free 的、主要是 CSS,几乎直接搬**。

## 批次 E · 工程化

- [ ] **E1 `web-react/Dockerfile` + compose 服务** — 照 `web/Dockerfile`,但**构建上下文可以是 `./web-react` 自己**(不再需要仓库根,这正是自包含买到的)。
- [ ] **E2 `web-react-ci.yml`**(⚠ 冒烟断言"零控制台错误"时**不要丢弃第一次加载** —— 我最初写的是"必须丢弃",归因错了,见 B3 轮次日志。CI 里 `npm ci` 之后根本没有 `.vite` 缓存,实测真冷启动零错误。真实风险窄得多:**只经动态 `import()` 可达的裸包**会被首轮依赖扫描漏掉、即使冷缓存也会触发 re-optimize + 强制刷新 —— B6 的 `import.meta.glob('/src/pages/**/*.tsx')` 是这条路上最可能的下一个。对症办法是给那些包写 `optimizeDeps.include`,**不是放宽断言**) — lint → test → build → dev server 冒烟(5175,`--strictPort`,**断言内容而非状态码**——未知路径命中 SPA fallback 返 200 + index.html,只查状态码的检查会在什么都没证明的情况下通过;并断言仓库根 403)。**不带**任何共享层闸门与 `/@fs` 断言。paths 只有 `web-react/**`。
- [ ] **E3 `dev.bat`/`dev.sh` 带上 web-react** — 目前只起 backend + web。
- [ ] **E4 文档** — 根 `CLAUDE.md` 加 `web-react/` 段落,写明两个模板各自自包含、零共享,**且这是刻意选择**;**不要**出现任何"必须一起带上"的措辞。site/ 加一页 React 模板上手(degit 一条命令)。

- [ ] **E5 `gen:api` 漂移闸门**(R2 现场发现) — R2 修的那个字段缺了几个月没人发现,根因**不是** `unwrap`(这一点我第一版写错了,见轮次日志):typecheck 永远比的是**代码 ↔ schema**,从不比 **schema ↔ 后端**。schema 陈旧 = 两边一起冻住 = 恒绿。**推论:就算把 97 处响应类型全改成 schema 派生,陈旧照样一点都不红。**今天守着这条契约的只有"人记得跑 `gen:api`"。

  附带两条事实,别再搞混:①`unwrap<T>(res: { data?: unknown }): T` 确实把响应体丢成 `unknown` 再按调用方断言强转,97 处全是断言——但那治的是"手写类型与 schema 不一致",与陈旧是两回事;②openapi-fetch **在请求侧是真约束的**(`createClient<paths>` 让路径字面量 / `params.path` / `params.query` / `body` 全部受 `schema.d.ts` 管;例外是 `index.ts:548/551/553` 回收站三个端点用 `as any` 把这层绕过了)。**由此产生一个不对称,这才是闸门的真实能力边界:重新生成后请求侧的漂移会红,响应侧不会——闸门只能告诉你"schema 变了",告诉不了你"哪儿坏了"。**CI 加一步:起 MinimalHost、跑 `gen:api`、`git diff --exit-code web/src/api/schema.d.ts`(以及 R3 之后的 `web-react/src/api/schema.d.ts`)。配方现成——`backend-release.yml:77` 已经在做"起 MinimalHost 抓 openapi"这件事,照抄即可。**不做**把 97 处响应类型改成 schema 派生:**首要理由是它根本不治陈旧**(见上),其次才是仓库里零先例、手写 DTO 是有意的可读性选择。

- [ ] **E6 pro-components 转正跟进** — beta(`3.1.14-2`)→ stable 后立刻升版并跑全量验收;**尤其 B10 记的 vitest 导入坑(无扩展名 deep-import)与 B11 遗留的真 ProTable 运行时开放问题(列持久化/挂载即调/工具栏 locale),stable 后重验能否解**。**这条不封口,长期挂着**。

- [x] **E7 md-editor 运行时 CDN 资产 → 全离线(自包含硬伤,C5 review 处置时发现)** — 维护者裁定「你决定吧」→ 决定**两模板都改成完全离线,不引新依赖、不做本地化管线**(web-react `ca3910a` + web/ `fda311f`)。md-editor(rt/v3)默认挂载即从 unpkg 懒加载 highlight/katex/mermaid/prettier/**echarts**。修:①组件上 `no-{katex,mermaid,highlight,prettier}` 关掉前四个(通知用不到公式/图表,代码高亮设为可选、消费者要则经 `editorExtensions.highlight.instance` 自行本地化);②echarts **无 `no*` 旗标**,`config({editorExtensions:{echarts:{instance:{}}}})` 给空 no-op —— 既断 CDN,又避开其默认 `parseOption` 用 `new Function` 对(管理员可写的)通知正文求值。**证据驱动**:先加自包含守卫测试(挂载后不得注入指向外网的 link/script),它先因剩下的 echarts 而红,补 no-op 后转绿;掉任一 no* 或 echarts instance 都重红(happy-dom 禁加载但仍注入 DOM 标签,故可查、非空)。**顺带一并修 web/**(md-editor-v3 同 API):这是 R2 之后**刻意、经授权的第二处动 web/** —— 已发布模板里的真 XSS(C5 review 延后的 HIGH)+ 自包含洞,值得修透,非迁移漂移。**残留(已知、记下)**:cropper/screenfull 是编辑器按需(点全屏/裁剪才拉,非挂载预载),公开的 MdPreview 无工具栏故完全离线;管理端编辑器这两项极少用,消费者要绝对气隙可自行裁 `toolbars`。判据:web-react tsc/oxlint/vitest 387/387/build 全绿;web/ oxlint/vue-tsc/vitest 46/46/build 全绿。

## 不做清单（有依据,防反复）

- [~] **`web-shared/` 共享层** — 2026-07-20 推翻,理由见本文件开头四条证据。不要再抽第二次。
- [~] **npm workspaces / 发布共享包** — 与"消费者 fork 后自己拥有模板"的产品模型冲突:一个装出来、改不动的依赖,对 fork-and-own 模板是负资产。
- [~] **"两边 locale 键必须一致"的 CI 闸门** — 两个模板本就该能有意分叉。
- [~] **ProLayout** — 布局壳自建(B8)。参考 ant-design-pro 的页面结构,不引它的框架选型。

## 轮次日志

（每轮追加:做了哪条、判据、变异结果、**预测与实测不符的地方**、下一条。）

### 2026-07-21 · C10 review lane(独立 opus,隔离 worktree)→ REQUEST-CHANGES(1 HIGH + 1 LOW)→ 修复 `7b68e8b`

审 role(`26609da`)重、module(`6f12b57`)轻。lane 无法在本 worktree 重跑判据(web-react 不在其 base HEAD、无 node_modules)→ 改类型级推理 + 后端源码核验(已标注)。抓一处真实运行时接线缺陷:
- **HIGH:role 状态列 `StatusSwitch` 漏 `onChange`/reload**(`index.tsx` enabled 列)。`StatusSwitch` 是悲观受控(`<Switch checked={value}>`,value 恒等父传 `r.enabled`),成功仅靠 `onChange?.(next)` 通知父层重拉。role 列没传 `onChange`、页也不重拉 → 切换成功弹「成功」toast 但**开关视觉停在原位**(checked 未变),管理员见「绿提示+开关没动」矛盾反馈极可能二次点击 → 后端被回切。RBAC 屏高风险。tsc/antd-lint/现有测试**结构上都测不到**:index.spec 的状态列用例直接取 `props.request(false)` 调,从不渲染/触发 `onChange`,正落 mock 边界外。对照证据:module 页做对了(`onChange={() => void load()}`),Vue 参照 `role/index.vue` 用 `'onUpdate:value'` 就地改行。
- **LOW:数据范围 `OrgTreeSelect` 清空 → `customOrgIds` 变 undefined**(`as number[]` 掩盖类型)。功能安全(后端 `SetRoleDataScopeAsync` 用 `customOrgIds is { Count: > 0 }` 兜底,null/空落 `csv=""`),仅类型不严谨。
- **修复 `7b68e8b`**:①状态列补 `onChange={reload}`(照抄 module 页范式,`reload` 已在组件内 `useCallback(()=>tableRef.current?.reload(),[])`;加进 columns 依赖),②`setCustomOrgIds((v as number[]) ?? [])`。**加回归钉**:新用例「状态列:切换成功回调必须重拉表格」断言 `sw.props.onChange` 真存在 + 触发它调 `reloadMock`(DataTable mock 经 imperative handle 暴露 `reload: reloadMock`,逐测 mockClear)。**变异证伪(预测先行)**:预测抹掉 `onChange={reload}` → 新用例红、其余 10 不动 → `1 failed | 10 passed`;**实测精确命中**(唯一红的正是新钉,零 mismatch)。判据(修后):**tsc 0 / antd-lint 6.5.1 net 0 / oxlint 0 / vitest 11/11(原 10 + 回归 1)/ build ✓ 49.29s**。
- **lane 首轮核过为正确的接缝(采信)**:grantMenu 三态纯逻辑全对(buildGroups/groupState/withXxxChecked/collectChecked/filter*);**round-trip 稳定性逐态验证**(父 granted 变→子 useEffect 重建 groups 不上抛无死循环、`buildGroups(tree, collectChecked(next)) === next`、「取消单按钮不取消菜单」能存活 round-trip);collectChecked 对顶级 Menu 收两次 id → 后端 `SetRoleMenusAsync` 做 `.Distinct()` 无害(且与 Vue 同构);权限码逐字节对齐后端(role/module 全套);**无 C9 式漏字段**(roleToInput 5 字段/5 Form.Item、moduleToInput 8/8,validateFields 即全量);数据范围回填/保存、`OrgTreeSelect multiple` 经 rest 透传真多选;删除警示三档;FormContainer `destroyOnHidden` 隔离角色间状态泄漏;module 内置保护(禁删/禁停/只读 Tag)。

**复审 lane(独立 opus、隔离 worktree)→ APPROVE(0 阻断)**:7 探针逐条确认——HIGH 修复链路完整(StatusSwitch 成功→onChange→reload→ProTable 重取→回显真实态,悲观模型下无「先翻再回弹」)、`() => void` 对 `(next:boolean)=>void` 结构性可赋值、与 module 语义等价(enabled 非过滤列切换后不掉出当前页)、`reload` 入依赖无害必要(空依赖 useCallback 身份稳)、LOW `?? []` 仅兜 null/undefined 不动正常数组路径、**回归钉真会红不空过**(mockClear 隔离、断言前计数 0、onChange 就是 reload 本体经 mock 句柄落 reloadMock)、无兄弟路径遗漏(单删/批删/save 三写路径本已 reload,三授权抽屉存的是非表列关联数据无需 reload)。lane 提示门禁未在 base-ref worktree 重跑——已在主树跑绿(tsc0/antd-lint0/oxlint0/vitest 11/11/build✓)为最终放行证据。主树干净、复审 worktree 自清、无游离。**C10 收口。**

**下一条**:进 **C11 config 四标签页**(SysBaseConfig / SecurityConfig / UploadConfig / OtherConfig)。已只读扫过 Vue 源:tab 容器 `index.vue`(39)+ 4 tab 面板(SysBase 70 / Upload 76 / Other 150 / Security 188),各按 groupCode 拉配置、`saveBatch` 存;web-react `configApi` 已有 saveBatch/siteInfo/passwordPolicy/flushConfig,开工先核「按 group 拉」方法齐否。

### 2026-07-21 · C10a 角色 + GrantMenuTable(`26609da`)

C10 后半、全港最大项。role 页 = DataTable CRUD + 3 抽屉(授权菜单 / 数据范围 / 授权用户)+ 批删带持有人数警告。三态勾选纯逻辑全抽 grantMenu.ts(变异钉)。
- **GrantMenuTable 三态**:自定义三列网格(目录|菜单|按钮)+ antd Checkbox indeterminate;`buildGroups`(顶级 Catalog 取菜单子 / 顶级 Menu 自成行)、`groupState`(全勾=checked / 部分=indeterminate)、`withCatalog/Menu/ButtonChecked`(不可变切换——**按钮全勾连带菜单勾、取消按钮不取消菜单**,对齐 Vue)、`collectChecked`(目录勾或半勾 + 菜单勾 + 按钮勾)、`filterByModule/BySearch`。React 走不可变更新 + 受控上抛(≠ Vue 就地改 reactive)。
- **接线**:OrgTreeSelect `multiple` **免扩展**(rest 已透传给 antd TreeSelect,scope 时的顾虑作废);UserPicker 命令式 ref `open(ids)`/`onConfirm`;数据范围 customOrgIds 逗号串↔数组;批删 content 异步查各角色 countUsers(复用 userApi.page roleId,403 退通用文案不阻断);role 表单 5 字段全注册 validateFields 即全量(无 C9 漏字段)。
- **变异 32/32 全致死,残留 clean**(4 文件);4 处安全过杀(M-GM1/3/4/5 预测 1 实测 2–6):`groupState`/`toMenuRow`/`buildGroups` 是地基助手,几乎每个 grantMenu 测试都用 buildGroups 造 fixture,动地基则连带红多条——非漏杀。判据:tsc0/antd-lint net0/oxlint0/vitest **27/27**(grantMenu 9 + roleForm 3 + GrantMenuTable 5 + 页 10)/build OK(47.12s)。role/module i18n 键 C0 已全量预置;权限码(role add/PUT/DELETE/batch-delete + role/menu、datascope、users)与后端 seed 对齐。
- **踩坑**:占位符 role/index.tsx(早期 UnderConstruction 落点)须先读再覆盖;buildGroups 里 `checked/indeterminate` 显式设又被 `...groupState` 覆盖触 TS2783(删冗余);toggleCollapse 三元当语句触 oxlint no-unused-expressions(改 if/else);GrantMenuTable spec 搜索占位是 `common.search='查询'` 非「搜索」。

**下一条**:开 **C10 review lane(role + module 一并独立审)**;通过后进 **C11 config 四标签页**。

### 2026-07-21 · C10b 应用(模块)管理页(`6f12b57`)

C10 拆分前半:标准 CRUD(moduleApi.list 静态表 + 表单弹窗)。纯逻辑抽 moduleForm.ts(变异钉),本页接线。
- **内置保护**:`isBuiltin` = code==='system';内置应用禁删 + 禁停(停了门户失联,模块/菜单管理页都在它下面);无 PUT 权限退化只读状态标签(保留可见性);停用先确认。
- **全量 update**:StatusSwitch 与编辑共用 moduleToInput(可空归一空串);表单渲染全部 8 字段 → validateFields 即全量(**无 C9 那种未注册漏字段——已警惕**)。
- **变异 15/15 全致死,残留 clean**;2 处安全过杀(M-IX6/IX7 预测 1 实测 2):删除按钮可见性被「内置计数」+「删除查找」两测同时断言,任一删除门变异连带红 2。判据:tsc0/antd-lint net0/oxlint0/vitest **10/10**(moduleForm 4 + 页 6)/build OK(51.15s;**首跑因 cwd 漂到仓库根 ENOENT 未真跑,已从 web-react 重跑绿**——git 要仓库根、npm 要 web-react,cd 别混)。
- **module 不单开 review**:标准 CRUD、无新组件/新接缝(同 C6 那批),其 review 并入 C10a(role)的 C10 review lane 一并审(照 C6 批量审先例)。

**下一条**:C10a role + GrantMenuTable(含 `OrgTreeSelect` multiple 扩展)。

### 2026-07-21 · C9 review lane(独立 opus,隔离 worktree)→ REQUEST-CHANGES(1 HIGH)→ 修复 `632f9ed` → 复核 APPROVE

**独立 lane 价值再兑现**:抓到一个我在 C8b 修过的同类缺陷,这次是 HIGH。
- **HIGH:`saveItem` 漏带隐藏 FK `dictTypeCode`**。item 弹窗只注册 label/value/sort/enabled,`validateFields()` 只回收已注册字段 → 丢掉 openItemAdd/Edit 经 setFieldsValue 注入的 `dictTypeCode`。reviewer 追 `@rc-component/form`(validateFields 只解析已注册 field entities)+ 对后端 `DictService.cs` 核实:**新增** → 后端拿 record 默认 `""` 建**孤儿项** + `loadItems(undefined)` 右栏空白 + 假成功 toast;**编辑** → `entity.DictTypeCode = ""` 把项从类型**摘除(数据损坏)**。tsc/lint 全绿(`v` 类型 DictItemInput,`v.dictTypeCode` 编译过但运行时 undefined)。
- **MEDIUM(HIGH 溜过之因)**:index.spec 无用例开弹窗调 saveType/saveItem → 变异 24/24 全杀却漏这条(「绿证明不了未被执行的代码」的活教材)。
- **修复 `632f9ed`**:`getFieldsValue(true)` 取全量(与 C8b LOW-1 同法,统一表单-save 取值范式),dictTypeCode 不再漏;补 saveItem/saveType 弹窗 save 测试。变异证伪:退回 `validateFields()` → saveItem 测试精准红(failed=1)。
- **复核 APPROVE**:reviewer 追 `@rc-component/form useForm.js` 源码证实 getFieldsValue(true) 取 setFieldsValue 注入的整个 store(add/edit 两路 dictTypeCode 均带回)、saveType 未动且本就完整、按引用返回无别名风险、无 stale-key 泄漏、校验失败仍留弹层。判据(修后):tsc 0 / oxlint 0 / dict index spec **10/10** / build OK。
- reviewer 首轮核过无恙的接缝:onRow/rowClassName 经 pro-components `...rest` 真透传(读库源码,非 C7 死接缝)、竞态守卫(reqIdRef 比 Vue code-compare 更稳,handles A→B→A)、stopPropagation 冒泡序、权限码逐字节、StatusSwitch 悲观 + type reload/item 本地更新。收口:主树 632f9ed 干净,review 隔离 worktree 只读 → 自动清理。

**下一条**:C9 收口,进 **C10 角色 + 模块**。

### 2026-07-21 · C9 字典主从分栏(`d43cc47`)

主从原型:左=类型 DataTable(CRUD + 点击选中 + active 高亮 + 批删),右=该类型的项(裸 antd Table + CRUD)或空态。任何类型/项增删改后 `invalidate(code)` 失效下拉缓存。按 org 范式:纯逻辑抽 `dictForm.ts`(变异钉),本页接线。
- **扩展 DataTable(非新组件)**:加可选 `onRowClick` + `activeRowKey`,经 `onRow`(点击 + 指针手型)/ `rowClassName`(命中行套 `.data-table-active-row`,DataTable.css 用 `--color-primary-light`,theme-aware)透传——主从左栏两者都要,忠对 Vue ProTable 的 active-row-key/row-click;**向后兼容**(现有页两者都不传)。续守 pro-components 隔离(树表用 TreeTable、fetcher 表用 DataTable)。
- **承重接缝**:①`loadItems` 请求序号竞态守卫(await 期间切类型 → 过期响应不覆盖新选中);②左栏行内 switch/op 单元套 `<div onClick={stopPropagation}>`(否则点开关/按钮冒泡到行 → 误切选中类型);③类型状态开关成功后**重载 fetcher**(ProTable 缓存不自更新,不重载 switch 弹回旧值),项状态则本地 setItems 更新(裸 Table,轻量);④管理端 `items`(含停用 + id)≠下拉投影,不复用。
- **变异 24/24 全致死,预测=实测零缺口**:M-DF1–8(type/itemToInput 全量每字段 + remark 归一 + blankType/Item)、M-IX1–13(类型状态全量/stopProp×2/invalidate/reload/置灰/删除、项状态全量/invalidate/删除、activeRowKey、selectType 断链 [M-IX13 连带红 行点击+竞态+字典项=3])、M-DT1–4(onRow onClick/cursor、rowClassName 命中/类名)。三文件残留 clean。
- 判据:`tsc` 0 / `antd lint` 6.5.1 net 0 / `oxlint` 0 / `vitest` **22/22**(dictForm 6 + 页 8 + DataTable 6→8)/ `build` OK(49.59s)。dict/common i18n 键 C0 已全量预置;权限码与后端 dict CRUD 路由对齐。

**下一条**:开 **C9 review lane**(独立 opus、隔离 worktree、不自审);通过后进 **C10 角色 + 模块**。

### 2026-07-21 · C8b review lane(独立 opus,隔离 worktree)→ APPROVE(2 LOW,均修)

独立 lane 审 C8b(七探针 + 判别力质疑),裁定 **APPROVE**:0 CRITICAL/HIGH/MEDIUM,2 LOW。
- **深度兑现**:reviewer 追到 **installed antd 6.5.1 / rc-select 1.8.2 源码**逐链验三处「tsc/lint 不红但运行时可死」的绑定——①AutoComplete `showSearch.filterOption`(v6 从顶层移入 showSearch)经 AutoComplete→Select(combobox)→ rc-select `useSearchConfig` **真过滤**;②`Form.useWatch` 在 destroyOnHidden 里读 form store 不依赖挂载,`setFieldsValue` 先于 `setOpen` 故取值准;③IconPicker 可选化后 Form.Item 注入链成立。权限码对后端 `PermissionCode.Build`(路径全小写规范化)+ `DefaultMenuSeed.cs` **逐字节**核过。syncShell 门户树≠管理树、失败静默不误报、saveBatch 以全量 checked 保存(不漏隐藏勾选行)全部确认。
- **LOW-1(修 `84750e9`)**:ButtonManager 单条编辑表单只注册 5 字段,`validateFields()` 只回收已注册 → add/update 漏带 visible/path/component/icon,违反 menuForm 明写的全量 update 契约(按钮场景当前惰性,但表单复用即抹空的 latent trap)。**修**:`getFieldsValue(true)` 取全量(含 setFieldsValue 注入但未渲染的字段)。变异钉:改回 `getFieldsValue()` → 单增测试红 1。
- **LOW-2(修 `84750e9`)**:saveBatch 逐个 add 中途失败 → 前 k-1 已建但 onChanged 未触发、batchRows 未更新 → 重试重复创建。**修**:记已成功 code,失败时从 batchRows 剔除 + onChanged(让已成功项从 usedCodes 消失)。新增中途失败测试;变异钉:去掉剔除+onChanged → 该测试红 1。
- 判据(修后):`tsc` 0 / `antd lint` 6.5.1 net 0 / `oxlint` 0 / `vitest` **28/28**(ButtonManager 6→7)/ `build` OK(51.45s)。收口:主树干净,review 隔离 worktree 只读未改 → 自动清理。

**下一条**:C8 全批收口(C8a/C8b + 双 review APPROVE),进 **C9 主从分栏(dict)**。

### 2026-07-21 · C8b 菜单树表 + ButtonManager(`b28817d`)

C8 后半、全港最大项(menu ~325 + ButtonManager ~307 React 行)。按 org 范式:纯逻辑抽 `menuForm.ts`(变异钉),两组件接线。
- **承重接缝逐条移**:UNASSIGNED 哨兵 + moduleFilter 默认跟随当前应用(modules 载后校正);`stripButtons`(按钮不进主树);**只过滤顶级**(moduleId 仅顶级目录有、子树继承);`buildButtonInfo`(徽标 count + **搜索用 perms**——按钮剥出树后「这码归哪页」只能靠父页命中,否则藏起=搜没);`subtreeIds` 父级排除防成环;`menuRowToInput` 全量(漏字段抹空);`syncShell` React 化——`enter(currentModuleId)` 重拉门户树、路由**反应式派生**(≠ Vue 命令式 `buildRoutesForModule`),失败静默;`currentAppPrefix` 软过滤;组件路径取 `viewComponentPaths` glob。
- **ButtonManager**:Vue 的 imperative `ref+defineExpose` → React **props 受控**(`menu != null` 即开);列表弹窗用原生 `Modal`(只关闭底栏,FormContainer 无纯关闭态);单增/编辑 + 从路由批量(未用路由过滤 + 方法默认标题 + 应用软过滤带「显示全部」逃逸)。**用原生 antd Table 非 ProTable** → spec 可真渲染(不撞 pro-components 墙),集成式测列表/删除/批量/软过滤/单增。
- **antd v6 抉择(记档)**:①组件路径/权限码用 **AutoComplete**(v6 单选 Select 无 freeform,而组件路径「文件未建先占位」、权限码「拉取失败手输」是承重),权限码分组头简化为扁平(本应用在前);②踩 4 类 v6 弃用(tsc 不红、antd lint 逮):`showSearch={{optionFilterProp}}`(Select)、`showSearch={{filterOption}}`(AutoComplete)、`Tag variant="filled"`(替 `bordered`)、`Space vertical`(替 `direction`)。
- **IconPicker 顺带改**:`value/onChange` 改可选,以便直接进 `Form.Item`(Form 运行时注入);唯一调用方(menu)本就靠注入,现有 spec 显式传值不受影响。
- **变异 29/29 全致死**:M-MF1–10(默认表单/全量映射/剥按钮/按钮信息/子树/软过滤/方法标题)、M-IX1–10(顶级过滤 [连带红 load+keyword=2]/全量 update/停用才确认/权限门/按钮列显隐/更多▾删除/打开 ButtonManager/perms 搜索/syncShell)、M-BM1–9(buttons 派生/权限门/save 全量/批量 code+标题+未用过滤 [M-BM6 连带 batch+soft=2]/软过滤/删除 id)。**预测≠实测 1 处**:**M-BM1** 预测 3、实测 6——ButtonManager 每条用例开头共享 `findByText('GET...')` 前置,buttons 清空后该前置在全 6 条里都超时抛错(**安全过杀,非漏杀**);tally 脚本把「6 failed (6)」0-passed 格式误标 NORUN(redNames 抓到全 6 即铁证)。三文件残留 clean。
- 判据:`tsc` 0 / `antd lint` 6.5.1 net 0 / `oxlint` 0 / 靶向 `vitest` **27/27**(menuForm 14 + index 7 + ButtonManager 6)/ `build` OK(49.07s)。menu i18n 键 C0 已全量预置,无新增;权限码与后端菜单 CRUD 路由逐字对齐。

**下一条**:开 **C8b review lane**(独立 opus、隔离 worktree、不自审);通过后 C8 全批收口,进 **C9 主从分栏(dict)**。

### 2026-07-21 · C8a review lane(独立 opus,隔离 worktree)→ APPROVE

独立 lane 对**主树 b247fc2 的真 C8a 代码**审(七探针 + 判据复跑),裁定 **APPROVE**:0 CRITICAL/HIGH/MEDIUM,1 LOW。
- **最有价值处**:reviewer 不停在"看着对",而**逐层追 installed antd 6.5.1 / pro-components 3.1.14-2 源码**证实 TreeTable 的 `expandable` 绑定**活着**——它是普通 config 对象经 `pro-components/lib/table/Table.js:706-723` 的 `...rest` 直透 antd Table,**无** C7 HIGH-1 那种 `cloneElement` 末位展开覆盖;`onExpandedRowsChange` 收 key 数组与 `@rc-component/table` interface(179/186)一致,`childrenColumnName` 默认 `'children'` 与 `buildTree` 嵌套对齐。**这批不是 C7 那种死接缝。**
- 其余六探针全核过:`rowToInput` 全量六字段(漏字段 → org spec 的 `toHaveBeenCalledWith` 精确断言会红,有判别力)、filterTree 浅拷贝→重拉推理成立、受控展开播种/手动折叠不被重置推理成立、四权限码与 `DefaultMenuSeed.cs`(Id 71/72/73/98)+ `OrgController.cs` **逐字节一致**、`excludeSubtreeOf` 剪自身子树防成环、save `parentId ?? 0` 归一 + validateFields 在 try 外故校验失败留弹层。
- **LOW-1(接受,记档)**:TreeTable.spec 与 org.spec 都 mock 掉真 ProTable,故**升级期**若 antd 改名 `onExpandedRowsChange`/停透传 `expandable`,两 spec 仍全绿而运行时展开已死(「测了自己的 mock」的固有缺口)。**为何不补**:①只有渲染真库才逮得到,而真库撞 vitest 深导入墙(全模板 mock ProTable 的既定原因);②不像 C7 HIGH-1 有可钉的元素形状,此处无廉价确定性钉;③全模板 ProTable 运行时一贯 dev/E2E 保(B10/B11/C 批同 posture)。为一个升级接缝现搭 E2E 属越界。留 B12 实点 + E 批 E2E 兜。**如实记录的已知缺口,非假装堵上。**
- reviewer 复跑判据(主树):`tsc` 0 / `antd lint` 6.5.1 net 0 / 三 spec `vitest` 16/16;StatusSwitch 另有 7 例独立覆盖真 click→request→悲观回滚。收口:核主树 b247fc2 字节一致、移除隔离 worktree。

**下一条:C8b**(menu ~530 + ButtonManager ~360,全港最大项)。

### 2026-07-21 · C8a 机构树表 + `TreeTable` 薄封装(`a5f9775`)

C8 拆分前半:静态树表原型,把 pro-components(beta)的**另一形态**(静态 `dataSource` + 受控展开)隔离进新 `TreeTable`,与 `DataTable`(fetcher 模式)并列——两模式塞一个组件会堆条件复杂度,故并列而非扩展(C8 scope 定案兑现)。
- **`TreeTable`**:`dataSource`+`pagination:false`+`search:false`+`options.reload:false`+受控 `expandable`。**踩一处 v6/beta 坑**:antd Table 展开回调 prop 是 **`onExpandedRowsChange`**(非直觉的 `onExpandedRowKeysChange`),且回调收到的是**展开行的 key 数组**(名不符实);写错触发 TS2561、tsc 逮到。封装内适配成对外的 `onExpandedRowKeysChange`。
- **`orgForm.ts`(纯逻辑变异钉)**:`blankForm`/`rowToInput`。**全量 update 契约**——后端无独立启停端点,StatusSwitch 行内改状态与 openEdit 回填**共用** `rowToInput`,漏一字段就抹空该字段,故逐字段带全(category 归一 null)。
- **org 页**:`buildTree` 拼树 → 客户端 `filterTree` → 播种默认全展开(`expandableIds`)、data/筛选变后 `useEffect` 重算(否则命中藏折叠祖先里);StatusSwitch 成功后**重拉整树**而非写行(`filterTree` 浅拷贝写不回源树,开关会弹回);操作列收敛「编辑 + 更多▾」按各自权限码显隐;复制弹窗;`OrgTreeSelect` 剪自身子树防成环。
- **变异 18/18 全致死,预测=实测零缺口**:M-OF1–6(默认表单 + 全量映射每字段)、M-TT1–5(dataSource 转发 / pagination|search|reload 关 / 受控展开适配 / persistKey→columnsState)、M-OR1–7(load buildTree+播种、StatusSwitch 全量、PUT 权限置灰、更多▾ 三项 perm 门 + 删除 confirm+重拉、remove id、expandableIds 播种);**M-OR1** 去 buildTree 如预测**连带红 2 条**(load 拼树 + 关键字过滤同塌);三文件残留 clean。
- **顺带二次去 monitor flake(`09d9d69`)**:C7 那次(`0305bd1`,waitFor 1000→5000)没根治——全量 gate 又挂,这次是 **`Test timed out in 5000ms`**(测试级超时,非 waitFor)。**根因是 antd Button loading 态吞 onClick**(`handleClick` 里 `innerLoading` 直接 return):满载 + GC 停顿下 CPU 卡可能先于 `setLoading(false)` 渲染出现,此刻点刷新被吞、第二次永不发生——**抬超时治不了被吞的点击**。修法:点击前先等按钮退出 loading + per-test 超时抬 15s 留渲染滞后头寸。隔离 3/3 确定性绿。
- 判据:`tsc` 0 / `antd lint` 6.5.1 net 0 / `oxlint` 0 / 靶向 `vitest` 16/16(orgForm 5 + TreeTable 5 + org 6)/ 全量 `vitest` **474 passed / 1 failed**(唯一红即上述 monitor flake,已根治;C8a 三 spec 全在 474 内)/ `build` OK(50.41s)。**未重跑全量**:那条 flake 正是全量不可靠之源,拿它验 flake 修复本末倒置,隔离确定性 + 根因即证据。

**下一条**:照 C6/C7 各自即时开 review lane 的先例,开 **C8a review lane**(独立 opus、隔离 worktree、不自审);通过后进 **C8b(menu ~530 + ButtonManager ~360,全港最大项)**。

### 2026-07-21 · C7 review lane(独立上下文 opus)→ REQUEST-CHANGES(1 HIGH)→ 修复 → 复核 APPROVE

**独立 lane 的价值兑现**:抓到一个我自己标记「运行时未测」的真 HIGH——正是那处没被单测覆盖的接缝。
- **HIGH-1(`4c850c5`):op 操作人搜索是死的 no-op**。`formItemRender: (_, {value, onChange}) => <UserSelect value=... onChange={(v)=>onChange?.(v)}/>` 在 pro-components **3.1.14-2** 里坏了,reviewer 追到确切链:①`SchemaForm/valueType/field.js` 的 wrapper 在调 `formItemRender` 前 `omitUndefined({...config, onChange: undefined})` **删掉了 config.onChange**(故我解构到的 onChange 是 undefined、`(v)=>onChange?.(v)` 恒 no-op);②真正的绑定由 `ProFieldCore.js` 经 `cloneElement(newDom, {...fieldProps, ...newDom.props})` 注入,而 `newDom.props` **末位展开**——我显式写的 onChange **盖掉**了注入的 `onChangeCallBack`。净效果:选了操作人 → antd Select 触发 onChange → 被 no-op 吞掉 → 表单 operatorId 恒空 → `opPage` 不带 `OperatorId` → 「谁」这条审计筛选静默返回全部人。**修法**(reviewer 开的方子):裸返 `<UserSelect allowClear placeholder=.../>`,让 pro-components 经 cloneElement 注入 value/onChange(经 ApiSelect 的 ...rest 直达 antd Select)。
- **MEDIUM-1(判别力缺口,HIGH-1 为何溜过)**:fetcher 单测直接 `captured.fetcher({operatorId:7})` 合成入参,只证 fetcher **转发**手递的键,从不经 ProTable 搜索表单 → 死的 UserSelect→form 绑定对任何测试都不可见,故 20/20 变异全杀仍出了个死筛选。**处置**:全真 ProTable 表单在 happy-dom 跑不了(C2b+ 全靠 mock DataTable 绕的那堵墙),故改**确定性钉根因**——新增 op 测试断言 `formItemRender(...)` 返回的 UserSelect 元素 `props.onChange`/`props.value` **皆 undefined**(重新显式写任一个即重现 HIGH-1)。变异校验:加回 `onChange` → 精准红 1。reviewer 复核**认可此测试是正解**(全真表单等于测库本身、且撞 happy-dom 墙)。
- **顺带去一条 flake(`0305bd1`)**:首次全量 gate 挂在 `monitor/index.spec` 的 `waitFor(...toHaveBeenCalledTimes(2))`——默认 1000ms 在全量重载 + 本机内存紧张(GC 停顿)下偶发超时(monitor 单跑 7/7、非 C7 回归)。放宽到 5s,断言不变。
- **复核 APPROVE**:reviewer 重追链确认裸 UserSelect 彻底消除 shadow、注入接通 operatorId;确认无新问题。判据(修后):`tsc` 0 / `antd lint` 6.5.1 net 0 / 全量 `vitest` **459/459**(+1 op 回归)/ `build` OK。review 后按纪律核对主树字节一致、移除隔离 worktree。

### 2026-07-21 · C7 日志三页(`169493a`:op/login/exception)

只读列表批,照 B11 范式:共享纯逻辑抽 `log/logFormat.ts`,三页 `index.tsx` 只接线。这三页共享一个逻辑模块、是一条台账项,故**单条 commit**(非 C6 那种逐页)。
- **op**(操作日志):审计三问「谁/何时/干了什么」——操作人按 id 精确筛(UserSelect 进 `formItemRender` 搜索项)、时间范围、操作名/路径/成败;paramJson 走 json CodeBlock、异常信息危险色 `<pre>`;详情抽屉直填行数据(后端无 op/{id})。
- **login**(登录日志):resultCode 把 AuthService 的 40001–40005 翻成 `error.auth.*` 人话(裸 40004 对排查无用);设备列 `uaSummary`;姓名缺失回落账号。无抽屉(字段少)。
- **exception**(异常日志):exceptionType/path 搜索;traceId + 堆栈进详情抽屉。
- **首次用 ProTable 内建搜索表单**(C6 全 `search:false`),踩到两处 **v6 改名坑(tsc 不红、`antd lint` 逮到)**:①`renderFormItem`→**`formItemRender`**(beta 版 ProColumns 只认后者,前者 TS2353);②Drawer `width` 弃用→**`size`**(v6.5.1 的 `size` 接受 `number`,故 `size={560}` 既保精确宽度又不弃用,不必退成 default/large 档)。
- **变异 20/20 全致死,预测=实测零缺口**:M-LF1–7(operatorText id 串/loginNameText 账号回落/prettyParam 缩进+catch/loginResultKey 三码+null 回落)、M-OP1–6(fetcher title/operatorId/createTime 映射、reload、perm 门、detail)、M-LG1–4(account 映射、resultCode 文案分支、name 回落、reload)、M-EX1–3(exceptionType 映射、detail、reload);四文件残留 clean。
- log/error.auth i18n 键两侧本就齐全,无新增键。菜单 component 路径 `system/log/{login,op,exception}/index` 与后端 seed(DefaultMenuSeed.cs:134/139/144)对齐。判据:`tsc` 0 / `oxlint` 0 / `antd lint` 6.5.1 net 0 / 全量 `vitest` **458/458**(+25:logFormat 12 + op 5 + login 4 + exception 4)/ `build` OK(50.7s)。
- **如实记**:操作人 UserSelect 走 `formItemRender` 的搜索表单**运行时绑定**(value/onChange 注入)happy-dom 里没实测——spec 只钉 fetcher 的 operatorId 映射 + 列形状,活表单绑定留 dev/B12 实点(同 B10/B11 对 ProTable 运行时行为的 posture)。

**下一条 C8**:树表原型 `org` + `menu`(含 `ButtonManager` 子组件,写操作要 `hasPerm` 门,沿用 dev 上 J4 的对齐)。

### 2026-07-21 · C6 review lane(独立上下文 code-reviewer/opus,隔离 worktree base HEAD)→ **APPROVE**

审 C6 全批七页 + 三件 C6 引入共享物(useBatchDelete / utils/ua / DataTable rowSelection),范围精确排除夹在提交序列里的 **E7** markdown 提交(`fda311f`/`ca3910a`,不属 C6)。**无 HIGH、无涉数据/安全 MEDIUM**。六处「静默出错接缝」逐一对着**类型定义 + 后端 seed** 核过并全部判正确:
- **session `{sessionid}` 小写**确认是**有意**:`PermissionCode.cs:15` 对整条 route template `ToLowerInvariant()`,故后端 `{sessionId}` 归一成 `{sessionid}`,`DefaultMenuSeed.cs:151` 正是这形状,页面串匹配。
- recycle(seed 171-172)、cache(122-125)、position(75-77)、notice(101/103)权限码逐一对上后端 seed。
- position `rowToInput` 核到 `PositionInput` 恰 `{name,code,sort,enabled}` 四字段无隐藏字段;notice `receiverValid` 用 `?? ReceiverType.All`(nullish 保住 0 值)且校验器只挂条件渲染的 receiverIds 字段;cache thunk 晚绑定 + ask 取消真拦截;file 仅成功清选刷新;session 自踢守卫渲染路径不可绕。
- antd v6.5.1 用法离线 CLI 复核全对(Select `showSearch` 对象形 optionFilterProp、Progress strokeColor 免 status 枚举、Tabs items、Card size、`<Can>` 返 Fragment 不破 Row/Col gutter context)。

**4 条 LOW 处置**:
- **LOW-1(recycle 判别力缺口)已堵**:原 seam 测试只在默认 `user` 页签断言 `restore('user',5)`,故「把 restore/purge 的 `activeTab` 硬编码成 'user'」的变异能存活 —— 而这正是本页最高危接缝(选错类型静默还原错实体),我原变异只钉了 id(RC2/RC3)没钉类型参数。改测试**先切到角色页签**再渲染操作列、断言 `restore('role',5)`/`purge('role',5)`。补测变异 **2/2 全致死,预测=实测**:M-RC-A(restore 硬编码 user)红 1、M-RC-B(purge 硬编码 user)红 1,均红在切角色页签那条;基线 4/4 绿,残留 clean。
- LOW-2(session 自踢在 `userInfo` 未载时 `isSelf` 全 false):**接受** —— userInfo 在路由守卫先于本路由载入,且纯 UX(踢自己=登出),后端权威 authz。
- LOW-3(cache `busy` 单键,兄弟卡按钮不禁用):**接受** —— 各卡 ask 二确 + 幂等,无害。
- LOW-4(monitor memPct 可 >100,working set vs GC 可用内存不同分母):**接受** —— antd Progress 视觉裁剪,纯 cosmetic。

review 后按纪律核对主树字节一致(HEAD 未变、工作树干净)、移除隔离 worktree。

**下一条:C6 全批收口,进 C7。**

### 2026-07-21 · E7 md-editor 全离线(维护者授权「你决定吧」→ 两模板都修 + 顺带修 web/)

**决定**:md-editor 改成完全离线,不引新依赖、不做本地化管线(理由:通知系统用不到 katex/mermaid;代码高亮是可选增强;本地化 highlight 要喂 hljs 实例 + 主题 CSS,对公告编辑器过度工程)。

- **web-react `ca3910a`**:组件加 `no-{katex,mermaid,highlight,prettier}`;`setupMarkdown` 的 `config` 补 `editorExtensions.echarts.instance:{}`(echarts 唯一无 `no*` 旗标者,空 no-op 既断 unpkg 又杜绝其 `parseOption` 对正文 `new Function`)。
- **web/ `fda311f`**(md-editor-v3 同 API):镜像 `lib/markdown.ts`(`withXssPlugin` + `setupMarkdown`)、两组件加 `no-*`、main.ts 调用。**这一并把 C5 review 延后的 web/ HIGH XSS 也修了** —— R2 后刻意、经授权的第二处动 web/。
- **证据驱动(而非假设)**:先写自包含守卫「MarkdownView 挂载后不得注入指向外网的 link/script」,它先因 echarts 而**红**(暴露了 no* 关不掉的 echarts),补 no-op instance 后**转绿**;再变异证非空 —— 掉 `noHighlight`→红、echarts instance 置 null→红,基线复绿 4/4。**预测≠实测的收获**:原以为 4 个 `no*` 就够,实测 happy-dom 抓到 echarts 仍被注入,才发现 echarts 无 `no*`、必须走 instance —— 守卫测试当场把这个盲点逼了出来。
- **残留(如实记)**:cropper/screenfull 是编辑器按需加载(点全屏/裁剪),非挂载预载;公开 MdPreview 无工具栏=完全离线,管理端编辑器这两项极少用,要绝对气隙可裁 `toolbars`。
- 判据:web-react tsc0/oxlint0/vitest **387/387**(+1 守卫)/build;web/ oxlint0/vue-tsc0/vitest **46/46**(+2)/build。

**下一条**:回到 C6b(先补 `useBatchDelete` + DataTable rowSelection,再 notice/file)。

### 2026-07-20 · C6 标准列表页批(**全 7 页落完**:position、notice、file、session、recycle、cache、monitor)

**C6c 剩余三页 `9ddaf71`/`bdb0b76`/`732d1fe`**(recycle / cache / monitor):三页各自新文件、互不改动既有文件,共用一次全量 `vitest`+`build` 闸门(省本机重进程),但逐页原子提交。判据一次覆盖三页:`tsc` 0 / `oxlint` 0 / `antd lint` 6.5.1 net 0 / 全量 `vitest` **433/433**(+15:recycle 4 + cache 4 + monitor 3 + monitorFormat 4)/ `build` OK(58.8s)。**变异共 21/21 全致死**,3 处预测≠实测(均比预测更强)+ 1 处 NORUN 伪影已辨明:
- **recycle**(M-RC1..5):切页签换类型、恢复/彻底删按 `activeTab`+行 id 定位、两道权限门。**M-RC4/RC5(权限门 `has`→`!has`):预测红 1、实测红 2** —— 反转后「恢复/彻底删」定位测试因按钮消失一并塌。
- **cache**(M-CA1..4):`ask` 二确守卫、portal 代际 vs 清除条数文案分支、`rebuild` 标志、权限门。**M-CA1(去 `ask` 取反守卫):预测红 1、实测红 3** —— 未确认路径放行后,三条 trigger 断言(不执行/清条数文案/portalDone 文案)连带红。
- **monitor 页**(M-MO1..3):0 容量盘过滤、手动刷新再拉、挂载即拉。**M-MO3(`void load()`→`void 0`):`failed=NORUN`** —— monitor 三条用例全红时 vitest 不打印 "passed" 段,正则取不到计数;经 redNames 列出三条(挂载拉快照/手动刷新/0 盘过滤)确认为**全红伪影而非未跑**。
- **monitorFormat**(M-MF1..9):`fmtBytes` 三档 + 边界(0→'0 B'、非 B 档 2 位、超 TB 封顶)、`uptimeParts` 天/时分解、`pct` 四舍五入 + 防除零、`usageColor` 90/75 阈值。全部预测=实测。
- i18n recycle/cache/monitor 块两侧本就齐全,无新增键。happy-dom teardown 那次 `https://ex.com/r` 的 AbortError/NetworkError 栈仍是 hermetic 噪声(套件 exit 0)。

**C6c-session `985f223`**(在线会话):只读列表 + 行内强制下线,踢自己置灰(防误把自己下线),设备列 `uaSummary` 解析 UA 成「浏览器 · 系统」。
- **顺带把框架无关的 `utils/ua.ts` verbatim 搬来**(与 `web/` 字节一致,`diff` EXIT 0;R4 当初记的"暂不搬、等用到的页面再搬"到此兑现)。
- **变异 9/9 全致死,含 2 处预测≠实测(均比预测更强)**:M-UA1(join ` · `)、M-UA2(未识别回落原始串)、M-UA3(Edge 排序:正则打歪则 Edge UA 误判 Chrome)、M-UA4(Windows NT 10 标签)、**M-UA5(去 `filter(Boolean)`:预测红 1、实测红 3 —— 未识别回落与空回落连带,因 `['','']` join 成 ` · `)**、M-SS1(`=== currentUserId` 自踢判定)、**M-SS2(权限门 `!has`→`has`:预测红 1、实测红 2 —— 超管下反向返 null,自踢测试也塌)**、M-SS3(`disabled={isSelf}`)、M-SS4(设备列 `uaSummary` 不解析)。
- session i18n 键两侧本就齐全,无新增键。判据:`tsc` 0 / `oxlint` 0 / `antd lint` 6.5.1 net 0 / 全量 `vitest` **418/418**(+10:ua 6 + session 4)/ `build` OK(55s)。

**C6b-file `082db2c`**(文件管理):只读列表(按文件名搜)+ 工具栏上传(整传/分片)+ 行内下载/删除 + 批量删除。**顺带补齐 Vue 侧有、web-react 缺的两件基建**:
- **`useBatchDelete` hook**(React 版,镜像 Vue composable):受控勾选态 `{selectedKeys,setSelectedKeys,hasSelection,run}` + 二次确认(useConfirm 挂起守卫防连点)+ **仅成功才清选刷新**(失败保留勾选可重试)。有意不 memo `run`(每渲染重建以闭最新 selectedKeys,避 stale-closure)。
- **`DataTable` 加受控 `rowSelection` 透传**:类型走 `TableProps<T>['rowSelection']`(不深导、不抹类型),转发给内层 ProTable —— **了结 `UserPicker.tsx:78` 当初记的 ponytail 延后项**(勾选依赖 DataTable 尚未支持的 rowSelection)。
- **纯逻辑抽 `fileFormat.ts`**:`formatSize`(字节→B/KB/MB/GB,1024 进制)+ `triggerBlobDownload`(createObjectURL→`<a download>`→点击→移除并 revoke)。下载走 blob(Bearer 自动带);单删/批删走 useConfirm/useBatchDelete。页面 `index.tsx` 只接线。
- **变异 11/11 全致死,预测=实测零缺口**:M-FF1/2(formatSize 两处 `<1024` 边界)、M-FF3(toFixed(1))、M-FF4(download 名)、M-FF5(revoke 释放)、M-BD1(空选守卫)、M-BD2(map(Number))、M-BD3(仅成功分支)、M-BD4(清选)、M-BD5(刷新)、M-DT1(rowSelection 透传)。
- i18n file 块两侧本就齐全,无新增键。判据:`tsc` 0 / `oxlint` 0 / `antd lint` 6.5.1 net 0(DataTable + file/index)/ 全量 `vitest` **408/408**(+11)/ `build` OK(53s)。

**C6b-notice `5373528`**(消息通知管理):发布广播/定向通知 + 分页列表 + 只读 Markdown 查看 + 删除。照 B11 范式:纯逻辑抽 `noticeForm.ts`(`blank` 默认表单、`typeLabelKey`/`typeTagColor` 枚举→文案/色、`receiverValid` 接收范围条件必填),页面 `index.tsx` 只接线(FormContainer 发布弹窗、`Form.useWatch` 驱动的条件 receiverIds 字段带 validator、MarkdownEditor 正文、MarkdownView 查看 modal、Can/useHasPerm 权限门)。
- **会静默出错的核心 = `receiverValid`**:全体广播(All)免选,定向(Role/User)必须≥1;写反会放行「定向空发」或挡住正常广播。变异钉死其两个分支 + `typeLabelKey`/`typeTagColor` 映射 + `blank` 默认 receiverType。
- **v6.5.1 坑**:`Select optionFilterProp` 已废,`antd lint` 抓到(tsc 不红),改 `showSearch={{ optionFilterProp: 'label' }}` 对象形。查看走原生 Modal `footer={null}`(信息展示非表单);正文渲染复用 C5 `MarkdownEditor`/`MarkdownView`,XSS 兜底(`setupMarkdown` 全局)已覆盖。
- **变异 5/5 全致死,预测=实测无缺口**:M-NF1/2(receiverValid 两分支)、M-NF3(typeLabelKey)、M-NF4(typeTagColor)、M-NF5(blank 默认 receiverType)。
- i18n notice 块两侧本就齐全,无新增键。判据:`tsc` 0 / `oxlint` 0 / `antd lint` 6.5.1 net 0 / 全量 `vitest` **397/397**(+10:noticeForm 6 + index 4)/ `build` OK(53.6s)。happy-dom teardown 中止 XSS 集成测试那次 `https://ex.com/r` fetch 打出 AbortError/NetworkError 栈是 hermetic 配置的正常噪声,非失败(套件 exit 0)。

**C6a `ad5d849`**(position 岗位管理):标准列表 CRUD —— 名称搜索 + 工具栏 + 表格 + 行内启停 + 新增/编辑弹窗。照 B11 范式:纯逻辑抽 `positionForm.ts`(`blankForm` + `rowToInput`),页面 `index.tsx` 只接线(DataTable 列 / StatusSwitch / FormContainer / Can / useConfirm)。
- **唯一会静默出错的接缝 = `rowToInput`**:岗位无独立启停端点,行内 StatusSwitch 与编辑回填都走**全量 update**,全量替换语义下漏一个字段就抹空该行 → 单测断四字段全映射且不漏带 id/createTime。`blankForm` 默认值(启用/sort 0)与 fetcher 非串搜索过滤一并钉。
- **变异 5/5 全致死,预测=实测无缺口**:M-PF1/2/3(rowToInput 逐字段)、M-PF4(blankForm 默认)、M-IX1(fetcher 去类型守卫)。
- i18n position 块(增改共用 code 禁改、codePlaceholder 等)两侧本就齐全,无新增键。路由 glob `/src/views/**/*.tsx` 覆盖,菜单指到即可达。
- 判据:`tsc` 0 / `oxlint` 0 / `antd lint` 6.5.1 net 0 / 全量 `vitest` **386/386**(+6)/ `build` OK。

**下一条:开 review lane 审整个 C6 全批**(position/notice/file/session/recycle/cache/monitor 七页),base `dev`、隔离 worktree,不在本上下文自审。通过后进 C7。

### 2026-07-20 · C5 review 处置(review-c5 lane:REQUEST-CHANGES → 1 HIGH 已修 + MED-1 + LOW-1;另发现 1 条自包含硬伤)

**REQUEST-CHANGES,0 CRIT,1 HIGH + 1 MED + 3 LOW。**opus 独立 lane 在隔离 worktree 审 C5a `3eb0feb` + C5b `5822ae3`(base `dev`):**逐字搬两处 `diff` EXIT 0 坐实**(chunkUpload.ts、lib/echarts.ts 与 `dev:web/` 字节一致,无静默漂移),并**独立复现变异**(min(99)→min(100) 杀进度封顶、去 resume 守卫杀双续传、max(1,ceil)→ceil 杀空文件片)证判据非回声;worker 池领片 `pending[cursor++]` 先占后 await 无竞态、off-by-one 干净、Chart 生命周期(dispose+重 init 配对、resize 必清、optionRef 避陈旧)、echarts 正确 tree-shake 均获独立背书。修复提交 `e6e7e97`:

- **[HIGH 已修] 通知 Markdown 存储型 XSS —— C5b「无 XSS 面」的说法是错的** —— Markdown **天然放行内联 HTML**,而 md-editor-rt 默认 `html:true` + `sanitize` 恒等空操作 + 预设链**不含** XSS 过滤(lane 在 `node_modules` 逐层 trace 坐实)。通知作者(带权限即可,未必超管)存 `<img src=x onerror="...localStorage.getItem('tenon-auth')...">`,任意查看者(含读通知的超管)打开即执行 → JWT 外泄账号接管。**修**:新 `lib/markdown.ts` —— `withXssPlugin`(纯函数接缝)把库自带 `XSSPlugin`(基于已随 md-editor-rt 打进来的 js-xss,**零新依赖**)追加进 markdown-it 链;`setupMarkdown()` 幂等,`main.tsx` 渲染前全局注册。判据:**M-MD1/2/3 钉接线**(丢追加/改 type/挂错插件)+ **一条端到端集成测试**(`setupMarkdown()` 后渲 `MarkdownView` 传 `<img onerror>`,断渲染后 img 无 onerror —— 这是真行为判据,非回声)。更正了 C5b 提交与两组件注释的误述。**继承自 Vue 侧**(web/ md-editor-v3 同缺口)—— 非 C5 回归,但 web/ 该同修,列 E7 决策。
- **[MED 已修] chunkUpload spec 假 File 的 slice 忽略入参 → 字节偏移零覆盖** —— lane 探到 `file.slice(i*CHUNK,…)`→`slice(0,0)` **存活**(4 passed):唯一可能悄悄发错/空字节的路径没测。**修**:补一条**真实 11MB `File`** 多片用例,断上传各片 blob.size 多重集 =[1MB,5MB,5MB]。**M-CU1**(即 review 那条 `slice(0,0)`)现被钉死。
- **[LOW 已修] Chart 加载中切主题丢 loading 遮罩** —— 主题 effect dispose+重 init 出新实例却没补 `showLoading()`。**修**:`loadingRef` + init 后 `if(loadingRef.current) chart.showLoading()`。命令式 canvas 无单测(同 Chart 其余生命周期),dev/B12 实点,**如实记为已知判据缺口**。
- **[LOW 记录不改] `uploadId!` 非空断言 / uploadImages 空图链 + `Promise.all` 全败** —— 均 `chunkUpload.ts`/`MarkdownEditor.tsx` **逐字搬**;为守「diff EXIT 0」的逐字契约不在 web-react 单侧改;契约违例响应本就走后端重算报错、空图链是极小 UX。记录不动。

**另:我实现 HIGH 的集成测试时发现一条 review 没探到的自包含硬伤 →** md-editor-rt 的 `MdPreview`/`MdEditor` 挂载即往 DOM 注入指向 **unpkg CDN** 的 `<link>/<script>`(highlight/katex/mermaid 懒加载资产),happy-dom 真去 fetch(联网机拖慢套件、离线 CI 网络噪声)。**测试侧根因堵死**:`vite.config.ts` 加 `happyDOM.settings.disable{JavaScript,CSS}FileLoading + handleDisabledFileLoadingAsSuccess`(一处令全套件 hermetic 不触网;不影响主题桥读内联 tokens.css)。**产品侧**(应用运行时同样拉 unpkg,**两模板都有**)是更大的自包含缺口,列 **E7** 待决策(本地化 highlight 实例 vs 关掉通知用不到的 katex/mermaid vs 接受 CDN;是否顺带修 web/),**不塞进本次处置**(一次只闭一条)。

**判据全绿**:`tsc` 0、`oxlint` 0(改动 8 文件)、全量 `vitest` **380/380**(376→380:+2 XSS 接线 +1 XSS 集成 +1 真实切片)、`vite build` 待跑。本批无 antd 组件改动免 antd lint。**预测≠实测**:M-MD1(丢追加)预测红 1、实测 3 条全红(含集成;NORUN 是「全红无 passed 段」正则伪影,redNames 三条俱在已致死);M-MD3(挂错插件)预测红 1、实测红 2(集成测试也抓「挂了空插件」)——两处都比预测更强。主树验字节一致 + 移除 `agent-aa58538a473ffddec` worktree。

**下一条 C6**(标准列表页批):`position`(可编辑 Sort)、`notice`、`session`、`file`、`recycle`、`cache`、`monitor`,照 B11 `<DataTable>` + `userForm.ts` 拆分范式。

### 2026-07-20 · C5 上传 + 富文本 + 图表(拆 C5a 上传 / C5b 编辑器+图表两轮)

**C5a `3eb0feb`**(chunkUpload + FileUpload):
- `utils/chunkUpload.ts` **逐字搬**(框架无关,`@/api`+`@/types/api` 1:1;web-react fileApi 已有 chunkInit/chunkUpload/chunkComplete/upload 全套)。切片→整文件 SHA-256→chunkInit(秒传/续传)→限并发(3)传缺失片→complete。
- `FileUpload`:封 antd `Upload` 的 `customRequest`。上传编排抽 `performUpload(file, chunked, cb)` 便于变异钉(chunked 分支、loadingChange true/false 时序、失败弹错、进度透传)。
- **变异 11/11 全致死**。两处预测≠实测(均变紧):**M-CU3**(去 `!done.has(i)` 跳过)连带红「全部已收→不再传」;**M-FU1**(`chunked?`→`false?`)连带红「onProgress 透传」。
- 测法:`chunkUpload.spec` mock fileApi + **假 File(size 纯数字不真分配 + stub crypto.subtle)** 控分片数,钉秒传/续传/封顶99/uploadId;`FileUpload.spec` 测 performUpload——**@/api mock 要连 `ApiError` 一起给**(translateError 走 `err instanceof ApiError`,整体 mock 否则丢了它、错误路径炸)。

**C5b `5822ae3`**(MarkdownEditor/View + Chart):
- `lib/echarts.ts` **逐字搬**:tree-shaken `use([...])` 注册 + `buildEChartsTheme()` 现读 CSS token 变量(单一色源=tokens,随明暗/accent)。
- `Chart`:**裸 echarts.init**(不引 vue-echarts 那类 wrapper)。主题(明暗/accent)变 → dispose+以新主题 init(echarts 主题 init 时固化)+ 种当前 option(option 存 ref,不进重建依赖,避 exhaustive-deps 又不漏最新);option 变 → `setOption(notMerge)`;window resize;卸载 dispose。
- `MarkdownEditor`/`MarkdownView`:md-editor-rt `MdEditor`/`MdPreview`(`modelValue`/`onChange`/`theme`/`onUploadImg`),随明暗;存 Markdown 源文本(~~前台库自渲染,无 XSS 面~~ **—— 这句错了,见 C5 review 处置**)。`uploadImages` 抽出(传签名直链 viewUrl,非 storagePath)。
- **变异 8/8 全致死**(buildEChartsTheme 色板/文字/轴线/背景 6 + uploadImages 2)。**发现**:happy-dom **能**经 `getComputedStyle(root)` 解析 inline 设的 CSS 自定义属性 → buildEChartsTheme 可直接单测(在 root 上 setProperty 一套已知值断主题对象)。M-ME2 电池显 `NORUN` 是脚本正则局限(该文件两用例全红时 vitest 摘要无 "passed" 段),redNames 双红证实已杀,非存活。
- **如实记**:Chart 命令式 canvas 在 happy-dom 不可单测(无 canvas 尺寸),判据落 buildEChartsTheme + dev/B12 实点(同 B10 DataTable/ProTable posture)。

**判据全绿**:C5a/C5b 各自 `tsc` 0、`oxlint` 0、`antd lint` 0(FileUpload);全量 `vitest` 376/376(364→372→376)、`vite build` OK(md-editor-rt + echarts + 样式 import 在 rollup 解析)。

**下一条**:开 review lane 审整个 C5(C5a + C5b,base `dev`、隔离 worktree);之后进 C6(标准列表页批)。

### 2026-07-20 · C4 review 处置(review-c4 lane:APPROVE + 1 MED + 5 LOW → 采纳 MED + 4 LOW)

**APPROVE,0 CRIT/HIGH。**opus 独立 lane 在隔离 worktree(junction 复用 node_modules)审 C4;我另核主树 C4 两提交完好、移除遗留 worktree。lane 实测全绿(`tsc`/`oxlint`/`vitest` 16/16/`build`/`antd lint` v6.5.1 net 0),并**独立复现 3 变异**(svgToIcon `vb![2]`→`vb![0]` 杀 2 条、filterNames 去 trim 杀同引用条、overflow→0 杀 cap 条)证判据非回声,另 grep 坐实零 `@iconify/react` 默认入口 / 零 `fetch`/`navigator.onLine` / 零跨模板引用、`addCollection` 幂等、body 正则跨行正确、i18n 嵌套正确。修复提交 `2f5348e`:

- **[MED 已修] 真 `loadIconNames` 全链路无集成测试,mock 掩盖接线** —— IconPicker.spec mock 掉 `@/lib/icons`,而 lib/icons.spec 只测了 loadIconNames 的**未知前缀**分支。若 `COLLECTIONS` 前缀与 `loaders` 键分叉、或漏 `addCollection`,运行时网格空、**零红测试**。修:导出 `isBundled`(对齐 Vue 包)+ lib/icons.spec 加①`COLLECTIONS.every(isBundled)` 结构性钉、②真加载最小集 `ep` 端到端(非空 + 已排序,钉 load→addCollection→sortedIconNames 链路)。教训:**mock 掉数据源的组件测,必须另有一条不 mock 的集成测兜真接线**(与 B11/C2 "mock ProTable 要另验真映射"同源)。
- **[LOW 已修] svgToIcon 丢了 viewBox 原点** —— 只取 `vb[2]/[3]`(宽高),弃 `vb[0]/[1]`(min-x/min-y)。今仅 star.svg(原点 0 0)无碍,但 `viewBox="-2 -2 24 24"` 类未来 svg 会偏移/裁切。修:承接 `left/top`(iconify 支持,默认 0)+ 非零原点用例。
- **[LOW 已修] IconPicker effect 无 `.catch`** —— chunk import 拒绝时留陈旧列表 + 未处理拒绝。修:`.catch` 清空该 Tab。
- **[LOW 已修] 清空按钮键盘不可达** —— `role="button"` 无 `tabIndex`/`onKeyDown`,键盘用户点不了清空。修:补 `tabIndex={0}` + Enter/Space(嵌套 role=button 的 ARIA 纯度纳为已知小瑕,普遍如此,不过度重构)。
- **[LOW 已修] 在线 tab 的 4 个死 i18n 键** —— `online`/`onlinePlaceholder`/`use`/`offlineHint`(在线 tab 已砍,`grep` 证零引用)。删(zh+en)。**推翻 C4 那轮"留着不删"的判断**:两模板可分叉指的是键**可以**不同,不是保留**确定死**的键;在线 tab 是永久性砍除,这 4 键无未来消费者 → 删是对的(ponytail 删优于留)。
- **[LOW 未采纳] 网格无虚拟化 / 启动 eager 加载全部集合** —— cap 300 下无碍,lane 亦言无需动作;C3 已记的既有权衡,不改。

改后复验:`tsc` 0 / `oxlint` 0 / `antd lint` 0 / 全量 `vitest` 364/364 / `build` OK;主树净、worktree 已移除。

**C4 收口。下一条 C5**:上传 + 富文本 + 图表(`FileUpload` 分片/秒传/续传 + `MarkdownEditor` + `Chart`)。

### 2026-07-20 · C4 轻量内联 IconPicker(`da7db2c`)

建在 C3 的 `@iconify/react/offline` + 4 集合基建上。**去在线 tab**:Vue 版有个"在线"tab(自由输入任意 iconify 名、`navigator.onLine` 提示),对气隙自包含模板是反模式,砍掉——本版只离线集合 + 本地 svg。

- **`lib/icons` 扩展**:`COLLECTIONS`(Tab 元数据)、`loadIconNames(prefix)`(懒加载 JSON + `addCollection` + `sortedIconNames`,按前缀缓存;与 setupIcons 幂等叠加,import() 同缓存 JSON 只下一次)、`sortedIconNames`/`svgToIcon` 抽纯逻辑、本地 svg 模块级注册(`import.meta.glob('/src/assets/svg/*.svg', {?raw,eager}')` → `svgToIcon` → `addIcon 'local:<name>'`)。`star.svg` 从 web/ 拷入。
- **`IconPicker`**:触发器(AppIcon 当前图标 + 值 + 清空)开 antd Modal,内 Input 搜索 + Tabs(4 集合 + 本地)+ `filterNames` 过滤 + cap 300 网格 + overflow "还有 N 个"。受控 `value`/`onChange`,值 `prefix:name`/`local:name`。
- **`common.clear`**(zh+en)补齐——清空按钮 aria-label 要它,原来 `clear` 只在 `userPicker`/`log` 下,`common.clear` 不存在(spec 里 aria-label 渲成字面量 `common.clear` 才发现,先写断言逼出的缺键)。

**判据全绿**:`tsc` 0、`oxlint` 0、`antd lint` 0(IconPicker 的 Modal `width`/`destroyOnHidden`、Tabs `items`/`type`/`size`、Input `allowClear`/`prefix` 均 v6 无废弃)、全量 `vitest` 361/361(345→361,+16)、`vite build` OK。

**变异 14/14 全致死**(lib/icons 6 + IconPicker 8;预测 `$JOB/tmp/c4-mutation-predictions.md`)。两处预测≠实测:
- **M-IP3 多杀 5 条**(预测 1、实测 6):`filterNames` 空 keyword 分支 `: names`→`: []` 后,**所有**用空 keyword 渲染网格的组件用例(开弹窗/点选/搜索/本地/cap)全红——它们都靠 `filterNames(names,'')` 返全量。变紧非漏网。
- M-IP4/IP5 预测按"断言数"、电池按"失败用例数"计,非真差异。

**取舍/如实记**:①IconPicker 组件测 mock 掉 `@/lib/icons`(避在测试里加载真实大 icons.json),`lib/icons.spec` 另测真 lib(svgToIcon/sortedIconNames/getLocalIconNames→star/loadIconNames 未知前缀→[]/AppIcon 渲 local:star 端到端);真集合加载+cap 在 dev/build 与 B12 实点。②pick 关窗断回调契约不断 DOM(happy-dom 无过渡,同 C1 FormContainer)。③Vue 侧 `iconPicker.online`/`onlinePlaceholder`/`offlineHint`/`use` 键在 React 版无消费者,留着不删(两模板本可分叉;删了徒增 en 镜像 churn)。

**下一条**:开 review lane 审 C4(base `dev`、隔离 worktree);之后进 C5(上传 + 富文本 + 图表)。

### 2026-07-20 · C3 review 处置(review-c3 lane:REQUEST-CHANGES → 1 HIGH 已修 + 1 LOW 采纳)

opus 独立 lane 在隔离 worktree(junction 复用 primary node_modules)审 C3a+C3b;我另核主树 C3 三提交完好、移除遗留 worktree。lane 实测全绿(`tsc`/`oxlint`/`antd lint` v6.5.1 四文件/`vitest` 30/30/`build`),并**独立复现 3 处变异**坐实判据非回声(`iconName` `||`→`??`、`highlightCode` 丢转义、`computeStrength` `<=1`→`<1` 各自红对的用例),另逐一核对 CodeBlock XSS 边界、PasswordStrength 与 Vue 逐字对齐、`code.css` 暗色选择器与 `useAntdTheme` 打的 `data-theme` 一致、`.ts`+createElement 选型合理、零跨模板引用。

- **[HIGH 已修 `bea1b59`] 图标走了 `@iconify/react` 在线默认入口 → 未注册图标 phone home 到 `api.iconify.design`,破坏气隙保证** —— lane 静态证实默认入口打包 `sendAPIQuery`、`/offline` 入口零 API 且导出同名符号;运行时证据:PasswordStrength.spec 渲染未注册的 `ph:` 图标时 happy-dom 抛 fetch `AbortError`(在线构建真发了请求),DetailPage.spec(纯 antd 图标)不抛。**根因是移植时把 Vue 的 `OfflineIcon`(离线渲染器)降成了裸在线 `<Icon>`**。触发面:①`setupIcons` fire-and-forget 的启动竞态(集合 chunk 未就绪前渲染的 Icon 触发 API 首拉);②菜单/模块自由填的前缀在 4 集合之外;③手敲拼错。修:`AppIcon.tsx` + `lib/icons.ts` + `AppIcon.spec.tsx` 三处导入换 `@iconify/react/offline`(drop-in,同符号、零 API 面)。**离线入口下未就绪只出占位、绝不触网**——一改同时堵死上面三条触发面,并静默了那条测试 AbortError。改后复验:tsc 0 / vitest 345/345 / build OK / PasswordStrength.spec 无 AbortError。**这点我实现时自己标注过("未注册前缀会触网")却选择"记录延后"——review 正确升级为 HIGH:自包含保证是二元的,而修复只是换个子路径入口。教训:气隙/安全类的"已知特性"别当可延后项,尤其当堵它是 drop-in 时。**
- **[LOW 采纳] CodeBlock 已注册语言路径的转义只靠 hljs 库保证、无本地断言** —— 补一条 `highlightCode('{"x":"<b>"}','json')` 不含原始 `<b>` 的断言,把"注册路径也不放原始 HTML 进 `dangerouslySetInnerHTML`"钉成回归守卫(hljs 哪天回退转义即红)。安全边界防呆,采纳。
- **[LOW 未采纳] PasswordStrength effect 在 StrictMode dev 下双拉策略** —— 幂等 GET、dev 限定、与 Vue 同源,无害;不加去重(YAGNI)。

**C3 收口。下一条 C4**:轻量内联 IconPicker(搜索+网格,建在 C3 已落的 `@iconify/react/offline` + 4 集合基建上,加本地 svg glob 注册)。

### 2026-07-20 · C3 展示件(拆 C3a 图标基建 / C3b 展示件两轮提交)

C3 五件里 AppIcon 牵出一个真实边界问题,开工前经维护者 Q&A 定案(见下),再动手。

**开工前决策(维护者 Q&A)**:
- **AppIcon 的 iconify 基建前移进 C3**:AppIcon 的职责就是渲染后端配的任意 iconify 串,离线集合基建(`@iconify/react` + 4 集合)本是 C4 名下,但 AppIcon 必需它、否则菜单只能占位。裁定:渲染器地基进 C3(`lib/icons.setupIcons`,镜像 Vue 的 `web/src/lib/icons.ts`),C4 缩为**轻量内联选择器**(搜索+网格,不发包)+ 本地 svg glob。
- **CodeBlock 引 highlight.js 对齐 Vue**(而非退成纯 `<pre>`)。
- **IconPicker 做轻量内联版**,非纯文本框、也非重虚拟化完整移植。

**C3a `98968b9`**(图标基建 + AppIcon + TenonLogo):
- `lib/icons.ts`:`setupIcons()` 用 `addCollection` 异步注册 4 个离线集(懒 chunk,非阻塞);main.tsx 首屏调一次。本地 svg + 选择器网格留 C4。
- `AppIcon`:`<Icon>` 薄封装 + `iconName(icon, fallback)` 纯逻辑(`|| fallback`,空串也兜)。`fallback` prop 让菜单目录用 `ph:folder-duotone`、叶子 `ph:dot-outline-duotone`(对齐 Vue `renderIcon`)。
- `TenonLogo`:内联品牌 SVG,`logoColors(dark)` 纯逻辑。
- 接线:`useLayoutMenu.iconFor`(`.ts` 文件,用 `createElement` 非 JSX)+ 模块选择页占位 → AppIcon 真渲染。
- **变异 6/6 全致死**,预测逐条吻合(M-AI3 恒兜底连带红 AppIcon 冒烟,如预测)。
- **顺带修** module 页 B7 遗留的废弃 `Tag bordered={false}`(v6;`antd lint` 扫全部改动文件逮到)——`git grep` 证全库仅此一处,DictTag 那处 C1 已修。

**C3b `dc01107`**(DetailPage + CodeBlock + PasswordStrength):
- `DetailPage`:Vue `@back`→`onBack` 回调、`#actions` 插槽→`actions` prop、`n-spin`→antd `Spin`。
- `CodeBlock`:highlight.js core + json,`highlightCode`/`escapeHtml` 纯逻辑;**未注册语言降级走 `escapeHtml`——因经 `dangerouslySetInnerHTML`,转义是安全边界(防 XSS)**;配色 `styles/code.css` 明暗两版(不引 hljs 自带主题);`navigator.clipboard` 复制。
- `PasswordStrength`:`computeChecks`/`computeStrength`/`buildRules` 纯逻辑逐字对齐 Vue,effect 拉 `configApi.passwordPolicy` + 默认策略兜底;勾选图标用 C3a 的 AppIcon(`ph:check-circle-fill`/`ph:circle`)。
- **变异 16/16 全致死**(DP 3 / CB 5 / PS 8)。一处预测≠实测:
  - **M-PS6 多杀一条**(预测 1、实测 2):`p.length >= min`→`>` 不光红「边界 len==min」,连带红「达标但短→2」——后者密码 `Abc12345` 恰 len8==min,边界 bug 把它 minLength 翻假 → 强度掉 1 档。变紧非漏网。

**判据全绿**:C3a/C3b 各自 `tsc` 0、`oxlint` 0、`antd lint` 0 deprecated;全量 `vitest` 344/344(320→326→344)、`vite build` OK。

**取舍/如实记**:①`@iconify/react` 对未注册前缀的图标会打在线 API——但选择器只提供已注册的 4 集合 + 本地 svg,配出来的图标必离线可解,唯手敲未知串才触网(与 Vue 同款特性,记之,非本轮堵)。②图标集合 build 后是大 chunk(4 个 `icons-*.js`,gzip 合计 ~1.2MB),`setupIcons` 首屏并发拉全部 4 个以保任意前缀离线可渲染;首屏非阻塞。按前缀懒加载需自定义 @iconify provider,YAGNI 到带宽有抱怨再说。③`web-react/COMPONENTS.md` 仍留 E4。

**下一条**:开 review lane 审整个 C3(C3a + C3b),base `dev`、隔离 worktree;之后进 C4。

### 2026-07-20 · C2 review 处置(review-c2 lane:REQUEST-CHANGES → 1 HIGH 已修)

opus 独立 lane 审 C2a+C2b(隔离 worktree、junction 复用 node_modules、实验后还原);我另核 `git diff` 只含本次修复、无泄漏,并移除遗留 worktree。lane 实测:vitest 30/30、`antd lint` 四文件全净、`tsc` 净;并逐一坐实竞态守卫非空转(拆掉即翻结果)、fetch-in-ref 无多余/漏加载、orgId→reload 时序无陈旧闭包、v6 showSearch 对象/fieldNames cast 正确、纯逻辑全对、`open(ids)→confirm` 全链路真。

- **[HIGH 已修 `49d672b`] UserPicker 中间面板搜索静默失效 —— 且是我自己没抓到的绿谎** —— account 列可搜(未设 `search:false`),antd ProTable 按 `dataIndex` 发 `q.account`,而 `fetchUsers` 只读 `q.name` → 输入账号搜索 → 参数被丢 → 返回**全量未过滤**列表(比缺搜索更糟,用户以为"搜到了全部")。**10/10 变异门没抓到,因为 fetcher 测试手喂了 `{ name: '张' }` —— 一个 UI 根本产生不了的 key(name 列 search:false),把错误契约钉死了**。改 `fetchUsers` 读 `q.account`(对齐可见的 account 搜索框 + Vue 原版),spec 改喂 `{ account }`;**变异复核**:把 fetcher 改回读 name → 该用例立刻转红(证明新测试真抓得住)。教训:**mock 掉 ProTable 后,喂给 fetcher 的搜索 key 必须是"可搜列的 dataIndex 真会发出的那个",否则测试测的是一条 UI 不可达的死分支**——sibling user 页两列都可搜、读 `q.account`+`q.name`,是这条的反证。
- **[LOW 未采纳] reopen 后可能双取**(orgId 7→undefined 过渡 + ProTable 自身初取,各一次;同 orgId=undefined,纯 perf nit)——不阻断,真在网络面板看到再修(YAGNI)。
- **[LOW 未采纳] `orgTreeData(flat, 0)` 会过度剪枝**——但 `excludeSubtreeOf` 是编辑机构 id(雪花,永不 0),不可达;防御性备注,不改。

**下一条 C3**(按台账顺序):进批次 C 剩余展示组件/页。

### 2026-07-20 · C2b UserPicker(选择器族下半,C2 完结)

`9798216`。三面板弹窗:左机构树 / 中可选用户 DataTable / 右已选列表。命令式 `open(ids)` 经 `forwardRef`+`useImperativeHandle` 暴露,`onConfirm(ids)` 回调;固定 modal 形态。

**判据全绿**:`tsc` 0、`oxlint` 0、`antd lint` 0 deprecated、`vitest` 314/314(+13)、`vite build` OK。

**变异 10 处(8 纯逻辑 + 2 接线),全部致死**(预测 `$JOB/tmp/c2b-mutation-predictions.md`)。一处预测≠实测:
- **M-UP8 少算**(预测 1、实测 2):去掉 `hydrateSelected` 的占位兜底(`map.get(id) ?? {占位}` → `map.get(id)`),不光纯逻辑测红,`open([2,9])→confirm` 组件测也红(selected 里混进 undefined,confirm 出 `[2, undefined]`)——变紧不是漏网。

**设计/取舍**:①纯逻辑(orgIdFromKey/addUsers/removeUser/filterSelected/hydrateSelected)与组件同文件导出(避 Windows 大小写文件名冲突 `UserPicker.tsx` vs `userPicker.ts`;与 C1 `resolveDictTag` 同款),spec 一文件测纯逻辑 + 接线;②组件测 mock 掉 `@/components/DataTable` 绕 pro-components vitest 墙,命令式 `open` 经 ref 驱动,`open(ids)→confirm` 走全链路验 hydrate;③`orgIdFromKey` 把 Vue 版 `key && key !== 0` 的冗余(`key` 为 0 已 falsy)简化成 `key || undefined`,并注明**别写 `?? undefined`**(会把「全部」根 key 0 当有效部门下传)——变异 `||`→`??` 正好钉住这条。④**有意不做批量勾选**:依赖 DataTable 尚未有的 rowSelection + 当前页数据,如实记入代码注释与 commit,待批量删除页需要时统一给 DataTable 加。

**下一条**:开 review lane 审整个 C2(C2a + C2b),base dev、隔离 worktree;之后进 C3。

### 2026-07-20 · C2a 选择器族上半(ApiSelect / UserSelect / OrgTreeSelect)

`71e0bc5`。C2 拆两轮:本轮三个 select 组件,`UserPicker`(三面板弹窗,依赖 DataTable)留 C2b。C2 复选框待 C2b + review 后再勾。

**判据全绿**:`tsc` 0、`oxlint` 0、`antd lint` 三文件全 0 deprecated、`vitest` 301/301(+17)、`vite build` OK。

**变异 13 处,全部致死,无存活**(预测 `$JOB/tmp/c2a-mutation-predictions.md`)。一处预测≠实测:
- **M-AS4 少算**(预测 1、实测 3):把 `if (immediate) void load('')` 改成"总是加载",连带红了**所有** `immediate:false` 用例(防抖、非 remote 两条也用 immediate:false 起手)——变紧不是漏网。

**设计**:①`ApiSelect` 把 load/竞态/防抖/immediate 逻辑抽成 `useRemoteOptions` hook,renderHook 直接测——竞态守卫用"两次 load 交错落地、旧的晚到不覆盖新的"确定性钉死,不趟 antd Select DOM;②`fetch` 存 ref:父常传内联 fetch(每渲染新引用),进 effect 依赖会让 immediate 反复触发,ref 化只读最新;③reloadOn 值不进 load、只作 effect 触发依赖。

**antd v6 坑(这次上来就 lint 全部文件,C1 教训生效,当场逮住)**:Select 的 `filterOption`/`optionFilterProp`/`onSearch` 顶层属性 v6 废弃,收进 `showSearch` **对象**(`showSearch={{ filterOption, optionFilterProp, onSearch }}`);TreeSelect `treeNodeFilterProp` 同废——OrgTreeSelect 本就没默认开搜索(对齐 Vue),索性删掉该 prop,消费者要搜自己经 rest 传 v6 的 `showSearch` 对象。

**下一条 C2b**:`UserPicker`(机构树 + 可选用户 DataTable + 已选列表 三面板,`open(ids)` 命令式;spec 需 mock DataTable 绕 pro-components vitest 墙)。C2b 落地后开 review lane 审整个 C2。

### 2026-07-20 · C1 review 处置(review-c1 lane:APPROVE + 1 LOW)

**APPROVE,0 CRIT/HIGH/MED,1 LOW。**opus 独立 lane 在隔离 worktree 里评审 + 实测(检出 c727934、junction 复用 primary node_modules、实验后还原);我另核 `git diff` 只含本次修复、无泄漏,并移除遗留 worktree(`git worktree remove`,B11 那次泄漏教训的复查)。lane 跑过的对抗性验证:①`antd info --detail` 逐条坐实三处 v6 替换名正确且语义等价(`size` 收 number、`mask.closable`、`destroyOnHidden`);②变异掉 `toDictOptions` 的 enabled 过滤 → DictSelect render 测试真转红(非空转);③隔离复现 `setFieldsValue` 先于开弹 + `destroyOnHidden` 时序 → add/edit 回显正常,B12 延期成立;④`extraRef` 透传护栏重构后存活;⑤`shouldCloseAfterConfirm` 六路 + loading 四条关闭途径逐一核对无误。

- **[LOW 已修 `70071ff`] DictTag 用了废弃的 Tag `bordered`** —— v6.5.1 `bordered` 废弃、`variant="filled"`(默认)即无边框,`bordered={false}` 是只打告警的 no-op。根因是**流程漏**:清 v6 废弃时我只 `antd lint` 了 FormContainer 一个文件,漏扫 DictTag。已删,并**补扫全部 5 个 C1 文件 → 零 deprecated**。教训:`antd lint` 要扫本轮**所有**改动文件,别只扫"我觉得有风险的那个"。
- **[LOW 未采纳] `handleConfirm`/`onSwitch` 无 `if (loading) return` 前置闸** —— lane 自评低置信、实测经真实 DOM 事件**不可达**(两次 click 是独立 tick,React 事件间会刷新 disabled),且与 B11 同款。加一道不可达的冗余闸属 YAGNI + 会引入无测试覆盖的逻辑,**有意不加**;真观察到双提交再补。

### 2026-07-20 · C1 字典三件套 + 表单容器(共享组件层第一批）

四个组件 + 各自 spec，纯逻辑抽出变异钉死。`804b3db`（组件）+ `b826df7`（user 页退成共享组件）。

**判据（四件套 + antd lint 全绿）**:`tsc` 0、`oxlint` 0、`vitest` 284/284（+32：DictSelect 4 / DictTag 9 / FormContainer 11 / StatusSwitch 8）、`vite build` OK、`antd lint` 0 deprecated。

**变异 17 处，全部致死**（预测先写在 `$JOB/tmp/c1-mutation-predictions.md`）。三条预测≠实测,按价值记:
- **M-SS5 存活（真 green-lie，最值钱的一条）**:`if (successMsg !== false)`→`if (true)` 后走 `message.success(false ?? …)`=`message.success(false)`,弹的是**空内容** toast;原断言只查 `queryByText('操作成功')` 为 null——空 toast 里当然没这四个字,断言照过。根因是**断言选错观测量**(查特定文案 ≠ 查"弹没弹")。改数 `.ant-message-notice` 节点=0;复跑 clean 8 绿、施 M-SS5 转红。教训:`x ?? default` 传 `false` 不兜底(`??` 只兜 null/undefined),"不弹 toast" 一律断 notice 节点数,别断某句文案。
- **M-DT4 / M-FC1 少算**（预测 2/3、实测 3/5):都是漏了"组件 render/behavior 用例也压着同一分支"——M-DT4 的 miss→原始值 也被 DictTag render smoke 覆盖;M-FC1 破坏"成功即关"连带红了 `确认成功` 与 `提交中`(两者都 await 那次关闭)。方向是变**紧**不是漏网,记之。

**antd v6 改名坑（`tsc` 一个不红,靠 `antd lint` 逮）**:`Modal/Drawer maskClosable`→`mask={{ closable }}`、`Drawer width`→`size`(v6 的 `size` 收 number,非只预设)、`Drawer destroyOnClose`→`destroyOnHidden`(5.25.0 起,与 Modal 一致)。先按记忆写了旧名,`antd info --detail` 逐个核过替换名才改——CLI 是这批坑唯一的探照灯。

**发现/取舍**:①FormContainer 的关窗判定抽成 `shouldCloseAfterConfirm`(`!== false` + catch→false),`onConfirm` 返 false 或抛错留窗、其余关——save/doReset 因此改成 API 失败 `return false`(B11 里是手写 setOpen);②happy-dom 不跑 CSS 过渡,antd 关闭动画 transitionend 永不触发、弹层子节点不卸载——断"关没关"改断 `onOpenChange(false)` 回调,不断 DOM 消失(useConfirm.spec 同理断 promise 结果);③DictSelect 省了 Vue 版的 loading 圈(`useDictOptions` 不暴露在途态,字典小且缓存命中,ponytail)。

**推迟(如实记,别假装做了)**:`web-react/COMPONENTS.md` 到 E4(现靠文件头注自述);FormContainer `destroyOnHidden` + 页里 `setFieldsValue` 先于开弹的时序(与 B11 同款,潜在 antd 未挂载告警)留 B12 实点。

**下一条 C2**:选择器族(`ApiSelect` 远程分页+防抖+竞态守卫、`UserSelect`、`OrgTreeSelect` 扁平→树含子树排除、`UserPicker` 弹窗)。开工前先另起 review lane 审 C1。

### 2026-07-20 · B11 review 处置(review-b11 lane:APPROVE + 3 LOW)

**APPROVE,0 CRIT/HIGH/MED。**lane 没只读走过场,做了独立验证:①**独立复现两处变异**(`password || undefined`→`|| null` 红在 spec:17;`toUpdateInput` orgId→null 红在 spec:41),坐实这两条判据有真判别力、非回声;②**对着真 `.d.ts` 核 API**(pro-components 3.1.14-2):`ProColumns.search?: boolean` 确认默认列都进搜索表单、`search:false` 才排除(我的假设成立),`hideInSetting`/`columnStateType`/`destroyOnHidden` 全对,`sorter:true`(布尔非比较器)= 服务端排序;③**权限码与后端路由 + Vue 版逐一对平**(`{METHOD}:/{小写路由}`,UserController 四条全对);④独立追 `openEdit→save`,确认 `extraRef` 后展开压过默认与表单值,**数据丢失守卫在任何 validateFields 行为下都稳**;⑤确认 `Record<string,any>` 放宽不破 `DataTable.spec`(unknown 可赋给 any)。

**3 LOW 处置:**
- **[LOW-1 已修] `index.spec` 未桩 `dictApi.items`** → mount 触发 `useDictOptions('gender')` 真发请求打 `ECONNREFUSED`(被 `.catch` 吞、测试仍绿,但噪音盖真错、且 vitest 收紧未处理错误就会红)。补 `vi.spyOn(dictApi,'items').mockResolvedValue([])`,与另两处桩一致;实测噪音消失。
- **[LOW-2 不改代码、加注防呆] `fetchUsers` 未 memo**:lane 独立确认无害(pro-table 经 `useRefFunction` 读 request,父重渲染不重取);反倒 memo 配错依赖才是真 footgun。加一行注释钉住"有意不 memo,别顺手优化"。
- **[LOW-3 信息项] 原型建号无 org/职位/头像**:`extraRef` 从 `blankForm()` 种(全 null),是批次 C 的编辑器缺席所致,非数据丢失(新记录)。已在码注写明。

**[开放问题仍开]** lane 确认**浏览器外无廉价路径**验真 ProTable 运行时(vitest externalize 卡 antd 无扩展名 deep-import;`@vitest/browser`/Playwright 都要浏览器二进制,本机没有)。mock 测的是**接缝**(DataTable prop 接线 + fetcher 映射),ProTable→localStorage 写是上游职责。**B10 开放问题维持指派 B12 手动 + E2 冒烟,判定为合理而非缺口。**

**[进程教训,记下防复发] review lane 的 `isolation:worktree` 这次没真隔离** —— lane 把两处变异**打在主 checkout**(`web-react/` 本体)再撤,不是它自己的 worktree。这正是 B5 zzflake 污染那类风险。**本次已独立核 `git diff HEAD` 为空、无残留、无游离 worktree,无损**;但今后发 review lane 必须要么在 prompt 里**钉死"只在你自己的 worktree 里改,绝不碰主 checkout"**,要么收到 APPROVE 后**先验主树干净再信结论**(本轮已这么做)。

四件套复跑:tsc 0 / vitest scoped 16/16(噪音清)。full suite 复核中。

### 2026-07-20 · B11 system/user 标准列表原型(`8eea775`)

第一条真 CRUD 页,踩着真 ProTable(经 `<DataTable>`)。写侧用 antd 原生 `Modal`+`Form`;机构树筛选/头像上传/字典选择器/`useBatchDelete` 属批次 C,原型有意不带(各处 ponytail 注)。真 API 用 `@ant-design/pro-components` 3.1.14-2 + antd 6.5.1 + React 19.2。

**对着真 `.d.ts` 核过的 pro-components 3.x(antd v6 线)与经典版差异,别凭记忆:**
- 列排除搜索表单是 **`search: false`**,这版**没有 `hideInSearch`**(经典版有)。默认每列都进搜索表单 → 非搜索列一律 `search: false`。
- 行选择是 ProTable 的 `rowSelection` **prop**,不是 Naive 那种 `type:'selection'` 列 —— 当前 `<DataTable>` 没暴露它,故**批量删除连同 `rowSelection` 一起推到批次 C**,原型只做行内单删(单删已覆盖"删")。
- `request` 签名 `(params & {current,pageSize}, sort, filter) => Promise<Partial<RequestData>>`;`columnsState` 键就是 `{persistenceKey, persistenceType}`;`hideInSetting`/`hideInTable`/`valueEnum`/`sorter`/`fixed` 都在。
- Modal 的 `destroyOnClose` 在 v6 已更名 **`destroyOnHidden`**(两者都在,用新名)。

**纯逻辑抽 `userForm.ts`(空值语义 + 超管自锁判据),`userForm.spec` + `index.spec` 变异钉死。6 处点变异打不相交断言,预测先写进 `$CLAUDE_JOB_DIR/tmp/b11-mutation-predictions.md` 再跑,预测 6 红/10 绿,实测逐条命中:**

| 变异 | 预测 | 实测 |
|---|---|---|
| M1 toAddInput `password \|\| undefined`→`\|\| null` | 红「password 留空→undefined 非 null」 | **红** |
| M2 toUpdateInput orgId 透传→null | 红「orgId… 原样回送」 | **红** |
| M3 canDelete 去超管守卫 | 红「canDelete 超管一律 false」 | **红** |
| M4 detailToForm `password:''`→`'x'` | 红「password 永远空串」 | **红** |
| M5 fetchUsers `page:q.page`→`q.pageSize` | 红 index 用例1(page 期望2得20) | **红** |
| M6 fetchUsers account 守卫去掉 | 红 index 用例2(account 期望 undefined 得 123) | **红** |

预测保绿的 10 条全绿,无预测不符。**这轮无"预测与实测不符"。**

**发现一条真数据丢失陷阱(已防):** `UpdateUserInput` 是**全量替换**,编辑弹窗没有 org/职位/头像控件,若不把 `orgId/positionId/directorId/avatar` 从 detail 原样带回,save 就把这些字段**置空** —— 即"编辑一下用户名就清了他的机构/头像"。用 `extraRef` 暂存透传字段、save 合回,并在 `userForm.spec` 单钉(M2)。

**一处 B10 文件的必要放宽(第一个真消费者逼出来的):** `<DataTable>` 泛型约束 `Record<string, unknown>` **拒收业务实体 interface**(`UserItem` 没有隐式索引签名)。改 `Record<string, any>`(ProTable 自身也是 `<T = any>`),`unknown` 那版严格但对**生成的实体类型**根本不可用。四件套复跑全绿。

**B10 开放问题的处置(不假称结掉):** request 挂载即调 / 列持久化到 `localStorage['protable:sys-user']` / 工具栏 locale 跟 ConfigProvider —— 这三条是**活浏览器**行为(要登录 + 后端 + 真 DOM)。`vite build`(rollup 6050 模块含真 ProTable 全量转译)证明**编译打包层无问题、pro-components 的坑确系 vitest 独有**;但运行时三条**本 SSH 会话无浏览器可驱动**,按"禁止假完成"如实**仍记开放**,重指派到 **B12 手动验收 + E2 Playwright 冒烟**(那才是台账给"对真组件验列持久化 + 中英切换"划的家)。页面 spec 的 vitest 债照 B10 预案解决:**mock `@/components/DataTable`**(比 mock pro-components 更干净,把坑挡在 wrapper 边界外),核心逻辑走 `userForm.spec` 纯函数变异网。

四件套:tsc 0 / oxlint 0 / vitest 252 通过(新增 16,含 6 变异钉) / vite build 绿。

下一条:**B12 阶段一验收**(手动对照 Vue 版 10 链路,含 B11 遗留的浏览器验收:user 增删改查 / 搜索排序分页 / 列设置持久化 / 中英切换)。**这条本就是人工步骤**,由维护者在 RDP/本机实点;E2 落地后其中可自动化的部分(列持久化、locale)转 CI 冒烟。

### 2026-07-20 · B10 review 处置(review-b10 lane 隔离:APPROVE + 5 LOW)

lane 在干净环境**独立确认了 pro-components/vitest 债是真的**:它试了**四个**配置杠杆(默认 externalize / `server.deps.inline` / `deps.optimizer.ssr` / `server.deps.fallbackCJS`)**全失败** —— `.ssr`(我没试过的那个)还撞更深的墙(`@ant-design/icons` no known conditions for "." specifier)。**mock 不是漏了简单解法,是确实无解**;止损决定得到外部背书。lane 还对着真 `.d.ts` 核了所有 prop 名(`persistenceKey`/`ActionType.reload`/request 签名/`current`)全对,并指出 **DataTable.tsx 导真 ProTable → tsc 独立守 prop 名**,mock 不构成盲区。9 变异独立全 kill,M8 的 `{字段:null}` 判别力独立复现。

**5 LOW,处置 3:**
- **[LOW-3 已改] `...filters` 前置**:分页/排序是算出来的、必须权威;搜索表单万一有字段叫 `page` 就不会覆盖(16 页都没有,消除未来 footgun)。
- **[LOW-1/2 已注释] 列级 filter 不支持 + 多列排序只取首列**:有意的天花板(筛选走搜索表单;后端 OrderBySafe 单字段),注释写清免得后人以为是遗漏。
- **[LOW-4 defer] `rowKey` 窄于 ProTable 的 `string|GetRowKey`**:YAGNI,等有页需要函数键再放宽。
- **[LOW-5 out-of-scope] 927kB 单 chunk**:antd+pro 既有体积,非 B10 引入。

**[开放问题] 真 ProTable 运行时未验(mock 掩盖):** request 挂载即调、列持久化到 `localStorage['protable:{key}']`、工具栏 locale 跟 ConfigProvider —— 类型 + build 使其"很可能对",但**真正未证实**。这是披露的债,**B11 的 dev 实点 + E2 的 CI 冒烟必须对真组件验列持久化 + 中英切换**(wrong-at-runtime-but-right-at-type 的 bug 只会在那露头)。

四件套仍绿(toProTable 8/8,tc/lint 0)。

下一条:**B11 system/user 页** —— 落地时**对真 ProTable 验**上面那三条运行时行为(dev 实点),把 B10 的开放问题结掉。

### 2026-07-20 · B10 DataTable 薄封装

`eb3de2f`。两半:`toProTable` 纯适配(可测)+ `<DataTable>` 组件(隔离 pro-components)。

**`toProTable`**:`(params)=>{items,total}` fetcher → ProTable 的 `{data,success,total}` request。三处映射:current→page、`{字段:'ascend'|'descend'}`→`{sortField,sortOrder:'asc'|'desc'}`(后端 `OrderBySafe` 只认 "desc",但仍映成 'asc' 免得后端将来严格校验时"排序不生效")、搜索表单字段透传。**9 变异全 kill,一处缺口(同 B6a 形状)**:M8(sortField 无 order 也给)最初**绿**——空 sort `{}` 时字段名本就 undefined,`order ? field` 那道守卫被短路。补一条**清空排序 `{字段:null}`**(antd 清排序的真实形状)才隔离它,重跑红。

**`<DataTable>`**:薄封装 ProTable,16 个 CRUD 页只依赖它、不碰 ProTable API;columnsState 持久化到 `protable:{key}`;reload 用 forwardRef 暴露干净句柄(不外泄 pro-components 的 `ActionType`)。

**proTable UI 键:不加。** 台账点名"此时才定"——antd ProTable 的工具栏文案(列设置/密度/刷新)由 pro-components 自带 intl 提供、跟 `ConfigProvider locale` 自动切,不需要新键。Vue 侧那 8 个是它 Naive ProTable 的自留文案,用不上。

**一处环境技术债(如实记):`@ant-design/pro-components` 在 vitest 里导不进来。** 它传递依赖的 `antd/es/locale/zh_CN.js` 用了**无扩展名 deep import**(`import '../calendar/locale/zh_CN'`),vitest 的 SSR-externalize 解析器不补 `.js` → `Cannot find module`。试过 `server.deps.inline`、`deps.optimizer.web.include`(含 antd)、清 `.vite` 缓存 —— **都没解决**。**build(rollup)与 dev(Vite)正常,本条 build=0 即证明生产可用**,只有 vitest 的 externalize 路径缺这一步。打了 5 轮配置无果,**止损**:DataTable.spec **mock 掉 ProTable**,测 wrapper 真正的职责(prop 接线:request 用 toProTable、columnsState 键、rowKey、reload 转发、toolbar),真 ProTable 渲染/本地化留 **B11(dev 实点)+ E2(CI 冒烟)**验;核心的 toProTable 已 9 变异钉死。**B11 的页面 spec 会撞同一个坑,记为债,B11 时正式解**(要么 mock 模式沉淀成 helper、要么深挖 vitest 配置)。

**顺带一个 React 19 版本差异**:`useRef<ActionType>()` 要初值 → `useRef<ActionType|undefined>(undefined)`,typecheck 兜住。

四件套:`lint=0` / `typecheck=0` / `vitest=0`(236 passed / 29 files) / `build=0`。

下一条:**B11 system/user 页**(标准列表原型:搜索+工具栏+表格+分页+服务端排序+列设置;增删改查全通、列设置刷新保留)。**先解 B10 记的 pro-components vitest 债**。

### 2026-07-20 · B9 review 处置(review-b9 lane 隔离:REQUEST CHANGES → 1 HIGH 已修)

`f8a1f1b`。**这轮是评审 + 变异纪律最实的一次兑现:review 抓到一个我引入的真回归,而我自己的绿测试放过了它。**

**[HIGH 已修] `confirm` 执行期间取消/Esc 未拦 → 动作成功但外层拿到 false(Vue 原版的回归)。** 我声称"antd 的 busy 守卫内置故不必手写",**只对一半**:onOk 返 Promise 时 antd 只锁 OK 钮(loading)+ mask,**取消钮与 Esc 不锁**。后果:用户点确定删除、请求在途时点取消/按 Esc → onCancel → `resolve(false)` → 调用方跳过成功分支(表格不刷新),而 action 后台照样成功 + 迟到的成功 toast —— 正是 Vue 版 busy 守卫点名要防的"已删成功但表格没刷新",而我把那层整段删了。**先独立复现**(取消/Esc 挂起期各一条测试,当前红:`expected false to be undefined`),再修:onOk 启动即 `instance.update` 禁用取消钮 + 关 keyboard,从根上让 onCancel 不再触发。

**[MEDIUM 已修] 我那条 busy 守卫测试无判别力(空测试)。** 它断"body 含『删?』+ 二次不触发",**区分不出「锁住未关」与「正在关(离场动画)」**——`void run().then(resolve)` 变异(去掉返回 Promise、antd 不锁关)照样 7/7 绿。唯一能判别的观测量是 **OK 钮的 loading 类**,我没断。已补断 loading 类 + 取消/Esc 挂起路径。**这正是我一路记的"跑绿的用例什么都不证明"—— 我写了条空测试当"比 Vue 短"的唯一依据,是 review 的独立变异把它戳穿的。**

**修复途中又踩 + 纠正一次冗余闸(B5a 教训重演):** 第一版修复同时加了 `instance.update` 和 onCancel 里的 `started` 逻辑门——两道冗余,**去掉任一单独都不红**(update 禁按钮 / started 挡 resolve,各自兜底),单点变异测不出;且 `started` 返回 undefined 会让 antd 在取消时关掉弹窗而 action 仍在途。**收敛成单一机制**(只留 `instance.update`):去掉它现在取消/Esc 两条都红,可被钉住。

三个机制(OK loading / 取消守卫 / Esc 守卫)现各有变异钉死。台账更正:上条"busy 守卫内置故不必手写"**不实**——antd 只兜一半,"短"来自删掉了仍在干活的守卫。

lane 另三条 LOW(Can 空数组无测试、ask 的 Esc 路径无测试、台账未勾)均属实,前两条是覆盖缺口不阻断,后一条此条即补。

四件套:`lint=0` / `typecheck=0` / `vitest=0`(222 passed / 27 files,+2 取消/Esc) / `build=0`。

下一条:**B10 `<DataTable>` 薄封装**(隔离 pro-components beta、toProTable 适配 toPage 契约、排序映射后端、columnsState 持久化、proTable UI 键此时定)。

### 2026-07-20 · B9 权限门 + 确认/消息基建

`63e071f`。三件里实际两个新实体:`<Can>` + `useConfirm`;message "承接"无需新建 —— antd 内置、已在 LoginPage/ModuleChooser/LayoutShell 用 `App.useApp().message`。

**`<Can code>`** 替 Vue 的 `v-auth` 指令(React 无指令,用组件包裹)。判定(超管 fail-open / 未加载 fail-closed / 精确命中)**收敛在 `auth.hasPerm`**(B3 已变异钉死),Can 只接规则 + 数组 OR/`every` AND,不重写——与 v-auth 同一个 hasPerm,两模板判定一致。8 用例(命中/不命中/超管/未加载/OR/AND 各态)。

**`useConfirm`** 三 API(ask/confirm/run)搬到 `App.useApp().modal + message`。**比 Vue 93 行短**:Naive 要手写 busy 守卫防连点/执行中关窗,antd 的 `modal.confirm` onOk 返 Promise 时**自动 loading OK 钮 + 期间锁关闭**,守卫内置。补一条测试钉住这条依赖(挂起的 onOk 期间弹窗不关、action 只跑一次)——它验的是 **antd 契约**(我据此不写守卫),antd 改行为这条会红。run 吞异常并 toast,故 onOk 必 resolve、弹窗必关;失败经 translateError toast 且 resolve(false)。7 用例。

**两个 course-correct:**
1. **antd 对两个中文字的按钮自动插空格**(autoInsertSpace),可及名是「确 定」不是「确定」→ `getByRole('button',{name:'确定'})` 找不到。用容忍空格的正则 `/确\s*定/`。**又一个「库渲染得和字面量不一样」** —— 与 StrictMode / i18n 未初始化 / 键盘→click 同族:断言要对准**环境/库真正产出的东西**,不是我以为的字面量。
2. **`modal.confirm` 返回 `{destroy,update}&thenable`,不是真 Promise** —— `as Promise<boolean>` 抹类型被 typecheck 兜住。`ask` 改用 onOk/onCancel 显式 resolve,类型天然对,不 cast。

四件套:`lint=0` / `typecheck=0` / `vitest=0`(220 passed / 27 files) / `build=0`。

下一条:**B10 `<DataTable>` 薄封装**(隔离 pro-components beta、`toProTable` 适配 `toPage` 契约、排序映射后端、`columnsState` 持久化、labels i18n 驱动)。**proTable 那批 UI 键此时才定**(antd ProTable 自带 locale,要不要加、加什么在这条决定,别提前猜)。

### 2026-07-20 · B8 review 处置(review-b8 lane 隔离 worktree:APPROVE + 5 LOW)

`183bec7`。lane 在隔离 worktree 里对 `menuItems`/`useLayoutMenu` 逐条打变异(8+3 全 kill 独立复现)、写 4 个 scratch spec、四件套真跑。**无 CRITICAL/HIGH/MEDIUM,5 LOW,全部处置。**

- **[LOW-3 已修,最实的一条] `data-gray` effect 零测试覆盖 + `useAntdTheme` 注释过时。** 删探针那批里,`data-gray` 是**唯一新搬入、却没判据钉**的行为——我当时笼统说"探针检查已被 theme/i18n/app spec 覆盖"就把它漏了。**教训:删旧代码时若同时搬入新行为,那句"已覆盖"不成立,新行为要单独补判据。** 抽成 `theme/useDocumentGrayscale.ts`(hook,可 renderHook 测)+ 2 条 DOM 断言(切灰阶翻 `data-gray`、初值 true 挂载即打),去掉 toggle 即红。`useAntdTheme` 那段"grayscale 还没搬"的过时注释改成指向新 hook、别重复实现。
- **[LOW-2 已修] `/module` 优先的注释归因不准。** 我写"放在最前,静态段优先",暗示数组位置起作用;实际是 **react-router 按特异性排名裁决,与顺序无关**(lane 挪到末位仍赢)。**注释里的因果归因也要能被证伪** —— 改成"react-router 按特异性排名,与数组顺序无关;写最前只是可读性"。
- **[LOW-1 已注明] `openKeysFor` 同一 path 挂多目录只展开第一处祖先。** 真实菜单不会同一路由挂两处,不改代码,注释点明。
- **[LOW-4/5 已修] `useLayoutMenu` 重复 `react` import 合并;`header-bg` 兜底值 `0.72`→对齐 token 的 `0.82`**(死分支,但值读起来误导)。

lane 自己也踩了个环境坑并讲清了:junction 主 worktree 的 node_modules 跑全量 → `antd-theme.spec` 的 `?raw` 撞 `server.fs.allow` 边界报红;物理 `npm install` 后 203/24 全绿。**这正是"环境的红"** —— 与我这一路记的 StrictMode / i18n 未初始化 / happy-dom 一次性流同族,只不过发生在 reviewer 的 harness 上。

四件套:`lint=0` / `typecheck=0` / `vitest=0`(205 passed / 25 files,+2 灰阶) / `build=0`。

下一条:**B9 权限 + 消息 + 确认基建**(`<Can code>` 替 v-auth、`App.useApp().message` 承接 Vue 侧 74 处 useMessage、`useConfirm` 用 `Modal.useModal()` 重写)。

### 2026-07-20 · B8 布局壳(拆两步)

`cb62290`(菜单派生)+ `cac1efc`(壳 + 接线 + 删探针)。**只做 vertical** —— Vue 的 7 种布局模式、mix rail、移动抽屉、水印、realtime、tabs(D1)、设置抽屉全是后续或不做,台账明确"暂不带 tabs"。

**B8-1 `menuToItems`**(`cb62290`):菜单树 → antd items,规则对齐 Vue `toOptions`(sort / 剥 Button+隐藏 / Catalog 空则丢 / 叶子 key=path / 标题含 `.` 走 i18n)。**外链叶子照常入菜单**(key=URL),点击 window.open —— 与 buildRoutes **跳过**外链相反(M8 专钉这半)。`tr`/`iconFor` 注入,纯逻辑可测。**8 变异全 kill**;`openKeysFor`(子菜单跟随路由展开)4 例。

**B8-2 壳 + 接线**(`cac1efc`):`useLayoutMenu` 接反应式(menuTree + 路由)→ selectedKeys/openKeys 跟随路由、内部导航/外链 open;`LayoutShell` = Sider(236/76 折叠)+ Header(62 blur:折叠钮/明暗/登出)+ Content(Outlet)。Protected 路由表**把动态路由嵌进壳**(菜单页在壳内,/module 选择页全屏在壳外,对齐 Vue layout-children vs top-level)。`[data-density]`/`[data-gray]` 搬进 `styles/chrome.css`,灰阶用 App 里单独 effect toggle(不塞 useAntdTheme 依赖,免得只切灰阶白重建 antd 主题);Content padding 用 `var(--pad-page)` 让密度也到手写壳。**探针页连路由 + 组件 + 那批 import 一起删干净**(检查项已被 theme/i18n/app spec 覆盖)。壳行为测试 7 条(菜单渲染/选中跟路由/内部导航 vs 外链 open/折叠/明暗/登出即使 500)。

**两个 course-correct,记下:**
1. **`app.collapse` 等文案我又凭印象**(本轮第二次栽文案上)——但这次真源恰好对上,红的真因是 **LayoutShell.spec 没 import `@/locales`**,i18n 未初始化 → `t('app.collapse')` 返回 key `'app.collapse'` 而非"折叠菜单"。菜单那 4 条没暴露是因为 TREE 标题走字面量、不经 `t()`。**用 `t()` 的行为测试必须 import `@/locales`。** 又一个「环境决定断言」样本:i18n 初没初始化决定 t() 产出 key 还是文案。
2. **antd `Menu` 的 `items` 类型**:我的 `MenuItem[]` 结构一致但 antd `ItemType` 是带 null 的递归联合,TS 不直接认 —— 精确到 `MenuProps['items']` 的结构转换,**不是 `as any` 抹类型**(注释写清)。

四件套:`lint=0` / `typecheck=0` / `vitest=0`(203 passed / 24 files) / `build=0`(无双-import 告警)。

下一条:**B9 权限 + 消息 + 确认基建**(`<Can code>` 替 v-auth、`App.useApp().message` 承接、`useConfirm` 用 `Modal.useModal()` 重写)。

### 2026-07-20 · B7 review 处置(review-b7 lane 隔离 worktree:APPROVE + 1 MEDIUM)

`325d7b6`。lane 在隔离 worktree 里独立复现了 4 变异全 kill、三处承重守卫(鼠标 stopPropagation / 登出 try-catch / glob 排除),并抓到一个**我引入的真 MEDIUM**。

**[MEDIUM 已修] 键盘按 Enter 激活"设为默认"会进应用、且设不了默认。** 卡片 `role="button"` + `onKeyDown(pick)`,内层「设为默认」按钮是它的 DOM 后代,keydown 冒泡到卡片 → 卡片 `preventDefault` 把按钮原生的 Enter→click 压掉 + 调 `pick` 把人带进应用。**我的 `stopPropagation` 只挡鼠标路径,键盘路径没等价物** —— 而 commit 里恰恰写了"stopPropagation 保证设默认不进应用",那句只对鼠标成立。**先独立复现(键盘回归测试红:`setDefault` 0 次调用)再修**:卡片 `onKeyDown` 加 `if (e.target !== e.currentTarget) return`,只认自己被聚焦时的按键。去掉守卫回归测试即红。

**一条判据教训**:回归测试最初我断"`setDefault` 被调",实测环境**观测不到** —— `fireEvent.keyDown` 不像真浏览器那样把按钮上的 Enter 翻译成原生 click(那是浏览器行为,jsdom/happy-dom 不复刻)。改断**bug 的真实症状「有没有被带进应用」**(`switchMock` 不被调),那个测得到、也正是要守住的。**"断言要挑测试环境真能观测的症状,不是理论正确但环境测不到的那个"** —— 与 B6 的 StrictMode、B5 的 happy-dom 一次性流同源:**环境决定了哪些断言有意义**。

**顺带落实 review 的 LOW**:补 `pick`/`setDefault` 失败路径测试(LOW-1,原来只测鼠标成功路径给了假信心);`pick`/`setDefault`/`logout` 共用 `busy` 门防快连点双发 + 给设默认也上 Spin(LOW-4)。**LOW-3(`<Button>` 嵌在 `role="button"` 卡片里的 ARIA 嵌套,是 MEDIUM 的结构根因)只治了行为,更彻底的重构(整卡不做 role=button)留后续 —— 标清,不假装做全。**

四件套:`lint=0` / `typecheck=0` / `vitest=0`(182 passed / 22 files,+4) / `build=0`。

**方法论累积到此(都在本台账各处)**:「跑绿/跑红都可能骗人」现已见过这些家 —— 判据没判别力(回声/自指)、分支被兜底替代不可 kill、变异根本没应用(缩进对不上)、**环境没实现失败模式**(happy-dom 一次性流)、**环境缺运行时那半**(StrictMode)、**断言挑了环境观测不到的症状**(键盘→click)。共同根:**判据的有效性取决于测试环境到底跑了什么**,写断言前先问"这个环境真会产生我要断的那个现象吗"。
### 2026-07-20 · B7 模块选择页 + switchModule/setDefault

`628d1c5`。在 B6 的 `enterInitial` 之上补齐 useModule 剩两支 + 把 /module 占位换真选择器。

**`switchModule`** = enter + (D1)清标签 + **返回**新应用首页,由调用方(选择页组件,有 router 上下文)导航——useModule 保持 router-free,阶梯与这两支都留在可单测层。**`setDefault`** = 调后端 + 本地同步 `defaultModuleId`(角标立刻转移,不重拉)。4 变异(不 enter / 返回定值 / 不调 API / 不同步本地)全 kill。

**选择页**:应用卡片网格、当前应用描边、默认角标 / 设默认动作(`stopPropagation` —— 设默认不该顺带把人带进应用,专门一条用例钉住)、卡片键盘可激活、空态登出(后端 500 也照清会话跳登录)。7 条行为测试;switchModule/setDefault 在页面里 mock 掉(它们自己已变异),只验接线。

**`views/module/**` 加进 glob 排除 + `NON_PAGE_PREFIXES`**:选择页是 Protected 里的静态路由,留在路由 glob 里会**复发** B6 LOW-2 修的静态+动态拆包冲突。已加注释"静态路由页都要排出 glob",防再犯。build 确认双-import 告警仍为零。

**两个 course-correct,记下:**
1. **`module.empty` 文案凭印象写错**(`当前无…` vs 真源 `暂无…`)——面向用户的文案不能凭记忆,要从真源核对。测试当场抓住,但本可先 Read。
2. **换掉 /module 占位后,Protected.spec 断 UnderConstruction 的 `/module` 文本那条红了**——改占位页要同步改断言它内容的测试。全量跑抓住(单跑 chooser spec 不会露)。

图标:`module.icon` 是 iconify 字符串,本模板未引 iconify,卡片统一用 `AppstoreOutlined` 占位;**图标映射留后续,不算做完**(页面注释 + 此处都标了)。

四件套:`lint=0` / `typecheck=0` / `vitest=0`(178 passed / 22 files) / `build=0`。

下一条:**B8 布局壳**(antd 原生 `Layout` 自建,不用 ProLayout):侧栏 + header + 菜单树派生 + 明暗 + 选中态跟随路由;`[data-gray]`/`[data-density]` 样式搬进 `styles/`;删探针页(连 /_probe + Probe 组件)。

### 2026-07-20 · B6 review 处置(review-b6 lane:APPROVE 无阻断,首次用 worktree 隔离)

`b21ef37`。**这次 review lane 用 `isolation: "worktree"` 跑**——它在自己的 worktree 里跑了 12 变异 + 4 probe、每处 `git diff` 确认变异真落地,全程碰不到主线树。**前两个 lane 的共享树污染问题就此根治**,这是把台账那条教训("给 lane worktree 隔离")落成实际做法。lane 独立复现坐实:M1 确实冗余、阶梯顺序被钉死、remembered/default 有效性检查互不替代、真实 glob 测试确会在 glob 失效时红、外链兜底缺口已真修。**无 CRITICAL/HIGH/MEDIUM,5 条 LOW。**

- **[LOW-1 已修] StrictMode 下 `enterInitial` 双调。** `main.tsx` 用 `<StrictMode>`,守卫 effect mount→unmount→remount,`booted` 在 remount 仍 false → modules/permissions/profile/menu 各拉两次(仅 dev,幂等无损)。**但 reviewer 点出:`Protected.spec` 的 `toHaveBeenCalledOnce` 只因 harness 没套 StrictMode 才成立,不代表运行时。** 加在途去重(同 dict/site store 模式),让 dev 与 prod 一致;补一条**真套 StrictMode** 的渲染测试,去掉去重它红(2 次≠1)。**这条是"绿得没判别力"的另一面:断言本身对,但它绿是因为测试环境缺了运行时的那一半。**
- **[LOW-2 已修] iframe/login 永久无法 code-split。** `embed/iframe`(buildRoutes 静态 import)与 `login/LoginPage`(App 静态路由)同时被 glob 动态匹配 → 静态+动态双 import → 永远不拆(build 三条告警的来源)。glob 负模式加排 `login/**`、`embed/**`、`_placeholder/**`。修后 **build 那三条双-import 告警消失**。(占位页仍 1 chunk 是小 re-export 被内联,B11 真页有内容自会拆。)
- **[LOW-3 已修] `viewComponentPaths` 下拉暴露内部组件。** `embed/iframe`(选它渲染无 src 空 iframe)、`_placeholder/UnderConstruction`(占位页)本不该做菜单落点。`viewKeysFrom` 过滤前缀从只 `login/` 扩到 `login/`+`embed/`+`_placeholder/`,与 glob 排除对齐;更新其用例断言内部组件被挡在下拉外。
- **[LOW-4 接受,记下] iframe 行为测试在 happy-dom 里触发真实 fetch。** 渲染真 `<iframe src="https://...">`,happy-dom 会去 fetch → 未处理拒绝噪声。测试只断 `src` 属性、仍绿且判别力不受影响。不改:iframe 分支只对 http(s) URL 触发(`isHttpUrl`),换 `data:` 就不走那条分支了,为消噪声反而弱化判据不划算。噪声无害。
- **[LOW-5 已改台账] `.spec` 排除可测性,我原记悲观。** 见上方 B6b-1 条的更正。

四件套:`lint=0` / `typecheck=0` / `vitest=0`(167 passed / 21 files,+1 是 StrictMode 那条) / `build=0`(双-import 告警清零)。

**工程结论进台账**:review lane 一律走 `isolation: "worktree"`。共享树的三桩事故(zzflake 注入、未复原的变异、我撞 mid-toggle 的假复现)全部源于共享工作树,隔离后一次没再发生。

### 2026-07-20 · B6b-2 守卫 + 门户接线(B6 完成)
### 2026-07-20 · B6b-2 守卫 + 门户接线(B6 完成)

`5af2763`。B6 有决策风险的另一半:`useModule.enterInitial` 阶梯 + `<Protected>` 三守卫 + 接进 App。

**`enterInitial` 阶梯逐字对齐 Vue**:空→chooser / 记住且有效→enter / 单个→enter / 默认且有效→enter / 否则 chooser;权限码 fail-closed、超管 fail-safe、头像回填。React 关键差异:`enter` 只把 `menuTree` 写 store,路由经 `useRoutes` 派生,**不需要 Vue 的 `return to.fullPath` 重解析**。chooser 态**不需要单独分支**:未 enter → menuTree 空 → 动态路由空 + homePath 回落 /module。

**`<Protected>` 三守卫按 Vue 顺序**:未登录→/login、强制改密→锁死改密页(在门户重建**之前**)、routesReady 未就绪→跑一次 enterInitial 转圈。本地 `booted` 标志只跑一次(routesReady 在 chooser 态永远 false,拿它做重跑判据会打点风暴)。

**阶梯变异:10 条预测先写,9 kill。** M1(空 modules 早返回)**不可 kill——与最后那句 chooser 兜底冗余**,行为仍被兜底钉住(空→chooser,且都不调 menu)。这是本项目又一个"分支被后续兜底替代"样本(同 B6a 的 M4)。M6(阶梯顺序:default 判在 remembered 前)红在"记住优先于默认"那条——顺序被钉住。M2/M4/M5/M7/M8/M9/M10 全红如预测。

**两个变异脚本/类型的假绿,顺带记:**
1. **匹配串缩进对不上(4 空格 vs 文件 2 空格)→ 四条替换 no-op,输出照样全绿**,与"判据没抓住"无法区分。脚本里的 `assert s.count==1` 是必要护栏(它 traceback 挡下了),读输出要先看 traceback 不能只看 passed。**"变异全绿"的第四种家:变异根本没应用。**
2. 测试里 `as never` 上做 spread → typecheck 炸(`Spread types may only be created from object types`)。拿 `as never` 抹类型的反噬,typecheck 兜住了。改回正经 `UserProfile` 类型。

**守卫 6 条行为测试**(渲染进真 router):三守卫 + F5 重建目标页 + chooser 落 /module + enterInitial 失败清会话跳登录。

**堵上 B6b-1 记的缺口**:加一条**不注入 glob、指向真页面文件**的 buildRoutes 测试。上面所有路由测试都注入假 glob,真实 `import.meta.glob(['...','!...spec'])` 若因语法/版本匹配为空,一条都不红而 app 零动态路由。这条证明那条数组负模式在本 Vite 版本确实解析到文件。(顺带:build 仍 1 个 chunk,是 Vite 把"只 re-export UnderConstruction 的小占位页"内联了,功能无碍,B11 真页有内容自会拆——`建设中` 文案在主包里,页面可达。)

四件套:`lint=0` / `typecheck=0` / `vitest=0`(166 passed / 21 files) / `build=0`。

下一条:**B7**(模块选择页 + `switchModule`/`setDefault`——enterInitial 已就位,补切换与设默认那两支,把 /module 占位换成真选择器)。

### 2026-07-20 · B6b-1 materialization(描述符 → RouteObject)

`a417a70`。B6b 的机械半:B6a 的描述符 → react-router `RouteObject[]`。view 描述符 → `React.lazy`+`Suspense`,iframe → `IframeView`,`name`/`title`/`icon` 进 `handle` 留给 B8 布局壳。**glob 注入**,用假表渲染进真 router 来测映射;`hasView` 与取 loader 用**同一张 glob**,"存在"与"能加载"不会打架。**不需要 Vue 的 `return to.fullPath` 重解析**:路由从 `menuTree` 派生,menuTree 一变 React 自然重渲染重匹配。

占位页(dashboard/system/user/system/role)re-export 共用的 `UnderConstruction`,显示当前路径 —— 联调时能一眼分清"路由对了只是页没实现"vs"路由错了"。B11 逐个替换成真页。

5 条行为测试(渲染进真 router 断页面文字/iframe src/handle/同源真相源)。**没做变异**:决策逻辑(有分支的那部分)全在 B6a 已变异钉死,这里是机械映射,行为测足够。

**两个如实记的点:**
1. **页面这次没被 code-split**(build 只 1 个 JS chunk)。因为 `buildRoutes` 还没被入口 import(App 仍用探针兜底),整个模块连同 glob 被 tree-shake。这是 B6b-2 接线**之前的预期态**,接线后页面才拆包 —— 留作 B6b-2 的验证点。build 成功本身证明那条 `import.meta.glob(['/src/views/**/*.tsx','!...spec.tsx'])` 数组负模式能编译。
2. **`.spec.tsx` 排除**:它活在 glob 负模式(构建期)那层。~~单测够不到~~ —— **这句我记悲观了,B6 review(LOW-5)反证:** 因 `src/views/**` 下真有 `.spec` 文件,可用「让菜单指向一个真实 `.spec` 组件键 → 断言不建路由(带排除)vs 建路由(去排除)」的 probe 直接判别,排除是 load-bearing 且**可测**的。方向本就安全(低估可测性,不是虚报覆盖),已在 B6b-2 用「不注入 glob、指真页面文件」的正向测试把 glob 匹配那半钉住。

四件套:`lint=0` / `typecheck=0` / `vitest=0`(145 passed / 19 files) / `build=0`。

下一条:**B6b-2**(`useModule` 决策阶梯 + `<RequireAuth>` 三守卫 + 接进 App.tsx 替换探针兜底)。那是 B6 有决策风险的另一半,配自己的变异回合;接线后验证页面 code-split。

### 2026-07-20 · B6a 菜单树 → 路由描述符(动态路由的决策半)

`3877df4`。**B6 拆子步**(仿 B5):a 是「哪些菜单节点建路由、建成什么」的纯决策逻辑,b 是 react-router 的 materialization(React.lazy/Suspense/RouteObject)+ `<RequireAuth>` 守卫。把决策剥出来单独测,不必拉进 Suspense。

**目录约定**:用 `src/views/`(登录页已在那,且对齐 Vue 侧 `views/`),glob 将是 `/src/views/**/*.tsx`。台账原文里 `/src/pages/**/*.tsx` 只是随口一提,以 `views/` 为准。

规则逐条对齐 Vue `useAuthMenu.buildRoutesForModule`:flatten → 跳非 Menu/无 path → 跳 `isHttpUrl(path)` 外链 → `isHttpUrl(component)` 走 iframe → 无 component 跳 → `hasView` 缺失 warn+跳 → 否则 view 描述符。**`hasView` 与 `warn` 注入**,不在函数里直接摸 glob/console —— 前者让"组件存不存在"可控,后者让那条 warn 分支**测得到**(否则永远命中不了)。

**九条变异,预测先写,一条揪出判据缺口(与 B4/B5 同源):**

M4(去掉 `isHttpUrl(path)` 外链跳过)**预测红、实测绿**。原因:我那条外链测试用了 `component: ''`,去掉外链跳过后节点照样被下面的 `!component` 跳过 —— **外链跳过与无组件跳过对这个输入互为冗余**,测试没隔离外链规则。这已经是本项目第 N 次同一形状:**判据的输入要让被测规则成为唯一起作用的闸**,否则别的规则替它兜了底,变异照样绿。修法:外链节点带一个**存在的** component,外链跳过就成了唯一能拦下它的闸。补完重跑 M4 如期红。

其余八条如预测。M7(不查 hasView)双断言都红 —— warn 未被调 + 描述符多一条,两个断言缺一这条变异就可能溜过去(预测里写了这是重点)。M5(iframe 判据反相)红 7 条,全是真受影响(都断 component/iframe 形状),非噪声。

`lint=0` / `typecheck=0` / 目标 spec 14 passed。**未跑全量**(避免与刚结束的 review lane 的残留状态混淆,且这条纯逻辑无跨文件耦合);下条落地前会全量确认。

下一条:**B6b**(materialization + `<RequireAuth>` + `useModule` 决策阶梯 + 接进 App.tsx 替换探针兜底)。需要至少一个占位页做 glob 落点。

### 2026-07-20 · B5a review 处置 + 那条偶发红的最终定性(review-b5b lane:APPROVE 无阻断)

B5a 的独立 review lane(review-b5a)卡住不出结论、已 TaskStop,B5a 并入 review-b5b 重审(它审的登录页正依赖 client.ts,同一个 lane 最自然)。结论 **APPROVE 无阻断**:三机制逐字对齐 Vue、宿主替换如预期、specs 有判别力。lane 独立抽查了机制①的判别力(把 `request.clone()` 改成存原请求,POST 重放用例红在**真的 undici 错误** `Cannot construct a Request with a Request object that has already been used.`——正是 spec 头部声称 happy-dom 会掩盖的那个),坐实 `@vitest-environment node` 那步值回票价。

**那条"未证实的偶发红"现在定性了——比我原来记的精确得多。原诊断是"识别对了一个真实通道,但归错了因"。**

我原来的假设:`calls` 模块级 + 旧 setup 的桩迟到回调 push 进新数组。lane 三条证据把它拆开:

1. **结构性(决定性)**:桩在 `mine.push(c)` 是**发起 fetch 时**记账,不是 Response settle 时。要 push 落到下一条用例,fetch **调用本身**必须晚于发起它的用例结束 —— 那需要 client.ts 里有**未 await 的 fetch 路径**。而三条路径(openapi-fetch 初始请求、`await bare.POST` 刷新、`return fetch(retry)` 重放,openapi-fetch 会 await 它)全在 await 链里。**每条请求都被 await 到底,没有 fetch 调用能活过它的用例。**延迟某个 handler 的 Response 也没用 —— push 在调用时已经发生了。
2. **实证**:回退成 bug 版,6 次单文件绿 + 1 次全量绿(126/17)。无污染时连全量都不 flake。
3. **对抗构造**:lane 照我的建议造了 fire-and-forget 请求 + 门控延迟刷新,想让重放 fetch 在数组换掉之后才发。**没干净泄漏,反而把整个文件生命周期搞崩**(`beforeEach` 的 `localStorage.clear()` 对所有用例抛错)。根因:`test-setup.ts` 直接赋值 `globalThis.localStorage`,而配置的 `unstubGlobals`/`afterEach` 在用例间拆全局 —— 未 await 的请求活过用例后撞上被拆掉的全局**直接炸**,而不是静默 push 进下一个数组。**punchline:测试套件主动拒绝未 await 的活,你连一个 fire-and-forget 都塞不进去而不让文件崩。"每条请求都 await"这条纪律正是泄漏够不到的原因。**

**定性**:通道**原理上真实**(bug 版的桩确实闭包在模块级 `calls` 上,一个 post-swap 的 fetch 调用会 push 错数组),但**从这个套件够不到**。那次历史偶发红,最可能是并发 review lane 在那个窗口往共享工作树注入了 `zzflake.spec.ts`(client.spec.ts 旧版逐字副本)造成的**跨文件/跨进程全局桩串味** —— 正是我当时记下的第二个候选解释,现在被 lane 的结构分析扶正。per-setup 数组修复**按构造安全、零成本**,关掉了那个理论通道,保留。

**这轮把"偶发红"这类问题的处置补全了一条方法**:偶发红有三种可能的家 —— 文件内共享状态(我原来的假设)、跨文件/跨进程全局污染(实际的家)、或者根本不是代码而是**并发进程改了同一棵树**(这次的元凶)。前两种要在**隔离**环境里分辨;而第三种是这次全程的背景噪声(两个 review lane 都往共享树里留过东西),它会把前两种的证据全部污染。**教训升级**:review lane 与主线共用工作树时,主线在 lane 活跃期跑测试套件拿到的任何"复现/不复现"都不可信 —— 我这轮就吃了一次(bug 版留在树里时我跑出 3/6"复现",实为撞上 lane 的 mid-toggle)。以后要么等 lane 空闲、要么给 lane worktree 隔离。

**两条 LOW(都不改,记下,均与 Vue 侧对等)**:①`url.includes('/api/v1/auth/login')` 子串守卫也会误伤一个**恰好含该子串**的消费者路径(如 `/api/v1/tenant/auth/login-audit`)—— 理论问题。②`api/index.ts` 的 `recycleApi` 用 `as any` 兜运行时选定的 `{type}` 路径参(openapi-fetch 无法为运行时决定的 path 定型)—— 而 `api/index.ts` 是从 `dev` **逐字复制**的,这个 cast Vue 侧本就有、是继承来的,pragmatic 且局部,acceptable。两条都不值当改。

**B5 至此全部 review 完:B5a + B5b 双 APPROVE。**

### 2026-07-20 · B5b review 处置(review-b5b lane:APPROVE 无阻断)

`84dc3e3`(前端)+ `36b34c1`(后端 core)。review lane 独立复现了关键变异(它自己重跑,不信我的记录),四件套自跑退出码全 0,台账逐条核实无不实。**四条 LOW 全是 advisory,无阻断**,处置了三条:

- **[LOW-1 已改] 子树无 message 那条断言太弱。** 原来是 `not.toContain('returned an object')` —— 耦合 i18next 那句英文 debug 文案的**确切措辞**,且放过别的错误输出(回落成空、别的 debug 变体也能过)。改成 `toBe('error.auth')`(ApiError 无 message 时把 msgKey 当 message → 走 message 分支)。**不是自指**:期望值是按构造函数已知行为写死的字面量。重跑 te()→裸 exists 变异,仍如期红。**这条本身是"N 条红 ≠ N 条覆盖"的镜像:一条断言红了,也可能红得不对地方(耦合了无关的措辞)。**
- **[LOW-4 已改] 验证码刷新是 click-only 的 `<div>`。** 看不清要换一张却只有鼠标能点。补 `role="button"` + `tabIndex` + Enter/Space + `aria-label`。可访问性是不砍的那一类,虽小必做。
- **[LOW-2 已改,跨到后端] `dangerouslySetInnerHTML` 的信任边界只写在前端注释里,面向的是前端开发者。** 而内核的整个前提是 `ICaptchaProvider` 可替换 —— 消费者写一个把请求参数拼进 SVG 的 provider,验证码端点又是**登录前匿名**可达,就是 pre-auth XSS,而前端零净化。lane 独立查了后端三个内置 provider,确认 SVG 全服务端自生成、零请求输入入串(可信)。**契约补写到后端 `Captcha.Svg` 参数注释上**,provider 作者看得到的地方,不只是前端注释。**不加 DOMPurify**:自生成验证码上是过度设计,且破坏零额外依赖目标 —— 与 Vue 侧 `v-html` 完全对等,不是回归。
- **[LOW-3 不改] 登录后 `navigate('/')` 落到探针页、无鉴权守卫。** 是 B6(动态路由 + 守卫)、B8(布局壳)之前的**预期临时态**,lane 明确标了"别把 Probe-after-login 当成走通的流程"。
- **[INFO 不改] `remember` 复选框两个模板都是装饰性的**(Vue 侧也只绑不接线,token 无条件落 localStorage)。不是 B5b 回归。

lane 还替我把那条最关键的**跨模块图 `instanceof`** 查实了(我标"务必动手"的 Experiment E):`resetModules` 后 mocked `@/api` 跨代复用,`ApiError` 是**同一个类对象**,`translateError` 的 `instanceof` 按构造成立、不靠运气,`...actual` spread 是 load-bearing。台账 note #1(并行模块图咬 unmocked user store 而不咬 mocked ApiError)由此坐实。

后端只改 XML 注释,单独过 `dotnet build TenonAdmin.Core`(0 警告 0 错误)确认注释语法没写坏;未跑 dotnet test(注释改动不涉逻辑),也未与 vitest 并发。

**B5a 的独立 review lane(review-b5a)卡住了不出结论,已 TaskStop,B5a 并入 review-b5b 重审**(见下一条待办)。B5a 的 client.ts 正是登录页依赖的,同一个 lane 审最自然,不另开第三个。

下一条:等 **review-b5b 出 B5a 结论**(尤其那个泄漏通道能否确定性复现),处置后接 **B6 动态路由**。

### 2026-07-20 · B5b 下半 · site store + translateError + 登录页

三个提交:`46feef6`(site store + translateError)、`2213a87`(登录页 + 路由)。

**B5 至此收尾**(拆成 a/b 两半,见上)。b 这半又拆了三步落地:site store 与 translateError 是纯逻辑、可先各自钉死;登录页要路由才跳得动,单独一步。

#### site store(`46feef6`)

`useSite` 从 Vue 的 `reactive` + 模块级变量改成 zustand store,在途缓存照旧模块级。**一处与 dict store 相反的语义特意留着并加了用例**:site 拉取**失败不自动重试**(`inflight` 不清),只 `load(true)` 或整页重载能救回。这是 Vue 侧的有意选择(站点信息是纯装饰,每个消费者都调 load(),自动重试会在后端持续不可用时反复打点),用例名里写了"与 dict store 语义相反,已知且有意",改语义时它会红,提醒两个模板一起改。

一处**与 Vue 的行为微分叉**:title 用 `s.title || DEFAULTS.title`(空串回落内置名),Vue 是 `if (s.title) site.title = s.title`(空串留住上一次的值)。差别只在"已成功拉过、再 force 却拿到空 title"时可观测。取这边写法是因为结果只取决于本次响应、不取决于调用历史。已在注释里写清这个分叉。

变异 S1–S6,预测先写,**六条全中**(在途缓存、force、空串回落、字段兜底 ×2、catch)。

#### translateError(`46feef6`)—— B4 那条 `te()` 判据终于接到真实消费者

`te()` 在 B4 落地时只有单元层用例守着;`translateError` 是它**唯一的消费者**,这一步把两者接上。补了一条走真实消费路径的判据:msgKey 恰好是**子树路径**时,`te()` 退回后端原文,不把 i18next 那句 `"key 'error.auth' returned an object instead of string."` 当文案弹给用户。变异(把 `te()` 换回裸 `i18n.exists()`)——那句 debug 文本原样进了用户可见字符串,两条新用例都红。Vue 侧没有对应用例(那边 `te` 是 vue-i18n 自带的,没人需要证明它);这边手写,所以必须有。

#### 登录页(`2213a87`)

只做**账号密码**这一条路径。短信免密 / 短信二次验证 / 外部 SSO **暂不做**——Vue 侧那三块占了 `LoginForm.vue` 一半篇幅,各要后端端点与倒计时状态机,一次压上四条链路不值当。`react-router-dom` 这一步落地(不推到 B6):跳不动的登录页是假完成,而 B6 反正要用。`App.tsx` 加 `BrowserRouter` + antd 的 `<App>`(在 ConfigProvider 之内,`message`/`modal` 才拿得到主题与 locale)。

验证码票据一次性,失败后自动换图——这是判据钉得最紧的一条(不换的话用户照旧图再输必再失败,且第二次错误码与第一次不同,极难归因)。变异 L1–L5 各命中目标用例。

**两条判据脆弱性,都在这一步现形,记进纪律:**

1. **`resetModules` 造出的是并行模块图,跨图读写永远对不上。** 「成功:落会话」首次红在 `expected '' to be 'A'`:断言读的是文件顶部静态导入的 user store,而页面写的是 `import('./LoginPage')` 连带新建的那一份。`mount()` 改成返回新图里的 store。与 `api/client.spec.ts` 那个偶发红同源。

2. **`vi.clearAllMocks()` 只清调用记录、留着实现。** `mockResolvedValueOnce` 链没消费完的返回值会泄漏到下一条用例。症状最阴:L1 变异(失败后不换图)让**"math 提示语"那条无关用例连带红**,而单跑那条在 L1 下是绿的。也就是说**"一条变异让 N 条用例红"不等于"N 条用例都覆盖了这个缺陷"——其中可能有纯噪声**。改成 `beforeEach` 里 `mockReset`(清实现)+ 泄漏修掉后重跑 L1,只红目标那一条。这是判别力的另一面:不只要问"改坏了它会不会红",还要问"红的这些是不是都真的在测它"。

四件套真绿:`lint=0` / `typecheck=0` / `vitest=0`(126 passed / 17 files) / `build=0`。

下一条:**B5 的 review lane**(a 已发出,b 待发)。之后 **B6 动态路由**。

### 2026-07-20 · B5b 上半 · dict store + useDictOptions

`604955c`。三条规则(缓存命中 / 并发去重 / invalidate 竞态守卫)逐字对齐 Vue 侧;Vue 那边 4 条用例,这边 13 条(多出来的是下面两个发现,加上 `useDictOptions` 的 hook 行为)。

**发现一:`.finally` 里那半个竞态守卫此前没有任何判据。** D4(删成无条件 `pending.delete`)——**其余 12 条全绿**,与预测一致(预测里写了"若真绿,要么补更窄时序的用例,要么如实记为缺口")。它保的时序比 `.then` 那半窄:`load → invalidate → 再 load`(pending 里已是 req2)→ **req1 这时才 settle**,无条件 delete 会把 req2 的登记一起抹掉,第三次 load 于是另起请求。后果不是脏数据(那归 `.then` 那半守着),而是**去重悄悄失效**——一屏 N 个下拉就是 N 次请求。已补,补完重跑 D4 如期红在新用例上。

**发现二:`EMPTY` 常量不干它注释里声称的那件事。** 这条是我自己写的注释被自己的变异打脸:

- D7b:`?? EMPTY` → `?? []`(兜底仍在选择器外)→ **全绿**。因为它本来就是对的。
- D7a:把兜底挪进选择器(`useDictStore((s) => s.cache[code] ?? [])`)→ **四条全红**,React 原话 "The result of getSnapshot should be cached to avoid an infinite loop"。

**起作用的是兜底的位置,不是用不用常量。**原注释把功劳记在常量上,已改。`EMPTY` 留着的真实理由另有其二:空态时引用跨渲染稳定(下游 `memo` 不被白白叫醒),以及 `Object.freeze` 挡住「谁在返回数组上原地 push」——那会污染所有空态消费者,与 `stores/auth.ts` 里 `EMPTY()` 写成工厂防的是同一件事。返回类型随之改成 `readonly DictItem[]`,那是意图的类型表达,不是为迁就 freeze 加的 cast。

D1/D2/D3/D6/D8 均如预测(缓存命中、并发去重、`.then` 守卫、invalidate 连带清 pending、`useEffect` 依赖数组)。D6 连带红了 3 条并有一条 5 秒超时。

顺带:这是本模板**第一个真正跑起来的 `.tsx` spec**。R3 曾把 `test.include` 里的 `.tsx` 标为"漏掉时 vitest 不报错、CI 全绿、那些用例从来没执行过"的静默风险——现在它由一个实际执行的 tsx 用例证实,不再是声称。另外 `globals: false` 下 **testing-library 的自动 cleanup 不会注册**(它挂在全局 `afterEach` 上),要自己调 `cleanup()`,否则第二条用例起 `getByTestId` 报 "Found multiple elements"。

#### 一次未能定论的偶发红(如实记,别当已修)

一次全量 `npm test` 里 `client.spec.ts` 的「没有 refreshToken 时连刷新都不发」红了:`refreshCount()` 实测 1,而那条用例自身只发一个 GET。单跑该文件、以及随后 4 次全量都绿。

**假设**:`calls` 是 spec 的模块级变量、`vi.resetModules()` 不碰它;而上一个 `setup()` 建的 fetch 桩仍被上一个 client 模块闭包持有,它的迟到回调会 push 进**新一轮**的数组。已改成每次 `setup()` 新建数组、由桩闭包直接持有。

**但这个假设没被证实,而且出现了第二个候选解释:** 排查中发现工作树里多了一个 `src/api/zzflake.spec.ts` —— 是 `client.spec.ts` **改动前那一版**的逐字副本,时间戳晚于我的改动,不是我建的。本 session **不隔离 worktree**,而 B5a 的 review lane 正在同一棵树里跑变异,基本可以确定是它留下的复现脚本(测试数从 101 跳到 110 就是它带进来的 9 条)。也就是说那次偶发红发生在"另一个进程正在改同目录文件"的窗口里。

**所以:改动是对的(那个泄漏通道在代码上确实存在),但"改动修好了那次红"是未证实的。**已请 review lane 判定该通道能否被确定性复现;构造不出来就按未证实结案。

> **【后续已定性,见上方「B5a review 处置」条】** review 用结构分析 + 对抗构造把这条查透了:那个"文件内通道"**原理真实但从这个套件够不到**(每条请求都被 await,没有 fetch 调用能活过它的用例;硬造 fire-and-forget 会撞上被拆的全局直接崩,而非静默 push)。上面"第二个候选解释"(并发 lane 注入 `zzflake.spec.ts` 造成跨进程全局桩串味)才是那次红的元凶。**per-setup 修复按构造安全,保留;偶发红归因于跨进程污染,不是文件内通道。**

**顺带一条工程教训:并行 review lane 与主线共用工作树时,lane 在 `src/**` 下留的临时 spec 会被主线的 `npm test` 收进去,污染计数、也污染对偶发失败的归因。**以后给 lane 的提示词里要写明:复现脚本放 `$CLAUDE_JOB_DIR/tmp`,别落在 `src/` 下。

四件套:`lint=0` / `typecheck=0` / `build=0` / 目标三个 spec 22 passed(全量计数因上述临时文件不可信,待它清掉后重取)。

下一条:**B5b 下半**(登录页 + 后端可配 logo 的 `useSite`)。

### 2026-07-20 · B5a api client + endpoint wrappers

`6989f1e`。**B5 拆成 a/b 两半**:a 是 api client + `api/index.ts`(台账点名"唯一一处重复有真实技术风险的代码",值得独占一轮变异),b 是登录页 + dict store。拆的理由是变异预算——把 UI 和这三个中间件机制塞进同一轮,变异集合会大到没人认真跑。

`api/index.ts` 是**逐字复制**(`diff` 与 `dev:web/src/api/index.ts` 为空):它零框架耦合(唯一一处 `.value` 是数据字段不是 ref),`@/*` 下导入路径完全相同。

**跳登录改成整页导航,是有意的行为分叉。** Vue 侧 `router.replace('/login')`;这里 `window.location.assign`。B6 的动态路由走 `useRoutes`(组件 API),模块级拿不到 router 实例,软跳要额外接缝;而会话死亡这条路上整页重载反而更干净(动态路由、字典缓存、标签页一次清空,不必指望每个 store 的 reset 都写全)。两边都不带 redirect 参数,用户侧无差异。

#### 判据环境:这份 spec 跑 node,不跑 happy-dom

**happy-dom 没实现「请求体是一次性流」。** 这是本轮最贵的一个发现,过程值得记全:

1. 第一版桩里 body 用 `req.clone().text()` 读。M1(`replayable.set(request, request.clone())` → 存原请求)与 M2(整行删掉)**双双全绿**。
2. 第一次归因:桩用 clone 读,原始流从没被消费,「一次性」这个前提在测试世界里不存在。改成 `req.text()` 直接读。**M1/M2 仍然全绿。**
3. 只好上探针,直接观测三件事,结果是:`WeakMap` **命中 = true**(接线没问题)、原请求 `bodyUsed` **= true**(桩确实消费了)、而 **`new Request(已消费的请求)` 照样吐得出原 body**。
4. 换 undici(Node 原生 fetch)同样步骤:抛 `TypeError: Cannot construct a Request with a Request object that has already been used.`,克隆则正常。

**结论:不是桩的问题,是环境的问题 —— happy-dom 把 body 缓冲了,那个陷阱在它里面根本不存在,再怎么调桩也造不出来。** 整份 spec 加 `// @vitest-environment node`,连带三处环境适配(`VITE_API_BASE` 给绝对源、`window` 桩、以及 `window.localStorage` —— zustand 的 persist 按 `typeof window !== 'undefined'` 判断浏览器存储,一旦定义了 `window` 就去拿 `window.localStorage`,桩里漏了报的是 `Cannot read properties of undefined (reading 'setItem')`,和 location 毫无关系)。

**这条要一般化:「用例绿」有三种可能 —— 代码对、判据没判别力、或者环境里根本没有那个失败模式。前两种已经吃过亏,第三种是新的。** 凡是判据依赖**平台语义**(流的一次性、事件循环时序、存储配额、CSP)的,先问一句:测试环境实现了那条语义吗?

#### 变异:9 条,预测先写

| # | 变异 | 预测 | 实测 |
|---|---|---|---|
| M1 | 存原请求而不是克隆 | 请求体那条红 | ❌→✅ happy-dom 下**全绿**;换 node 后如预测红 |
| M2 | 整行删掉克隆 | 同 M1 | ❌→✅ 同上 |
| M3 | `refreshing ??=` → 直接 `return doRefresh()` | 并发那条红 | ✅ |
| M4 | `.finally(() => refreshing = null)` 删掉 | 归位那条红 | ✅ |
| M5 | `bare` → `client` | **可能仍绿**(URL 短路也挡) | ✅ 全绿 —— 预测对了 |
| M6 | 删 `/auth/refresh` URL 短路 | 若 M5 绿则也绿 | ✅ 全绿 |
| M7 | M5 + M6 同时 | 递归那条红 | ✅ 红,且是 **5 秒超时**(真无限递归) |
| M8 | 刷新失败不清会话 | 清会话那条红 | ✅ |
| M9 | 重放不补新令牌 | 令牌那条红 | ✅ 红 3 条(重放后仍带旧令牌 → 后两条连带) |

**M5/M6/M7 那组推翻了台账自己的说法。** 台账把 `bare` 列为「防刷新递归」的机制之一,实测是:**防递归有两道冗余的闸,拆掉任何一道单独都测不出来**。将来谁把 `bare` 整个删掉,原来那八条用例一条都不红。已补一条属于 `bare` 自己的判据 —— 不测「会不会递归」(测不出),改测它的直接可观测后果:**`bare` 没挂 authMiddleware,所以刷新请求不带 `Authorization` 头**。补完重跑 M5,如期红在这条新用例上。顺带说清了 `bare` 真正的第二个价值:刷新用的是 refreshToken,本就不该把一个已经过期的 accessToken 捎上去。

M9 预测不完整(只列了令牌那条,实际连带并发那条也红,因为重放仍带旧令牌就还是 401)——和 B4 的 M3 同一个毛病:**顺着「这条变异针对哪条用例」想,没反过来枚举「哪些用例会碰这段代码」。**

`pageParams`/`toPage` 补了 3 条用例。它们是**消费者接缝、站内无调用方**,坏了不会有任何东西变红(Vue 侧同样没测,是共同缺口,已记 `docs/refinement-ledger.md` 待办候选)。

四件套真绿:`lint=0` / `typecheck=0` / `vitest=0`(88 passed / 12 files,原 72/10) / `build=0`。

下一条:**B5b**(登录页 + dict store)。dict store 的 typeCode 缓存 + 并发去重 + `invalidate` 竞态守卫是 B3 推迟下来的,那三件事的变异要单独跑。

### 2026-07-20 · B4 review 处置

`b4757d3`。review lane 报 2 HIGH / 2 MEDIUM / 4 LOW,两条 HIGH 都独立复现过才动手。

**[HIGH-1] DatePicker 面板在中文下是英文的,而这件事上一轮被当成"antd 文案跟着切"的**正面证据**写进了 commit message 和台账。** `ConfigProvider locale` 只切 antd 自己的 chrome 文案;面板里的**月份名 / 星期缩写 / 周起始日**归 dayjs 管——`@rc-component/picker/generate/dayjs.js:157-162` 拿 `DatePicker.lang.locale`(`'zh_CN'`)去调 `dayjs().locale('zh-cn').localeData()`,而 antd **一行 dayjs locale 都不 import**(`grep -rln "dayjs/locale" node_modules/antd` 零命中),dayjs 对未注册的 locale 名**静默回落 en**。实测:

```
未注册: monthsShort=Jan Feb Mar | weekdaysMin=Su Mo Tu | firstDayOfWeek=0
已注册: monthsShort=1月 2月 3月 | weekdaysMin=日 一 二 | firstDayOfWeek=1
```

即中文界面 + `Jan/Feb` + 周从**周日**起,而 tsc / lint / 全部用例全绿、控制台一声不吭。修:`locales/index.ts` 里 `import 'dayjs/locale/zh-cn'`,`dayjs` 从 antd 的传递依赖提升为显式 dependency。

两个次级判断也验了,都成立:**只需注册、不需要全局 `dayjs.locale()`**(picker 每次都显式 `.locale(...)`,探针全程没调过 setter);**`en` 是 dayjs 内置的**,不用导。

放在 `locales/index.ts` 而不是 review 建议的 `main.tsx`:那样 spec 就测不到(spec 只 import `@/locales`)。

**判据本身走的是 antd 自己那条路** —— 它自己的 generate config、它自己的 dayjs 实例、从 antd locale 对象里取的标识符。直接断 `dayjs().locale('zh-cn')` 的话,`node_modules` 里有两份 dayjs 时(我们注册进 A、antd 用 B)**照样绿**而面板依旧英文。这条用例要能区分那两个世界。

**[HIGH-2] 探针那句断言是自指的 —— 这条我完全没看见,是本轮最有价值的产出。**

```js
const greetingOk = greeting === (i18n.language === 'en-US' ? 'Hello, 张三' : '你好,张三')
```

期望值从 `i18n.language` 推导,而它正是被测变量,两边同步移动,等式恒成立。关键机制是我上一轮想错的那一步:删掉 `lng: useAppStore.getState().locale` 后 **`i18n.language` 不是 undefined,而是被 `fallbackLng` 顶成 `'en-US'`**,所以自指等式在中文下也照样成立。

于是上一轮台账里"第 5 步在中文下再刷一次才有判别力"这句话**只有前半句是真的**:一页两种语言确实肉眼可见,但**没有任何断言会红**,判别力全靠人盯屏幕。按本台账自己的纪律,那就是一个不可能失败的检查。已改成比 `p.locale`(store),让"i18n 说英文而 store 说中文"这件事本身变红。

**这是"跑绿的用例什么都不证明"第三次咬到我,而且这次咬的是我为了防它而特意设计的第 5 步。**共同形状:判据的期望值与被测值**共用同一个源**。前两次是"从我恰好看过的地方推全称结论",这次是"从被测变量推期望值"。下次写断言时的自检问句:**期望值是从哪来的?如果它和实际值来自同一个变量,这条就是回声。**

**[MEDIUM-1] `te()` 与 vue-i18n 在 message function 上相反,上一轮"三态与 Vue 一致"不属实。** 实测 vue-i18n `te(fn)=true`、`t(fn)="函数文案"`;本模板 `te(fn)=false`。**有意不对齐**(方向安全:挡住把函数塞进 React 渲染;仓库里不存在函数值形状),但已从"声称一致"改成一条**记录当前契约的用例** —— 让"有意"和"忘了"长得不一样。八种形状(string / 子树 / 缺失 / 数组 / 数组下标 / 纯数字键 / 空串 / 结尾点键)+ 回退链两边一致,唯此一格分道。

**[MEDIUM-2] `te` 的源码位置记错了包。** 在 `vue-i18n/dist/vue-i18n.mjs:628`,**不在 `@intlify/core-base`**(那边只出三个 helper)。我自己 grep core-base 时也是零命中,当时没深究。这句话的全部价值就是"可复现",指错包等于没验。已改。

**回退链那一格上一轮没钉住(我自查发现的)。** 两边隔离实测(只调 `te` 不调 `t`,免得分不清谁在回落):键只存在于 fallback locale 时,vue-i18n 与本模板**都返回 true**。已补用例,键是当场 `addResource` 注入的而不是从 `en-US.ts` 里挑一个"恰好 zh-CN 没有"的 —— 两份文案本就该是镜像,哪天有人补齐缺口,挑出来的键就两边都有了,那条用例会**静默失去判别力而依旧全绿**。

LOW 三条一并落实:嵌套键用例自己设语言(原先吃上一个用例 `afterEach` 的残留,单跑就没这个前提);`ext/README.md` 补 `escapeValue:false` 的代价(插值结果不得进 `dangerouslySetInnerHTML` / `<Trans shouldUnescape>`)、花括号解析不出值时原样保留、键值不能是函数。LOW-1(探针脚本不在仓库、不可复现)**不修,记账**:一次性探针就是一次性的,但 HIGH-2 证明了"只有探针能覆盖"的断言本身就是风险信号 —— 该进 spec 的就该进 spec。

**四条变异,预测先写,方向全中,一处预测不完整:**

| # | 变异 | 预测 | 实测 |
|---|---|---|---|
| M1 | 删 `import 'dayjs/locale/zh-cn'` | 中文那条红,其余绿 | ✅ 1 failed / 71 passed |
| M2 | `te()` 只查当前 locale | 回退链用例红,子树绿 | ✅ 1 failed / 71 passed |
| M3 | `te()` 退回裸 `exists()` | 子树红,**回退链仍绿** | ⚠️ 子树红 **+ 函数值那条也红**(2 failed) |
| M4 | `fallbackLng: false` | 回落用例 + 回退链用例都红 | ✅ 2 failed / 70 passed |

M3 的重点预测——**回退链用例在这条变异下必须仍绿**——命中了,证实那两条用例互不覆盖(`exists` 本来就跨 locale)。漏预测的是函数值那条也会红(裸 `exists` 对函数返回 true)。方向没错,但说明我列预期失败集合时只顺着"这条变异针对哪条用例"想,没反过来枚举"哪些用例碰这段代码"。

四件套真绿:`lint=0` / `typecheck=0` / `vitest=0`(72 passed / 10 files,原 68/9) / `build=0`,逐个跑、不并发。

### 2026-07-20 · B4 i18n 接线

> **本条有三处已被下一轮 review 证伪,更正见上面「B4 review 处置」**:①「三态与 Vue 一致」实为 2/3(message function 那格相反);②`te` 在 `vue-i18n.mjs` 不在 `@intlify/core-base`;③「第 5 步才有判别力」——那句断言是自指的,任何变异下都不会红。原文保留不改,更正在后。

`14b9f8f`。合并规则 R4 已落地并测过,这一条只往上接 i18next。三处默认值改写各自变异证伪过(去掉单花括号红 2、去掉 `escapeValue:false` 红 1、去掉 `nsSeparator:false` 红 1)。

**那条 MEDIUM 属实,而且判别值很刺眼。**`i18n.exists('error.auth')` 是 **true**,而 `t('error.auth')` 返回 `"key 'error.auth (zh-CN)' returned an object instead of string."` —— 一句英文 debug 文本。消费者是错误提示那条路径(Vue 侧 `utils/error.ts:11` 的 `te(msgKey) ? t(msgKey) : message`),所以后端只要发来一个恰好是子树路径的 msgKey,用 `exists()` 就会把那句话弹给用户。

  **"与 Vue 侧行为相反"这句我去源码验了,没照抄。**`@intlify/core-base` 的 `te`:只在解析结果是 string / message AST / message function 时返回 true,子树解析成普通对象 → false。所以补的 `te()` 写成 `exists(key) && typeof t(key, {returnObjects:true}) === 'string'`,三态与 Vue 一致,而且**恰恰在子树这一格与 `exists()` 分道**(变异:换回 `exists()` 红那一条)。

**七处变异,预测先写,全部命中** —— 包括 **M7 如预测全绿**:把 `lng: useAppStore.getState().locale` 整句删掉,单测一条都不红,因为所有用例都显式 `changeLanguage`。首帧语言是浏览器侧的事,归探针管。

**探针 14 条断言,而第 5 步是特意设计的。**前四步(初始中文 → 切英文 → 刷新 → 切回中文)看着已经很全,但**只在英文下刷新是没有判别力的** —— EN 恰好等于 `fallbackLng`,把 `lng` 初值删掉,那一步照样绿。第 5 步在**中文**下再刷一次才有判别力:删掉的话 i18n 回落 en-US(英文问候)而 antd 跟着 store 走(中文空态),一页两种语言。

**修正后的 Vite 诊断第一次经受独立检验,通过了。**探针首跑又报 3 条 `Invalid hook call` —— 而这次日志写的是 `new dependencies optimized: antd/locale/zh_CN, antd/locale/en_US, react-i18next, i18next`,与 B3 那次(zustand)是**完全不同的依赖集**,现场却一模一样:dev server 活着时新增裸包。删掉 `node_modules/.vite` 真冷启动:**零错误、零 re-optimize 行、14 条断言全过**。B3 若没改那条 E2,它现在已经错第二次了。

其余 review 处置:`resources` 不导出(spec 一律断 `t()` 的行为,不断资源对象 —— 后者是把喂进去的东西再读一遍);`fallbackLng` 换成行为断言(切到没有资源的语言,取到英文而非键名);store 订阅补 `import.meta.hot.dispose`(与 `stores/app.ts` 的 matchMedia 监听同一个模式);`init` 前加 `void`;副作用导入移到 `main.tsx` 与 `tokens.css` 并列,并删掉那条把顺序保证归因于 import 位置的错注释 —— 真正的保证是模块求值整体早于渲染。`ext/README.md` 补了两条约束:键名别用冒号(`nsSeparator:false` 是为了让后端 msgKey 取得到字,不是鼓励用冒号分层)、键名必须指向文案而非子树(`te()` 在错误路径上挡住了它,但你自己写 `t()` 时挡不住)。

下一条:**B5**(api client + 登录页 + dict store)。那是台账里点名"唯一一处重复有真实技术风险的代码",三个中间件机制要逐字保留,值得单独一轮变异。

### 2026-07-20 · B3 三个 Zustand store

`8d0bf45`。**是三个不是四个**:`dict.ts` 第 6 行 `import { dictApi } from '@shared/api'` 是**运行时**依赖,而 `api/index.ts` 随 B5 的 client 一起走 —— 与 R4 撞到的是同一条边界。其余三个只引类型与常量,R4 都已落地。`Density` 按台账从 `antd-theme.ts` 移进 app store,桥改成转口。探针页从本地 `useState` 改成三条**细粒度**订阅。

**台账记的那条 review LOW 属实,而且比记的更糟**:`auth-hooks.spec.tsx` 有条用例叫「渲染次数不随 store 写入无限增长」,而体内**一次 store 写入都没有** —— 只有一次手动 `rerender()`。它声称守的失败模式(选择器返回新建函数 → 无限重渲染)走的是 store **订阅通知**那条路径,手动 rerender 根本碰不到。改成三次真写入 + 各断一次重渲染,另补一条「同引用写回不重渲染」。

**那条决策现在有实证了。**把 `useHasPerm` 改成台账警告的危险写法(`useAuthStore((st) => (code) => hasPerm(st, code))`),四条用例全红,React 自己报出 `The result of getSnapshot should be cached to avoid an infinite loop`。此前"纯函数 + 细粒度 hook"只是个断言,现在是可复现的事实。

**六处变异,预测先写,全部命中**:M1 `isDark` auto 恒 false → 红 2;M2 `hasPerm` 去掉 `permissionsLoaded` 守卫 → 红 1;M3 `homePath` 去掉 `/module` 兜底 → 红 2;M5 `partialize` 混入 `systemDark` → 红 2;M6 `merge` 去掉 `LAYOUT_MODES` 校验 → 红 1。

**M4 如预测全绿,已补上**:`EMPTY` 从工厂改成常量,一条都不红 —— 而这个危险是**代码注释里自己写明的**(常量的话三个数组在初始态与每次 reset 之间是同一个实例,谁就地 `sort()`/`push()` 一下就永久污染净态)。补了一条"就地改过数组之后再 reset 仍然干净"的用例,变异证实会红。**自己写明的隐患没有守卫,等于只是记了个愿望。**

**浏览器探针**:偏好扛过 F5(明暗/主色/密度全留存)、`systemDark` 确认未入库、`--color-primary` 已写回。

**踩到一个会误导人的陷阱 —— 而我给它开的药方是错的,已改。**探针报了 `Invalid hook call` 和 `Cannot read properties of null (reading 'useCallback')`,看着像 React 双实例。**机制我判对了**:Vite 重新预打包新依赖(zustand)并强制刷新(`optimized dependencies changed. reloading`),那个窗口模块图短暂不一致,不是代码缺陷。

**但触发条件判错了,而错误的那一半被我写成了 E2 的验收要求。**我写的是"冷启动会触发,所以 CI 冒烟必须丢弃第一次加载"。review 让我实测:`rm -rf node_modules/.vite` 之后真冷启动 —— **零错误、server 日志里零 re-optimize**。原因是 esbuild 的依赖扫描沿 `index.html → main.tsx → App.tsx → stores/app.ts` 走,**在响应第一个请求之前**就发现了 zustand。真正会触发的是**陈旧缓存**:dev server 活着的时候往模块图里加一个新裸包 —— 正是我当时的现场。

**而 CI 里 `npm ci` 之后根本没有 `.vite` 缓存**,落的就是刚才那种真冷启动。所以我那条警告防的是不可能发生的事,却顺手给了 CI **一张丢弃真实首屏错误的许可证** —— 一个不可能失败的检查,正是这本台账存在的意义所要禁止的东西。E2 已改成相反的措辞,并把真实风险窄化到"只经动态 `import()` 可达的包"(B6 的 `import.meta.glob` 是下一个候选),对症办法是 `optimizeDeps.include`。

**教训**:这次不是"没验证就写断言",我确实观察到了现象;错在**把一次观察的伴随条件当成了因果条件**。观察到"第一次加载报错"就写下"冷启动会报错",而没问一句"是冷启动本身,还是那次恰好还带着别的东西"。判据纪律里"先跑一条能打自己脸的命令"这条,对因果归因同样适用 —— `rm -rf .vite` 就是那条命令。

**B3 review 处置**(`10aac19`;1 HIGH / 3 MEDIUM / 8 LOW)。**逐条对齐那部分全部成立** —— review 把 `hasPerm` 三条规则、`homePath` 阶梯、`isDark` auto 解析、三个 persist 白名单、DEFAULTS 字段集、auth 净态、user 登录态与 Vue 侧逐条比过,无一处不符;`EMPTY` 用 `Omit<AuthState,'reset'>` 约束这一点还**比 Vue 原版更紧**(那边 reset 是逐字段手写,漏改是静默的)。dict 推迟到 B5 也成立,而且理由比我写的更强:dict 的"纯逻辑部分"几乎不存在,缓存/去重/竞态守卫全是包在那一次 API 调用外面的编排,先搬就得做成只有一个产品的工厂 —— 而那正是 B5 刚否掉的东西。

**[HIGH]那条 Vite 结论,我机制判对了、触发条件判错了,而错的那一半被我写成了 E2 的验收要求。**已在上面就地改正。**教训是"把一次观察的伴随条件当成了因果条件"**:我确实观察到第一次加载报错,但没问"是冷启动本身,还是那次恰好还带着别的东西"。`rm -rf node_modules/.vite` 就是那条能打自己脸的命令,一跑就零错误。

**[MEDIUM]`exportSettings` 是移植遗漏,而且台账接不住它。**Vue 的 app store 有 9 个 action,我搬了 8 个。漏的那个给设置抽屉「复制配置」用,而**台账从 B 到 E 没有任何一条会碰到设置抽屉**,C0 那条"从存档搬运剩余条目"也带不出它(它在 `web/` 而非存档的 React 侧)。已补。

**[MEDIUM]两处"注释里写明了隐患、却没有用例守着"的缺口,与 M4 是同一类。**M4 那次我自己抓到并补了,这两处没有:①**matchMedia 桥完全无守卫** —— 把查询串改成 `DARK-TYPO`,54 条全绿、tsc/lint 也绿,因为所有 `isDark` 用例都用 `setState({systemDark})` 直接注入、**从不经过这座桥**;症状是「auto 档在所有设备上永远显示亮色」,没有任何即时线索。已补三条用例(`resetModules` + 桩 matchMedia),查询串写错与不注册监听各红一条。②**`partialize` 漂移** —— Vue 的 `persist: true` 自动覆盖新增字段,而这里是手写字面量,且"落盘键集"那条用例硬编码了同一份清单,**两份会一起变陈旧**。往 DEFAULTS 加一项而忘了 partialize,四件套全绿,症状是"抽屉里改了、看着生效、F5 消失"。守卫改成从 `exportSettings()` 反推(它本身就是"DEFAULTS 同名键的现值"),变异点名缺失字段。

**[LOW×8 已处置的几条**:①`reset 净态`那条里 `menuTree`/`modules` 两句是**摆设** —— 三句 `expect` 顺序执行,第一句一红后两句根本不跑;已合成一次断言,三个数组都进报文。②`toBe(3)` 与"无 StrictMode wrapper"耦合(实测 StrictMode 下是 6),已注明。③"三条细粒度订阅"**不实,实为六条**(三条状态 + 三条 action),三处措辞已改 —— 一个被 grep 打脸的数字该改。④探针页只有 light/dark 两档,而 B3 起 `auto` 是**持久化的默认值**,点一下就永久塌掉还落盘;已补 auto 档。⑤台账 C1 那句"数据基座是 B3 的 dict store"已改 B5。

**留待观察(review 的低置信项)**:B6 的 `import.meta.glob('/src/pages/**/*.tsx')` 可能是"只经动态 import 可达、首轮扫描看不见"的下一个候选,真会在冷缓存下触发 re-optimize + 刷新。到 B6 时当场验一次,别等 CI 假红。


下一条:**B4**(i18n + review 处置)。

### 2026-07-20 · B2 主题桥(含那条我自己引入的亮色回归)

`5bd7ada`。搬 `theme/{antd-theme,useAntdTheme}.ts` + spec,导入改 `@/*`,六条 review 处置全部落实。

**回归的机理已在 antd 源码层面坐实,数字全对上。**`alias.js:24-26`:`shadowBaseColor = new FastColor(colorShadow); shadowBaseAlpha = shadowBaseColor.a; getShadowColor = a => base.clone().setA(shadowBaseAlpha * a)` —— **基色的 alpha 被乘进每一层**。而 antd 自己的默认值:亮色 `#000`(**不透明**)、暗色 `rgba(255,255,255,0.2)`(就是注释里说的"抽屉白色发光")。所以我给的 `rgba(20,27,45,0.16)` 让 `boxShadowDrawerLeft` 三档从 `0.08/0.12/0.05` 塌成 `0.0128/0.0192/0.008`,**淡 6.25 倍**。两处改为不透明:亮 `#141B2D`、暗 `#000000`。令牌旁写明"这是基色、别加 alpha、别直接写进 box-shadow"。

**旧断言的无效性也当场证明了**:M1(退回 `rgba(...,0.16)`)只红新的量级断言,而旧那句 `not.toMatch(/255,255,255/)` **在这个变异下是绿的** —— 两种写法都不含白色。一条在坏与好两种状态下都绿的断言,不是断言。

**台账那条 MEDIUM 是错的,已改**:`MAP_PAIRS` 补 `colorBgContainer|colorBgElevated` 照字面做会让**暗色**红在非缺陷上 —— 这对只有亮色恒等(antd 亮色两者同为 `getSolidColor(bgBase, 0)`,暗色是 8 与 12),而我们的令牌恰好镜像了这个结构(亮色同为 `#FFFFFF`,暗色 `#1F2229`/`#262A31`)。`MAP_PAIRS` 已按模式拆成 BOTH / LIGHT_ONLY 两组。

**六处变异,预测先写,全部命中**:

| 变异 | 预测 | 实测 |
|---|---|---|
| M1 亮色退回 `rgba(...,0.16)` | 只红亮色量级 | ✅(旧色相断言不红,正是要点) |
| M2 暗色退回 `rgba(0,0,0,0.45)` | 只红暗色量级 | ✅ |
| M3 `colorFillTertiary` 退回 hover 色 | **绿,一条不红** | ✅ —— **判据缺口,见下** |
| M4 删掉 `controlItemBgHover` 的 EXEMPT | 明暗两条恒等都红 | ✅(`#EBEDF0 ≠ #F2F3F5` / `#2E333B ≠ #262A31`) |
| M5 按台账字面把那对放进 BOTH | 只红暗色恒等 | ✅ —— 证明偏离台账是必要的 |
| M6 antd 源码 `?raw` 读成空串 | 明暗两条红在自检行 | ✅ |

**判据缺口(如实记,没堵)**:M3 全绿说明**填充阶归位这件事没有任何自动检查守着**。恒等闸门只查"成对的两个值是否相等",不查"值是否符合设计意图" —— 退回 hover 色之后 `controlItemBgHover` 与 `colorFillTertiary` 反而又相等了,EXEMPT 变成多余但不报错。要堵得断到具体令牌值(如 `colorFillTertiary === v('--color-fill')`),那等于把实现抄进测试;暂不做,记在这。

**`node:fs` 换成 `?raw`**(R3 review 的结论:`@types/node` 无法限制在单文件)。踩到一个前提:**vitest 默认 `css: false` 会把 CSS 导入桩成空串,连 `?raw` 也是空**,不报错 —— 那样 `v()` 全空、`defined()` 滤光所有键,spec 里每条恒等断言都变成恒真。已开 `test.css: true` 并写明理由。

**浏览器探针 24 种组合(明暗 × 6 accent × 2 密度)全绿、零控制台错误**,但过程里踩到两件 spec 证明不了的事:

1. **端口静默漂移。**首跑探针"什么都没渲染"—— 因为 5174 被前几轮残留的 dev server 占着(`TaskStop` 停掉了 npm 包装进程,**vite 子进程活了下来**),Vite 默认行为是**静默挪到下一个可用端口**,我的 server 去了 5175,而探针连上 5174 拿到的是 **Vue 模板的页面**。已加 `server.strictPort: true`:宁可起不来,也不要连上另一个应用还以为是自己坏了。**这条要记住:`TaskStop` 一个 `npm run dev` 不保证 vite 子进程也死。**
2. **两条 antd v6 改名,`tsc` 一条都不红**:`Space.direction → orientation`、`Alert.message → title`。正是台账「参考项目」段落里点名的那类地雷。按约定查了离线 CLI 而不是凭记忆改(`antd info Space/Alert --version 6.5.1`,两个新名都确认存在),并全仓扫了 `bodyStyle`/`iconPosition`/`Card.bordered`,无其他残留。

探针页(`App.tsx`)换成 B2 版:本地 `useState` 驱动明暗/accent/密度,把阴影量级、恒等抽样、NaN 尺寸、tokens 是否进文档逐条渲染成肉眼可见的红绿。**B3 落 store 后把本地 state 换成 store**,`useAntdTheme` 入参形状不变。`Density` 暂定义在 `antd-theme.ts`,**B3 移进 app store** 后这里改成转口。

**B2 review 处置**(`6254b35`;3 HIGH / 3 MEDIUM / 3 LOW,全部处置)。

**[HIGH]三个具名阴影是角色名,不是序数阶梯 —— 我按 1/2/3 装反了。**逐个 grep `token.xxx` 的消费者核实:`boxShadow` **只有 Modal** 用、`boxShadowSecondary` 是 Dropdown/Select/Tabs/FloatButton、`boxShadowTertiary` 是 message 与 **Segmented 选中滑块**;antd 原档里 `boxShadow` 与 `boxShadowSecondary` **值完全相同**(都是 6/16 大浮层),`Tertiary` 才是 1px/2px 的微阴影。而 Naive 的 `boxShadow1/2/3` 是由小到大 —— 照序号搬,Modal 拿到卡片微阴影,**一个 24px 高的 Segmented 滑块下面挂 48px 模糊、0.18 alpha**。已对调。

  **最该记的不是这个错,是它怎么活过验收的**:那个缺陷在 24 轮探针里**每轮都在屏幕上**(探针页顶部正好三个 Segmented),而我报了"全绿"——因为探针只读 `boxShadowDrawerLeft` 的 alpha,**三个具名阴影一个都没探**。"24 种组合全绿"这句话的强度完全取决于探针集覆盖了什么,而我当时没有问这个问题。现在探针断模糊半径的单调性,装回去会在 24 组合里逐个红出 `Modal 6px > 弹层 30px > 滑块 48px`。

**[HIGH]探针页那条"tokens.css 进了文档"恒真。**它断的是 `colorBgContainer !== '#000000' && !!colorPrimary` —— 两句都永真(`colorPrimary` 必有值,`bgContainer` 明暗两种 algorithm 下都不可能是纯黑),**在它自己声称检测的失败模式下照样绿**。这正是本条 commit 花大力气消灭的那类断言,在探针页原地复活了一个。改成直接问文档要 `--color-shadow`。

**[HIGH]`colorFill` 漏了,而闸门结构性看不见它。**填充阶覆盖了 Alter/Quaternary/Tertiary/Secondary,唯独漏掉顶端的 `colorFill`,于是文字按钮 **hover 是不透明 `#EBEDF0`、pressed(走 `colorBgTextActive ← colorFill`)却是半透明黑** —— 不是深浅差一档,是**种类**差异,落在有色或图案底上露馅。闸门看不见的原因是结构性的:`colorBgTextActive|colorFill` 两侧**都不在**我们的覆盖集里,`touched` 直接把这对滤掉了。已补,并加了一条"填充阶没有成员停在半透明默认上"的性质断言。

**[MEDIUM]M3 那个我记为"要堵得抄实现"的缺口,其实堵得上,而且不用抄。**review 给的办法是**让豁免双向生效**:登记了"有意打破"的每一对,今天必须**真的还是断开的**。把 `colorFillTertiary` 退回 hover 色之后,`controlItemBgHover` 与它反而又相等了 —— 那条豁免静默变成多余,而"你声称打破了但其实没打破"是**可观测的结构事实**,不需要任何令牌字面量进测试。变异证实:上一轮这个变异**一条都不红**,现在红两条。

**[MEDIUM]map 层还漏了两对(都在暗色)**:`getSolidColor(bgBase,26)` 在暗色下是**三路**恒等(`colorBorder`/`colorBorderDisabled`/**`colorBgSpotlight`**),`getAlphaColor(textBase,0.04)` → `colorFillQuaternary`/**`colorBgBlur`**。已加 `MAP_PAIRS_DARK_ONLY` 并把两对登记进 EXEMPT 带理由(Tooltip 是**反色**浮层、毛玻璃染色,本就不该跟随我们的表面阶)。

**[MEDIUM]阴影用例在"tokens.css 没加载"下三条断言全绿** —— 亮色会回落 antd 默认 `#000`,那也是不透明、派生 alpha 也正好是 0.08/0.12/0.05,于是这一轮**无法区分"令牌对"和"令牌根本没到"**。恒等用例本来就有自检行,这里没有,标准不一致。已补。

**[LOW]** `ALIAS_PAIRS` 从 `> 30` 改成 `toBe(51)`:地板只抓得住归零,抓不住"51 掉到 31"(antd 一次 minor 重排就可能少查 20 对而全绿)。**[LOW]** `afterAll` 那句"同一个 happy-dom 环境"是错的 —— vitest 默认 `isolate: true`,跨 spec 文件不会串;复位该做,但理由是同文件内后续 describe。**[LOW]** 暗色派生阴影的天花板已写进 `tokens.css`:基色 alpha 已是 1,派生只能到 0.08/0.12/0.05,而手写三档是 0.30/0.44/0.52(**重 3-6 倍**),所以暗色下 Modal/Dropdown 会明显重于 Drawer —— 这是 antd 的结构约束,`colorShadow` 修不了,**别再有人把 alpha 加回去**。

review 同时确认了几件我拿不准的:亮色 `#141B2D` 比纯黑淡 1-2 级、方向对且不会过重;暗色纯黑已是 `colorShadow` 这个杠杆的**最大值**,比原来的 `rgba(0,0,0,0.45)` 严格更好;`ALIAS_PAIRS` 正则今天**零遗漏**(51 对,其余 12 处提到 `mergedToken` 的全是算术/合成,非恒等);`strictPort` 不妨碍 E2(CLI 的 `--port` 优先级高于配置)。


**B2 处置的核查 lane(`2531bfb`)——它在我的处置里又找出一个新缺陷,这次是我自己引入的。**

**填充阶顶端两档塌成同色,按下去毫无反馈。**上一条我补 `colorFill` 时给了 `--color-fill-hover`,而 `colorFillSecondary` 也是它。antd 的 filled 按钮与 `Input.Search` 把三态分别接到 `colorFillTertiary`(静息)/`colorFillSecondary`(hover)/`colorFill`(按下)(`button/style/variant.js:176-178` 逐行核过),所以 hover 与 active **渲染完全一样**。

  **两道现成的闸门都结构性看不见它**:恒等闸门看不见(这三个之间不是 antd 声明的恒等),半透明检查也看不见(三个值都不透明)。这正是「给了值但给错」——上一轮我自己在 review 里问过"这条能否被给错值骗过",答案是能,而且**当场就骗到了我自己**。已加第三档令牌 `--color-fill-active`,并补一条断"三态互不相同"的用例(变异证实:退回 hover 档红两条,报文直接列出塌陷的三个值)。

  底部那对(`colorFillTertiary` 与 `colorFillQuaternary` 同为 `--color-fill`)**也塌了但无害** —— 逐个组件查过,没有任何组件同时消费这两个,不构成相邻状态。

  Naive 侧不需要第三档,所以这是两个模板**有意分叉**的一处,记在令牌旁。

核查同时确认的:三条新用例都真的会红(阴影单调性用真 `getDesignToken` 跑了全部 **6 种排列**,只有正确那一种通过,包括历史 bug 和 shadow-2/3 对调都红);四条 EXEMPT 今天确实都断开且断得有意;`ALIAS_PAIRS` 独立复算确为 **51**。

**两处如实的边界**(记下不改):①阴影单调性只跑亮色 —— 映射是三行无分支代码,两种主题走同一条路径,对"变量装错槽位"这个缺陷类够用;只有"tokens.css 里暗色三档自己写得非单调"这种作者失误会漏。②`maxBlur` 取最大 px 数,只对当前这种「偏移+模糊」两段、无扩散的值成立,已在代码旁注明真到那天怎么改。

**核查还指出一件更大的事:`web-react/` 目前不进任何 CI**(`web-ci.yml` 的 paths 只有 `web/**`)。也就是说上面所有"四件套绿"全是本地手动跑的,`ALIAS_PAIRS: toBe(51)` 这个钉子今天**没有任何自动化在执行**。这条已经是台账的 **E2**,不是新发现,但值得在这里点明它的当前后果。


下一条:**B3**(四个 Zustand store)。

### 2026-07-20 · R4 框架无关层落进 src/

`fbac63a`。types / locales / tokens.css / theme / utils / schema.d.ts 全部落地。**一处也不用改写导入** —— 这批文件之间的相对导入本来就都在同目录内(只有 `types/api.ts` 引 `./menu`),`@/*` 改写是我预想中的工作量,实际为零。

**台账这条与 B5 冲突,已按 B5 处置**:R4 原文写 `api/{index,schema.d}.ts` 一起搬,但 `api/index.ts` 第一行就是 `import { client } from './client'`,而 B5 明文规定**不搬 archive 那版 client**(那版为服务两个模板抽了 `ApiAdapter`/`createApiClient` 工厂,现在只有一个宿主,工厂没有存在理由),要以 `dev` 上 `web/src/api/client.ts` 为蓝本重写。所以 R4 只带纯生成物 `schema.d.ts`,**`api/index.ts` 随 client 一起走 B5**。

`locales/ext.ts` 按台账内联进 `src/locales/index.ts`。顺手改了 `ext/README.md` 里两处**已经不成立**的措辞:它还写着"那两个文件只是转口桶,真源在 `web-shared/locales/`,两个官方模板共用"和"文案与 Vue 模板共用同一份真源" —— 共享层没了,在本模板里它们就是真源。这类过时文档不会让任何检查变红,只会误导消费者。

**四件套绿,但这批文件四件套证明不了什么** —— 它们还没有消费者,typecheck 只看类型、build 会把没人用的摇掉。所以另外查了两件实事:①`tokens.css` 真进了产物(dist 里 2925B 的 CSS,抽查 `--color-primary`/`--color-bg-container`/`--color-fill` 都在);②`messages` 确实被摇掉了(产物里 0 命中,符合"没人消费"的预期)。

**合并逻辑补了 spec,因为它是这批里唯一的非平凡逻辑。**四处变异,预测先写,全中且判别干净:

| 变异 | 红了哪条 |
|---|---|
| `deepMerge` 改成浅合并 | 只红「深合并不顶掉兄弟键」 |
| 去掉 locale 前缀过滤 | 只红「中文扩展不漏进英文」 |
| `isPlainObject` 允许数组 | 只红「数组不往下钻」 |
| `deepMerge` 就地改 base | 红「不改动入参」**外加**「新命名空间」—— 共享的 BASE 被前一条用例改过,污染串到后面。这正是那条不变性用例存在的理由 |

**已知判据缺口原样带过来,没堵上**:`ext/` 目录按设计是空的(那是消费者的地盘),所以**没有任何常驻用例能证明 `import.meta.glob('./ext/*/*.ts')` 这个路径打得中** —— 把它写错成 `'./extt/...'`,上面六条用例一条都不会红。已在 spec 顶部注明。

**R4 review 处置**(1 HIGH / 3 MEDIUM / 5 LOW;搬运保真度 10/10 逐字节相同,无夹带、无契约漂移)。

**[HIGH,事实错误,已改]我写的「archive 上也没有直接测过合并逻辑」是假的。**archive 上有 **8 条**直接测 `withExt` 的用例,在 **`web/src/locales/index.spec.ts`**,而且**那个文件在本分支 HEAD 上一直活着**。我只读了 `archive:web-react/src/locales/index.spec.ts`(那 8 条是 i18next 接线,不是 9 条),就得出"没测过"。**而那个文件的头部注释逐字告诉了我去哪找**:「深合并规则本身在 `@shared/locales/ext`,由 `web/src/locales/index.spec.ts` 的 8 条用例钉着 —— 两个模板引的是同一个函数,这里不重跑。」

  后果不只是措辞:我以为在补白,实际是**平行重写**,于是没去 port 那 8 条,漏了「没有 ext 文件时原样返回」(已补)。另一条「对象覆盖字符串时 ext 侧胜出」**判断为冗余不补** —— 它与「同名标量键」走同一分支(`isPlainObject(baseVal)` 为假),行为等价。**是判断后不补,不是漏掉。**新 spec 里那条「数组不当作对象往下钻」是 archive 8 条里没有的净增益,已记进 `refinement-ledger` 反向 port 回 Vue 侧。

  **这是同一类错误的第二次。**第一次是 R2 那句「`f1f579e` 是整个重构里唯一一处动 `web/`」。两次的形状完全一样:**把「我没找到」当成「不存在」,而没让这个判断本身可证伪。**上次一条 `git log --stat` 能打脸,这次一条 `git grep -l withExt archive/…` 能打脸,两次我都没跑。台账开头的判据纪律要加一条:**凡是要写下"没有 X"/"唯一的 X"这类否定或全称断言,先跑一条能打自己脸的枚举命令,把它和结论一起记下来。**没有那条命令,这个断言就还是个印象。

**[MEDIUM,已修]`messages` 的形状会让 B4 静默全挂。**我导出的是裸的合并结果,而 i18next 把 `resources` 的**第二层当命名空间** —— 直接传进去的话 `error`/`common` 会被当成 ns,默认 ns `translation` 不存在,而 i18next 对缺键的处理是**返回键名本身**,于是整站文案变成一堆点分英文,不抛错、不告警、四件套全绿。已改成导出包好 `translation` 的 `resources`,B4 无从写错。

**[MEDIUM,已修]`mod.default` 缺守卫 = 消费者写错一个字就整站白屏。**`ext/` 是消费者的地盘,是本文件**唯一面向外部输入**的地方,所以类型声明它非空不算数。消费者把 `export default {...}` 写成 `export const foo = {...}` 时 `mod.default` 是 undefined,而 glob 是 eager 的、在模块顶层求值 —— `deepMerge` 当场抛在 import 链上,白屏,且那句 `Cannot convert undefined or null to object` 完全指不到是哪个文件。已加守卫:**点名路径**后跳过,不静默。archive 那 8 条也没覆盖这个。

**[LOW,已修]`main.tsx` 的 import 顺序。**`tokens.css` 原先写在 `./App` 之后。对渲染时读取无所谓(所有 import 求值完才轮到模块体),但 **ESM 求值顺序就是书写顺序**,所以 `App` 及其整条传递依赖图会先求值完 —— 期间任何**模块求值期**的 `getComputedStyle` 都读到空值,而空值喂给 antd `ConfigProvider` 的失败方式是颜色悄悄退回默认,不抛错。**B2 的主题桥正好是可能踩这个的地方。**已提到 `./App` 之前,注释写明区别。

**[LOW,已部分堵]glob 缺口的说法原先比事实更悲观。**目录路径那一段**是可钉的**:用已提交的 `ext/README.md` 当锚(不引入任何 fixture),`./extt/`、`../ext/`、目录被挪走都会红。钉不住的只有 `*/*.ts` 那一段的深度与扩展名 —— 那一半是结构性的:一个「按设计为空」的目录,空结果与坏 glob 在运行时不可区分。原注释整体说"不可钉",会让下一个人连能做的那一半也放弃。已改到这个粒度并加了探针。

**[LOW,记下待办]`ext/README.md` 有一句现在为假**:「`locales/index.ts` 里已把 i18next 的默认 `{{name}}` 改过来了」—— B4 才成立。同段的 `useTranslation()` 同理。**文档超前于代码**,方向与之前那两处过时措辞相反。不改(3 行改动 + 一次回访不如记一笔),但**B4 落地时必须回来核对** —— 这一条现在没有任何检查会让它变红。

三条新用例各自做了变异,只红自己那条:撤掉守卫 → 「忘写 default」红;glob 写错 → 探针红;守卫降级成静默跳过 → 「必须点名」那句红(`ab651ab`)。

下一条:**B2**(主题桥 + review 处置,含那条 `--color-shadow` 亮色回归)。

### 2026-07-20 · R3 脚手架(自包含)

`414a2e4`。三处共享层接线删干净:`@shared` alias、`openapi-fetch` alias、`server.fs.allow` **整条删除**(不是收窄)。删而不是改,是因为那一项设置会**整个替换**默认白名单,写漏一项就是 dev server 连 `GET /` 都 403,而 lint/typecheck/build 一个都发现不了;自包含之后源码全在项目根内,默认值即最紧。`tsconfig` 的 `paths` 只剩 `@/*`,`include` 去掉 `../web-shared/**`,`lint` 从 `cd .. && oxlint --config …` 简化成裸 `oxlint`(与 `web/` 一致,配置自动发现),补了自己的 `gen:api` 与 `openapi-typescript` 依赖。

`types` 按台账**不加 `"node"`**。它是项目级的,加上之后 `process.env` 在浏览器源码里也能静默通过 typecheck 而运行时是 undefined,且 `web/` 那边没有这条——同一行代码搬过去会红,两个模板的判据必须一样紧。确认 `vite.config.ts` 不在 `include` 里,所以它自己用 `node:fs`/`node:url` 不受影响。

**验:lint / typecheck / build 三件绿。第四件 `npm test` 退出码 1** —— 还没有任何 spec。**不加 `passWithNoTests` 去糊掉它**,那只会把"一个 spec 都没跑"和"跑过且通过"混成同一个绿。第一批 spec 在 R4 落地时再验这一件。

**顺带验了 `test.include` 里的 `.tsx`,因为它的失败模式是彻底静默。**预测先写:两个临时 spec(`.ts` + `.tsx`)都会跑;把 glob 改成只 `.ts`,`.tsx` 那个会消失且 vitest 一声不吭。实测完全命中——现状 `Test Files 2 passed (2)`,变异后 `1 passed (1)`,**没有警告、没有报错、依然是绿的**。B3 的 `auth-hooks.spec.tsx` 正是这个形状,这条要是漏了它会一直"通过"到没人发现。验完删除探针。

**一处自打脸**:我第一次查 `npm test` 退出码时写的是 `npm test 2>&1 | tail -8; echo $?`,报出来 `0`——那取的是 `tail` 的状态不是 `npm` 的。本轮反复在说"检查要能失败",而我自己这条检查报了个假绿。改成先重定向再取 `$?`,真实退出码是 1。

**R3 review 处置**(打回 1 HIGH / 3 MEDIUM / 5 LOW,代码可发,阻断项只有那条书面保证)。

**[HIGH,已改]`/// <reference types="node" />` 不是文件级豁免 —— 我写反了,而且写进了 tsconfig 注释、commit message、本台账三处。**自己复验了三段 tsc:①只有一个用 `process.env` 的浏览器文件 → `TS2591` 报错(守卫在);②只有一个带三重斜线的 spec → 通过(三重斜线确实管用);③**两个同时存在 → 报错消失**。原因是模块 import 是文件级的而**全局声明不是**:`@types/node` 只要被任何一个文件拉进 program,`process`/`__dirname`/`Buffer` 就进了全局作用域,对同一 program 里所有文件生效。也就是说这条守卫会在 **R4/B2 第一次照我写的那句话行事时静默自我关闭**,四件套全绿。**这是"不可能失败的检查"的镜像版:一个在被使用的瞬间关掉自己的检查。**

  处置:tsconfig 注释改成事实,并钉死替代方案 —— **spec 要读文件就别走 node**,用 `import css from '@/styles/tokens.css?raw'`(`vite/client` 已声明 `?raw` 模块),零 tsconfig 手术,且 spec 与浏览器走同一套解析。真到非 node 不可那天,单开 `tsconfig.spec.json` + `references`,而且**必须真的挂上** —— `web/tsconfig.node.json` 就是个没人引用的死配置(review 实测 `vue-tsc --listFiles` 里 `@types/node` 命中 0),别重蹈。**B2 写主题桥 spec 时按这条来。**

**[MEDIUM,已改]`fs.allow` 的默认值不是"项目根"。**结论(删除正确)成立 —— review 实起 dev server 实测:后端 `appsettings.Development.json`、`dev-jwt.key`、仓库根 `CLAUDE.md` 经 `/@fs/` 全 403,白名单只有 `web-react/` 一项,比原先的 `['.', '../web-shared']` 严格更紧。但**理由错了**:Vite 的默认是 `searchForWorkspaceRoot()`,逐级上溯找 `pnpm-workspace.yaml`/`lerna.json`/带 `workspaces` 的 `package.json`。今天解析到 `web-react/` 自己,**仅仅因为仓库根还没有 package.json**。哪天一仓两模板顺手在根上加了 workspaces,白名单会**静默扩张到仓库根**,那三个路径立刻可读而四件套一个都不响。注释已改成这个事实,并写明届时必须显式写回 `fs: { allow: ['.'] }`。

**[MEDIUM,已改]`test-setup.ts` 是 R3 唯一有逻辑的文件,却一次都没被执行过。**补了 `test-setup.spec.ts`,顺带把 `npm test` 从退出 1 变成真绿 —— 比"记录一条偏离等 R4"便宜。两处变异都先写预测再跑,全中:①`length` 恒返回 0 → 红(`expected +0 to be 1`);②摘掉 `setupFiles` → 红,而且报的**逐字就是**该文件注释里警告的那句 `Cannot read properties of undefined (reading 'setItem')` —— 证明这个 shim 在当前 Node 上仍在承重,不是抄来的老配置。

**[LOW,已改]**`setupFiles` 的隔离粒度是**每 spec 文件一次**,不是每 test 一次;原注释写的"测试之间互不串味"过强。已改成文件级,并写明要 test 级隔离的 spec 自己写 `beforeEach` —— 不在 setup 里统一加,是因为 R4 要搬的那批 store spec 是照现在这个语义写的。

**[LOW,记下不改]**`vite.config.ts` 处在所有检查的盲区:不在 `tsc` 的 `include`、oxlint 的 `env` 没有 `node`、运行时走 esbuild 不做类型检查,所以里面 `process.env`/`readFileSync`/`fileURLToPath` 三处 node 用法**一个都没被校验过**。archive 上同样如此(那边的 `types:["node"]` 也覆盖不到它,同样不在 include 里),**不是本次引入的回归**,`web/` 那边也是同样的空档。不为它加一套 project references,记着。

**[LOW]**`414a2e4` 的 commit message 通篇讲"删",没提这次还**新增**了 `gen:api` 与 `openapi-typescript` 依赖。已提交不改写,记在这。

四件套复测(不走管道、取真实退出码):lint / typecheck / test / build 全部 0。

下一条:**R4**(框架无关文件落进 `web-react/src/`)。

### 2026-07-20 · R2 + R2b 带回 `web/` 的三条修复

**R2**(`abd9d1e`)。搬 `schema.d.ts` 后起 MinimalHost 真跑了一次 `gen:api`,生成结果与搬过来的那份**逐字相同**——搬运可信。`vue-tsc` 绿。

**R2 现场发现,已开 E5**:台账原先把"字段缺了几个月没人发现"归因于 `configApi.siteInfo()` 那个手写内联类型。**归因错了,而且方向是往轻里错的。**`unwrap<T>(res: { data?: unknown }): T` 把响应体丢成 `unknown` 再按调用方断言强转,所以 97 处 `unwrap<...>` 里**具名的那 90 处一样是断言**——`schema.d.ts` 陈旧时任何端点都不会红。照原归因去"修好那个内联类型"会让人以为这类漂移关上了,其实一点没关。CI 里也确实没有 `gen:api` 闸门(查过 `.github/workflows/*`)。

**R2b**(`8b52f71`)。cherry-pick 后五个文件与 archive 逐字一致,e2e 7/7 绿。但绿不算数,做了五处变异,**预测先写进文件再跑**(`r2b-predictions.md`),四条命中、一条如实落空:

| 变异 | 预测 | 实测 | 说明 |
|---|---|---|---|
| M1 恢复 `menus.length <= 1` | 绿 | **绿** | **信息量近乎为零,见下** |
| M3 新断言的 URL 改成 `/workbench` | 红 | **红** | 报出真实观察值 `/module`,证明新断言在对真实状态求值,没被跳过 |
| M2a 退回按名字查找、**保留**选中态守卫 | 红 | **红** | 失败点正是「文件管理」,收到的 class 里没有 `--selected` —— 与 A6 描述的目录 Id 30 遮蔽叶子 Id 78 完全对上 |
| M2b 退回按名字查找、**删掉**守卫(A6 之前原貌) | 绿 | **绿** | 假通过本身:点开的是目录、页面没换,`expectContentRendered` 断的是上一页 |
| M4 删掉展开动画的钉子 | 不确定 | 绿 3/3 | **未做确定性变异,不是做不了,见下** |

**以下四条是 R2b review 打回来的,原文已就地改正,把错处连同它为什么错一起留着——这批记录的价值全在这儿。**

**① M1 根本不是判别实验(我把功劳记错了地方)。**恢复旧断言 + 系统没变 → 绿,这件事与两个互斥假设**都相容**:(a) 断言恒真;(b) 断言正确且系统正确。它复现的不过是 A6 之前的状态,而那个状态本来就是绿的。真正证明恒真的是**源码 + M3**:`MenuService.cs:50` 的 `if (grantedMenuIds.Count == 0) return [];` 让零授权用户拿到 0 个模块,于是停在 `/module`;而 M3 变红时报出的实测值 `/module` 独立坐实了这个状态。要做成判别实验,得动 **SUT** ——把 `ComputeMyMenuTreeAsync` 改成不过滤再跑旧断言,必须仍绿。**没做**。

**② 「菜单过滤坏成任何样子都不红」这句话太宽。**侧边栏和门户是**两个方法**(`ComputeMyMenuTreeAsync:78` / `ComputeMyModulesAsync:42`)。坏在侧边栏那个 → 模块仍为 0 → 仍停 `/module` → 旧断言绿,这部分我对;但坏在门户那个并泄漏 ≥2 个菜单 → 用户进得去 → 侧边栏 ≥2 → 旧断言**会红**。准确说法是:**对它自己声称测的那件事(侧边栏菜单过滤)恒真**,不是对所有破坏恒真。

**③ M2a/M2b 得出的结论过强,已推翻。**我原先写"A6 的价值在守卫不在下标配对"。反例:**同名叶子**。守卫断的是重查到的那个 `item` 自己变成选中态——只要点到的是个真叶子它必然选中,守卫恒过;两个不同目录下有同名叶子时,按名 + `.first()` 会把同一个叶子点 N 次、另一个从未访问,**覆盖度静默缩水而守卫一路绿**,下标配对则从根上没有这个歧义。所以两者的失效类**部分不相交**:守卫抓「点到了不可选中的节点」(目录 Id 30 遮蔽叶子 Id 78),下标配对抓「覆盖度缩水」(同名叶子,当前种子里观察不到)。照原结论去省掉下标配对就是引回归。

**④ M4 的措辞错了:把一个「没做的实验」写成了「做不了的实验」。**这正是台账最该避免的说法。竞态是真的(Playwright 的可见性判据是非空 bounding box,`NFadeInExpandTransition` 高度 0 的首帧确实判为不可见),而它**可以**被做成确定性变异——`page.addStyleTag` 把展开过渡拉到 3s,或 CDP `Emulation.setCPUThrottlingRate`。任一之下删掉 `helpers.ts` 的钉子,多趟循环会二次点击 → **收起** → 叶子数掉 → 必红。**如实记成:本机时序 3/3 未复现,确定性变异未做。**

**⑤ 变异覆盖率没有台账字面看上去那么高。**R2b 做了 5 处变异,但**只覆盖到 2 个修复点**(RBAC 恒真断言、按名查找)。A5/A6 另外四个修复点**零变异**,靠推理过的:`workers: 1`、用例自建前置 `enterApp(SYSTEM_APP)`(这是 A5 最大的一条)、`LEAF` 判据换成 `:not(:has(arrow))`、「只点还没展开的目录」。写"做了五处变异"容易被读成"这批修复都验过了",**没有**。

**⑥ R2 的验证命令序列必须记下来,否则事后无法区分"有效检查"与"恒空检查"。**实际序列是:`git show archive:… > web/src/api/schema.d.ts` → 起 MinimalHost → `npm run gen:api`(覆盖同一文件)→ `git show archive:… > /tmp/archive-schema.d.ts` → `diff /tmp/archive-schema.d.ts web/src/api/schema.d.ts`。**比的是「实时后端生成的输出」vs「从 git 独立取出的 archive 内容」,不是文件跟自己比**,所以搬错文件或 archive 那份本身有错都会被抓住。若换成"生成后 `git add` 再 `git diff` 工作区"就是恒空的假检查——两者事后从 git 历史上分辨不出来。另外已实测 `openapi-typescript` 7.13.0 在后端没起时**硬崩、退出 1、不写文件**,所以"后端没起 → 文件没动 → diff 恒空"这条假失败路径不存在。

**⑦ 还有一条比 `gen:api` 更强、且与命令顺序无关的静态证据,R1 review 当时把它当噪声丢了。**那批 `isDelete` 属性重排不只是"codegen 排序抖动":新文件的顺序是 `isDelete → createTime → … → id`,即**最派生优先**,与 `BaseEntity.cs` 当前继承链(`PrimaryId ← AuditEntity ← BaseEntity`)逐字对上;而 `dev` 那份的顺序用当前继承链**推不出来**。也就是说新文件是当前后端源码会生成的那一份,旧的不是——纯静态、不依赖任何命令。

**R2b review 顺带发现两条潜伏的空转断言,已修(`1b7bd53`)**:①`LEAF` 那条"只有目录带箭头"的判据只在纵向菜单下成立,而 `AppHeader.vue` 有三个 `mode="horizontal"` 的 `n-menu`,其目录不带箭头。已限定到 `.sidenav`,并用探针量过:顶栏布局下不限范围会多抓 **2** 个非侧边栏项、限定后为 0(今天不发作只因 Playwright 每用例新 context、偏好为空 → 默认纵向)。②`.card` 的 `toHaveCount(0)` 冗余且脆:空态与卡片网格是 `v-if/v-else` 互斥分支,断到空态可见已蕴含零卡片,而按类名数"不存在"改个类名就永远绿——已删。

下一条:**R3**(`web-react/` 脚手架,自包含)。

### 2026-07-20 · R1 保存与重置

做了 R1。三条前置在动手前逐条查过,全部成立:

- `git ls-tree dev -- web-shared web-react` 输出为空 —— 抽取**从未进 `dev`**,所以这不是回滚,只是不再做第二次。
- `git ls-remote --heads origin feat/web-shared-extract` 为空 —— 分支从未推远端,改名不影响任何人。
- 工作树只有未跟踪的本文件,无待提交改动。

改名 `feat/web-shared-extract` → `archive/web-shared-extract`(仍指 `c59f76f`,B4 那次提交),从 `dev`(`71d660d`)开 `feat/web-react-template`。本文件作为未跟踪文件跨 `git switch` 带过来了,在新分支上首次提交。

**预测与实测不符**:预期 `git switch` 之后 `web-shared/` 与 `web-react/` 都消失,实测 `web-shared/` 消失而 **`web-react/` 还在** —— 它装了 `node_modules`(252M)、`dist/`、`.omc/`,全是 gitignore 产物,`switch` 只清跟踪文件。已确认无源码残留后整个删除。

删 `node_modules` 而不是留着省一次安装,是**有意的**:R3 会写一份新的 `package.json`,而旧 `node_modules` 里的包即使不再被声明也照样解析得到 —— 本地绿、CI 红,正是本台账反复防的那类静默失败。R3 反正要装一次。

`grep -rn '@shared\|web-shared' web/src web/vite.config.ts web/tsconfig.json` 为空,确认 `dev` 上的 `web/` 本就自包含,R2 之外无需再动它。

**R1 review 结果(verifier lane,只读)**:四点里三点成立——`archive` 的 19 个 commit 完好、新分支确实等于 `dev` + 一个文档提交、`f1f579e` 那份 `schema.d.ts` 与 `dev` 版的全量 diff 只有 `logo` 字段新增 + 十余处 `isDelete` 声明顺序抖动(codegen 排序差异,TS 结构化类型不看声明顺序),**没有任何为共享层做的形状改动**,R2 那句 `git show ... > web/src/api/schema.d.ts` 成立。

**第四点不成立,是本轮最有价值的产出**:R2 里"这是整个重构里唯一一处动 `web/`"被证伪。`archive` 上还有三个 commit 碰了 `web/`:

- `f3e70ba`(A5)、`4ea84ec`(A6)—— 只改 `web/e2e/*` 与 `playwright.config.ts`,**与共享层完全无关**,是独立的测试质量修复(含一条恒真的 RBAC 断言)。已开 **R2b** 带回。
- `371a07a`(B2 review)—— 新增的 `web/src/theme/mix.spec.ts` 测的是 `derivePrimary`,而那个函数只存在于 archive 的共享层,`dev:web/src/theme/mix.ts` 里根本没有,**不能照搬**。但它背后的发现是真的:`dev` 上 `naive-theme.ts:9` 与 `useTheme.ts:18` 各写了一遍同样三个魔数。已记进 `docs/refinement-ledger.md` 的 **A6**(那里才是 `web/` 打磨件的家),并写明**今天观察不到 case 不一致**——两个消费端都不区分大小写,这条的价值在消重不在修 bug,别把它写成 bug 骗自己。

另外确认 `backend/` 零改动(`git log --name-only dev..archive -- backend/` 为空),`.github/` 的改动全是围绕共享层闸门与 `server.fs.allow` 收窄——而 `dev:web/vite.config.ts` 本就没有 `server.fs.allow: ['..']`,**那个安全洞是抽取自己开的又自己补的,`dev` 从未暴露**,随重置一起作废。

教训按最一般的形式记下来:**台账条目里凡是"唯一/仅此一处/其余都是"这类全称断言,写的时候都是从意图推出来的,必须当场用 `git log --stat`(或等价的全量枚举)验一遍再写进去。**这次是我自己写的台账,一轮之后自己已经不记得推断和事实的分界了。

下一条:**R2**(把 `f1f579e` 带回 `web/src/api/schema.d.ts`)。注意它要起 MinimalHost 真跑 `gen:api` 复核,**不与任何重进程并发**。R2 之后接 R2b。
