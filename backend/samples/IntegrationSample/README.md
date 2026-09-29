# 第三方接入消费者示例：工单与对接方

用一个工单对接示例，把第三方接入模块的三种用法放在一个可运行的宿主里：对接方用接入凭据调开放接口建单、查单；建单前同步查询对接方目录；建单与「同步到对接方」的投递在同一事务提交，由后台投递器送达并在故障时安全恢复。对方是同目录下的本地可控第三方 `IntegrationMockPartner`，不需要任何商业账号。

## 运行

```bash
# 1. 本地可控第三方(http://127.0.0.1:5300,凭据 partner-secret)
dotnet run --project backend/samples/IntegrationMockPartner

# 2. 示例宿主(http://localhost:5100)。出站秘密经环境变量提供,也可以照 appsettings.Development.json.example 放进被忽略的本地配置
TenonAdmin__Integration__Outbound__Targets__partner__Secret=partner-secret \
  dotnet run --project backend/samples/IntegrationSample

# 3. 任选一套前端(web/ 或 web-react/):npm run dev,用首启打印的超管密码登录
```

「第三方接入」下的「接入应用」「开放接口调用记录」「第三方接口调用记录」「发送任务」四个页面都能直接用。两套前端的 `npm run test:e2e:integration` 也是用这个示例加本地可控第三方跑的。

界面中的「工单对接方（示例）」是本示例注册的数据范围，TenonAdmin 本身不提供这类业务档案；其取值是工单的 `PartnerCode`（如 `acme`）。

## 哪些是业务代码

| 文件 | 内容 |
|---|---|
| `Domain/PartnerTicket.cs` | 业务实体（`DataEntity`，机构隔离） |
| `Domain/PartnerTicketService.cs` | 建单：事务外先普通调用查对接方，再在同一事务里写工单并入队 |
| `Partner/PartnerDirectoryClient.cs` | 普通调用客户端（`OutboundAdapterBase`） |
| `Partner/TicketSyncAdapters.cs` | 两个投递适配器：现代接口（按幂等键去重、可查询）与旧接口（两者都不支持） |
| `OpenApi/PartnerTicketOpenController.cs` | 开放接口：只含可公开字段的 DTO，按「工单对接方（示例）」范围过滤与校验 |
| `OpenApi/PartnerScopePolicy.cs` | 自定义开放接口数据范围「工单对接方（示例）」 |
| `OpenApi/PartnerCallbackOpenController.cs` | 对接方回调最终结果：先按调用应用的工单对接方范围核实工单，再限定适配器与业务键交给 `IDeliveryConfirmationService` |
| `Controllers/PartnerTicketController.cs` | 后台接口：分页、建单（`[RolePermission]`） |
| `SampleSetup.cs`、`Program.cs` | 注册与两步接线 |

认证、授权、调用记录、地址安全、凭据注入、投递领取、退避、恢复与人工处理都来自 `TenonAdmin.Integration`，这里一行都没有写。

## 演示要点

- **开放接口**：在「接入应用」里新建应用、发放凭据，勾选 `api/open/v1/partner-tickets` 下的端点，给「工单对接方（示例）」范围绑定如 `acme`。之后带 `X-Api-Key` 调用只能看到、只能写入 `acme` 的工单；写入别的对接方返回 `49004`。
- **普通调用**：建单时对接方编码为 `missing` 会被对方目录判为不存在，返回业务码 `61001`，本地不写任何数据。
- **可靠投递**：后台 `POST /api/v1/sample/partner-ticket` 建单，`channel` 为 `modern`（默认）走可去重可查询的适配器，`legacy` 走两者都不支持的适配器。用 `POST http://127.0.0.1:5300/_mock/script`（如 `{"route":"tickets","behaviors":["drop"]}`，旧接口的路由名是 `tickets-nodedupe`）给下一次请求排上「执行后断开」「受理」「拒绝」等行为，再到「发送任务」看各自的走向：现代接口响应丢失后用同一标识重发、对方只执行一次；旧接口同样的情况进入「待核对」等人工确认；受理的记录在对方完成（`POST /_mock/tickets/{ticketNo}/complete`）后经轮询确认成功。`GET /_mock/stats` 可以看到对方实际执行了几次。
- **回调确认**：给对接方使用的接入应用再勾选 `POST:/api/open/v1/partner-callbacks/ticket-status`，它和工单端点一样声明「工单对接方（示例）」范围，用同一个绑定。对方带投递标识回调 `done` 或 `failed` 时，端点先按该应用的工单对接方范围找到对应工单，再只针对两个工单适配器、按工单 Id 确认；工单不在该应用范围内、标识不是工单投递或投递还没发出时一律返回 `NotFound`，所以一个对接方确认不了另一个对接方的投递。

完整说明见文档站「第三方接入」（`site/zh/guide/integration.md`），逐步清单见 `skills/wire-integration.md`。
