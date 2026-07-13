# 前端镜像:node 里构建 SPA,nginx 托管产物 + 反代 /api 到后端。
# 构建上下文是**仓库根**(见 docker-compose.yml),这样 .dockerignore 只需维护一份。
FROM node:22-alpine AS build
WORKDIR /src

# 先只拷依赖清单:package.json 没变时这层走缓存,不必每次重装依赖
COPY web/package.json web/package-lock.json ./
RUN npm ci

COPY web/ ./
# 内含 vue-tsc --noEmit —— 类型错误在这里就该炸,而不是等到运行时
RUN npm run build

FROM nginx:alpine AS runtime
COPY web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/dist /usr/share/nginx/html
EXPOSE 80
