# TenonAdmin —— 阶段小结与 Phase 2 计划

> 落定于 2026-07-07。设计单源见 `rebuild-design.md`,滚动进度见 `dev-plan.md`,本文件是**一次暂停点的存档**:
> 说明「T1–T10 做完了什么、验证到什么程度、接下来 Phase 2 干什么」。

## 1. 阶段结论:后端内核 M1 + M3 打完(T1–T10 全绿)

`dev` 分支,提交序见 `dev-plan.md §3`。每个任务一提交,均带「做了什么 + 验证了什么」。

| 任务 | 提交 | 交付 |
|---|---|---|
| M0/M1 骨架 | `373d0f6`…`0352a58` | 5 包分层 + 三行启动 + 零配置 SQLite CodeFirst + 幂等种子 + PBKDF2 超管 |
| M1 认证闭环 | `c46fa67` | AuthService 模板方法 + JWT 签发/验证 + `[RolePermission]` + 统一异常信封 |
| T1 RBAC | `de53a72` | 角色/菜单/关联 + 缓存化权限码聚合 + 授权与失效 |
| T2 用户/机构/职位 | `cd68d43` | 全套 CRUD + 分页 + 树 + 守住不出哈希/不提权/超管保护 |
| T3 多机构数据范围 | `e3a9f7e` | 五种范围 + 多角色合并 + 机构树展开 + SqlSugar 全局过滤器(招牌能力)|
| T4 会话/令牌 | `ac7ae3a` | 存哈希 + 轮换 + 复用检测 + 强退 + 在线列表 + 单端限并发 |
| T5 字典/配置 | `48d8cfb` | 读穿透缓存 + 变更即失效 + Channels 进程内事件总线 |
| T6 日志 | `7c885ec` | `[OperationLog]` 全局过滤器 + 字段名递归脱敏 + 登录成功/失败留痕 |
| T7 本地上传 | `c7216d1` | `IFileStorage` 扩展点 + 路径穿越围栏 + 三道校验 + 文件名重写 |
| T8 横切收尾 | `0635a1f`…`dfe32ab` | 兜底返回信封 / 个人中心 / 登录锁定 / SVG 验证码 / OpenAPI / HealthChecks / 模块禁用 |
| T9 测试工程 | `4d67377` `0e3fdd2` | xunit 单元 + WebApplicationFactory 集成 + **§8 可替换性六件套** + CI;**修用户程序集断点** |
| T10 NuGet 打包 | `21fd4fe` | 5 包 0.0.1-preview + tag 发布流水线 + 消费者端到端验证 |

### 已端到端验证(证据,非推断)
- **测试**:`dotnet test` **37/37 通过**(27 单元 + 认证全流程 + §8 六件套 + 数据范围);整解决方案 0 警告 0 错误。
- **打包**:`dotnet pack` → 5 个 `.nupkg`;洁净消费者工程从本地 feed 装 `TenonAdmin` → 三行装配还原+编译通过("装一个包三行启动"成立)。
- **过程中主动修正的设计缺陷**:`.gitignore` 锚定 bug、统一返回 vs OpenAPI 保真、验证码默认关、`AddTenonAdmin` 忽略 `ApplicationAssemblies`(用户模块即插即用是空的)——后者用两个集成用例先红后绿锁死。
- 清 2 个高危 CVE(SQLitePCLRaw、Microsoft.OpenApi)。

## 2. Phase 2 计划(T1–T10 之后的加固,发布前)

> **进展(2026-07-07)**:2.1 安全审计 + 2.2 代码审计已并为 **Phase 2a 全量自审**并完成(34 条确认发现全处置,报告见 `phase2-review.md`,滚动进度见 `dev-plan.md`)。2.3 的三项中:**RateLimiter + MySQL CI 归 Phase 2b**;**RoutePrefix/Version 更正为维持 v1.x 后置**(与本文原列冲突,以 rebuild-design T8d-ii『低频低价值、深耦合鉴权路径』为准)。

分三块,建议按序;每块仍走 loop:一轮聚焦、独立可验证、一任务一提交。

### 2.1 安全审计(security-reviewer 全量)
重点盘查以下面,确认没有被漏掉的口子:
- 认证:防账号枚举(统一 40001 + dummy hash 计时)、弱 JWT 密钥拒绝(<32B)、超管仅种子/DB 不可经 API 提权;
- 授权:默认拒绝是否真的默认拒绝(有没有无属性即放行的路径)、会话失活校验;
- 数据范围:空范围=看不到、`Unrestricted` 只在可信上下文出现(启动/种子)、过滤器有没有可被绕过的查询入口;
- 数据面:密码哈希(PBKDF2 600k + FixedTimeEquals)、路径穿越围栏、日志按字段名脱敏是否有漏网 key、上传后缀白名单不信 Content-Type;
- 依赖:`dotnet list package --vulnerable` 复查。

### 2.2 代码审计(code-reviewer 全量)
隐藏 bug / 设计缺陷 / 可简化 / 并发与边界。重点:AOP 审计填充与数据范围上下文在非 HTTP(种子/后台)下的行为、缓存 key 冲突、`async` 遗漏 `ConfigureAwait`(库代码)、`IDisposable`/连接生命周期、错误码与 `[MsgKey]` 覆盖完整性。

### 2.3 补做三个后置项(前面有意 defer,理由见各处注释)
- **RoutePrefix / Version**:路由前缀与版本可配;注意它会改权限码字面量(`{METHOD}:/{route}`),需同步权限码生成与已有种子,别静默失配。
- **RateLimiter**:接 `UseRateLimiter`,解决与"三行零配置宿主"中间件顺序的冲突(当初 defer 的原因)。
- **MySQL CI 矩阵**:集成测试连接串现固定 SQLite;需 ① 加 `MySqlConnector`;② `AdminAppFactory`/`DataScopeTests` 从环境变量读 `DbType`/连接串;③ `backend-ci.yml` 加 mysql 服务容器 + 一条 matrix 腿。

## 3. 打包/发布类 follow-up(不阻塞 Phase 2)
- `PackageIcon`:预览版暂缺,发稳定版前补(需 PNG/JPG)。
- `TenonAdmin.*` ID 前缀保留:首次发布后由 owner 在 nuget.org 手动申请。
- 首次真推:仓库 Secrets 配 `NUGET_API_KEY`,再打 tag `v0.0.1-preview`。

## 4. 更远(Phase 2 之后)
M2 Vue 前端:先 `DESIGN.md` + tokens 定稿(§7.1),再 `web/` Naive UI 模板消费 openapi 契约。React 模板留到 v1.x 拆独立仓。
