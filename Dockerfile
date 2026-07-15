# TenonAdmin 后端镜像（设计文档 §11）。构建 backend/samples/MinimalHost —— 那个"三行 Program.cs"故事中的示例宿主。
#
# 为什么用 MinimalHost 而不是 templates/content/tenon-app：
#   模板项目通过 <PackageReference Include="TenonAdmin" Version="…" /> 引用包——该包尚未发布到 nuget.org，
#   因此在 CI 中根本无法还原。MinimalHost 使用 ProjectReference 并从源码构建，是目前唯一能实际运行的宿主。
#   消费者自己的 Dockerfile 位于 templates/content/tenon-app/Dockerfile（仅还原路径不同）。
#
# MinimalHost 的 appsettings.json 没有 TenonAdmin 配置节（它是"零配置即可运行"的活证据），
# 所以所有配置都通过 docker-compose.yml 的 TenonAdmin__Xxx__Yyy 环境变量注入——这同时也演示了
# docs/deployment.md §0 中描述的双下划线配置方式。

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# NuGet.config 位于仓库根目录（已清除私有源，仅保留 nuget.org）；dotnet 会从项目目录向上查找它。
# 文件名大小写必须与仓库中的完全一致——Windows 不区分大小写，但 Linux 构建上下文会直接报"未找到"。
COPY NuGet.config ./
COPY backend/ backend/

RUN dotnet publish backend/samples/MinimalHost/MinimalHost.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./

# 两个数据目录在镜像中预先创建并设置所有权——compose 通过**命名卷**挂载它们：
# 命名卷首次挂载时会继承镜像目录中的内容和所有权，这是非 root 应用用户能写入的唯一方式。
# （绑定挂载会用宿主机的所有权覆盖，导致非 root 用户无法写入——这是容器化上传/SQLite 时最常见的坑。）
#   /app/data    SQLite 数据库文件 + 开发环境 JWT 签名密钥（相对于 ContentRoot，即 /app）
#   /data/upload 上传文件。**刻意放在 wwwroot 外部**：一旦有人在此镜像中添加 UseStaticFiles()，
#                wwwroot 下的上传目录将被匿名访问——这是一个认证绕过（见 docs/deployment.md 路线 A 警告）。
RUN mkdir -p /app/data /data/upload && chown -R $APP_UID:$APP_UID /app/data /data/upload

USER $APP_UID
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

# 此处不设置 HEALTHCHECK：aspnet 运行时镜像既没有 curl 也没有 wget，设了也只会始终失败。
# 健康检查留给编排层探测 /health（匿名端点，compose 和 k8s 均可直接使用）。
ENTRYPOINT ["dotnet", "MinimalHost.dll"]
