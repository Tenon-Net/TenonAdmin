# 非标准页面模板 (Page Variants)

`create-crud-frontend.md` 覆盖了最常见的 flat ProTable CRUD（Position 模式）。本文档补充三种常见变体，模式差异较大，不能硬套 flat CRUD。

## 变体一：树表（Org 模式）

适用于有父子层级的数据（机构、分类、菜单）。

### 与 flat CRUD 的核心差异

| 方面 | flat CRUD | 树表 |
|---|---|---|
| 数据获取 | `fetcher` prop（ProTable 内部管分页） | 手动 `load()` + `buildTree()` 传 `:data` |
| 分页 | 有 | `:pagination="false"` |
| 搜索 | 列级 `search: true` | 手动 `filterTree()` + `#toolbar` 搜索框 |
| 展开 | 不适用 | 受控 `expanded-row-keys` + 全展/全收按钮 |
| 新增 | 一种 `openAdd()` | `openAdd(parentId)` — 可新增子节点 |
| 刷新 | `tableRef.value?.refresh()` | 调 `load()` 重新拉取 |

### 关键代码模式

```typescript
import { buildTree, expandableIds, filterTree, type Tree } from '@/utils/tree'

const tree = ref<Tree<SysOrg>[]>([])
const loading = ref(false)

async function load() {
  loading.value = true
  try {
    tree.value = buildTree(await orgApi.list())  // list 接口返回平铺数组
  } finally {
    loading.value = false
  }
}
onMounted(load)

// 搜索：客户端过滤
const keyword = ref('')
const filteredTree = computed(() => {
  const kw = keyword.value.trim().toLowerCase()
  if (!kw) return tree.value
  return filterTree(tree.value, (n) => n.name.toLowerCase().includes(kw))
})

// 展开控制：数据变化时重算展开节点
const expandedKeys = ref<number[]>([])
watch(filteredTree, (t) => (expandedKeys.value = expandableIds(t)), { immediate: true })
```

### Template 骨架

```vue
<ProTable
  :columns="columns"
  :data="filteredTree"
  :loading="loading"
  row-key="id"
  :pagination="false"
  :toolbar="{ refresh: false }"
  :expanded-row-keys="expandedKeys"
  @update:expanded-row-keys="(keys: number[]) => (expandedKeys = keys)"
>
  <template #toolbar>
    <n-input v-model:value="keyword" clearable placeholder="搜索..." style="width: 220px" />
    <n-button quaternary @click="toggleExpandAll">
      {{ allExpanded ? '全部收起' : '全部展开' }}
    </n-button>
    <n-button type="primary" @click="openAdd(0)">新增</n-button>
  </template>
</ProTable>
```

### 注意事项

- `list` 接口返回全量平铺数据（非分页），后端用 `GET list` 而非 `GET page`
- 后端删除需检查 `HasChildren`（有子节点不允许删除）
- 表单中的"上级节点"用 `OrgTreeSelect` 组件，需传 `:exclude-subtree-of="editingId"` 防止选自己/子孙为父（成环）
- NDropdown 适合收纳多个操作（编辑/新增子节点/删除），避免操作列过宽

**参考源码：** `web/src/views/system/org/index.vue`（276 行）

---

## 变体二：主从/左右分栏（Dict 模式）

适用于父子关系紧密的数据对（字典类型+字典项、分类+子项）。

### 与 flat CRUD 的核心差异

| 方面 | flat CRUD | 主从分栏 |
|---|---|---|
| 布局 | 单表 | 左右两栏（flex wrap） |
| 数据关系 | 独立 | 左侧选中 → 右侧按选中加载 |
| 表格 | 一个 ProTable | 左 ProTable + 右 n-data-table |
| 批量删除 | 一组 useBatchDelete | 两组（主/从各一组） |
| CRUD 状态 | 一套 show/form/editingId | 两套（主/从各一套） |
| 缓存 | 无 | 改动后需 `dictStore.invalidate()` 刷缓存 |

### 关键代码模式

```typescript
// 主从选中态
const selectedType = ref<SysDictType | null>(null)
const items = ref<SysDictItem[]>([])

async function loadItems() {
  const code = selectedType.value?.code
  if (!code) { items.value = []; return }
  itemsLoading.value = true
  try {
    const list = await dictAdminApi.items(code)
    // 竞态守卫：await 期间用户可能已切换，过期响应不覆盖
    if (selectedType.value?.code === code) items.value = list
  } finally {
    if (selectedType.value?.code === code) itemsLoading.value = false
  }
}
async function selectType(r: SysDictType) {
  selectedType.value = r
  await loadItems()
}

// 两组独立的批量删除
const { checkedKeys: typeCheckedKeys, hasSelection: typeHasSelection, run: typeBatchDelete } = useBatchDelete({
  remove: dictAdminApi.typeBatchRemove,
  refresh: () => { selectedType.value = null; items.value = []; typeTableRef.value?.refresh() },
})
const { checkedKeys: itemCheckedKeys, hasSelection: itemHasSelection, run: itemBatchDelete } = useBatchDelete({
  remove: dictAdminApi.itemBatchRemove,
  refresh: () => loadItems(),
})
```

### Template 骨架

