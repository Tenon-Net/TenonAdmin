# ADR 0003 — 实时通知(SignalR):默认关的纯增强、进程内无 backplane、按会话精确 force-logout

- 状态:已采纳(2026-07-18)
- 相关:`docs/refinement-ledger.md` 批次 F;[[ADR-0001]] / [[ADR-0002]] 同属精致化台账决策存档

## 背景

两处"实时性"痛点:①公告未读角标靠前端 30s 轮询(`NoticeBell.vue`);②管理员强退一个会话后,被踢用户要等自己下次发请求才吃 401(`SessionService`/`ActiveSessionAttribute`),中间可能数分钟无感。本批把这两条从"最终一致 / 惰性"提为"即时推送"。SignalR 属 ASP.NET Core 共享框架(`FrameworkReference Microsoft.AspNetCore.App` 已在 csproj),**零新增 NuGet**,契合"运行时只依赖 SqlSugarCore + Microsoft.*"红线,故内核内置(而非卫星包)。三路只读探查核准落点后落地,记此防反复。

## 决策一:默认关,纯增强(`Realtime.Enabled` 默认 false)

关闭 = 完全维持既有行为(公告轮询 + 强退惰性 401):Hub 不映射、不建长连接,`IRealtimePublisher` 走 Services 层 `NoopRealtimePublisher`(业务代码照调不误)。开启才挂 `AddSignalR()` + 注册基于 SignalR 的真实现。理由:实时是增强不是新契约;消费者未备好长连接基建(尤其多副本)时不应被动承担;开箱最简、最安全。

## 决策二:进程内 Hub,不带 backplane(留给消费者)

内置 `SignalRRealtimePublisher` 经 `IHubContext<TenonHub>` 推送,**进程内**。多副本下推送只达连到同一副本的连接。降级:公告靠**保留的 30s 轮询**达最终一致;跨副本 force-logout 退回惰性 401。要即时跨副本,消费者给 SignalR 叠 Redis backplane(`AddStackExchangeRedis`)。理由:内核不引 Redis(它是可选包),backplane 是部署决策,不进内核默认。

## 决策三:按会话(sid)精确 force-logout,非按用户

连接建立时按 claims 入 `user-{sub}` 与 `session-{sid}` 两组。force-logout 推到 `session-{sid}`,**只踢被吊销的那次登录**,不误伤同一用户的其他在线会话(单端/多端并存)。触发点收口在 **`SessionService.RevokeAsync`**——所有下线路径(强退 / 超并发收敛 / 刷新令牌复用 / 停用删号 `RevokeAllForUserAsync`)的唯一汇聚处,一处接线全覆盖(根因点)。

## 约定与后果

- **事件名约定**:`force-logout`(→ 前端清会话 + 提示 + 跳登录,与 `api/client.ts` 刷新失败路径同款收尾)、`notice-changed`(→ 前端重拉未读角标)。**纯推送**,无客户端可调 Hub 方法;负载让客户端回查(推送只作信号,公告不带正文)。
- **鉴权**:Hub `[Authorize]` + JwtBearer;浏览器 WebSocket 带不了 Authorization 头,令牌走 query `access_token`,**仅在 Hub 路径**由 `OnMessageReceived` 采信(不放宽普通 API 取令牌方式),保留原 `OnChallenge`(40006 信封)。
- **注册顺序**:真实现在 `AddTenonAdminServices()` **之前** `TryAddSingleton`,压过 Services 的 Noop(TryAdd 先到者胜);消费者前置注册自有 `IRealtimePublisher` 再压过二者(可替换性,进六件套)。
- **前端**:`@microsoft/signalr`(`^10`,对齐 .NET 10);鉴权外壳(`default.vue`)挂载即连、卸载即断;初次连接失败(实时关→Hub 404)**静默退回轮询**(`withAutomaticReconnect` 不重试初次 start,不刷屏);`NoticeBell` 保留 30s 轮询兜底(推送 + 轮询双腿);dev Vite 加 `/hub` 代理(`ws:true`)。
- **验证取舍**:**未**在测试工程引 `Microsoft.AspNetCore.SignalR.Client` 跑真 HubConnection(避免测试专用包 + `TestServer` 传输层易碎)。接线由单测锁(默认 Noop、`RevokeAsync`/`PublishAsync` 触发、开启→真实现 + Hub 401、关闭→404、六件套);**推送全链路**由一次性 Node 脚本(前端同款 `@microsoft/signalr` 直连 MinimalHost :5100)冒烟通过——`notice-changed` 与 `force-logout` 均即时收到(negotiate 无令牌 401 / 带真令牌 200)。
