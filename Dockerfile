# TenonAdmin 后端镜像(设计 §11)。构建的是 backend/samples/MinimalHost —— 那个「三行 Program.cs」的样例宿主。
#
# 为什么是 MinimalHost 而不是 templates/content/tenon-app:
#   模板工程引的是 <PackageReference Include="TenonAdmin" Version="…" /> —— 一个还没发布到 nuget.org 的包,
#   在 CI 里根本 restore 不出来。MinimalHost 走 ProjectReference、从源码构建,是今天唯一能真跑起来的宿主。
#   消费者自己的 Dockerfile 见 templates/content/tenon-app/Dockerfile(只差一个还原路径)。
#
# MinimalHost 的 appsettings.json 里没有 TenonAdmin 段(它是「零配置即跑」的活样板),
# 配置全部由 docker-compose.yml 用 TenonAdmin__Xxx__Yyy 环境变量喂进来 —— 顺带把 docs/deployment.md §0
# 的双下划线配置故事证明一遍。

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# NuGet.config 在仓库根(clear 掉私有源,只留 nuget.org);dotnet 会从项目目录向上找到它。
# 大小写照抄仓库里的真实文件名 —— Windows 上随便写都能过,Linux 的构建上下文里会直接 "not found"。
COPY NuGet.config ./
COPY backend/ backend/

RUN dotnet publish backend/samples/MinimalHost/MinimalHost.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./

# 两个数据目录在镜像里先建好并改属主 —— compose 用**具名卷**挂它们:
# 具名卷首次挂载会从镜像目录带走内容与属主,非 root 的 app 用户才写得进去。
# (bind mount 会用宿主属主覆盖,非 root 直接写不了 —— 这是容器化上传/SQLite 最常见的翻车点。)
#   /app/data    SQLite 库文件 + 开发期 JWT 密钥(相对 ContentRoot,即 /app)
#   /data/upload 上传物。**刻意放在 wwwroot 之外**:一旦有人给这个镜像加 UseStaticFiles() 托管前端产物,
#                wwwroot 下的上传目录就会被匿名直出 —— 那是鉴权绕过(docs/deployment.md 路线 A 的警告)。
RUN mkdir -p /app/data /data/upload && chown -R $APP_UID:$APP_UID /app/data /data/upload

USER $APP_UID
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

# 不放 HEALTHCHECK:aspnet 运行时镜像里既没有 curl 也没有 wget,写了只会恒失败。
# 健康检查交给编排层探 /health(匿名端点,compose/k8s 都能直接用)。
ENTRYPOINT ["dotnet", "MinimalHost.dll"]
