# 参考应用执行台账 · `tenon-example`(多机构数据范围 · 面向企业评估者)

> **来源**:2026-07-24 grilling 定向。全部方向决策见 §1,grilling 已钉死,执行期不回炉。
> **当前状态**:P0 已完成;第二轮 dogfood 抓到的 launch-profile 坑已随 `v0.3.2` 发布,`tenon-example` 已升级并采用正式修复(不再靠环境变量绕过);P1(CRM 实体 + 后端)、P2(种子数据)、P3(前端页)均已完成——头条(同一菜单、三个账号分别看到 214/128/42 行,跨范围不可见)已用真实浏览器登录验证过。**P4 已完成并上线**:用户提供服务器信息并明确授权替换,`tenon-example` 已经 `docker compose` 部署上线,`tenonadmin.52moyu.net` 的宿主 Caddy 已切到新栈(旧内核 demo 栈已备份并停止,未删除,一条命令可回滚)。第三轮 dogfood 又抓到一个模板坑,已随 `v0.3.3` 发布(见下)。**上线后用户 grilling 纠偏**:CRM-only 的登录体验不对——demo 应该展示内核出厂即带的完整后台(组织/用户/角色/菜单/字典/配置/日志/文件)再叠加 CRM,不是只有 CRM 一个菜单;已修:总部管理员额外拿到内核「系统」模块的完整授权(真实角色授权,非 superAdmin 绕过),`superAdmin` 密码转为公开的第四试用账号,清掉了模板遗留的 `SampleDoc` 空表,已重新验证并重新上线。`tenon-example` 是独立公开**参考应用仓(单仓多模块,CRM 为首个旗舰模块)**,不在本仓内;本文件只维护战略里程碑与 dogfood 回流,新仓由其 README / ledger 维护实现细节。
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
- **状态**:已完成(2026-07-24)。提交 `2236b3b`(`tenon-example`,P2 已在 `fc285dc` 补齐三账号固定行数验收)。`Customer : DataEntity`;六件套齐全(`Modules/Crm/` 下 Models/Interface/Service/ErrorCode/DI/Controller);`BizErrorCode.CustomerNotFound = 60001`;`CustomerService` 静态复核(grep)确认无 `CreateOrgId`/机构过滤代码,唯一命中是类自身文档注释里的这句保证。`CrmModuleSeed` 注册 `SysModule`(`ModuleId=1000`,`code=crm`,`DefaultRoute=/crm/customer`)供 P2 引用,不预置菜单。新增 `GET /api/v1/biz/customer/scope`,返回结构化 `CustomerScopeDto`(`Kind`/`OrgName`/`VisibleOrgCount`/`IncludeSelf`),算法据 `IDataScopeContext.Current` + 完整机构树判定"全部/根机构及以下/单机构/指定若干机构",`IncludeSelf` 与 `Kind` 正交叠加。新建 `tests/tenon-example.Tests`(12 用例全绿):CRUD/分页往返、跨机构详情不可见与改删被拒(经 `CustomerService`,不绕过服务层触发内核通用 IDOR 守卫——那条已由内核自己的 `DataScopeTests` 覆盖)、scope 算法 8 组用例(含"华南少广州"这种非整棵子树、不得误判为"及以下"的边界)。端到端手工验证:裸 `dotnet run`(不设环境变量,顺带验证 0.3.2 的 launch-profile 修复)、真实登录、HTTP 全链路增删改查、`/openapi/v1.json` 挂全五条路由,Release build 0 警告 0 错误。
- 要求:实体 `Customer`,**必须继承 `DataEntity`**(死穴:只有 `DataEntity` 带 `CreateOrgId` / `CreateUserId` 数据范围锚点;继承错了整件事不成立)。字段:名称 / 联系人 / 电话 / 意向金额 / 状态。普通登录用户新增时,归属机构与创建人靠 AOP 自动填,业务代码不设。
- 要求:`create-crud-backend` 出六件套(Models / Interface / Service / ErrorCode / DI / Controller);业务路由固定为 `api/v1/biz/customer`;Controller 挂 `[RolePermission]`。消费者错误码集中在 `BizErrorCode` 常量类,从 `60000` 起步,调用时强转内核 `ErrorCode`,不修改内核枚举。
- 要求:**`CustomerService` 零过滤代码** —— 这个"什么都没写"要在源码里可见(README / 文里指给评估者看)。
- 要求:后端集成测试先锁住 CRUD、分页与数据范围写守卫:直接设置受限 `IDataScopeContext`,证明跨范围详情不可见、更新 / 删除被拒;不要用缺少写权限导致的 403 冒充数据范围验证。P1 不验三账号固定行数,该验收依赖 P2 种子。
- 验收:`GET /api/v1/biz/customer/page` 契约出现在 OpenAPI;后端测试绿;静态复核 `CustomerService` 不包含 `CreateOrgId`、机构 Id 或手写机构过滤条件。

