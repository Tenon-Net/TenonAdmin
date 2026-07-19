# DetailPage

详情页外壳:**返回按钮 + 标题 + actions 插槽 + body 插槽**。用于按记录展示只读详情(独立选项卡 / 就地切换两种形态共用)。补偿非菜单详情路由的空面包屑 —— 菜单树里没有 `:id` 详情路由,故头部面包屑为空,详情页自带标题与返回。

## Props

| 名 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `title` | `string` | — | 头部标题(**已翻译**;通常传记录名) |
| `loading` | `boolean` | `false` | body 套 `n-spin` 的加载态 |
| `showBack` | `boolean` | `true` | 是否显示返回按钮 |

## Emits

- `back` —— 点返回。**语义交父级决定**:
  - 路由态(独立标签):`router.push(列表路径).then(() => tabs.removeTab(当前详情 path))`,关掉本详情标签回列表。
  - 就地态(列表页内切换):清掉列表页的 `detailId` 状态即回列表。

## Slots

- 默认 —— 详情主体(通常 `n-descriptions`)。
- `actions` —— 头部右侧动作区(可选,如「编辑」按钮)。

## 用法

配套约定式详情路由(`views/**/detail.vue` → `/<路径>/:id/detail`,见 `src/router/detailRoutes.ts`)与 `useTabTitle()`(数据加载后把标签标题改成记录名)。做法、骨架与选型见 `skills/create-page-variant.md` 变体四(内核未内置示例页,按骨架在你的模块下丢一个 `detail.vue` 即可)。
