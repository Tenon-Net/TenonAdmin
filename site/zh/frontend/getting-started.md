# Vue 前端入门：接通第一个页面

这条路线只做一件事：在现有 Vue 模板里新增一个只读的「岗位预览」页，并让它从侧栏菜单打开。后端无需改动；页面复用已有的岗位分页接口。完成后，你会亲手走过页面文件、生成类型、API 封装、动态菜单和权限五个连接点。

## 0. 启动并认识界面

先启动后端，再启动 Vue 模板：

```bash
dotnet run --project backend/samples/MinimalHost
```

```bash
npm --prefix web install
npm --prefix web run dev
```

Vite 默认监听 `http://localhost:5175`，实际地址以终端输出为准。用首启时控制台打印的管理员账号登录。登录后的主要工作区由左侧菜单、顶部应用栏和中间页面组成：

[![Vue 管理后台界面](/screenshots/vue-admin.png)](/screenshots/vue-admin.png)

先打开**岗位管理**。如果列表为空，先新增一条测试岗位，记住它的名称。后面看到这条数据，就能确认新页面确实接通了后端，而不是只渲染了一段静态文字。

## 1. 先认四个位置

后续操作只会碰到下面四处：

| 位置 | 作用 | 本次是否修改 |
|---|---|---|
| `web/src/views/` | 页面组件 | 新增页面 |
| `web/src/api/index.ts` | 按领域封装后端调用 | 直接复用 `positionApi.page` |
| `web/src/api/schema.d.ts` | 从 OpenAPI 生成的类型 | 只读取，禁止手改 |
| 系统管理 → 菜单管理 | 把文件路径变成用户可访问的路由 | 新增菜单 |

TenonAdmin 不要求给每个业务页手写路由。菜单记录里的组件路径会在登录后映射到 `views/` 下的文件。

## 2. 先新建一个静态页面

新建 `web/src/views/example/position-preview/index.vue`：

```vue
<script setup lang="ts">
import { NCard } from 'naive-ui'
</script>

<template>
  <n-card title="岗位预览">
    页面文件已经被 Vue 加载。
  </n-card>
</template>
```

先不要接接口。此时只有一个可识别的结果：路由接通后，浏览器应该显示「页面文件已经被 Vue 加载」。

## 3. 用菜单把文件变成路由

打开**菜单管理**，在当前应用下新增一条菜单：

| 字段 | 值 |
|---|---|
| 类型 | 菜单 |
| 名称 | 岗位预览 |
| 路由路径 | `/example/position-preview` |
| 组件路径 | `example/position-preview/index` |

组件路径不带 `web/src/views/` 前缀，也不带 `.vue` 后缀。保存后刷新浏览器，让菜单树和动态路由重新加载。点击新菜单，应看到刚才那句静态文字。这一步只验证「菜单 → 路由 → 文件」，即使后端岗位接口暂时不可用也能判断结果。

如果页面显示缺失组件，先对照这三处是否完全一致：

```text
菜单 component: example/position-preview/index
文件位置:       src/views/example/position-preview/index.vue
浏览器路由:     /example/position-preview
```

## 4. 再接上真实接口和类型

确认静态页能打开后，用下面的完整内容替换 `index.vue`：

```vue
<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { NAlert, NButton, NCard, NList, NListItem, NSpin } from 'naive-ui'
import { positionApi } from '@/api'
import type { SysPosition } from '@/types/api'
import { translateError } from '@/utils/error'

const rows = ref<SysPosition[]>([])
const loading = ref(false)
const error = ref('')

async function load() {
  loading.value = true
  error.value = ''
  try {
    const page = await positionApi.page({ page: 1, pageSize: 20 })
    rows.value = page.items
  } catch (e) {
    error.value = translateError(e)
  } finally {
    loading.value = false
  }
}

onMounted(load)
</script>

<template>
  <n-card title="岗位预览">
    <template #header-extra>
      <n-button :loading="loading" @click="load">刷新</n-button>
    </template>
    <n-alert v-if="error" type="error">{{ error }}</n-alert>
    <n-spin v-else :show="loading">
      <n-list bordered>
        <n-list-item v-for="row in rows" :key="row.id">
          {{ row.name }} · {{ row.code || '—' }}
        </n-list-item>
      </n-list>
    </n-spin>
  </n-card>
</template>
```

刷新页面后，列表中应出现第 0 步创建或记下的岗位。视图没有直接写 `fetch`，只调用 `positionApi.page()` 并得到统一的 `{ items, total }`。令牌、刷新、CSRF、响应解包和错误翻译都由已有层次处理。

### 类型从哪里来

打开 `web/src/api/index.ts`，搜索 `positionApi`。分页函数最终调用：

```ts
client.GET('/api/v1/sys/position/page', {
  params: { query: { Current, Size, Name, SortField, SortOrder } },
})
```

路径、查询参数和响应 DTO 的原始类型来自 `src/api/schema.d.ts`。后端接口改变后，在仓库根目录运行：

```bash
npm --prefix web run gen:api
```

后端必须正在 `http://localhost:5100` 运行。生成后再运行类型检查，调用方不匹配的地方会直接报错。不要手改 `schema.d.ts`，下次生成会覆盖它。

## 5. 给普通角色补齐访问权

超级管理员会直接通过前端权限判断。用普通角色验收时，需要同时授予两项：

1. 新增的「岗位预览」菜单；
2. 现有岗位查询权限 `GET:/api/v1/sys/position/page`。

[![角色权限配置界面](/screenshots/role-permissions.png)](/screenshots/role-permissions.png)

在截图所示的权限树里，先勾选新菜单，再找到岗位查询对应的接口权限。只勾菜单时页面入口可能出现，但数据请求仍会被后端拒绝；两项都勾上才是完整授权。

登出并用该角色重新登录。能看到菜单且列表成功加载，说明菜单权与接口权都已生效。随后撤掉岗位查询权限再登录：菜单是否保留取决于你的菜单授权，但接口必须返回 403。前端隐藏按钮只改善操作体验，服务端仍是最终权限边界。

## 6. 做完再检查

依次完成下面五项：

- 从侧栏进入页面，能看到岗位名称；
- 复制当前 URL 后刷新，仍回到同一页；
- 网络面板中的请求是 `/api/v1/sys/position/page`；
- 普通角色有查询权限时成功，无权限时收到 403；
- 以下命令通过。

在仓库根目录运行：

```bash
npm --prefix web run lint
npm --prefix web run typecheck
```

## 接下来按问题读

已经跑通的页面就是后续原理页的共同样本：

| 想解决的问题 | 下一页 |
|---|---|
| 入口为何必须按固定顺序安装 | [项目结构与启动](/zh/frontend/structure) |
| 菜单怎样找到刚建的 `.vue` 文件 | [路由与动态菜单](/zh/frontend/routing) |
| 请求怎样自动带令牌并处理 401 | [HTTP 请求层](/zh/frontend/request) |
| 响应怎样变成数据或可展示错误 | [响应契约与错误码](/zh/frontend/api-contract) |
| 怎样控制按钮显隐 | [前端权限](/zh/frontend/permission) |
| 怎样把硬编码文案放进词典 | [国际化](/zh/frontend/i18n) |
| 怎样让自定义样式跟随主题 | [主题与图标](/zh/frontend/appearance) |
| 刷新后为何仍能回到动态页面 | [多应用门户与路由守卫](/zh/frontend/portal-guards) |
