# 参考应用执行台账 · `tenon-example`(多机构数据范围 · 面向企业评估者)

> **来源**:2026-07-24 grilling 定向。全部方向决策见 §1,grilling 已钉死,执行期不回炉。
> **当前状态**:P0 已完成;第二轮 dogfood 抓到的 launch-profile 坑已随 `v0.3.2` 发布,`tenon-example` 已升级并采用正式修复(不再靠环境变量绕过);P1(CRM 实体 + 后端)已完成,P2(种子数据)未开始。`tenon-example` 是独立公开**参考应用仓(单仓多模块,CRM 为首个旗舰模块)**,不在本仓内;本文件只维护战略里程碑与 dogfood 回流,新仓由其 README / ledger 维护实现细节。
> **目标**:给「中小团队 / 企业内部后台」这个人群一个能信、能 dogfood 的证据 —— 一个独立开源的 CRM-lite 参考应用,吃**已发布的 NuGet 包**、degit `web/`、部署上线,头条是**多机构数据范围当场生效**。
> **驱动方式**:仿 `docs/refinement-ledger.md` —— 逐条执行、每条独立英文 conventional commit、可断点续跑;用 `/loop` 或 `/goal` 逐轮推进。
> **执行协议**:本文件的 P0–P4 是阶段门,不是跨仓细任务清单。P0 在 `tenon-example` 首个提交中创建 app 自己的 ledger,把 P1–P4 拆成可逐条勾选的实现任务;实现提交与勾选只发生在新仓。每个阶段验收通过后,再在 `tenon-admin` 用单独 docs 提交更新对应状态与证据链接。P5 的回流项继续在本仓执行。
> **台账选址**:本文件住在 `tenon-admin`(战略驱动 + dogfood 收件箱);`tenon-example` README / ledger 是 app 实现期的唯一细粒度事实源,不在两个仓重复维护同一组复选框。
> **实现授权边界**:本决定只授权 `tenon-example` 演示基于既有内核积木的业务模块;任何拟进入内核或卫星 package 的能力，必须先以独立 dogfood 证据立项，本决定本身不构成实现授权。
> **版本纪律**:后端 `TenonAdmin`、`TenonAdmin.Templates` 与 degit 的 `web/` 必须来自**同一个稳定 release**;禁止后端钉发布包、前端却拉 `dev`。
> **验证纪律**:后端改动跑新宿主实打 + curl;前端 `npm run gen:api` + `npm run typecheck` + `npm run lint` + `npm run dev` 实点;两个重进程不并发。**验证 = 跑出来的证据,不是"代码写完了"。**

---

## 1. 决策全表(grilling 钉死)

| 维度 | 结论 |
|---|---|
| 根目标 | 拿真实使用者(约束是需求 / 信任,不是功能) |
| 目标人群 | 中小团队 / 企业内部后台 |
| 下一产物 | 一个真实业务参考模块 |
| 业务域 | 多分公司客户(CRM-lite) |
| 载体 | 独立消费者仓库,吃已发布包 |
| 演示头条 | 数据范围当场生效 |
| 前端 | `web/`(Vue + Naive) |
| 仓结构 | 单仓多模块参考应用 `tenon-example`;CRM 为旗舰模块 #1 |
| 模块边界 | 未来模块(工作流等)= 加码项,时间盒见分晓后进**同一仓**;可复用引擎属内核 / 卫星包,不进本仓 |

**仓结构与模块边界(2026-07-24 追加)**:一个企业内部后台天生多模块,所以 `tenon-example` 是**一个持续生长的多模块真实系统**,不是一堆单模块 demo —— 对 solo 维护者是一个部署、一条上手路径、一个 URL。CRM 是它的**第一个也是旗舰模块**;工作流 / 资产 / 审批等是 **§4 时间盒证明这条路走得通(≥3 陌生人真实评估动作)之后**才加的**加码项**,加进**同一个仓**,不另开新仓(P0 上手路径的证明价值只有第一次是新的)。**一个边界**:若某"模块"其实是想要**可复用的引擎 / 能力**(如通用工作流引擎,消费者能直接吃),那属于 `tenon-admin` 或独立 package 仓,**不进本参考应用** —— `tenon-example` 只演示"用内核现有积木搭业务模块",不孵化内核能力；进入内核或卫星包必须有独立 dogfood 证据并另行立项。

---

## 2. 演示头条 —— 唯一叙事