### P2 · 种子数据(叙事弹药 —— 最不起眼最易翻车)
- **状态**:已完成(2026-07-24)。提交 `fc285dc`(`tenon-example`)。
- 机构树与客户数按要求写死:集团总部 → 华南大区(深圳 42 / 广州 43 / 东莞 43,合计 128)与华北大区(北京 43 / 天津 43),全国合计 214;客户只挂在 5 个分公司叶子机构,数值由代码按分公司循环生成(非手写字面量),不占用大区/总部节点。
- 三个演示用户均为**非超级管理员**:总部管理员绑定 `All`;华南区域经理绑定 `OrgAndChildren`;深圳专员绑定 `Org`。`Org`/`OrgAndChildren` 按内核 `DataScopeProvider` 约定解析到**用户自己的** `OrgId`,机构因此定在用户行而非角色行。
- 种子同时创建了角色、角色数据范围、用户、用户角色与 CRM 菜单(1 个页面节点 + 3 个只读按钮),三个角色**同等**被授予 `GET:/api/v1/biz/customer/page`、`GET:/api/v1/biz/customer/{id}`、`GET:/api/v1/biz/customer/scope`——三账号的差异纯粹是数据范围,不是功能权限。消费者固定 Id 落在 `1000–1999`(客户占 `1000–1213`),9 个 `ISeedData` 实现全部幂等(连接表用 `DedupColumns`)。
- 客户种子显式填 `CreateOrgId`/`CreateUserId`(种子无登录上下文,不能靠运行期 AOP)。
- 验收证据:`CrmSeedIdempotencyTests` 用**真实** `AddTenonAdmin` 组合根连续起两次同一 SQLite 文件(真正跑 `DatabaseInitializer`,不是手工 `CodeFirst.InitTables`),行数、演示密码哈希两次一致,`214/128/42` 两次都对;外加真实 `dotnet run`(首启 429 行种子、二次启动 0 新增)+ 三账号真实登录 + `page`/`scope` 接口核对 + 深圳账号读北京客户详情返回 `CustomerNotFound`。**顺手抓到一个测试期坑**(非产品缺陷):`AddTenonAdmin` 会把 `IDataScopeContext` 换成挂 `HttpContext.Items` 的实现,脱离真实 HTTP 请求时其 setter 静默空操作——纯内存自检场景要显式换回 SqlSugar 层的实现,文档其实已经写了这条("非 HTTP 场景回退"),这次是踩了一遍才真正确认。

