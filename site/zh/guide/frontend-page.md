# 前端加一个页面

接着上一篇[端到端加一个业务模块](/zh/guide/business-module)——后端已经有了 `GET/POST/PUT/DELETE /api/v1/sample/doc` 这组接口。本篇把它做成一个能操作的管理页面。

## 1. 重新生成 API 类型

前端不手写接口类型,而是从后端跑起来后的 OpenAPI 契约生成。后端(带着刚加的 `sample/doc` 接口)先跑起来,再在 `web/` 下:

```bash
npm run gen:api
```

它执行的其实是 `openapi-typescript http://localhost:5100/openapi/v1.json -o src/api/schema.d.ts`(见 `web/package.json`)——从 `/openapi/v1.json` 重新生成 `src/api/schema.d.ts`。

::: warning 不要手改 `schema.d.ts`
它是生成产物,后端接口一变就得重新跑 `gen:api`,手改的内容下次生成会被覆盖。
:::

## 2. 加类型 + 封装 API

在 `web/src/types/api.ts` 补一个领域类型(与后端 DTO 字段对齐,后端是驼峰序列化):

```ts
/** 示例机构隔离文档(对齐后端 SampleDoc)。 */
export interface SampleDoc {
  id: number
  title: string
}
```

在 `web/src/api/index.ts` 里按域加一组,基于 `client.ts` 导出的 typed client、用 `unwrap<T>` 解包统一信封:

```ts
import type { SampleDoc } from '@/types/api'

export const sampleDocApi = {
  list: () => client.GET('/api/v1/sample/doc', {}).then((r) => unwrap<SampleDoc[]>(r)),
  create: (title: string) =>
    client.POST('/api/v1/sample/doc', { body: { title } }).then((r) => unwrap<number>(r)),
  rename: (id: number, title: string) =>
    client.PUT('/api/v1/sample/doc/{id}', { params: { path: { id } }, body: { title } }).then((r) => unwrap<boolean>(r)),
  remove: (id: number) =>
    client.DELETE('/api/v1/sample/doc/{id}', { params: { path: { id } } }).then((r) => unwrap<boolean>(r)),
}
```

`unwrap` 已经处理了两种失败形状(带 `code` 的业务信封、不带 `code` 的 `ProblemDetails`),视图层直接 `catch` 后丢给 `translateError` 就行,不用在这里重复判断。

`sample/doc` 的 `List` 接口不分页,直接返回数组——所以这里不用 `toPage`/分页参数三件套(那是给 `PagedList<T>` 端点用的,参考 `dictAdminApi.typePage` 的写法)。

## 3. 写列表页

先看 `web/COMPONENTS.md`——它是前端共享组件的索引,写页面前必读,页面用到的 `FormContainer`(弹窗表单容器)、`useConfirm`(二次确认)在这里都有约定和范例页指路。

`sample/doc` 是不分页的平铺列表,不需要 `ProTable`,用裸 `NDataTable` 就够(参考 `web/src/views/system/dict/index.vue` 右侧字典项面板的同款写法)。新建 `web/src/views/sample/doc/index.vue`:

```vue
<script setup lang="ts">
import { h, reactive, ref, onMounted } from 'vue'
import { NButton, NCard, NSpace, NInput, NPopconfirm, NForm, NFormItem, NDataTable, useMessage, type DataTableColumns, type FormInst, type FormRules } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import FormContainer from '@/components/FormContainer/index.vue'
import { useConfirm } from '@/composables/useConfirm'
import { useAuthStore } from '@/stores/auth'
import { translateError } from '@/utils/error'
import { sampleDocApi } from '@/api'
import type { SampleDoc } from '@/types/api'

const { t } = useI18n()
const message = useMessage()
const { run } = useConfirm()
const authStore = useAuthStore()

const rows = ref<SampleDoc[]>([])
const loading = ref(false)
async function load() {
  loading.value = true
  try {
    rows.value = await sampleDocApi.list()
  } catch (e) {
    message.error(translateError(e))
  } finally {
    loading.value = false
  }
}
onMounted(load)

const columns: DataTableColumns<SampleDoc> = [
  { title: () => t('sampleDoc.title'), key: 'title' },
  {
    title: () => t('common.operation'),
    key: 'op',
    width: 130,
    render: (r) =>
      h(NSpace, { size: 4 }, () => [
        authStore.hasPerm('PUT:/api/v1/sample/doc/{id}')
          ? h(NButton, { size: 'small', quaternary: true, type: 'primary', onClick: () => openEdit(r) }, () => t('common.edit'))
          : null,
        authStore.hasPerm('DELETE:/api/v1/sample/doc/{id}')
          ? h(
              NPopconfirm,
              {
                onPositiveClick: () =>
                  run(() => sampleDocApi.remove(r.id), t('common.deleted')).then((ok) => { if (ok) load() }),
              },
              {
                trigger: () => h(NButton, { size: 'small', quaternary: true, type: 'error' }, () => t('common.delete')),
                default: () => t('sampleDoc.deleteConfirm', { title: r.title }),
              },
            )
          : null,
      ]),
  },
]

// ── 新增/编辑弹窗 ──
const show = ref(false)
const formRef = ref<FormInst | null>(null)
const editingId = ref<number | null>(null)
const form = reactive({ title: '' })
const rules: FormRules = {
  title: { required: true, whitespace: true, message: () => t('sampleDoc.titleRequired'), trigger: ['input', 'blur'] },
}
function openAdd() {
  editingId.value = null
  form.title = ''
  show.value = true
}
function openEdit(r: SampleDoc) {
  editingId.value = r.id
  form.title = r.title
  show.value = true
}
async function save() {
  await formRef.value?.validate()
  try {
    if (editingId.value === null) await sampleDocApi.create(form.title)
    else await sampleDocApi.rename(editingId.value, form.title)
    message.success(t('common.success'))
    await load()
  } catch (e) {
    message.error(translateError(e))
    return false
  }
}
</script>

<template>
  <n-card :bordered="true">
    <template #header-extra>
      <n-button v-auth="'POST:/api/v1/sample/doc'" type="primary" size="small" @click="openAdd">
        {{ t('common.add') }}
      </n-button>
    </template>
    <n-data-table :columns="columns" :data="rows" :loading="loading" :row-key="(r: SampleDoc) => r.id" />
  </n-card>

  <FormContainer
    v-model:show="show"
    :title="editingId === null ? t('sampleDoc.addTitle') : t('sampleDoc.editTitle')"
    :width="420"
    :on-confirm="save"
    :confirm-text="t('common.save')"
  >
    <n-form ref="formRef" :model="form" :rules="rules" label-placement="left" :label-width="60">
      <n-form-item :label="t('sampleDoc.title')" path="title">
        <n-input v-model:value="form.title" :placeholder="t('sampleDoc.title')" />
      </n-form-item>
    </n-form>
  </FormContainer>
</template>
```

要点(都来自 `web/COMPONENTS.md` 的既有约定,不是这里现造的):

- **`FormContainer`** 用 `onConfirm` 协议接管 loading/关闭——`save()` 返回 `false`(或抛出)时弹窗不关,方便展示校验错误。
- **`useConfirm().run`** 配 `n-popconfirm`:popconfirm 当触发器,确认后的动作与成/败 toast 交给 `run`。
- **`v-auth`**:按钮级权限,值是路由权限码本身(`POST:/api/v1/sample/doc`),不命中直接把 DOM 节点移除。操作列里的编辑/删除按钮额外用 `authStore.hasPerm(...)` 判断是否渲染,双重收口在同一份权限码上。
- **错误处理留在视图层**:`catch (e) { message.error(translateError(e)) }`,不在 API 层弹 UI。

## 4. 挂进菜单(页面才可见)

不用手动改 `router/`。动态路由是登录后根据菜单树自动注册的——`composables/useAuthMenu.ts` 用 `import.meta.glob('/src/views/**/*.vue')` 把菜单的 `Component` 字符串映射到 `.vue` 文件。你只需要在**菜单管理**页建一个节点:

| 字段 | 值 | 说明 |
|---|---|---|
| Type | 菜单 | |
| Path | `/sample/doc` | 路由地址 |
| Component | `sample/doc/index` | → `/src/views/sample/doc/index.vue`(不带前后缀) |
| 所属应用 | 选一个模块 | 仅顶级目录有效 |

保存后重新登录(或刷新触发路由重建),菜单里就能看到这个页面了。如果控制台报 `[menu] 缺少视图组件`,说明 `Component` 字符串跟文件路径没对上。

## 5. 补 i18n 文案

上面的页面用了 `sampleDoc.*` 一组 key,照现有页面的样子加进 `web/src/locales/zh-CN.ts` / `en-US.ts`:

```ts
sampleDoc: {
  title: '标题',
  titleRequired: '请输入标题',
  addTitle: '新增文档',
  editTitle: '编辑文档',
  deleteConfirm: '确认删除「{title}」?',
},
```

## 6. 提交前

```bash
npm run lint       # oxlint(lint:fix 自动修)
npm run typecheck  # vue-tsc --noEmit
```