```vue
<div class="master-detail-layout">
  <!-- 左：主表 -->
  <n-card class="pane">
    <ProTable
      :columns="typeColumns"
      :fetcher="api.typePage"
      :search="{ layout: 'inline' }"
      :active-row-key="selectedType?.id ?? null"
      :row-props="() => ({ style: 'cursor: pointer' })"
      @row-click="(row) => selectType(row)"
    />
  </n-card>

  <!-- 右：从表（选中后显示）-->
  <n-card v-if="selectedType" class="pane" :title="`${selectedType.name} 的子项`">
    <n-data-table :columns="itemColumns" :data="items" :loading="itemsLoading" />
  </n-card>
  <n-card v-else class="pane">
    <n-empty description="请先选择左侧项" />
  </n-card>
</div>

<style scoped>
.master-detail-layout {
  display: flex;
  flex-wrap: wrap;
  gap: var(--gap-card);
  align-items: stretch;
}
.pane {
  flex: 1 1 380px;
  min-width: 0;
}
</style>
```

### 注意事项

- 左侧用 `search: { layout: 'inline' }` 压缩搜索栏（窄面板无空间放搜索卡片）
- `:active-row-key` 高亮选中行，点击行触发 `@row-click`
- 右侧操作按钮中的 `stopPropagation` 防止列内按钮点击冒泡触发 `@row-click`
- 竞态守卫：`loadItems` 中 await 回来后要验证选中状态没变
- 如果主从数据影响全局缓存（如字典），每次写操作后调 `invalidate()`

**参考源码：** `web/src/views/system/dict/index.vue`（386 行）

---

## 变体三：侧栏筛选（User 模式）

适用于表格需要额外分类筛选维度的页面（用户按机构筛选、订单按客户筛选）。

### 与 flat CRUD 的核心差异

| 方面 | flat CRUD | 侧栏筛选 |
|---|---|---|
| 布局 | 单表 | 左侧树/列表 + 右侧 ProTable |
| 表格参数 | 固定 | `computed` 动态参数，响应侧栏选中变化 |
| 关联下拉 | 无 | `onMounted` 预加载多个下拉选项 |
| 跨页导航 | 无 | watch `route.query` 响应外部跳入参数 |
| 新增/编辑 | 单表单 | 表单字段多，可能需要 NGrid 多列布局 |

### 关键代码模式

```typescript
// 左侧机构树
const orgTree = ref<Tree<SysOrg>[]>([])
const selectedOrgId = ref<number | null>(null)
const tableParams = computed(() =>
  selectedOrgId.value == null ? {} : { orgId: selectedOrgId.value },
)

// 预加载关联下拉
onMounted(async () => {
  try {
    const { items } = await positionApi.page({ page: 1, pageSize: 200 })
    positionOptions.value = items.map((p) => ({ label: p.name, value: p.id }))
  } catch { /* 静默：配角下拉失败不打断列表 */ }

  try {
    orgTree.value = buildTree(await orgApi.list())
  } catch { /* 静默 */ }
})

// 跨页导航：角色页跳来 ?roleId=123
const route = useRoute()
watch(
  [tableRef, () => route.query.roleId],
  ([inst, roleId]) => {
    if (!inst) return
    const next = roleId == null ? undefined : Number(roleId)
    if (inst.params.roleId === next) return
    inst.params.roleId = next
    inst.search()  // 回第 1 页重查
  },
  { immediate: true },
)
```

### Template 骨架

```vue
<div class="sidebar-layout">
  <!-- 左侧筛选树 -->
  <n-card class="sidebar">
    <n-tree
      :data="orgTree"
      :selected-keys="selectedOrgId == null ? [] : [selectedOrgId]"
      key-field="id"
      label-field="name"
      @update:selected-keys="onOrgSelect"
    />
  </n-card>

  <!-- 右侧表格 -->
  <div class="main">
    <ProTable
      ref="tableRef"
      :columns="columns"
      :fetcher="userApi.page"
      :params="tableParams"
    >
      <template #toolbar>
        <n-button type="primary" @click="openAdd">新增</n-button>
        <n-button type="error" :disabled="!hasSelection" @click="batchDelete">
          批量删除
        </n-button>
      </template>
    </ProTable>
  </div>
</div>

<style scoped>
.sidebar-layout {
  display: flex;
  gap: var(--gap-card);
}
.sidebar {
  flex: 0 0 240px;
}
.main {
  flex: 1;
  min-width: 0;
}
</style>
```

### 注意事项

- `tableParams` 是 computed，侧栏选中变化时 ProTable 自动回第 1 页重查
- 预加载下拉用 `try/catch` 静默失败——配角数据拉不到不应阻断主列表
- 跨页导航需 `watch` 而非 `onMounted`：页面被 keep-alive 时 onMounted 不再触发，只有 query 变化触发 watch
- 同时 watch `tableRef`：首次加载时表格实例可能还没挂载，需要等实例就绪后再设参数
- 新增/编辑的 Input 类型可能不同（`AddUserInput` vs `UpdateUserInput`），根据业务需要决定是否拆分

**参考源码：** `web/src/views/system/user/index.vue`（515 行）

---

## 选型速查

| 你的页面特征 | 选哪个 |
|---|---|
| 平铺列表 + 分页 | `create-crud-frontend.md`（flat CRUD） |
| 有父子层级 / 树结构 | 变体一（树表） |
| 一对多从属关系，需同时看两张表 | 变体二（主从分栏） |
| 列表需要额外维度筛选（树/分类/标签） | 变体三（侧栏筛选） |
| 以上都不是 | 先看最接近的变体，按需调整 |