> 同一个「客户列表」页:总部账号看全国 214 条、深圳分公司账号看 42 条、华南区域经理看三个分公司的 128 条 —— 而翻遍 `CustomerService` 源码,**一行 `WHERE org_id` 都没有**。那行过滤是内核全局过滤器自动挂的。

每个页面、每张截图、那篇社区文,都只服务这一句。别的卖点(可替换性 / 横切自带 / 二开速度)同一个 app 顺带展示,但**叙事只有这一个头条**。评估者看懂这一幕的瞬间,就懂了他自己搭要写多少易错的手动过滤、以及榫卯替他消灭了整类越权 bug。

---

## 3. 执行阶段

### P0 · 走一遍上手路径(产物是坑单,不是 app)
- **状态**:已完成(2026-07-24)。
- 要求:建公开仓 `tenon-example`,先选定一个已发布的稳定版本 `<release-version>`(不 float、不 pre-release),记录对应 Git tag / commit;首个提交同时创建 app 自己的 README / ledger,承接 P1–P4 细任务。
- 要求:`dotnet new install TenonAdmin.Templates@<release-version>` 后执行 `dotnet new tenon-app`;核对生成的 `PackageReference` 精确等于 `<release-version>`。
- 要求:`npx degit Tenon-Net/TenonAdmin/web#v<release-version> web` 作前端;若 release tag 命名不等于 `v<release-version>`,以该 NuGet release 对应的实际 tag / commit 为准,不得拉 `dev`。
- 要求:零配置 SQLite 跑起来,核对广告项:三行启动 / 首启随机超管密码 / CodeFirst 建表 / OpenAPI 可读 / `gen:api` 通。
- 要求:**产出「第一个真实消费者踩到的坑」清单** → 按 P5 规则回流。**这是 P0 最值钱的东西。**
- 决策已锁定:CRM 消费者模块使用 `code=crm`、`ModuleId=1000`;三个试用用户的 `DefaultModuleId=1000`。试用用户均为非超级管理员，`MustChangePassword=false`、`LastPasswordChangeTime` 有值、不绑定手机号、`Enabled=true`、`IsSuperAdmin=false`。
- 决策已锁定:scope API 返回结构化 DTO，不返回固定中文句子；前端用 zh/en i18n 根据 DTO 组装范围文案。
- 验收:在全新目录按 README 完整执行 install → create → run;记录包版本、源码 tag / commit、restore / build / 首启日志、登录与 `/openapi/v1.json` 证据;能登录进后台并成功生成前端契约。
- **版本证据**:P0 首轮验证使用 `TenonAdmin` / `TenonAdmin.Templates` `0.3.0`,源码 tag `v0.3.0` 对应 commit `ac84cad325bb808e67321b7b1b4c8b37d6fa94bd`;当前最新稳定版为 `0.3.1`,tag `v0.3.1` 对应 commit `bc7a1eecbf64a7e2adcd0ee10225e0c6c6cafae1`。
- **产物证据**:公开仓 `https://github.com/Tenon-Net/tenon-example`;首个提交 `e54c17b`,P0 验收提交 `d31b732`,当前 P0 文档提交 `c7e2b5b`。
- **验证证据**:restore / Release build(0 warning,0 error)、SQLite 首启 / CodeFirst / 随机超管密码、真实 HTTP 登录、`/health` / `/health/ready` / `/openapi/v1.json`、`npm install` / `gen:api` / `typecheck` / `lint`、Playwright Chromium 真实登录以及空目录 README 复现均已通过;完整记录见 `https://github.com/Tenon-Net/tenon-example/blob/dev/docs/p0-validation.md`。
- **坑单回流**:`https://github.com/Tenon-Net/TenonAdmin/issues/22` 已由 commit `172605f` 修复并关闭,修复随 `https://github.com/Tenon-Net/TenonAdmin/releases/tag/v0.3.1` 发布;CRM 业务实现未开始。
- **发布后回验**:[x] 已完成(2026-07-24)。`tenon-example` 提交 `251cf6a` 把包引用与 `web/` 统一升到 `0.3.1` / `v0.3.1`(tag `v0.3.1` = commit `bc7a1ee`),restore、Release build(0 warning / 0 error)、SQLite 首启(25 实体 / 181 行种子)、真实登录(envelope code 0)、`/health` / `/health/ready` / `/openapi/v1.json`、`npm ci`、`npm audit`、`gen:api`(产物与 P0 契约逐字节一致)、test(14 文件 50 用例)、typecheck、lint、生产构建全绿;空目录用**已发布的** `TenonAdmin.Templates@0.3.1` 复现了 install → create → run 全路径。证据见 `https://github.com/Tenon-Net/tenon-example/blob/dev/docs/v0.3.1-revalidation.md`。`0.3.0` 那五条坑已全部关闭。
- **第二轮坑单(2026-07-24,比第一轮更狠)**:
  1. **模板不带 launch profile → 消费者第一条 `dotnet run` 直接启动失败。** 生成物没有 `Properties/launchSettings.json`,环境落到 `Production`,`EnableCodeFirstInProduction` 默认 false → CodeFirst 跳过 → 种子表不存在 → `InvalidOperationException`,只留一个空 `admin.db`。模板 README 却写着"零配置 SQLite 自动建表 + 种子"。三种方式复现(消费者仓 `dotnet run` / `dotnet run -c Release` / 空目录用发布包生成的干净工程),内核自己的 `backend/samples/MinimalHost` **有**这个 profile,所以缺口在模板。**这正是 P0 存在的理由**:`template-smoke` 只 build 不 run,7 个 CI 检查全绿也照样漏。修复已随 `v0.3.2`(commit `14a5b35`)发布:模板补 `Properties/launchSettings.json`(钉 `ASPNETCORE_ENVIRONMENT=Development`)+ 模板 README 说明 + `smoke-test.ps1` 加"逐字 `dotnet run` 必须能出 `/health`"这一道回归闸(拿掉 profile 重跑验证过确实会红)。`tenon-example` 已升到 `0.3.2`,用同一份 `Properties/launchSettings.json` 替掉了 README 里的环境变量绕行写法,`dotnet run` 原样能起。
  2. `npm audit` 报 7 条 high,全在构建期工具链(`brace-expansion`→`minimatch`→`@redocly/openapi-core`/`openapi-typescript`,以及 `postcss`),不进浏览器产物;公告晚于 `0.3.1`,且 `brace-expansion` 链只能靠破坏性升 `vue-tsc@3` 才断得掉,故本轮不动发布钉死的依赖。

