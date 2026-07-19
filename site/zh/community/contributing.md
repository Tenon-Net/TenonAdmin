# 贡献指南

对着 `main` 开的 PR 会被请回去重开：`main` 只接收发版合并，日常开发一律进 `dev`。这类约束还有几条，都不写在代码里，撞上了才知道。

## 开始之前

- Fork 仓库，clone 到本地。
- **开发在 `dev` 分支进行，`main` 只接收发版合并**：提 PR 请对准 `dev`，不要对准 `main`。发版时才会把 `dev` 合入 `main` 再打 tag（见 [CHANGELOG.md](https://github.com/Tenon-Net/TenonAdmin/blob/main/CHANGELOG.md)）。
- 报 bug / 提需求走 GitHub Issues 的三个模板（Bug report / Feature request / Question），仓库关闭了空白 issue。安全漏洞不要开公开 issue。

## 本地开发环境

TenonAdmin 分两半：`backend/`（.NET 10 内核 + 示例宿主 + 测试）和 `web/`（Vue 3 + Naive UI 管理端模板），两半可以独立改动，也可以一起改。

后端（仓库根目录跑；解决方案文件是 `.slnx`，不是 `.sln`）：

```bash
dotnet build backend/TenonAdmin.slnx -c Release
dotnet test  backend/TenonAdmin.slnx                       # xUnit + WebApplicationFactory,默认 SQLite
dotnet test  backend/TenonAdmin.slnx --filter "FullyQualifiedName~DataScopeTests"   # 只跑某个测试类
dotnet run   --project backend/samples/MinimalHost         # 零配置起服,http://localhost:5100
```

针对 MySQL 跑测试（对应 CI 矩阵里的一条腿）：

```bash
TENON_TEST_DBTYPE=MySql TENON_TEST_MYSQL="Server=127.0.0.1;Port=3306;User ID=root;Password=root;AllowPublicKeyRetrieval=true;SSL Mode=None;" dotnet test backend/TenonAdmin.slnx
```

前端（`web/` 目录下跑）：

```bash
npm run dev          # Vite,:5173,代理 /api 和 /openapi 到后端 :5100(可用 TENON_API_TARGET 覆盖)
npm run build         # vue-tsc --noEmit && vite build
npm run lint          # oxlint(lint:fix 自动修复)
npm run typecheck     # vue-tsc --noEmit
npm run gen:api       # 从一个正在运行的后端的 /openapi/v1.json 重新生成 src/api/schema.d.ts
```

嫌两边分开开麻烦，根目录的 `dev.bat` 会在两个独立窗口里同时拉起后端 + 前端（首次运行顺带装好 `web/` 依赖），`stop.bat` 停止它们。

::: warning 别手改 schema.d.ts
`web/src/api/schema.d.ts` 是从后端 OpenAPI 生成的契约文件，改了接口先跑 `npm run gen:api`（需要后端在跑），不要手写这个文件。
:::

## 包版本集中管理

后端依赖版本统一收在 [`backend/Directory.Packages.props`](https://github.com/Tenon-Net/TenonAdmin/blob/main/backend/Directory.Packages.props) 的 `<PackageVersion>` 里，新增或升级依赖改这里，**不要**在单个 `.csproj` 里单独锁版本。共享的构建/NuGet 元数据（作者、仓库地址、License 等）在 `backend/Directory.Build.props`。

## 提交信息：英文 Conventional Commits

仓库代码注释和文档是中文，但 **git commit 一律用英文**，格式是 `type(scope): subject`：

```text
fix(web): hide permission-gated buttons for users without access
feat(backend): add targeted notification delivery
docs: translate comments in root config and script files
refactor(services): split login flow into virtual steps
```

常见 `type`：`feat` / `fix` / `docs` / `refactor` / `test` / `chore`。`scope` 一般是 `web` / `backend`，或更具体的模块名。

## 跑测试：两条腿都要绿

CI 在 push / PR 触达 `backend/**` 时跑 build + test，配置在 `backend-ci.yml`。数据库矩阵是 `[sqlite, mysql, sqlserver, postgres]`。矩阵设了 `fail-fast: false`，所以一条腿红不会掩盖其它腿。矩阵之外还挂着一个 Redis 服务容器，跑 `RedisCacheTests` 的契约测试部分。另有一个 `template-smoke` 任务，验证 `dotnet new tenon-app` 能顺利 restore + build，也就是消费方拿到包后的第一条命令。改动 `backend/**` 之前，至少本地把 SQLite 默认腿和 MySQL 腿跑绿。`TestDb.cs` 按 `TENON_TEST_DBTYPE` 等环境变量给每个测试派生独立数据库，互不干扰。

前端 CI（`web-ci.yml`）在 push / PR 触达 `web/**` 时跑 `npm ci` → `npm run lint` → `npm test`（vitest）→ `npm run build`（build 已包含 `vue-tsc` 类型检查，不用单独再跑 `typecheck`）。

第三条是 `docker-smoke.yml`，它监听的路径和上面两条重合，改哪半边都会把它带起来。single 任务起一套容器，验证空库建表、种子写入、发令牌和反代都成立。multi 任务起两个副本，验的是两副本同时在线才暴露的那批问题。强制下线跨副本生效、锁定与限流不翻倍、机器 ID 不重、真实 IP 取得到。

::: tip 六件套测试是契约，不是普通测试
`ReplaceabilityTests`（设计文档里的「六件套」）锁定了 TryAdd 覆盖、虚方法重写、业务程序集挂载这几条可替换性保证。改动 DI 注册或 `TenonAdminSetup` 相关代码时，这组测试红了通常意味着破坏了消费方的替换路径，不要绕过或删测试，先看清楚破坏的是哪条保证。
:::

## PR 流程

1. 从 `dev` 切一个功能分支。
2. 改动尽量聚焦一件事，commit 信息按上面的规范来。
3. 本地跑通对应半边的 build/test/lint。
4. 提 PR，目标分支是 `dev`。CI 必须全绿：`backend-ci` / `web-ci` 按改动半边触发，`docker-smoke` 两侧都会触发。
5. 如果你在用 Claude Code 或其它 AI agent 参与开发，仓库对 Issue 分诊、领域文档、业务开发 skills 有一套约定，见 [Agent Skills 与 AI 辅助开发](./agent-skills)。

## 安全问题

**不要通过公开 issue 报告安全漏洞。** TenonAdmin 以 NuGet 包分发，内置认证、RBAC 和多组织数据权限。公开报告等于在补丁出来之前就对所有下游消费方公布 0-day。

请走 [GitHub 私密漏洞报告](https://github.com/Tenon-Net/TenonAdmin/security/advisories/new)。维护者会在 7 天内响应，并与你协调修复和披露节奏。详见 [SECURITY.md](https://github.com/Tenon-Net/TenonAdmin/blob/main/SECURITY.md)。

## 许可证

TenonAdmin 基于 [Apache License 2.0](https://github.com/Tenon-Net/TenonAdmin/blob/main/LICENSE) 开源，提交的代码默认以同一许可证贡献。
