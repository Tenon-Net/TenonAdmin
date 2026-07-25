# 你的 host 的容器镜像,这份文件不必改——不含项目名字面量(`dotnet new` 对含连字符的项目名做
# 文件内容替换时会把 "-" 安全化成 "_",跟文件名替换用的字面值对不上;glob + 运行时发现绕开这个坑)。
#
# 内核仓库里还有一份 Dockerfile(构建样例宿主 MinimalHost),那份是从源码构建内核的,给 CI 用;
# 你要的是这一份 —— 从 NuGet 装内核,构建你自己的 host。
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish *.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./

# 两个数据目录先建好、改属主 —— 用**具名卷**挂它们(具名卷会从镜像目录带走属主,非 root 才写得进去;
# bind mount 会用宿主属主覆盖,非 root 直接写不了)。
#   /app/data    SQLite 库文件 + 开发期 JWT 密钥(相对 ContentRoot)
#   /data/upload 上传物 —— 必须在 wwwroot 之外(见下)
RUN mkdir -p /app/data /data/upload && chown -R $APP_UID:$APP_UID /app/data /data/upload

USER $APP_UID
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

# 不放 HEALTHCHECK:aspnet 运行时镜像里没有 curl / wget,写了只会恒失败。
# 让编排层探 /health(存活)与 /health/ready(依赖:DB + 缓存),两个都是匿名端点。
#
# 入口程序集按 .deps.json 找,不写死项目名:一次 `dotnet publish` 的产物里,只有入口程序集
# 自带同名 .deps.json/.runtimeconfig.json,依赖包的 dll 没有——用它当"哪个是我"的判据。
ENTRYPOINT ["/bin/sh", "-c", "exec dotnet \"$(basename \"$(ls *.deps.json)\" .deps.json).dll\""]

# 跑起来至少要注入这些(生产缺任何一条都会以一条读得懂的错误 fail-fast,详见内核仓库 docs/deployment.md):
#   ASPNETCORE_ENVIRONMENT=Production
#   TenonAdmin__Jwt__SecretKey=<≥32 字节随机串>
#   TenonAdmin__Database__DbType / __ConnectionString
#   TenonAdmin__Database__EnableCodeFirstInProduction=true   # 仅空库首启需要;之后可关
#   TenonAdmin__Upload__RootPath=/data/upload                # 若用 UseStaticFiles 托管前端,必须在 wwwroot 之外
#   TenonAdmin__Id__WorkerId=<0-63,多副本必须各不相同>