### P1 · CRM 实体 + 后端(头条地基)
- **状态**:已完成(2026-07-24)。提交 `2236b3b`(`tenon-example`)。`Customer : DataEntity`;六件套齐全(`Modules/Crm/` 下 Models/Interface/Service/ErrorCode/DI/Controller);`BizErrorCode.CustomerNotFound = 60001`;`CustomerService` 静态复核(grep)确认无 `CreateOrgId`/机构过滤代码,唯一命中是类自身文档注释里的这句保证。`CrmModuleSeed` 注册 `SysModule`(`ModuleId=1000`,`code=crm`,`DefaultRoute=/crm/customer`)供 P2 引用,不预置菜单。新增 `GET /api/v1/biz/customer/scope`,返回结构化 `CustomerScopeDto`(`Kind`/`OrgName`/`VisibleOrgCount`/`IncludeSelf`),算法据 `IDataScopeContext.Current` + 完整机构树判定"全部/根机构及以下/单机构/指定若干机构",`IncludeSelf` 与 `Kind` 正交叠加。新建 `tests/tenon-example.Tests`(12 用例全绿):CRUD/分页往返、跨机构详情不可见与改删被拒(经 `CustomerService`,不绕过服务层触发内核通用 IDOR 守卫——那条已由内核自己的 `DataScopeTests` 覆盖)、scope 算法 8 组用例(含"华南少广州"这种非整棵子树、不得误判为"及以下"的边界)。端到端手工验证:裸 `dotnet run`(不设环境变量,顺带验证 0.3.2 的 launch-profile 修复)、真实登录、HTTP 全链路增删改查、`/openapi/v1.json` 挂全五条路由,Release build 0 警告 0 错误。
- 要求:实体 `Customer`,**必须继承 `DataEntity`**(死穴:只有 `DataEntity` 带 `CreateOrgId` / `CreateUserId` 数据范围锚点;继承错了整件事不成立)。字段:名称 / 联系人 / 电话 / 意向金额 / 状态。普通登录用户新增时,归属机构与创建人靠 AOP 自动填,业务代码不设。
- 要求:`create-crud-backend` 出六件套(Models / Interface / Service / ErrorCode / DI / Controller);业务路由固定为 `api/v1/biz/customer`;Controller 挂 `[RolePermission]`。消费者错误码集中在 `BizErrorCode` 常量类,从 `60000` 起步,调用时强转内核 `ErrorCode`,不修改内核枚举。
- 要求:**`CustomerService` 零过滤代码** —— 这个"什么都没写"要在源码里可见(README / 文里指给评估者看)。
- 要求:后端集成测试先锁住 CRUD、分页与数据范围写守卫:直接设置受限 `IDataScopeContext`,证明跨范围详情不可见、更新 / 删除被拒;不要用缺少写权限导致的 403 冒充数据范围验证。P1 不验三账号固定行数,该验收依赖 P2 种子。
- 验收:`GET /api/v1/biz/customer/page` 契约出现在 OpenAPI;后端测试绿;静态复核 `CustomerService` 不包含 `CreateOrgId`、机构 Id 或手写机构过滤条件。

