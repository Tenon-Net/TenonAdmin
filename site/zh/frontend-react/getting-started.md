# React 前端入门：接通第一个页面

这条路线在现有 React 模板里新增一个只读的「岗位预览」页，并从侧栏菜单打开它。后端无需改动；页面复用已有的岗位分页接口。完成后，你会亲手走过页面组件、生成类型、API 封装、动态菜单和权限五个连接点。

## 0. 启动并认识界面

先启动后端，再启动 React 模板：

```bash
dotnet run --project backend/samples/MinimalHost
```

```bash
npm --prefix web-react install
npm --prefix web-react run dev
```

React 模板固定监听 `http://localhost:5174`。用首启时控制台打印的管理员账号登录：

[![React 管理后台界面](/screenshots/react-admin.png)](/screenshots/react-admin.png)

先打开**岗位管理**。如果列表为空，先新增一条测试岗位，记住它的名称。后面看到这条数据，就能确认新页面确实接通了后端。

## 1. 先认四个位置

| 位置 | 作用 | 本次是否修改 |
|---|---|---|
| `web-react/src/views/` | 页面组件 | 新增页面 |
| `web-react/src/api/index.ts` | 按领域封装后端调用 | 直接复用 `positionApi.page` |
| `web-react/src/api/schema.d.ts` | 从 OpenAPI 生成的类型 | 只读取，禁止手改 |
| 系统管理 → 菜单管理 | 把文件路径变成用户可访问的路由 | 新增菜单 |

菜单树变化后，`useRoutes` 会重新根据它派生路由。业务页不需要维护第二张手写路由表。

## 2. 先新建一个静态页面

新建 `web-react/src/views/example/position-preview/index.tsx`：

```tsx
import { Card } from 'antd'

export default function PositionPreviewPage() {
  return (
    <Card title="岗位预览">页面文件已经被 React 加载。</Card>
  )
}
```

先不要接接口。路由接通后，浏览器应该显示「页面文件已经被 React 加载」。

## 3. 用菜单把文件变成路由

打开**菜单管理**，在当前应用下新增一条菜单：

| 字段 | 值 |
|---|---|
| 类型 | 菜单 |
| 名称 | 岗位预览 |
| 路由路径 | `/example/position-preview` |
| 组件路径 | `example/position-preview/index` |

组件路径不带 `web-react/src/views/` 前缀，也不带 `.tsx` 后缀。保存后刷新浏览器。点击新菜单，应看到上一节的静态文字。这一步只验证「菜单 → 路由 → 文件」。

如果页面显示缺失组件，逐项对照：

```text
菜单 component: example/position-preview/index
文件位置:       src/views/example/position-preview/index.tsx
浏览器路由:     /example/position-preview
```

## 4. 再接上真实接口和类型

确认静态页能打开后，用下面的完整内容替换 `index.tsx`：

```tsx
import { useEffect, useState } from 'react'
import { Alert, Button, Card, List } from 'antd'
import { positionApi } from '@/api'
import type { SysPosition } from '@/types/api'
import { translateError } from '@/utils/error'

export default function PositionPreviewPage() {
  const [rows, setRows] = useState<SysPosition[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  const load = async () => {
    setLoading(true)
    setError('')
    try {
      const page = await positionApi.page({ page: 1, pageSize: 20 })
      setRows(page.items)
    } catch (e) {
      setError(translateError(e))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void load()
  }, [])

  return (
    <Card title="岗位预览" extra={<Button loading={loading} onClick={() => void load()}>刷新</Button>}>
      {error && <Alert type="error" title={error} showIcon />}
      <List
        loading={loading}
        dataSource={rows}
        renderItem={(row) => <List.Item>{row.name} · {row.code || '—'}</List.Item>}
      />
    </Card>
  )
}
```

刷新页面后，列表中应出现第 0 步创建或记下的岗位。视图只调用 `positionApi.page()`，不直接拼 URL 或令牌。它得到统一的 `{ items, total }`；认证、刷新、CSRF、响应解包和错误翻译沿用模板现成的请求层。

### 类型从哪里来

打开 `web-react/src/api/index.ts`，搜索 `positionApi`。分页函数最终调用：

```ts
client.GET('/api/v1/sys/position/page', {
  params: { query: { Current, Size, Name, SortField, SortOrder } },
})
```

路径、查询参数和响应 DTO 的原始类型来自 `src/api/schema.d.ts`。后端接口改变后，在仓库根目录运行：

```bash
npm --prefix web-react run gen:api
```

后端必须正在 `http://localhost:5100` 运行。生成后再运行类型检查。不要手改 `schema.d.ts`，下次生成会覆盖它。

## 5. 给普通角色补齐访问权

超级管理员会直接通过前端权限判断。普通角色需要同时授予：

1. 新增的「岗位预览」菜单；
2. 现有岗位查询权限 `GET:/api/v1/sys/position/page`。

[![角色权限配置界面](/screenshots/role-permissions.png)](/screenshots/role-permissions.png)

在截图所示的权限树里，先勾选新菜单，再找到岗位查询对应的接口权限。只勾菜单时入口可能出现，但数据请求仍会被后端拒绝；两项都勾上才是完整授权。

登出并用该角色重新登录。能看到菜单且列表成功加载，说明菜单权和接口权都已生效。撤掉岗位查询权限后重新登录，接口必须返回 403。前端门控只负责操作体验，服务端仍是最终权限边界。

## 6. 做完再检查

- 从侧栏进入页面，能看到岗位名称；
- 复制当前 URL 后刷新，仍回到同一页；
- 网络面板中的请求是 `/api/v1/sys/position/page`；
- 普通角色有查询权限时成功，无权限时收到 403；
- 以下命令通过。

在仓库根目录运行：

```bash
npm --prefix web-react run lint
npm --prefix web-react run typecheck
```

## 接下来按问题读

| 想解决的问题 | 下一页 |
|---|---|
| 入口与主题为何依赖固定装配顺序 | [项目结构与启动](/zh/frontend-react/structure) |
| 菜单怎样找到刚建的 `.tsx` 文件 | [路由与动态菜单](/zh/frontend-react/routing) |
| 请求怎样自动带令牌并处理 401 | [HTTP 请求层](/zh/frontend-react/request) |
| 响应怎样变成数据或可展示错误 | [响应契约与错误码](/zh/frontend-react/api-contract) |
| 怎样用 `<Can>` 控制操作按钮 | [前端权限](/zh/frontend-react/permission) |
| 怎样把硬编码文案放进词典 | [国际化](/zh/frontend-react/i18n) |
| 怎样让自定义样式跟随主题 | [主题与图标](/zh/frontend-react/appearance) |
| 刷新后为何仍能回到动态页面 | [多应用门户与路由守卫](/zh/frontend-react/portal-guards) |
