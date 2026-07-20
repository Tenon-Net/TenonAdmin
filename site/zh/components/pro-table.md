# ProTable

TenonAdmin 前端几乎每一张列表页都是同一张表：`tenon-naive-pro-table`（独立 npm 包，`^0.3.1`）。它的模型只有两件东西：一个 `columns` 数组同时驱动搜索表单、字典单元格和列设置面板，一个 `fetcher` 函数把任意后端接回来。这页只讲一件事：在 TenonAdmin 模板里怎么把它用对。逐个 prop 的完整清单在包的 README，这里不重复。

## 一张列表页最少要写什么

拿岗位管理（`web/src/views/system/position/index.vue`）当骨架，删到只剩表格，它长这样：

```vue
<script setup lang="ts">
import { ProTable, type ProTableColumn } from 'tenon-naive-pro-table'
import { positionApi } from '@/api'
import { translateError } from '@/utils/error'
import type { SysPosition } from '@/types/api'

const columns: ProTableColumn<SysPosition>[] = [
  { key: 'name', title: () => t('position.name'), search: true },
  { key: 'code', title: () => t('position.code') },
  { key: 'createTime', title: () => t('common.createTime'), format: 'datetime' },
]
</script>

<template>
  <ProTable
    :columns="columns"
    :fetcher="positionApi.page"
    storage-key="sys-position"
    @error="(e) => message.error(translateError(e))"
  />
</template>
```

`name` 列写了 `search: true`，它就自动变成搜索表单里的一个输入框。`createTime` 写 `format: 'datetime'`，就按本地时间格式化。`storage-key` 记住这张表列设置存在 localStorage 的哪个键，取名规则见下文。没写的东西不用管，表格不会替你臆造。这段短代码已经把下面要讲的几条约定都用上了，逐个拆开看。

## fetcher 是唯一要你适配的地方

ProTable 对后端只有一个假设：`fetcher(params) => Promise<{ items, total }>`。TenonAdmin 的后端返回的不是这个形状。它返回 `PagedList<T>`（`{ current, size, total, items }`），翻页参数也叫 `Current`/`Size` 而不是 `page`/`pageSize`。这层差异不该散落在每个页面里，模板把它压在 `web/src/api/index.ts` 一处：

```ts
// 前端 {page,pageSize} → 后端 record 属性 {Current,Size}(PascalCase)
const pageParams = (p: { page: number; pageSize: number }) => ({ Current: p.page, Size: p.pageSize })

// 后端 PagedList<T> → ProTable 契约的 {items,total}
function toPage<T>(res): { items: T[]; total: number } {
  const p = unwrap<PagedList<T>>(res)
  return { items: p.items, total: p.total }
}

export const positionApi = {
  page: (params: { page: number; pageSize: number; name?: string }) =>
    client
      .GET('/api/v1/sys/position/page', {
        // 搜索键 name → 后端 PascalCase 的 Name;类型要求 Pascal,绑定本身大小写不敏感
        params: { query: { ...pageParams(params), Name: params.name } },
      })
      .then((r) => toPage<SysPosition>(r)),
}
```

所以页面里 `:fetcher="positionApi.page"` 直接把 api 层的方法传进去就行，映射已经在那里做完了。加新列表页时，照着 `api/index.ts` 里现成的 `userApi.page`/`positionApi.page` 复制一份形态，改端点和搜索字段名。`Current`/`Size` 不要在组件里手动拼。如果你是在 fork 上做业务，请粘进你自己的 `api/<域>.ts`，从 `./index` 导入 `pageParams`/`toPage`/`unwrap`，别把 `api/index.ts` 撑大。原因见[同步上游](/zh/guide/sync-fork)。

搜索列的 `key` 就是搜出去的参数名，`name` 列会以 `name` 进 `fetcher`。它最终落到后端哪个字段，由 api 层那一步 `Name: params.name` 决定。

## 模板里已经替你接好的三件事

**labels 全局注入，页面不再手传。** ProTable 的“搜索/重置/刷新/列设置”这些按钮文案要跟 i18n 走，模板在 `web/src/main.ts` 一次性接上，之后每个页面都继承：

```ts
app.provide(
  PRO_TABLE_DEFAULTS,
  createProTableDefaults({
    labels: computed(() => {
      void i18n.global.locale.value // 触发 locale 依赖收集,切语言即时重算
      const t = i18n.global.t
      return { search: t('common.search'), reset: t('common.reset'), /* …列设置/密度等 */ }
    }),
  }),
)
```

页面层因此不用写 `:labels`，只有要覆盖某一页的个别文案时才单独传。但列标题是个例外，它必须写成函数形式 `title: () => t('...')`。直接写 `title: t('...')`，只在建列那一刻求值，切语言不会更新。这条是提交 308a361 补的教训。

**错误留在视图层。** 包内不弹任何 UI，`fetcher` 抛错会 emit 到 `@error`，由页面决定怎么提示。全站统一写 `@error="(e) => message.error(translateError(e))"`：`translateError` 把后端的数字 `ErrorCode` 映射成当前语言的文案。

**操作按钮按权限显隐。** 授权模型是“权限码即路由”，页面里对每个动作查一次 `authStore.hasPerm('{METHOD}:/{route}')`，无权就不渲染那个按钮：

```ts
// 操作列 render 里,逐个按钮门控
authStore.hasPerm('PUT:/api/v1/sys/position/{id}')
  ? h(NButton, { onClick: () => openEdit(r) }, () => t('common.edit'))
  : null
```

