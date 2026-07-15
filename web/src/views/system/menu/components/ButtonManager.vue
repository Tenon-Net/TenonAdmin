<script setup lang="ts">
// 某个菜单/目录下「权限按钮」的专属管理弹窗 —— 把按钮从主菜单树里剥出来单独管,
// 树只剩目录/菜单,不再被几十个按钮撑长。按钮就是 type=Button 的 SysMenu 行,
// 增删改复用 menuApi.add/update/remove(parentId=当前菜单),后端零改动。
import { computed, h, reactive, ref } from 'vue'
import {
  NButton, NSpace, NForm, NFormItemGi, NGrid, NInput, NInputNumber, NSelect, NSwitch,
  NPopconfirm, NCheckbox, NEmpty, useMessage, type FormInst, type FormRules,
} from 'naive-ui'
import { ProTable, type ProTableColumn } from 'tenon-naive-pro-table'
import { useI18n } from 'vue-i18n'
import AppIcon from '@/components/AppIcon.vue'
import FormContainer from '@/components/FormContainer/index.vue'
import StatusSwitch from '@/components/StatusSwitch/index.vue'
import { useConfirm } from '@/composables/useConfirm'
import { menuApi } from '@/api'
import { translateError } from '@/utils/error'
import { MenuType, type MenuInput, type MenuTreeNode } from '@/types/menu'
import type { PermissionRouteItem } from '@/types/api'

// tree/routes 由父页传入并保持响应:父页 CRUD 后 load() 刷新 tree,本弹窗的 buttons 计算属性随之更新,
// 无需自己再发一次整树请求。
const props = defineProps<{
  tree: MenuTreeNode[]
  routes: PermissionRouteItem[]
  /** 当前所属应用的路由匹配前缀(sys/biz…);空=不按应用过滤,全量显示。 */
  appPrefix?: string
}>()
const emit = defineEmits<{ (e: 'changed'): void }>()

const { t } = useI18n()
const message = useMessage()
const { run } = useConfirm()

const show = ref(false)
const menuId = ref<number | null>(null)
const menuTitle = ref('')

/** 在整棵树里按 id 找节点(父页传的 tree 含按钮 children)。 */
function findNode(nodes: MenuTreeNode[], id: number): MenuTreeNode | null {
  for (const n of nodes) {
    if (n.id === id) return n
    const hit = findNode(n.children, id)
    if (hit) return hit
  }
  return null
}

/** 当前菜单下的按钮子节点(随父页 tree 响应更新)。 */
const buttons = computed<MenuTreeNode[]>(() => {
  if (menuId.value == null) return []
  const node = findNode(props.tree, menuId.value)
  return (node?.children ?? []).filter((c) => c.type === MenuType.Button)
})

/** 已占用的权限码,用于批量添加去重。 */
const usedCodes = computed(() => new Set(buttons.value.map((b) => b.permission).filter(Boolean)))

// ── 路由按应用软过滤 ────────────────────────────────────────────
// route.path 形如 /api/v1/sys/...;appPrefix 是该应用的路由段(sys/biz)。空前缀=不过滤。
// 软过滤:默认只列本应用路由,一键「显示全部」放开(业务页正当需要 /api/v1/sys/file 上传、/sys/dict 等共享系统路由)。
const routeSeg = computed(() => (props.appPrefix ?? '').trim().replace(/^\/+|\/+$/g, ''))
const belongsToApp = (path: string) => {
  const seg = routeSeg.value
  if (!seg) return true
  return path.includes(`/${seg}/`) || path.endsWith(`/${seg}`)
}
/** 是否存在「非本应用」的路由 —— 决定要不要展示「显示全部」开关。 */
const hasOtherRoutes = computed(() => routeSeg.value !== '' && props.routes.some((r) => !belongsToApp(r.path)))
const showAllRoutes = ref(false)