### P2 · 种子数据(叙事弹药 —— 最不起眼最易翻车)
- **状态**:未开始。
- 要求:机构树与客户数写死:集团总部 → 华南大区(深圳 42 / 广州 43 / 东莞 43,合计 128)与华北大区(北京 43 / 天津 43),全国合计 214。
- 要求:三个演示用户全部是**非超级管理员**:总部管理员绑定 `All`;华南区域经理绑定 `OrgAndChildren`;深圳专员绑定 `Org`。不使用“本部门 / 本人”含混配置,也不靠超级管理员绕过伪造“全部”效果。
- 要求:种子同时创建角色、角色数据范围、用户、用户角色与 CRM 菜单,显式授予 `GET:/api/v1/biz/customer/page`、`GET:/api/v1/biz/customer/{id}`、`GET:/api/v1/biz/customer/scope`。消费者固定 Id 使用 `1000–1999` 区间,同一实体内不得重复。
- 要求:CRM 消费者模块固定 `code=crm`、`ModuleId=1000`，默认路由和菜单归属该模块；三个试用用户的 `DefaultModuleId=1000`，且均满足 P0 已锁定的密码、手机号、启用和非超级管理员状态。
- 要求:客户种子显式填写 `CreateOrgId` 与 `CreateUserId`:启动种子没有登录用户上下文,不能依赖运行期 AOP 自动补锚点。各实现注册为 `ISeedData`,重复启动只补缺失行、不漂移既有数据。
- 验收:自动化测试连续初始化同一数据库两次,行数、固定 Id、角色授权和演示密码不变;三个账号调用同一 `GET /api/v1/biz/customer/page` 分别返回 `214 / 128 / 42`,跨范围详情不可见;P1 的写守卫测试保持绿。**没有这套,头条演不出来。**

### P3 · 前端 CRM 页(展示层)
- **状态**:未开始。
- 要求:`create-crud-frontend`(web/ 版)出列表 + 表单;i18n 双语键零缺口;菜单 / 权限接线。
- 要求:消费者新增 `GET /api/v1/biz/customer/scope`,挂 `[RolePermission]`。API 返回范围类型、机构名称、可见机构数等结构化 DTO，不返回固定中文句子；实现读取当前有效 `IDataScopeContext` 和完整机构树:不受限表达全部组织;受限时找出 `OrgIds` 中没有可见祖先的最小根,若其全部后代也在 `OrgIds` 中表达“根机构及以下”语义,单机构表达机构语义,其他组合表达指定机构数语义;`IncludeSelf` 单独表达仅本人或组合语义。现有个人资料接口不提供该信息,前端不得按账号硬编码,也不得假设合并后的 context 仍保留原始 `ScopeType`;前端必须通过 zh/en i18n 由 DTO 组装文案。
- 要求:列表页顶部用 scope 接口 + 分页 `total` 显式回显「当前范围:华南大区及以下 · 共 128 条」—— 切账号时那一幕才有冲击力。
- 验收:`gen:api` 后 `typecheck` / `lint` 绿;三账号实点,范围文本分别对应全部组织 / 华南大区及以下 / 深圳分公司,总数分别为 `214 / 128 / 42`。

