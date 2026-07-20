# React 模板台账（web-react/）

> 来源:2026-07-20 维护者裁定推翻 `web-shared/` 共享层方向,改为**两个自包含前端模板**。前身 `docs/react-port-ledger.md` 连同 A1–A3/B1–B4 的实现与两份 review 全部留在本地 `archive/web-shared-extract` 分支(未推远端),本台账取代它。
> 驱动方式:仿 `docs/refinement-ledger.md` ——逐条执行、每条独立英文 conventional commit、可断点续跑。
> 执行协议:**每次只做一条**;开工前有设计取舍/命名/行为边界疑问先向维护者确认;做完跑验证、勾选本文件、单独提交。**每条完成后另起 review lane**(`code-reviewer` / `verifier`),不在同一上下文自审。
> 验证纪律:每个模板各自跑四件套——`npm run lint` / `npm test` / `npx tsc --noEmit`(Vue 侧 `vue-tsc`) / `npm run build`。**本机内存紧张,一次只跑一个重进程,绝不与 `dotnet test` 并发。**涉端点跑 MinimalHost 实打 + `npm run gen:api`;体验件 `npm run dev` 实点。
> 判据纪律:**跑绿的用例什么都不证明,直到它被变异证伪。**凡要写下"没有 X"/"唯一的 X"这类否定或全称断言,**先跑一条能打自己脸的枚举命令**(`git log --stat`、`git grep -l`…),把命令与结论一起记下来 —— 否则那只是印象。这条是被同一类错误坑了两次之后加的(R2 的"唯一一处动 web/"、R4 的"archive 上没测过")。每条改动过的断言都要证明它还会红:预期失败集合**先写后跑**,预测与实测不符要记进轮次日志(这是最有价值的一类记录)。
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
- [ ] **E2 `web-react-ci.yml`**(⚠ 冒烟若断言"零控制台错误",**必须丢弃第一次加载** —— Vite 冷启动遇到新依赖会重新预打包并强制刷新,那个窗口真的会抛 `Invalid hook call`,与代码无关,B3 踩过) — lint → test → build → dev server 冒烟(5175,`--strictPort`,**断言内容而非状态码**——未知路径命中 SPA fallback 返 200 + index.html,只查状态码的检查会在什么都没证明的情况下通过;并断言仓库根 403)。**不带**任何共享层闸门与 `/@fs` 断言。paths 只有 `web-react/**`。
- [ ] **E3 `dev.bat`/`dev.sh` 带上 web-react** — 目前只起 backend + web。
- [ ] **E4 文档** — 根 `CLAUDE.md` 加 `web-react/` 段落,写明两个模板各自自包含、零共享,**且这是刻意选择**;**不要**出现任何"必须一起带上"的措辞。site/ 加一页 React 模板上手(degit 一条命令)。

- [ ] **E5 `gen:api` 漂移闸门**(R2 现场发现) — R2 修的那个字段缺了几个月没人发现,根因**不是** `unwrap`(这一点我第一版写错了,见轮次日志):typecheck 永远比的是**代码 ↔ schema**,从不比 **schema ↔ 后端**。schema 陈旧 = 两边一起冻住 = 恒绿。**推论:就算把 97 处响应类型全改成 schema 派生,陈旧照样一点都不红。**今天守着这条契约的只有"人记得跑 `gen:api`"。

  附带两条事实,别再搞混:①`unwrap<T>(res: { data?: unknown }): T` 确实把响应体丢成 `unknown` 再按调用方断言强转,97 处全是断言——但那治的是"手写类型与 schema 不一致",与陈旧是两回事;②openapi-fetch **在请求侧是真约束的**(`createClient<paths>` 让路径字面量 / `params.path` / `params.query` / `body` 全部受 `schema.d.ts` 管;例外是 `index.ts:548/551/553` 回收站三个端点用 `as any` 把这层绕过了)。**由此产生一个不对称,这才是闸门的真实能力边界:重新生成后请求侧的漂移会红,响应侧不会——闸门只能告诉你"schema 变了",告诉不了你"哪儿坏了"。**CI 加一步:起 MinimalHost、跑 `gen:api`、`git diff --exit-code web/src/api/schema.d.ts`(以及 R3 之后的 `web-react/src/api/schema.d.ts`)。配方现成——`backend-release.yml:77` 已经在做"起 MinimalHost 抓 openapi"这件事,照抄即可。**不做**把 97 处响应类型改成 schema 派生:**首要理由是它根本不治陈旧**(见上),其次才是仓库里零先例、手写 DTO 是有意的可读性选择。