// 权限码下拉:label 展示「METHOD /路径」便于辨认,value 就是要写进 permission 的码本身。
// 无前缀→平铺全量;有前缀且不显示全部→仅本应用;显示全部→按「本应用/其他应用」分组。
const permissionOptions = computed(() => {
  const toOpt = (r: PermissionRouteItem) => ({ label: `${r.method} ${r.path}`, value: r.code })
  const seg = routeSeg.value
  if (!seg) return props.routes.map(toOpt)
  const inApp = props.routes.filter((r) => belongsToApp(r.path))
  if (!showAllRoutes.value) return inApp.map(toOpt)
  const other = props.routes.filter((r) => !belongsToApp(r.path))
  return [
    { type: 'group', key: '__inApp', label: t('menu.routesInApp'), children: inApp.map(toOpt) },
    { type: 'group', key: '__other', label: t('menu.routesOther'), children: other.map(toOpt) },
  ]
})

/**
 * 「所属页面」下拉:全部非按钮节点(目录 + 页面),缩进体现层级。
 * 不排除目录 —— 无对应页面的权限码锚点(如 ping)就该挂目录。
 * 按钮是叶子、不可能成环,故无需像主页 parentOptions 那样排除自身子树。
 */
const parentOptions = computed(() => {
  const opts: { label: string; value: number }[] = []
  const walk = (nodes: MenuTreeNode[], depth: number) => {
    for (const n of nodes) {
      if (n.type === MenuType.Button) continue
      opts.push({ label: `${'　'.repeat(depth)}${n.title}`, value: n.id })
      walk(n.children, depth + 1)
    }
  }
  walk(props.tree, 0)
  return opts
})

function open(menu: { id: number; title: string }) {
  menuId.value = menu.id
  menuTitle.value = menu.title
  show.value = true
}
defineExpose({ open })

// ── 单个新增/编辑 ────────────────────────────────────────────────
const editShow = ref(false)
const editingId = ref<number | null>(null)
const formRef = ref<FormInst | null>(null)
const rules: FormRules = {
  title: { required: true, whitespace: true, message: () => t('menu.titleRequired'), trigger: ['input', 'blur'] },
}
const blank = (): MenuInput => ({
  parentId: menuId.value ?? 0, type: MenuType.Button, title: '', permission: '', sort: 0,
  enabled: true, moduleId: null, path: '', component: '', icon: '', visible: true,
})
const form = reactive<MenuInput>(blank())

/** 行 → 全量入参:回填与 StatusSwitch 行内改状态共用(后端无独立启停端点,均走全量 update)。 */
const toInput = (r: MenuTreeNode): MenuInput => ({
  parentId: r.parentId, type: r.type, title: r.title, permission: r.permission, sort: r.sort,
  enabled: r.enabled, moduleId: null,
  path: r.path ?? '', component: r.component ?? '', icon: r.icon ?? '', visible: r.visible,
})

function openAdd() {
  editingId.value = null
  showAllRoutes.value = false // 新增默认只看本应用路由
  Object.assign(form, blank())
  editShow.value = true
}
function openEdit(r: MenuTreeNode) {
  editingId.value = r.id
  showAllRoutes.value = true // 编辑时展开全部,保证已选的(可能跨应用的)权限码在下拉里可见
  Object.assign(form, toInput(r))
  editShow.value = true
}
async function save() {
  await formRef.value?.validate()
  try {
    // parentId 取表单值(而非当前弹窗的 menuId)——「所属页面」改了就是移动:
    // 保存后本列表少一条、目标页面徽标 +1,由父页重拉树自然反映。
    const payload: MenuInput = { ...form, type: MenuType.Button, moduleId: null }
    if (editingId.value === null) await menuApi.add(payload)
    else await menuApi.update(editingId.value, payload)
    message.success(t('menu.saved'))
    emit('changed')
  } catch (e) {
    message.error(translateError(e))
    return false
  }
}

// ── 从路由批量添加 ──────────────────────────────────────────────
type BatchRow = { code: string; method: string; path: string; title: string; checked: boolean }
const batchShow = ref(false)
const batchRows = ref<BatchRow[]>([])
const batchSaving = ref(false)
const batchKeyword = ref('')