### P3 · 前端 CRM 页(展示层)
- **状态**:已完成(2026-07-24)。提交 `80e7d0b`(`tenon-example`)。
- `create-crud-frontend`(web/ 版)业务模块模式出列表 + 表单,全部落在新文件(`types/crm.ts`、`api/crm.ts`、`locales/ext/{zh-CN,en-US}/crm.ts`、`views/crm/customer/index.vue`),上游自留地(`api/index.ts`/`types/api.ts`/`zh-CN.ts`/`en-US.ts`)一个字节未改;菜单接线零代码——P2 种子的 `Component="crm/customer/index"` 直接解析到位。
- 范围接口(`GET /api/v1/biz/customer/scope`,P1 已实现)按锁定算法返回结构化 DTO;前端 `scopeLabel()` 按 `Kind`(全部/根机构及以下/单机构/指定若干机构)与正交的 `IncludeSelf` 由 zh/en i18n 拼文案,不按账号硬编码、不臆测原始 `ScopeType`。
- 列表页顶部用 scope 接口 + `ProTable` 的 `@loaded` 分页 `total` 显式回显当前范围文案。
- 验收证据:`gen:api`/`typecheck`/`lint`/生产 `build` 全绿;真实 Playwright Chromium 对三个试用账号逐个登录,页面文本核对范围文案(全部组织 / 华南大区 及以下 / 深圳分公司)与总数(`214`/`128`/`42`)全部命中。**顺手补了一处 skill 参考模板没覆盖的点**:新增/编辑/删除按钮按 `auth.hasPerm` 真实授权码显隐,而非无条件展示——三个试用角色在 P2 只被授予只读权限,三账号因此都看不到写按钮,`superAdmin` 从多应用门户选入 CRM 后能看到完整 CRUD(超管 fail-open)。后端测试保持 13/13 绿,Release build 0 警告 0 错误。
- **顺带记的非产品缺陷**:本地最小消费方 host 未接 SignalR 通知铃铛的 Hub,浏览器控制台登录后持续报 negotiation 404——模板本身既有的噪音,与 CRM 无关,本轮不修。

### P4 · 部署 + 叙事包装
- **状态**:已完成(2026-07-25)。上线到 `tenonadmin.52moyu.net`,替换了内核自己的旧 demo。`tenon-example` 提交 `b39565e`(docker 修复 + compose 栈)、`ab895ea`(部署记录)。
- 授权:用户在会话中直接给出服务器 root 信息并明确说"你替换掉吧"——比上一轮"先本地验证"更进一步的授权,本轮据此执行了真正的上线。
- 部署方式:`tenon-example`(`dev`@`b39565e`)克隆到服务器 `/root/opt/tenon/tenon-example`,`docker compose`(MySQL + Redis + 后端 + Caddy 前端)先在临时端口(8090/8091)起栈、自证健康检查/三账号登录/`214`/`128`/`42`/DemoMode 写拒绝全部通过,再把宿主级 Caddy(`/etc/caddy/Caddyfile`,该服务器上还跑着好几个不相关站点)的 `tenonadmin.52moyu.net` 反代目标从旧栈的 18086 改到新栈的 8090、`systemctl reload caddy`(不影响同机其它域名),外部验证 `https://tenonadmin.52moyu.net/health`、`/health/ready`、`/login` 均 200。
- 备份与回滚:切换前对旧 `tenon-admin` 栈做了完整备份(未提交的 git diff、`.env`、`docker-compose.override.yml`、宿主 Caddyfile、`mysqldump --all-databases`,共 4.25MB/1905 行 SQL),旧栈容器 `stop` 而非删除(数据卷原样保留)。回滚 = 恢复 Caddyfile 备份 + reload + `docker compose -p tenon-admin start`,已确认旧容器仍在、可直接拉起,未做过实际回滚演练(那意味着真的下线新部署)。
- 公开只读:`TenonAdmin:DemoMode` 通过服务器本地的 `docker-compose.override.yml`(未提交,与旧栈同款约定)开启,已针对**真实上线环境**(非本地)复测:读接口 200,`POST .../customer/add` 返回 `41002`。
- 首屏引导与截图:README「CRM Module」小节的账号表、三张截图、demo mode 说明均已随部署更新为指向真实 URL;截图仍是本地那一轮拍的(渲染结果与生产字节一致,未重拍)。
- 未做:没有另建一个"2 分钟发现路径"专用首屏横幅(访客直接看 README 账号表即可);没有做真实回滚演练;没有把共享账号的密码/锁定状态重置做成自动化(种子本身幂等,够用)。

