# 第三方接入示例

`dotnet new tenon-app --integration` 生成本目录。认证、授权、数据范围、调用记录、地址安全、凭据注入、投递领取与恢复都在 `TenonAdmin.Integration` 包里，这里只有业务代码：

| 文件 | 演示什么 | 你要改的地方 |
|---|---|---|
| `SampleDocOpenController.cs` | 开放接口：第三方用接入凭据读写示例文档，按绑定机构隔离 | 换成你的实体、DTO 和数据范围 |
| `PartnerClient.cs` | 普通第三方调用：同步查询对方数据，不重试 | 标了【按对方协议填写】的路径与响应字段 |
| `SampleDocSyncAdapter.cs` | 可靠投递适配器：发送、结果识别、按幂等标识查询 | 同上；`SupportsIdempotency`/`SupportsQuery` 必须与对方真实能力一致 |
| `SampleDocSyncService.cs` | 本地建文档与投递记录同一事务提交 | 换成你的业务写入与载荷 |
| `SampleDocSyncController.cs` | 后台接口：触发上面两种调用 | 按需保留 |

对方协议照内核仓库里的本地可控第三方 `backend/samples/IntegrationMockPartner` 写成，不需要商业账号就能跑通下面三个流程。对接真实系统时改 `appsettings.json` 里 `partner` 目标的 `BaseUrl` 和凭据方式，再按对方协议改上面标注的方法。

## 跑通三个流程

先把三样东西起起来：

```bash
# 1. 本地可控第三方(在内核仓库目录执行;监听 127.0.0.1:5300,凭据 partner-secret)
dotnet run --project backend/samples/IntegrationMockPartner

# 2. 本项目。出站秘密只经环境变量或 user-secrets 提供,不写进 appsettings.json
TenonAdmin__Integration__Outbound__Targets__partner__Secret=partner-secret dotnet run

# 3. 任选一套前端模板(web/ 或 web-react/),npm run dev 后用首启打印的超管密码登录
```

后台左侧会多出「系统集成」：接入应用、开放调用记录、出站调用记录、可靠投递。

### 开放接口

1. 「接入应用」新增应用，打开「凭据」发放一条。完整凭据只显示这一次，形如 `tna_<keyId>.<secret>`。
2. 「授权与范围」勾选 `GET:/api/open/v1/sample-docs` 和 `POST:/api/open/v1/sample-docs`，给「机构」范围绑定可见机构。未勾选的端点一律拒绝。
3. 调用：

```bash
curl -X POST http://localhost:5100/api/open/v1/sample-docs \
  -H "X-Api-Key: tna_<keyId>.<secret>" -H "Content-Type: application/json" -d '{"title":"来自第三方"}'
curl http://localhost:5100/api/open/v1/sample-docs -H "X-Api-Key: tna_<keyId>.<secret>"
```

「开放调用记录」能看到每次调用的结果、耗时和追踪标识。停用应用或撤销凭据后，下一次调用立即返回 401。

### 普通第三方调用

用超管令牌调 `GET /api/v1/sample/doc-sync/partner/acme`，返回对方的合作方信息；`/partner/missing` 返回 `null`。「出站调用记录」里有这两次调用，地址不含查询串，请求头和秘密都不记录。

### 事务内可靠投递

`POST /api/v1/sample/doc-sync`（请求体 `{"title":"..."}`）返回文档 Id 和投递标识 `sample-doc:<Id>`。「可靠投递」里这条记录几秒内从「待处理」变成「成功」。

想看响应丢失时的处理，先给第三方排一个「执行后断开」：

```bash
curl -X POST http://127.0.0.1:5300/_mock/script -H "Content-Type: application/json" \
  -d '{"route":"tickets","behaviors":["drop"]}'
```

再新建一条。对方已执行、响应却丢了，这次结果未知。示例适配器声明了对方按幂等标识去重，投递器于是按退避（首次约 30 秒）用同一标识重发，对方回放原结果，记录变成「成功」；`GET http://127.0.0.1:5300/_mock/stats` 里这个标识只执行了一次。把 `SupportsIdempotency` 和 `SupportsQuery` 都改成 `false` 再试，同样的情况会进入「待核对」，由管理员在对方系统核实后在详情里确认。

其余行为（`accept` 异步受理、`reject` 拒绝、`unavailable`/`throttle` 暂不可用）见 `MockPartnerServer.cs` 顶部的说明。完整说明见文档站「第三方接入」。