/** 搜索过滤:返回 batchRows 里命中的同一批对象引用,勾选/改标题的状态跨搜索保留;
 *  saveBatch 仍以全量 batchRows 的 checked 为准,不会漏掉被过滤隐藏的已勾选行。 */
const filteredBatchRows = computed(() => {
  const kw = batchKeyword.value.trim().toLowerCase()
  const seg = routeSeg.value
  return batchRows.value.filter((r) => {
    if (seg && !showAllRoutes.value && !belongsToApp(r.path)) return false // 按应用软过滤(可一键放开)
    if (!kw) return true
    return r.method.toLowerCase().includes(kw) || r.path.toLowerCase().includes(kw) || r.title.toLowerCase().includes(kw)
  })
})

/** HTTP 方法 → 默认标题(可就地改)。 */
function defaultTitle(method: string): string {
  const m = method.toUpperCase()
  if (m === 'GET') return t('menu.btnQuery')
  if (m === 'POST') return t('menu.btnCreate')
  if (m === 'PUT' || m === 'PATCH') return t('menu.btnUpdate')
  if (m === 'DELETE') return t('menu.btnDelete')
  return method
}
function openBatch() {
  batchKeyword.value = ''
  showAllRoutes.value = false // 批量默认只列本应用路由
  // 只列当前菜单还没建过的路由,避免重复。
  batchRows.value = props.routes
    .filter((r) => !usedCodes.value.has(r.code))
    .map((r) => ({ code: r.code, method: r.method, path: r.path, title: defaultTitle(r.method), checked: false }))
  batchShow.value = true
}
const batchChecked = computed(() => batchRows.value.filter((r) => r.checked))
async function saveBatch() {
  const picked = batchChecked.value
  if (!picked.length) return false // 未选任何路由,别关弹窗
  batchSaving.value = true
  try {
    // ponytail: 逐个 add,量级大到卡顿再考虑后端批量端点
    for (const r of picked) {
      await menuApi.add({
        parentId: menuId.value ?? 0, type: MenuType.Button, title: r.title.trim() || r.method,
        permission: r.code, sort: 0, enabled: true, moduleId: null,
        path: '', component: '', icon: '', visible: true,
      })
    }
    message.success(t('menu.saved'))
    emit('changed')
    batchShow.value = false
  } catch (e) {
    message.error(translateError(e))
    return false
  } finally {
    batchSaving.value = false
  }
}

const columns: ProTableColumn<MenuTreeNode>[] = [
  { title: () => t('menu.title'), key: 'title', align: 'left' },
  { title: () => t('menu.permission'), key: 'permission', render: (r) => r.permission || '—' },
  { title: () => t('menu.sort'), key: 'sort', width: 70 },
  {
    title: () => t('common.status'),
    key: 'enabled',
    width: 84,
    render: (r) =>
      h(StatusSwitch, {
        value: r.enabled,
        request: (next: boolean) => menuApi.update(r.id, { ...toInput(r), enabled: next }),
        'onUpdate:value': () => emit('changed'),
      }),
  },
  {
    title: () => t('common.operation'),
    key: 'op',
    width: 130,
    render: (r) =>
      h(NSpace, { size: 2, wrapItem: false }, () => [
        h(NButton, { size: 'small', quaternary: true, type: 'primary', onClick: () => openEdit(r) }, () => t('common.edit')),
        h(
          NPopconfirm,
          {
            onPositiveClick: () =>
              run(() => menuApi.remove(r.id), t('menu.deleted')).then((ok) => {
                if (ok) emit('changed')
              }),
          },
          {
            trigger: () => h(NButton, { size: 'small', quaternary: true, type: 'error' }, () => t('common.delete')),
            default: () => t('menu.deleteConfirm', { title: r.title }),
          },
        ),
      ]),
  },
]
</script>