## 不做清单（有依据,防反复）

- [~] **`web-shared/` 共享层** — 2026-07-20 推翻,理由见本文件开头四条证据。不要再抽第二次。
- [~] **npm workspaces / 发布共享包** — 与"消费者 fork 后自己拥有模板"的产品模型冲突:一个装出来、改不动的依赖,对 fork-and-own 模板是负资产。
- [~] **"两边 locale 键必须一致"的 CI 闸门** — 两个模板本就该能有意分叉。
- [~] **ProLayout** — 布局壳自建(B8)。参考 ant-design-pro 的页面结构,不引它的框架选型。

## 轮次日志

（每轮追加:做了哪条、判据、变异结果、**预测与实测不符的地方**、下一条。）

### 2026-07-20 · B3 三个 Zustand store

`8d0bf45`。**是三个不是四个**:`dict.ts` 第 6 行 `import { dictApi } from '@shared/api'` 是**运行时**依赖,而 `api/index.ts` 随 B5 的 client 一起走 —— 与 R4 撞到的是同一条边界。其余三个只引类型与常量,R4 都已落地。`Density` 按台账从 `antd-theme.ts` 移进 app store,桥改成转口。探针页从本地 `useState` 改成三条**细粒度**订阅。

**台账记的那条 review LOW 属实,而且比记的更糟**:`auth-hooks.spec.tsx` 有条用例叫「渲染次数不随 store 写入无限增长」,而体内**一次 store 写入都没有** —— 只有一次手动 `rerender()`。它声称守的失败模式(选择器返回新建函数 → 无限重渲染)走的是 store **订阅通知**那条路径,手动 rerender 根本碰不到。改成三次真写入 + 各断一次重渲染,另补一条「同引用写回不重渲染」。

**那条决策现在有实证了。**把 `useHasPerm` 改成台账警告的危险写法(`useAuthStore((st) => (code) => hasPerm(st, code))`),四条用例全红,React 自己报出 `The result of getSnapshot should be cached to avoid an infinite loop`。此前"纯函数 + 细粒度 hook"只是个断言,现在是可复现的事实。

**六处变异,预测先写,全部命中**:M1 `isDark` auto 恒 false → 红 2;M2 `hasPerm` 去掉 `permissionsLoaded` 守卫 → 红 1;M3 `homePath` 去掉 `/module` 兜底 → 红 2;M5 `partialize` 混入 `systemDark` → 红 2;M6 `merge` 去掉 `LAYOUT_MODES` 校验 → 红 1。

**M4 如预测全绿,已补上**:`EMPTY` 从工厂改成常量,一条都不红 —— 而这个危险是**代码注释里自己写明的**(常量的话三个数组在初始态与每次 reset 之间是同一个实例,谁就地 `sort()`/`push()` 一下就永久污染净态)。补了一条"就地改过数组之后再 reset 仍然干净"的用例,变异证实会红。**自己写明的隐患没有守卫,等于只是记了个愿望。**

**浏览器探针**:偏好扛过 F5(明暗/主色/密度全留存)、`systemDark` 确认未入库、`--color-primary` 已写回。

**踩到一个会误导人的陷阱,已写进 E2**:首次冷启动时探针报了 `Invalid hook call` 和 `Cannot read properties of null (reading 'useCallback')`,看着像 React 双实例的严重缺陷。实际是 **Vite 首次遇到新依赖(zustand)重新预打包并强制刷新**(`optimized dependencies changed. reloading`),那个窗口里模块图短暂不一致。dev server 预热后不再复现,四件套也一直是绿的。**任何断言"零控制台错误"的 CI 冒烟都必须丢弃第一次加载**,否则每次依赖变动都会假红一次。

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
