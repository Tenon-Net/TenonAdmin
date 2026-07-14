# 前端镜像:node 里构建 SPA,Caddy 托管产物 + 反代 /api 到后端。
# 构建上下文是**仓库根**(见 docker-compose.yml),这样 .dockerignore 只需维护一份。
FROM node:22-alpine AS build
WORKDIR /src

# 先只拷依赖清单:package.json 没变时这层走缓存,不必每次重装依赖
COPY web/package.json web/package-lock.json ./
RUN npm ci

COPY web/ ./
# 内含 vue-tsc --noEmit —— 类型错误在这里就该炸,而不是等到运行时
RUN npm run build

# Caddy 而非 nginx:真上域名时把 Caddyfile 的 `:80` 换成域名,即自动申请/续期 Let's Encrypt 证书
# (见 web/Caddyfile 头注释)。仍想用 nginx 的:把下面两行换回 nginx:alpine + web/nginx.conf,那份配置还在。
FROM caddy:2-alpine AS runtime
COPY web/Caddyfile /etc/caddy/Caddyfile
COPY --from=build /src/dist /srv
EXPOSE 80
