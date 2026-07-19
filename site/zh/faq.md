# 常见问题

内核对可预期的配置错误会点名到具体的配置项、表、列，不甩一句笼统异常。建表闸门、`WorkerId` 缺配这类问题，顺着报错原文读通常比翻文档快。下面收的是报错帮不上忙的那几件：装完包、第一次把内核跑起来时最容易卡住的事。

## 首次启动该用什么账号密码登录？

账号是 `superAdmin`，密码取决于你走的是哪条路径。三种情况的密码来源完全不同，登不上多半是认错了路径：

- **本地 clone 跑 MinimalHost**：仓库里只有 `appsettings.Development.json.example` 模板，真文件被 gitignore，不入库。所以**全新 clone 首启走的也是下面那条随机密码路径**。想要固定密码，把模板拷成 `appsettings.Development.json`，再给 `Seed:AdminPassword` 填上值。仓库的开发约定值是 `Aa123456`，前端登录表单在 dev 模式预填的就是它。配好之后不再打印随机密码，控制台只有一行「已按配置创建超级管理员」的普通日志。
- **零配置 / 生产（没配 `Seed:AdminPassword`）**：内核生成一个 16 位随机密码，字符集剔除了 `0/O`、`1/l/I` 这类易混淆字符，好让你从日志里照抄。密码用 `LogWarning` 在启动日志里打一个醒目的边框，**只在真正建号的那次启动打印一次**：

```text
╔══════════════════════════════════════════════════════╗
║  TenonAdmin 首次启动,已创建超级管理员                  ║
║  账号: superAdmin
║  密码: xxxxxxxxxxxxxxxx
║  此密码仅本次显示,请登录后立即修改!                    ║
╚══════════════════════════════════════════════════════╝
```

- **compose 演示环境（仓库根 `docker-compose.yml`）**：密码走 `TENON_ADMIN_PASSWORD` 环境变量，默认 `Tenon@123456`。要改就在同目录 `.env` 里覆盖。

想固定成自己的密码，比如 CI、自动化场景，启动前配 `Seed:AdminPassword` 即可：

```json
{ "TenonAdmin": { "Seed": { "AdminAccount": "superAdmin", "AdminPassword": "你的密码" } } }
```

留空（默认）才走随机生成那条路径。种子只在 `sys_user` 表为空时跑一次：库里已经有任意用户，改这段配置都不会覆盖已存在的账号。

## 忘了看首启那次日志，随机密码找不回了怎么办？

找不回。密码写进库时就已经哈希，没有明文可捞。要么直接改库里那条超管记录的密码哈希，要么清掉 `sys_user`（或直接删库）让种子重新播一次。重播时配好 `Seed:AdminPassword`，这次就用你指定的值，不再随机。

## `appsettings.Development.json` 为什么不在仓库里？

它被 `.gitignore` 排除了，不在版本库里。缺它不影响启动：默认 SQLite + CodeFirst（启动时按实体类自动建表，不用手写建表 SQL）会自己把库和表长出来。里面放的是数据库连接串、JWT 密钥这类本地凭证，不该进 git。要固定超管密码或者落别的本地凭证，从 `backend/samples/MinimalHost/appsettings.Development.json.example` 拷一份改名即可。

## 换库、gen:api、代理、健康检查这些去哪找

这几件事各自有专页详解，这里只给现象和落点：

| 你想做的事 | 去哪看 |
|---|---|
| 把默认 SQLite 换成 MySQL / SqlServer / PostgreSQL | [快速上手](/zh/guide/getting-started) 的换库一节 |
| `npm run gen:api` 报错 / 生成的类型不对（得先起后端） | [前端 API 契约](/zh/frontend/api-contract) |
| 本地 `/api` 为什么能通、生产要不要配 CORS | [前端请求与代理](/zh/frontend/request) |
| `/health` 和 `/health/ready` 分别探什么、`/openapi` 生产 404 是不是漏了 | [部署指南](/zh/guide/deployment/) 的上线自检 |
| 多副本启动报 `WorkerId` 相关错误 | [容器化部署](/zh/guide/deployment/docker) |

表里都没有，去仓库 [issue](https://github.com/Tenon-Net/TenonAdmin/issues) 搜关键字。开新 issue 时把 .NET / Node 版本、`TenonAdmin:Database:DbType`、单实例还是多副本、完整报错堆栈一并带上，能省一轮来回。
