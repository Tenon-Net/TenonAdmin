# ProTable

> `tenon-naive-pro-table` —— 面向 **Vue 3 + Naive UI** 的列驱动 Pro 表格。

一个 `columns` 数组同时驱动**搜索表单、字典单元格渲染和列设置面板**;一个 `fetcher` 函数适配任意后端。所有能力渐进可忽略——不用的功能不会碍事。零运行时依赖,peer 仅 `vue ^3.3` + `naive-ui ^2.34`,ESM only。

<div style="display:flex;gap:.5rem;flex-wrap:wrap;margin:1rem 0">
  <a href="https://www.npmjs.com/package/tenon-naive-pro-table"><img src="https://img.shields.io/npm/v/tenon-naive-pro-table?color=cb3837&logo=npm" alt="npm"></a>
  <a href="https://github.com/Tenon-Net/tenon-naive-pro-table"><img src="https://img.shields.io/github/stars/Tenon-Net/tenon-naive-pro-table?logo=github" alt="GitHub"></a>
</div>

## 特性

- **列驱动一切** —— 列上声明 `search` 即成为搜索项;声明 `options` 后单元格翻译与搜索下拉共用同一份字典。
- **单函数后端契约** —— `(params: {page, pageSize, ...搜索项}) => Promise<{items, total}>`,任何请求/响应结构都在 fetcher 里映射,无其他配置。
- **列设置 + 持久化** —— 显隐、拖拽排序、左右固定;按 `storage-key` 存 localStorage,列定义演进时安全合并。
- **密度切换** —— 舒适/紧凑,跟随宿主 Naive 主题(包内零 CSS)。
- **请求竞态守卫** —— 快速翻页时乱序返回的过期响应直接丢弃。
- **文案语言响应** —— 列标题/label 支持 `() => string`,切换语言即时更新。
- **声明式格式化** —— `format: 'date' | 'datetime' | 'money'` 或自定义函数;**异步字典** `options: () => Promise<...>` 带 loading 与去重。
- **`useProTable` / `useTableCrud`** —— UI 无关数据核与可选 CRUD 弹窗状态机,独立导出。

## 安装

```bash
npm i tenon-naive-pro-table
```

## 快速开始

```vue
<script setup lang="ts">
import { ProTable, type ProTableColumn } from 'tenon-naive-pro-table'

interface User { id: number; account: string; name: string; enabled: boolean; createTime: string }

const columns: ProTableColumn<User>[] = [
  { type: 'index' },
  { key: 'account', title: '账号', search: true },
  { key: 'name', title: '姓名', search: true },
  {
    key: 'enabled', title: '状态', tag: true, search: true,
    options: [
      { label: '启用', value: true, tagType: 'success' },
      { label: '禁用', value: false },
    ],
  },
  { key: 'createTime', title: '创建时间', format: 'datetime' },
]

// 唯一的后端契约 —— 任意 API 在这里适配
async function fetcher({ page, pageSize, ...query }) {
  const res = await fetch(`/api/users?page=${page}&size=${pageSize}`).then(r => r.json())
  return { items: res.list, total: res.total }
}
</script>

<template>
  <ProTable :columns="columns" :fetcher="fetcher" storage-key="users" />
</template>
```

必须渲染在宿主的 `<n-config-provider>` 内部——表格跟随其主题、locale 与密度。

## 列定义

数据列继承 Naive UI 列属性(`width`、`fixed`、`align`、`ellipsis`、`sorter` 等全部透传),外加:

| 字段 | 说明 |
|---|---|
| `key` | 行字段名;同时是搜索参数默认键、列设置 id、`#cell-{key}` 插槽名。必填。 |
| `title` | `string \| () => VNodeChild`,函数形式渲染期求值——切语言即时生效。 |
| `render` | `(row, rowIndex) => VNodeChild`,自定义单元格,优先级最高。 |
| `format` | `'date' \| 'datetime' \| 'money' \| (value, row) => string`,声明式格式化。 |
| `options` | 字典:一处声明,单元格翻译 + 搜索下拉两处使用。 |
| `tag` | 翻译值渲染为 `NTag`(type 取命中项的 `tagType`)。 |
| `search` | `boolean \| SearchConfig`,`true` = 有 `options` 用 select,否则 input。 |
| `hide` / `hideInTable` / `hideInSetting` | 初始隐藏 / 仅作搜索项 / 不进列设置面板(典型:操作列)。 |

特殊列:`{ type: 'selection' | 'expand' | 'index' }`;操作列 = 普通列 + `render` + `fixed: 'right'` + `hideInSetting: true`。

单元格优先级:`render` → `#cell-{key}` 插槽 → `options` 翻译(+`tag`)→ `format` → 原始值。

## 核心 Props

| Prop | 说明 |
|---|---|
| `columns` | 必填 |
| `fetcher` | `(params) => Promise<{items, total}>`,远程模式 |
| `data` | 静态模式(客户端分页) |
| `search` | `false` 隐藏搜索卡片;`layout: 'inline'` 无卡片单行,适配窄栏/主从栏 |
| `storage-key` | 开启列设置 + 密度的 localStorage 持久化 |
| `labels` | 传 `computed` 即随语言切换 |
| `active-row-key` | 配合 `@row-click` 做主从选中高亮 |
| `row-draggable` / `drag-handle` | 行拖拽排序(sortablejs 懒加载),落库由宿主 |

其余属性(`striped`、`max-height`、`virtual-scroll`、`checked-row-keys` 等)原样透传给 `n-data-table`。

**事件**:`search`、`reset`、`loaded`、`error`、`row-click`、`row-drag-sort`——组件自身不弹消息,宿主在 `@error` 里处理。

## 全局默认(provide/inject)

在宿主根组件 `provide` 一次,所有 ProTable 继承;优先级恒为 **实例 prop / 列显式值 > 全局默认 > 内置兜底**。

```ts
// main.ts
import { createProTableDefaults, PRO_TABLE_DEFAULTS } from 'tenon-naive-pro-table'

app.provide(PRO_TABLE_DEFAULTS, createProTableDefaults({
  align: 'left',
  pageSizes: [10, 20, 50, 100],
  labels: computed(() => ({ search: t('common.search'), reset: t('common.reset') })),
}))
```

## 更多能力

树形/可展开行、服务端排序(`sorter: true`)、搜索折叠、虚拟滚动、合计行、合并单元格、跨页保持勾选等,均经 attrs / 列透传直接可用。

> 完整 API、行为约定与在 TenonAdmin 中的接入细节,见 [package README](https://github.com/Tenon-Net/tenon-naive-pro-table/blob/main/README.zh-CN.md) 与 [tenon COMPONENTS.md](https://github.com/Tenon-Net/TenonAdmin/blob/main/web/COMPONENTS.md)。