## 下一步

页面能跑之后,想把整套系统发布出去,看下一篇:[容器化部署一条龙](/zh/guide/deployment/docker)。

> 更完整的前端规范(命名约定、组件目录结构、ProTable 各种模式)见 `web/COMPONENTS.md` 和[前端规范](/zh/standard/frontend)。


---

<!-- TODO(rewrite): merged from frontend.md -->

# B. 前端

### B1. 重新生成 API 类型

后端跑起来后:

```bash
cd web && npm run gen:api     # 从 /openapi/v1.json 重生成 src/api/schema.d.ts(勿手改)
```

新端点即出现在类型里。

### B2. 封装　`web/src/api/index.ts`

按域加一组:

```ts
export const productApi = {
  page: (params: { page: number; pageSize: number; name?: string }) =>
    client.GET('/api/v1/biz/product/page', {
      params: { query: { Current: params.page, Size: params.pageSize, Name: params.name } }, // PascalCase
    }).then((r) => unwrap<PagedList<Product>>(r)).then((p) => ({ items: p.items, total: p.total })),
  add: (body: ProductInput) => client.POST('/api/v1/biz/product', { body }).then((r) => unwrap<number>(r)),
  // update/remove 同 menuApi 风格
}
```

### B3. CRUD 视图　`web/src/views/biz/product/index.vue`

蓝本:`views/system/menu/index.vue`(含 `NDataTable` + `NModal` 表单 + `NPopconfirm`)。列表逻辑用 `useTable`:

```ts
const { loading, rows, pagination, search, onPage } = useTable(productApi.page, {
  initParams: { name: '' },
  onError: (e) => message.error(translateError(e)),
})
```

- 列用 `h()` 渲染,操作列放编辑/删除按钮。
- 所有可见文本走 `t('...')`,i18n key 见 B6。
- 危险按钮可挂 `v-auth="'POST:/api/v1/biz/product'"`(当前 fail-open,见[前端规范](/zh/standard/frontend))。

### B4. 挂载菜单(页面才可见)

到**菜单管理**页建/确认菜单节点,关键是 `Component` 必须与文件路径对应:

| 字段 | 值 | 说明 |
|---|---|---|
| Type | 菜单 | 目录只作父节点,按钮只承载权限码 |
| Path | `/biz/product` | 即路由地址 |
| Component | `biz/product/index` | → `/src/views/biz/product/index.vue`(**不带前后缀**) |
| 所属应用 | 选模块 | 仅顶级目录有效 |

### B5. 路由如何解析(原理,无需手动加路由)

`composables/useAuthMenu.ts` 用 `import.meta.glob('/src/views/**/*.vue')` 把 `Component` 串映射到 `.vue` 文件,登录/刷新后自动注册为动态路由(名 `menu-${id}`,挂 `layout` 下)。**所以你不用动 `router/`**——建好 `.vue` + 配好菜单即可。若控制台报 `[menu] 缺少视图组件`,是 `Component` 串与文件路径没对上。

### B6. i18n　`web/src/locales/zh-CN.ts` / `en-US.ts`

加该页文案 key,以及 A6 里新错误码对应的翻译(`translateError` 按 code/msgKey 取)。

### B7. 提交前

```bash
npm run lint && npm run typecheck
```



---

<!-- TODO(rewrite): merged from checklist.md -->

# C. 端到端清单

**后端**
- [ ] 实体(选 `BaseEntity`/`DataEntity`)+ Sugar 特性 + 唯一索引
- [ ] `*Models.cs` DTO(record)
- [ ] `I*Service` + `*Service`(virtual、事务、查重带软删)
- [ ] `ServicesSetup` 里 `TryAddScoped` 注册
- [ ] 控制器(`[ApiController]`/`[Route]`/`[Module]`,每动作 `[RolePermission]`)
- [ ] `ErrorCode` 加码
- [ ] 热读才加缓存(`CacheKeys` + cache-aside + 失效)
- [ ] 种子(可选,固定 Id)
- [ ] 测试(`WebApplicationFactory`,SQLite/MySQL 双绿)

**前端**
- [ ] `npm run gen:api` 重生成类型
- [ ] `api/index.ts` 加一组
- [ ] `views/<模块>/<实体>/index.vue`(`useTable` + Naive 表格/表单)
- [ ] i18n 文案 + 错误码翻译
- [ ] `lint` + `typecheck` 通过

**配置权限(运行时)**
- [ ] 菜单管理建节点(Path/Component 对应)
- [ ] 角色管理勾选授权

