# 路线 A：单体部署

一个进程，一个端口，前后端同源。路线 A 就是这三句话，剩下的全是推论：CORS 和反向代理都不用管了。内部系统多半到这里就够，要接的只有静态托管的两行代码和上传目录的位置。

1. 把 `web/dist/*` 拷进 host 项目的 `wwwroot/`。
2. 在你自己的 `Program.cs` 里加两行原生 ASP.NET Core 代码。内核不代管前端托管，所以没有 `MapTenonAdminSpa` 之类的封装：

```csharp
var app = builder.Build();

app.UseStaticFiles();               // 托管 wwwroot 下的前端产物
app.MapTenonAdmin();                // API 端点(fallback 优先级恒最低,与这两行先后无关)
app.MapFallbackToFile("index.html"); // 前端 history 路由回退:未匹配到 API 的路径交给 SPA

app.Run();
```

3. **必须同时把上传目录挪出 `wwwroot`**：

```json
{ "TenonAdmin": { "Upload": { "RootPath": "./storage/upload" } } }
```

::: danger 不挪就是一个鉴权绕过
上传根目录默认是 `./wwwroot/upload`。上传的文件平时怎么取？走 `GET /api/v1/sys/file/{id}/download`，这个接口**要鉴权**。可一旦开了 `UseStaticFiles()`，`wwwroot/upload/**` 就被静态中间件**匿名**直出了。也就是说，任何人猜到或拿到路径就能下载，鉴权形同虚设。

你要是原本只为了让图片能显示，才想托管这个目录，那不需要。内核有签名直链，上传接口会在 `viewUrl` 字段里直接给你，地址长这样：`GET /api/v1/sys/file/{id}/view?sig=…`。它匿名可取，签名却不可伪造。于是 `<img src>` 能用，整个上传目录仍然锁着。
:::

跑起来后：`/` 是前端，`/api/v1/**` 是后端，`/health` 是探针，同源、无 CORS。

