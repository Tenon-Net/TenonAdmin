# 部署指南

面向**内核消费者**:你已经用 `dotnet new tenon-app`(或三行 `Program.cs`)跑通了本地开发,现在要把它发到服务器上。

`npm run dev` 之所以一切正常,是因为 Vite dev server 把 `/api`、`/openapi` 反代到了后端(`web/vite.config.ts`)——**这层代理只在开发期存在**。构建产物 `web/dist` 是一堆静态文件,谁来托管它、它怎么找到后端,是部署时必须回答的两个问题。本文给三条路线,选一条即可。

> Docker / docker-compose 不在本文范围(见 `docs/dev-plan.md` T-D4 与 `docs/rebuild-design.md` §11 的设计稿)。

---

## 0. 上线前必做(安全基线)

| 配置项 | 为什么必须改 |
|---|---|
| `TenonAdmin:Jwt:SecretKey` | 不配 = **开发密钥模式**:自动生成一把密钥落到 `./data/dev-jwt.key` 并打印警告。生产必须显式配置(≥32 字节随机串),且**不要进版本库**——用环境变量或密钥管理服务。 |
| `TenonAdmin:Database` | 默认 SQLite `./data/admin.db`。多实例 / 有并发写就换 MySQL / SqlServer / PostgreSQL。 |
| `TenonAdmin:Id:WorkerId` | **水平扩展时每个实例必须不同**(0–63),否则不同实例同毫秒发号会撞主键。单实例保持默认 0。 |
| `TenonAdmin:Upload:RootPath` | 默认 `./wwwroot/upload`。声明为数据卷(否则重部署丢文件);**若走路线 A 还必须挪出 `wwwroot`**,见下方警告。 |

### 首次部署到生产:必须显式允许建表

生产环境有一道**建表安全闸门**:`ASPNETCORE_ENVIRONMENT=Production` 时,即便 `EnableCodeFirst=true` 也**不会**自动建表(生产库通常 DBA 手工维护,应用不该擅自 ALTER)。因此空库首次上生产,二选一:

```json
{ "TenonAdmin": { "Database": { "EnableCodeFirstInProduction": true } } }
```

- **让它自己建**:首启时打开上面这项(建表 + 写种子),启动成功后可以再关掉。
- **DBA 手工建**:自行建好表结构后再启动,保持该项为 false。

> ⚠️ 空库 + Production + 没开这一项 = **启动直接崩**(`no such table: sys_schema_version`):建表被闸门跳过了,但种子还是照写。日志里会先有一条 `已跳过 CodeFirst 自动建表` 的警告——看到它就说明你落在这个坑里了。

首次启动会写种子,并在控制台**打印一次随机超管密码**——注意留存。想自己指定用 `TenonAdmin:Seed:AdminPassword`。

配置可以全部走环境变量,层级用双下划线:

```bash
TenonAdmin__Jwt__SecretKey='...'
TenonAdmin__Database__DbType='MySql'
TenonAdmin__Database__ConnectionString='Server=db;Port=3306;Database=tenon;User ID=...;Password=...'
TenonAdmin__Upload__RootPath='/data/upload'
```

## 1. 构建前端

```bash
cd web
npm ci
npm run build     # 产物在 web/dist/
```

`web/dist` 默认按**同源**方式请求后端(`src/api/client.ts` 的 `baseUrl` 为空,路径已含 `/api/v1`)。路线 A、B 都满足同源,因此**不需要配 CORS**;只有路线 C 才需要。

---

## 路线 A:单体部署(后端顺带托管前端)

一个进程、一个端口、同源 —— 内部系统首选。

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

> ⚠️ **不挪就是一个鉴权绕过**。上传根目录默认是 `./wwwroot/upload`,而上传的文件平时是通过**要鉴权**的 `GET /api/v1/sys/file/{id}/download` 取的。一旦开了 `UseStaticFiles()`,`wwwroot/upload/**` 会被静态中间件**匿名**直出——任何人猜到/拿到路径就能下载,鉴权形同虚设。

跑起来后:`/` 是前端,`/api/v1/**` 是后端,`/health` 是探针,同源、无 CORS。

## 路线 B:nginx 反代(前后端分离,但仍是同源)

nginx 托管静态产物,把 `/api` 转给后端。浏览器只看到一个源,所以**同样不需要 CORS**。

```nginx
server {
    listen 80;
    server_name admin.example.com;

    # 上传大小上限要 ≥ TenonAdmin:Upload:MaxSizeMb(默认 20MB);
    # nginx 默认只有 1m,不改的话上传大文件会得到 413 而不是内核的错误码。
    client_max_body_size 32m;

    root /var/www/tenon;          # web/dist 的内容
    index index.html;

    location / {
        try_files $uri $uri/ /index.html;   # 前端 history 路由回退
    }

    location /api/ {
        proxy_pass http://127.0.0.1:5000;   # 后端监听地址
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

> 登录日志里的 IP 取自请求;要让它记到真实客户端 IP 而不是 `127.0.0.1`,除了上面的 `X-Forwarded-For`,还需在 host 里启用 `UseForwardedHeaders()`(原生 ASP.NET Core 中间件,内核不代管)。

## 路线 C:真跨源(前端在 CDN / 独立域名)

只有这种情况才需要动 CORS,而且**两端都要配**:

前端 —— 构建期给出 API 源(不必改代码):

```bash
VITE_API_BASE=https://api.example.com npm run build
```

后端 —— 放行该源(默认 deny-all,不配就是浏览器全部被拦):

```json
{
  "TenonAdmin": {
    "Api": {
      "Cors": {
        "AllowedOrigins": [ "https://admin.example.com" ],
        "AllowCredentials": true
      }
    }
  }
}
```

`AllowedOrigins` 为空 = 不放行任何跨源;`AllowCredentials` 只在 origins 非空时才生效(不存在 `AllowAnyOrigin + 凭证` 这种组合)。CORS 策略由内核的 `IStartupFilter` 自动挂载在管道前段,**不需要你手写 `UseCors`**。

---

## 部署后自检

```bash
curl https://<你的域名>/health         # Healthy(存活)
curl https://<你的域名>/health/ready   # Healthy(DB + 缓存都通)
curl -i https://<你的域名>/api/v1/ping # 401 = API 路由通了(该端点需要登录)
```

再打开前端登录一次,确认能拿到菜单(说明 JWT 密钥、数据库、种子都对)。

注意 `/openapi/v1.json` **只在 Development 环境挂载**——它是给前端 `npm run gen:api` 用的契约源,不是生产端点;生产下 404 是预期行为。