<template>
  <!-- 列表弹窗:只有一个「关闭」底栏(不走 onConfirm 提交协议) -->
  <FormContainer
    v-model:show="show"
    :title="t('menu.buttonManagerTitle', { title: menuTitle })"
    :width="960"
  >
    <n-space vertical :size="12">
      <n-space>
        <n-button type="primary" size="small" @click="openAdd">{{ t('menu.addButton') }}</n-button>
        <n-button size="small" @click="openBatch">{{ t('menu.batchFromRoutes') }}</n-button>
      </n-space>
      <ProTable :columns="columns" :data="buttons" :pagination="false" row-key="id" storage-key="sys-menu-buttons" />
    </n-space>
    <template #footer>
      <n-space justify="end">
        <n-button @click="show = false">{{ t('common.close') }}</n-button>
      </n-space>
    </template>
  </FormContainer>

  <!-- 单个新增/编辑按钮 -->
  <FormContainer
    v-model:show="editShow"
    :title="editingId === null ? t('menu.addButton') : t('common.edit')"
    :width="600"
    :on-confirm="save"
    :confirm-text="t('common.save')"
  >
    <n-form ref="formRef" :model="form" :rules="rules" label-placement="left" :label-width="80">
      <n-grid :cols="1" :y-gap="4">
        <n-form-item-gi :label="t('menu.title')" path="title">
          <n-input v-model:value="form.title" :placeholder="t('menu.title')" />
        </n-form-item-gi>
        <!-- 改这里 = 把按钮移到别的页面(按钮不在主树里,这是唯一能改归属的入口)。 -->
        <n-form-item-gi :label="t('menu.buttonParent')">
          <n-select v-model:value="form.parentId" :options="parentOptions" filterable />
        </n-form-item-gi>
        <n-form-item-gi :label="t('menu.permission')">
          <n-space vertical :size="4" style="width: 100%">
            <n-select
              v-model:value="form.permission"
              :options="permissionOptions"
              filterable
              tag
              clearable
              :placeholder="t('menu.permissionPlaceholder')"
            />
            <n-checkbox v-if="hasOtherRoutes" v-model:checked="showAllRoutes" size="small">
              {{ t('menu.showAllRoutes') }}
            </n-checkbox>
          </n-space>
        </n-form-item-gi>
        <n-form-item-gi :label="t('menu.sort')">
          <n-input-number v-model:value="form.sort" :min="0" style="width: 100%" />
        </n-form-item-gi>
        <n-form-item-gi :label="t('common.status')">
          <n-switch v-model:value="form.enabled" />
        </n-form-item-gi>
      </n-grid>
    </n-form>
  </FormContainer>

  <!-- 从路由批量添加 -->
  <FormContainer
    v-model:show="batchShow"
    :title="t('menu.batchTitle')"
    :width="640"
    :on-confirm="saveBatch"
    :confirm-text="t('common.save')"
  >
    <n-empty v-if="!batchRows.length" :description="t('menu.noRoutesLeft')" style="padding: 24px 0" />
    <n-space v-else vertical :size="10">
      <n-space align="center" :size="12">
        <n-input v-model:value="batchKeyword" clearable :placeholder="t('menu.batchSearchPlaceholder')" style="flex: 1">
          <template #prefix><AppIcon icon="ph:magnifying-glass" :size="16" /></template>
        </n-input>
        <n-checkbox v-if="hasOtherRoutes" v-model:checked="showAllRoutes">{{ t('menu.showAllRoutes') }}</n-checkbox>
      </n-space>
      <n-empty v-if="!filteredBatchRows.length" :description="t('menu.noRoutesLeft')" style="padding: 16px 0" />
      <n-space v-else vertical :size="6" style="max-height: 55vh; overflow: auto">
        <div v-for="r in filteredBatchRows" :key="r.code" style="display: flex; align-items: center; gap: 10px">
        <n-checkbox v-model:checked="r.checked" />
        <span style="width: 210px; color: var(--color-text-secondary, #888); font-size: 12px">{{ r.method }} {{ r.path }}</span>
        <n-input v-model:value="r.title" size="small" :disabled="!r.checked" style="width: 160px" />
        </div>
      </n-space>
    </n-space>
  </FormContainer>
</template>