### P4 之后的用户 grilling 纠偏:demo 得是「内核全功能 + CRM」,不是「CRM-only」
- **状态**:已完成(2026-07-25)。`tenon-example` 提交见其自身 `docs/app-ledger.md`「Correction」章节。
- 用户实测上线后的 demo,反馈三个试用账号登录后只看到一个 CRM 菜单,不符合预期——demo 应该是内核出厂即带的完整后台(组织/用户/角色/菜单/字典/配置/日志/文件)基础上叠加 CRM,不是只有 CRM。`/grill-me` 走完整个流程(3 个并行 Explore agent 定位根因 → 4 问 AskUserQuestion 钉死方案 → 写 plan → 用户批准)。
- 根因不是渲染 bug,是种子缺口:内核自带「系统」模块(ModuleId=1)出厂即含完整后台菜单树,但三个 CRM 试用角色只被授予了 CRM 自己的 3 个按钮权限,前端门户按"当前用户菜单授权"算可见模块,零授权即算不出第二个模块,选择页自然不出现。
- 拍板结果:只给**总部管理员**追加系统模块的完整授权(全按钮,含增删改,真授权非 superAdmin 绕过);华南区域经理/深圳专员保持 CRM-only;`superAdmin` 密码(部署时已固定)转为公开的第四试用账号;模板遗留的 `SampleDoc` 空模块删除。
- 验证:总部管理员登录后 `/module` 选择页出现「系统」+「客户管理」两个应用,进入系统模块能看到组织/用户/角色/菜单/字典/配置/日志/文件全树且增删改按钮均可见可用(本地 DemoMode 关闭,真实生效,非绕过);华南/深圳两号复测无回归(128/42 不变);已重新部署上线并对生产域名复测。

### 第三轮 dogfood 发现:模板 Dockerfile 对连字符项目名坏掉
- 部署 `tenon-example` 时发现:模板生成的 `Dockerfile` 里 `dotnet publish TenonApp.csproj` / `ENTRYPOINT ["dotnet","TenonApp.dll"]` 会被 `dotnet new` 替换,但**连字符名字在文件内容替换里被安全化成下划线**(`tenon-example` → `tenon_example`),文件名替换却用字面值(`tenon-example.csproj`)——两者对不上,`docker build` 直接找不到文件。用 `dotnet new tenon-app --output hy-test` 百分百复现。
- 已在两仓修复(`tenon-admin` 提交 `0ad8605`、`tenon-example` 提交 `b39565e`):Dockerfile 不再含项目名字面量,`dotnet publish *.csproj` + 按 `.deps.json` 在运行时找入口程序集,任何项目名都免疫。
- **已发布**:`v0.3.3`(2026-07-25),`TenonAdmin`/`TenonAdmin.Templates` 均已推到 nuget.org,`backend-release` 三个 job(verify/pack-push/archive-openapi)全绿,GitHub Release 说明已用 CHANGELOG 对应段落替换,文档站版本徽章同步部署。
- **仍未做**:`templates/smoke-test.ps1` 的脚手架用名是 `Probe`(无连字符),这类坑目前仍不在 CI 覆盖范围内——留作后续加固项,不在本轮处理。

### P5 · dogfood 回流(整件事的真目的)+ 启动时间盒
- [ ] P0–P4 中**可复用于内核或模板**的坑开成 `tenon-admin` issue / ledger 条目;CRM 自身业务问题留在 `tenon-example`,不把消费者待办倒灌成内核噪音。
- [x] **改主意(2026-07-25)**:不对外发掘金/博客园/.NET 群。头条内容改写成仓内文档 `tenon-example/docs/showcase-multi-org-data-scope.md`,三份 README(en/zh-CN/ja)均已加链接引用。**§4 时间盒原定的启动信号(那一发)不再存在**——是否、以及用什么动作重新定义时间盒起点,留给用户后续决定,本轮不代为决策。
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