### P4 · 部署 + 叙事包装
- **状态**:未开始;生产域名切换必须另获维护者对切换窗口的明确授权,P0–P3 完成不自动授权上线。
- 要求:获授权后部署上线并**替换** `tenonadmin.52moyu.net`(consumer app 是现有 demo 的超集,含全部内核管理页 + CRM)。切换前备份现有配置 / 数据并记录可执行回滚步骤;上线后验证 `/health` 与 `/health/ready`。
- 要求:公开环境开启 `TenonAdmin:DemoMode`;三个共享试用账号只授予 CRM 读取权限,写按钮不展示、写请求仍由后端拒绝。新增 / 编辑 / 删除只在源码与本地环境演示。
- 要求:demo 首屏放**试用账号表**(总部 / 区域 / 分公司三账号 + 密码)+ 一句引导"用这三个账号分别登录看同一个客户菜单"。
- 要求:一张三账号同页不同结果的 GIF / 截图;README / 文档站加「Real app built on Tenon」入口。
- 验收:未登录访客能照引导在 2 分钟内自己看到那一幕;三个账号均不能写数据;健康检查、登录、客户分页与回滚步骤完成冒烟验证;共享账号具备可重复执行的密码 / 锁定状态重置办法。

### P5 · dogfood 回流(整件事的真目的)+ 启动时间盒
- [ ] P0–P4 中**可复用于内核或模板**的坑开成 `tenon-admin` issue / ledger 条目;CRM 自身业务问题留在 `tenon-example`,不把消费者待办倒灌成内核噪音。
- [ ] 写**P5 那篇社区文**(见 §2 头条),发掘金 / 博客园 + 投几个 .NET 群 —— **这一发即启动 §4 时间盒计时。**
- [ ] consumer 仓从此当**永久集成金丝雀**:内核每次发版后 bump 精确版本、重验一遍。

---

## 4. 成功信号 & pivot 触发器

- **信号 = 真实评估动作**:陌生人开「二开 / 部署」issue、fork consumer repo、进群问部署 / 定制。按陌生人去重,同一人多个动作只计 1;记录日期、来源、动作与后续。**star 不算**(star → 再做动作才算)。
- **时间盒**:app 上线 + P5 那篇社区文 + 投群,之后 **4 周**；分母至少包含有效触达的 **20 个目标画像真人**，记录触达日期、画像和反馈。
- **阈值**:≥ **3** 个不同陌生人做出真实评估动作 → 契合证明是对的瓶颈,**加码**(第二实体 / 第二模板 / 内容放大);**< 3(尤其 0–1)→ pivot 到发现 / 信任层**(内容营销 / 开源号召 / 找第二维护者)。
- **早期廉价探针**(不等 4 周):头条 demo 一能点,先抓 2–3 个目标画像真人看,直接问"打不打动、会不会押"。在沉没成本变大前先证伪一次假设。
- 数值按真实触达可调,但**动手前必须已写死一个,不许"做完再看"。**

---

## 5. 首个 4 周时间盒内不做清单(ponytail · 防退化成 ERP)

| 项 | 理由 |
|---|---|
| 第二个实体(合同 / 回款 / 报表 / 审批) | 头条是数据范围,一个 `Customer` 够;加实体只稀释叙事。 |
| 可替换性第二幕(现在做) | 头条落地后、若访谈里"会不会被套牢"被真提起再补"覆写现有内核 service"一幕;不预造。**尤其不为演示造 `ICustomerNoRule` 这种单实现抽象。** |
| `web-react/` 版 | 先赢一套(`web/`)。 |
| 任何推广动作(P5 那篇社区文之外) | 那是 artifact 落地后的下一层漏斗;现在做是往空漏斗灌水。 |
| 动内核架构 | 除非 dogfood 真暴露缺口 —— 那才是有依据地动。 |

---

## 6. 风险(honest)

1. **P2 偷懒 → 头条演不出来。** 最不起眼一步最致命,舍得花时间造机构树。
2. **`Customer` 没继承 `DataEntity`,或种子漏填锚点 → 数据范围根本不生效。** 运行期继承对了由 AOP 自动填;启动种子必须显式填 `CreateOrgId` / `CreateUserId`。
3. **做成"又一个后台" → CRUD 淹没头条。** 对策:每页每图都服务 §2 那一句。
4. **发布包真有坑 → P0 就卡住。** 这不是风险是收益 —— 卡住即抓到真实消费者的真问题,当场修 + 回流。
5. **公开共享账号可写 → 演示数据被污染。** 生产 demo 固定开启 `DemoMode` + 只读权限,并保留账号状态重置办法。