工具栏上的新增/批量删除按钮同理，用 `v-auth` 指令：`v-auth="'POST:/api/v1/sys/position/add'"`。权限码字符串必须和后端路由模板逐字一致（含 `{id}` 这样的占位段），否则永远命中不到。

`storage-key` 决定列设置和密度存到 localStorage 的哪个键（前缀 `protable:`），命名统一用 `{模块}-{页面}`，如 `sys-position`、`sys-user`。

## 树表：静态数据模式，坑不少

先问自己一个问题：这张表是平铺分页的列表，还是机构、菜单那样带层级的树？平铺就用上面的 `fetcher` 模式，翻页搜索都交给它。树不一样。它没有分页，一次把整棵拉回来自己摆。机构页和菜单页都走**静态 data 模式**，对应 `org/index.vue` 和 `menu/index.vue`：

```vue
<ProTable
  :columns="columns"
  :data="visibleTree"
  row-key="id"
  :pagination="false"
  :expanded-row-keys="expandedKeys"
  @update:expanded-row-keys="(keys) => (expandedKeys = keys)"
/>
```

树列设 `minWidth: 220 + fixed: 'left'`，横向滚动时不会丢掉“这是哪一行”。文本列一律 `ellipsis: { tooltip: true }`，否则长路径换行会把行高撑得参差。操作最多留两个：编辑，加一个 `n-dropdown` 的“更多▾”。四个操作平铺在 260~300px 里必然换行，横向滚动时又够不着，org 和 menu 都栽过这一跤。下拉项里的删除用 `useConfirm().confirm`（dialog），`n-popconfirm` 是内联触发器，塞不进 dropdown。

真正让人栽跟头的是下面四个静默失败：

**别加恒空列。** 菜单树剥掉按钮节点后只剩目录和页面，而权限码只挂在按钮上。于是“权限码”那一列 100% 是“—”。同理，关键字过滤跑在剥离后的树上，写 `n.permission` 永远命中不了。要按权限码搜，得去查节点的按钮子节点，参考 `menu/index.vue` 的 `buttonInfoById`。菜单页干脆把权限码列整个删了。

**搜索得自己算。** 静态 `:data` 模式下 ProTable 不做任何前端过滤。列上的 `search` 配置只负责渲染搜索控件和 emit，不会替你筛数据。所以关键字过滤走 `computed` + `utils/tree.ts` 的 `filterTree`。它的规则是：命中的节点连整棵子树保留，没命中但有后代命中的，作为祖先链保留。关键字放 `#toolbar` 的 `n-input`，别用列的 `search`。树表没有分页，那张搜索卡片会白占一整块高度。

**受控展开必须删掉 `default-expand-all`。** `:expanded-row-keys` 一旦传了，naive 就以它为准，初始的 `[]` 会把 `default-expand-all` 直接盖成“全折叠”。“默认全展开”得自己用 `expandableIds(tree)` 播种。还有一点：`data` 变了，受控 keys 不会自动跟着变。搜索或切应用之后要重算，否则命中结果藏在折叠的祖先里，看不见。

::: warning 行内改状态后要重拉，不能往行对象上写值
`filterTree` 剪枝时，“仅因后代命中而保留”的祖先是浅拷贝。搜索态下往行对象上写值（`r.enabled = v`）写的是这份副本，回不到源树。开关点完于是会自己弹回去。所以行内变更后调 `load()` 重拉，而不是本地写回。`StatusSwitch` 是悲观更新，请求成功才 emit，重拉一次就是最终态。
:::

## 排序、主从、窄栏搜索

这些都在样例页里现成可抄，各占一句：

- **列排序**（`user` 页）：列写 `sorter: true`，点表头把 `{ sortField, sortOrder }` 并进 `fetcher`；api 层要把它们映射成后端的 `SortField`/`SortOrder`（见 `userApi.page`）。后端按实体列白名单安全排序，非法字段忽略回退默认（`PagedListExtensions.OrderBySafe`），字段名就是实体属性名，大小写不敏感。
- **主从选中**（`dict` 页）：`:active-row-key` + `@row-click` 做行高亮；行内的开关/按钮 render 里记得 `stopPropagation`，否则点它会冒泡触发 `@row-click`。
- **窄栏搜索**（`dict` 页）：`:search="{ layout: 'inline' }"` 是无卡片单行版，配合列 `search: true`，适配主从右栏这类窄空间。

列宽拖拽、虚拟滚动、合计行、合并单元格这类，全部经 attrs 或列属性透传给内层 `n-data-table`。包里没拦的属性都原样往下传，所以不需要 ProTable 额外开 API。

## 版本与本地联调

排序、搜索折叠这些依赖 `^0.3.1`。0.3.0 有个已知问题：`fetcher` 模式下的行拖拽从不生效。原因是 Sortable 只在 `onMounted` 绑一次，而空表时 naive 根本没渲染 tbody。所以模板锁的是 `^0.3.1`。改了后端的排序或分页契约后，记得 `npm run gen:api` 重新生成 schema，注意后端要在跑。

要连着包的源码调，用 `NPT_LOCAL=1 npm run dev` 直连兄弟仓库，配置见 `web/vite.config.ts`。回路和图标包一样：改源码 → 发补丁版 → bump。

包的完整 prop、事件与逃生口 slot 以 [README](https://github.com/Tenon-Net/tenon-naive-pro-table/blob/main/README.zh-CN.md) 为准。本页只覆盖它在 TenonAdmin 模板里的接法。
