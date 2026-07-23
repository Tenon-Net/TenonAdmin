# 前端镜像:node 里构建 SPA,Caddy 托管产物 + 反代 /api、/hub、/health 到后端。
# **构建上下文是 web-react/ 自己**(自包含模板买到的:`cd web-react && docker build .` 即可,不依赖仓库根;
# 对照 web/ 那份上下文是仓库根、COPY 全带 web/ 前缀)。compose 里也用 `context: ./web-react`。
FROM node:22-alpine AS build
WORKDIR /src

# 先只拷依赖清单:package.json 没变时这层走缓存,不必每次重装依赖
COPY package.json package-lock.json ./
RUN npm ci

COPY . ./
# build = `tsc --noEmit && vite build` —— 类型错误在这里就该炸,而不是等到运行时
RUN npm run build

# Caddy 而非 nginx:真上域名时把 Caddyfile 的 `:80` 换成域名,即自动申请/续期 Let's Encrypt 证书
# (见 Caddyfile 头注释)。仍想用 nginx 的:把下面两行换回 nginx:alpine + web-react/nginx.conf,那份配置也在。
FROM caddy:2-alpine AS runtime
COPY Caddyfile /etc/caddy/Caddyfile
COPY --from=build /src/dist /srv
EXPOSE 80
