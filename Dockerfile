# TenonAdmin backend image (design §11). Builds backend/samples/MinimalHost —— the sample host from
# that "three-line Program.cs" story.
#
# Why MinimalHost instead of templates/content/tenon-app:
#   The template project references <PackageReference Include="TenonAdmin" Version="…" /> —— a package
#   not yet published to nuget.org, so it simply can't be restored in CI. MinimalHost uses a ProjectReference
#   and builds from source, making it the only host that actually runs today.
#   The consumer's own Dockerfile lives at templates/content/tenon-app/Dockerfile (only the restore path differs).
#
# MinimalHost's appsettings.json has no TenonAdmin section (it's the living proof of "zero-config and it runs"),
# so all config is fed in by docker-compose.yml via TenonAdmin__Xxx__Yyy environment variables —— which also
# demonstrates the double-underscore config story from docs/deployment.md §0.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# NuGet.config lives at the repo root (private feeds cleared, only nuget.org left); dotnet finds it by
# walking up from the project directory.
# Case must match the real filename in the repo exactly —— Windows lets you get away with anything, but
# the Linux build context will just say "not found".
COPY NuGet.config ./
COPY backend/ backend/

RUN dotnet publish backend/samples/MinimalHost/MinimalHost.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./

# Both data directories are created and chowned in the image up front —— compose mounts them with
# **named volumes**:
# a named volume's first mount carries over the content and ownership from the image directory, which is
# the only way the non-root app user can actually write to it.
# (A bind mount would override ownership with the host's, leaving the non-root user unable to write —— the
# most common pitfall when containerizing uploads/SQLite.)
#   /app/data    SQLite database file + dev-time JWT signing key (relative to ContentRoot, i.e. /app)
#   /data/upload uploaded files. **Deliberately kept outside wwwroot**: the moment someone adds
#                UseStaticFiles() to this image to serve frontend assets, an upload directory under wwwroot
#                would be served anonymously —— that's an auth bypass (see the docs/deployment.md route A warning).
RUN mkdir -p /app/data /data/upload && chown -R $APP_UID:$APP_UID /app/data /data/upload

USER $APP_UID
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

# No HEALTHCHECK here: the aspnet runtime image has neither curl nor wget, so one would just always fail.
# Health checking is left to the orchestration layer probing /health (an anonymous endpoint, usable
# directly by both compose and k8s).
ENTRYPOINT ["dotnet", "MinimalHost.dll"]
