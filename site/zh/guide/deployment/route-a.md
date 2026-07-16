# 路线 A:单体部署

后端顺带托管前端。一个进程、一个端口、同源 —— 内部系统首选。

1. 把 `web/dist/*` 拷进 host 项目的 `wwwroot/`。
2. 在你自己的 `Program.cs` 里加两行**原生 ASP.NET Core** 代码(内核不代管前端托管,因此不提供 `MapTenonAdminSpa` 之类的封装):

```csharp
var app = builder.Build();

app.UseStaticFiles();               // 托管 wwwroot 下的前端产物
app.MapTenonAdmin();                // API(必须在 fallback 之前)
app.MapFallbackToFile("index.html"); // 前端 history 路由回退:未匹配到 API 的路径交给 SPA

app.Run();
```

3. **必须同时把上传目录挪出 `wwwroot`**:

```json
{ "TenonAdmin": { "Upload": { "RootPath": "./storage/upload" } } }
```

::: danger 不挪就是一个鉴权绕过
上传根目录默认是 `./wwwroot/upload`,而上传的文件平时是通过**要鉴权**的 `GET /api/v1/sys/file/{id}/download` 取的。一旦开了 `UseStaticFiles()`,`wwwroot/upload/**` 会被静态中间件**匿名**直出——任何人猜到/拿到路径就能下载,鉴权形同虚设。

如果你原本是为了"让图片能显示"才想托管这个目录:**不需要**。内核有签名直链 `GET /api/v1/sys/file/{id}/view?sig=…`(上传接口在 `viewUrl` 字段里直接给你),匿名可取但签名不可伪造——`<img src>` 能用,而整个上传目录仍然锁着。
:::

跑起来后:`/` 是前端,`/api/v1/**` 是后端,`/health` 是探针,同源、无 CORS。

