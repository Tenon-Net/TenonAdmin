# 实时通知

`TenonAdmin:Realtime:Enabled` 默认是 `false`，而关着的时候整套系统行为不变：公告角标照样 30 秒轮询一次，被强退的人照样等到下次请求才吃 401。开启只是把这两处从「最终一致」提成「即时」，所以它是增强，不是新契约。

## 开关与配置

```jsonc
{
  "TenonAdmin": {
    "Realtime": {
      "Enabled": true,             // 默认 false
      "HubPath": "/hub/realtime"   // 默认值，改它要连前端一起改，见下文
    }
  }
}
```

关闭时内核不调用 `AddSignalR()`、不映射 Hub、不建任何长连接，`IRealtimePublisher` 落在 Services 层的 `NoopRealtimePublisher` 上。业务代码照调不误，只是什么也不发生。

SignalR 属于 ASP.NET Core 共享框架，`FrameworkReference` 里本来就有，所以这套东西**没有引入任何新的 NuGet 依赖**——运行时只依赖 SqlSugarCore 加 Microsoft.\* 这条红线没破，它才被放进内核而不是做成卫星包。

## 推什么

内核只推两个事件，都是**纯信号**，不带正文：

| 事件 | 谁触发 | 前端收到后做什么 |
| --- | --- | --- |
| `notice-changed` | `NoticeService` 发布公告 | 重拉未读角标 |
| `force-logout` | `SessionService.RevokeAsync` | 清会话、提示、跳登录页 |

负载留给客户端回查，是刻意的：推送只负责「有变化了」，具体内容仍走原来的接口。这样推送通道挂了也只是退回轮询，不会出现「界面上有一条谁也查不到的公告」。

Hub 上**没有任何客户端可调的方法**，它是单向的。

## 契约

```csharp
public interface IRealtimePublisher
{
    Task NotifyUserAsync(long userId, string @event, object? data = null, CancellationToken ct = default);
    Task NotifyAllAsync(string @event, object? data = null, CancellationToken ct = default);
    Task NotifySessionAsync(string sessionId, string @event, object? data = null, CancellationToken ct = default);
}
```

连接建立时，`TenonHub.OnConnectedAsync` 按 claims 把这条连接加进两个组：`user-{sub}` 和 `session-{sid}`。三个方法分别推给一个用户的所有在线端、所有人、以及某一次登录。

### 强退推给会话，不推给用户

这是三个方法里 `NotifySessionAsync` 存在的理由。管理员在「在线用户」里踢掉的是**一次登录**，不是一个人：同一个账号在手机上还开着的那个会话不该被连累。所以 force-logout 只推 `session-{sid}`。

触发点收在 `SessionService.RevokeAsync` 一处。强退、超并发收敛、刷新令牌复用检测、停用删号走的 `RevokeAllForUserAsync`，最后都汇到这里，接一处就全覆盖。

## 鉴权：令牌走 query

Hub 带 `[Authorize]`，走的还是 JwtBearer。但浏览器的 WebSocket 握手带不了 `Authorization` 头，所以令牌只能放 query：

```
/hub/realtime?access_token=<token>
```

`OnMessageReceived` 里对这个 query 参数的采信**限定在 Hub 路径下**：

```csharp
var accessToken = ctx.Request.Query["access_token"];
if (!string.IsNullOrEmpty(accessToken) &&
    ctx.HttpContext.Request.Path.StartsWithSegments(options.Realtime.HubPath))
    ctx.Token = accessToken;
```

普通 API 的取令牌方式一点没放宽。令牌进 URL 意味着它可能落进网关访问日志，把这个口子限死在一条路径上，是为了让这份代价只由 Hub 承担。

## 多副本：内置实现是进程内的

`SignalRRealtimePublisher` 经 `IHubContext<TenonHub>` 推送，**只到连在同一个副本上的连接**。两个副本时，A 副本发的公告推不到连在 B 上的人。

内核不给它配 backplane，因为那要引 Redis，而 Redis 在这里是可选包。降级路径本来就留着：公告有 30 秒轮询兜底，最终一致；跨副本的 force-logout 退回惰性 401。要即时，消费方自己给 SignalR 叠一层：

```csharp
builder.Services.AddSignalR().AddStackExchangeRedis("localhost:6379");
```

## 替换

真实现是在 `AddTenonAdminServices()` **之前** `TryAddSingleton` 的，压过 Services 层的 Noop。消费方要换自己的 `IRealtimePublisher`（比如推给 MQ 而不是 SignalR），照旧在 `AddTenonAdmin()` 之前注册，先到者胜：

```csharp
builder.Services.AddSingleton<IRealtimePublisher, MyMqPublisher>();
builder.Services.AddTenonAdmin(builder.Configuration);
```

这条替换路径被[「六件套」测试](/zh/backend/replaceability)锁着。

## 前端

`web/src/composables/useRealtime.ts` 是客户端，在鉴权外壳 `default.vue` 挂载时 `start()`、卸载时 `stop()`。`@microsoft/signalr` 版本对齐 .NET 10。

::: warning HubPath 改了前端不会跟着改
客户端里的路径是**写死的** `` `${baseUrl}/hub/realtime` ``。后端把 `TenonAdmin:Realtime:HubPath` 改成别的，前端连不上，而且不会报错——它会当成「实时没开」静默退回轮询。要改路径就得两边一起改。
:::

初次连接失败是**静默**的，这是设计：后端没开实时时 Hub 路径返 404，客户端不重试、不刷屏，直接退回 `NoticeBell` 的 30 秒轮询和「下次请求 401」的惰性登出。所以前后端开关状态不一致时，表现是功能降级而不是报错。

dev 环境下 Vite 要代理 `/hub` 并打开 `ws: true`，这一条已经在 `web/vite.config.ts` 里。

## 没做的验证

测试工程**没有**引 `Microsoft.AspNetCore.SignalR.Client` 去跑真的 `HubConnection`。单测锁的是接线：默认走 Noop、`RevokeAsync` 与 `PublishAsync` 会触发推送、开启后 Hub 返 401 而非 404、关闭后返 404、以及六件套的可替换性。

推送全链路是用一次性 Node 脚本直连 `MinimalHost` 冒烟过的，不在 CI 里。改动这一块时心里有数：接线有测试兜着，传输层没有。
