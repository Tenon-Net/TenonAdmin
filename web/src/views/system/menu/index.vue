<script setup lang="ts">
import { computed, h, onMounted, reactive, ref, watch } from 'vue'
import {
  NButton, NSpace, NTag, NForm, NInput, NInputNumber, NSwitch,
  NSelect, NPopconfirm, NGrid, NFormItemGi, useMessage, type FormInst, type FormRules,
} from 'naive-ui'
import { ProTable, type ProTableColumn } from 'tenon-naive-pro-table'
import { useI18n } from 'vue-i18n'
import AppIcon from '@/components/AppIcon.vue'
import IconPicker from '@/components/IconPicker/index.vue'
import FormContainer from '@/components/FormContainer/index.vue'
import StatusSwitch from '@/components/StatusSwitch/index.vue'
import { useConfirm } from '@/composables/useConfirm'
import { viewComponentPaths, buildRoutesForModule } from '@/composables/useAuthMenu'
import { useAuthStore } from '@/stores/auth'
import { menuApi, moduleApi } from '@/api'
import { translateError } from '@/utils/error'
import { expandableIds, filterTree } from '@/utils/tree'
import { MenuType, type MenuInput, type MenuTreeNode } from '@/types/menu'
import type { ModuleRow, PermissionRouteItem } from '@/types/api'

const { t } = useI18n()
const message = useMessage()
const { run } = useConfirm()
const auth = useAuthStore()

const loading = ref(false)
const tree = ref<MenuTreeNode[]>([])
const modules = ref<ModuleRow[]>([])
/** 「未分配」哨兵:雪花 id 无 0,用它表示 moduleId==null 的顶级目录分组。 */
const UNASSIGNED = 0
/** 所属应用筛选:默认跟随当前进入的应用,modules 加载后再校正。 */
const moduleFilter = ref<number>(auth.currentModuleId ?? UNASSIGNED)
/** 后端实时路由表(含消费方自建控制器)——权限码下拉的数据源,免手敲 `GET:/api/v1/...`。 */
const routes = ref<PermissionRouteItem[]>([])

async function load() {
  loading.value = true
  try {
    tree.value = await menuApi.tree()
  } catch (e) {
    message.error(translateError(e))
  } finally {
    loading.value = false
  }
}

/**
 * 菜单改完顺手重建当前应用的壳层(侧边栏 + 动态路由)——否则新建的菜单要 F5 才出现,
 * 而这与「组件路径写错→菜单静默消失」是同一个症状,管理员无从分辨自己错在哪。
 * 失败静默:菜单本身已存成功,壳层没刷新不该把成功报成失败,下次 F5 自愈。
 */
async function syncShell() {
  if (!auth.currentModuleId) return
  try {
    await buildRoutesForModule(auth.currentModuleId)
  } catch {
    /* 见上 */
  }
}
onMounted(async () => {
  await load()
  try {
    modules.value = await moduleApi.list()
    // 若默认筛选值不在选项内(如当前未进入任何应用),回退到第一个模块。
    if (!filterOptions.value.some((o) => o.value === moduleFilter.value)) {
      moduleFilter.value = modules.value[0]?.id ?? UNASSIGNED
    }
  } catch {
    // 模块列表仅供「所属应用」下拉;拉取失败不阻塞菜单管理主流程。
  }
  try {
    routes.value = await menuApi.routes()
  } catch {
    // 路由清单仅供「权限码」下拉;拉取失败(如未授该码)退化为手输,不阻塞菜单管理主流程。
  }
})

const typeTag = (ty: MenuType) =>
  ty === MenuType.Catalog ? { text: t('menu.typeCatalog'), type: 'info' as const }
  : ty === MenuType.Menu ? { text: t('menu.typeMenu'), type: 'success' as const }
  : { text: t('menu.typeButton'), type: 'warning' as const }

// ── 弹窗表单(FormContainer:loading/底栏/关闭时机由容器按 onConfirm 协议接管)──
const show = ref(false)
const formRef = ref<FormInst | null>(null)
const rules: FormRules = {
  // whitespace: 保留原「!form.title.trim() 禁用保存」的语义,纯空白不算填写
  title: { required: true, whitespace: true, message: () => t('menu.titleRequired'), trigger: ['input', 'blur'] },
}
const editingId = ref<number | null>(null)
const blank = (): MenuInput => ({
  parentId: 0, type: MenuType.Menu, title: '', permission: '', sort: 0,
  enabled: true, moduleId: null, path: '', component: '', icon: '', visible: true,
})
const form = reactive<MenuInput>(blank())

/** 所属应用仅对顶级目录(parentId==0)有效——后端对子节点强制置空,前端据此隐藏字段。 */
const isTopLevel = computed(() => form.parentId === 0)

const typeOptions = computed(() => [
  { label: t('menu.typeCatalog'), value: MenuType.Catalog },
  { label: t('menu.typeMenu'), value: MenuType.Menu },
  { label: t('menu.typeButton'), value: MenuType.Button },
])
const moduleOptions = computed(() => modules.value.map((m) => ({ label: m.title, value: m.id })))

/** 顶部筛选下拉:各应用 +「未分配」。 */
const filterOptions = computed(() => [
  ...moduleOptions.value,
  { label: t('menu.moduleUnassigned'), value: UNASSIGNED },
])

/**
 * 按所属应用过滤:moduleId 只存在顶级目录(parentId==0)上,子节点为 null 靠继承,
 * 所以只过滤顶级数组、子树自动跟随;UNASSIGNED 归拢 moduleId==null 的顶级目录。
 */
const filteredTree = computed(() =>
  tree.value.filter((r) => (moduleFilter.value === UNASSIGNED ? r.moduleId == null : r.moduleId === moduleFilter.value)),
)

// ── 关键字搜索 + 展开控制 ─────────────────────────────────────────
// 内核开箱就播了 90 行菜单,全展开、没搜索、没折叠 = 第一天打开就糊脸。
// ProTable 静态 data 模式不做任何前端过滤(它只在 fetcher 模式下发请求),所以过滤得自己算;
// 关键字放 #toolbar 而不是用列的 search 配置:树表没有分页,搜索卡片会白占一整块高度。
// 只喂给表格,不喂给 parentOptions —— 父级下拉必须始终是完整的当前应用子树,不能随搜索缩水。
const keyword = ref('')
const visibleTree = computed(() => {
  const kw = keyword.value.trim().toLowerCase()
  if (!kw) return filteredTree.value
  return filterTree(filteredTree.value, (n) =>
    n.title.toLowerCase().includes(kw) ||
    (n.path ?? '').toLowerCase().includes(kw) ||
    (n.permission ?? '').toLowerCase().includes(kw),
  )
})

// 展开受控:一旦传了 expanded-row-keys,naive 就以它为准,default-expand-all 会被初始值直接覆盖成"全折叠",
// 所以"默认全展开"得自己播种。data 变了受控 keys 也不会自动跟着变 —— 搜索/切应用后要重算。
const expandedKeys = ref<number[]>([])
watch(visibleTree, (t) => (expandedKeys.value = expandableIds(t)), { immediate: true })

const allExpanded = computed(() => expandedKeys.value.length > 0)
const toggleExpandAll = () => (expandedKeys.value = allExpanded.value ? [] : expandableIds(visibleTree.value))

// 权限码下拉:label 展示「METHOD /路径」便于人眼辨认,value 就是要写进菜单的权限码本身。
// filterable + tag:可搜可选,也保留手敲逃生口(消费方端点尚未部署、或想先占位时)。
const permissionOptions = computed(() =>
  routes.value.map((r) => ({ label: `${r.method} ${r.path}`, value: r.code })),
)
// 组件路径下拉:取自 import.meta.glob 的真实文件表 —— 选得到的一定存在,不会再"填错→菜单静默消失"。
const componentOptions = computed(() => viewComponentPaths.map((p) => ({ label: p, value: p })))

/** 收集以 id 为根的子树全部 id(含自身)——编辑时排除,防止把节点挂到自己的子孙下形成环。 */
function subtreeIds(nodes: MenuTreeNode[], id: number): Set<number> {
  const out = new Set<number>()
  const find = (list: MenuTreeNode[]): MenuTreeNode | null => {
    for (const n of list) {
      if (n.id === id) return n
      const hit = find(n.children)
      if (hit) return hit
    }
    return null
  }
  const collect = (n: MenuTreeNode) => {
    out.add(n.id)
    n.children.forEach(collect)
  }
  const root = find(nodes)
  if (root) collect(root)
  return out
}

/** 父节点下拉:根 + 全部目录/页面(按钮不能当父),缩进体现层级;编辑时排除自身子树。 */
const parentOptions = computed(() => {
  const exclude = editingId.value === null ? new Set<number>() : subtreeIds(tree.value, editingId.value)
  const opts: { label: string; value: number }[] = [{ label: t('menu.parentRoot'), value: 0 }]
  const walk = (nodes: MenuTreeNode[], depth: number) => {
    for (const n of nodes) {
      if (n.type !== MenuType.Button && !exclude.has(n.id)) {
        opts.push({ label: `${'　'.repeat(depth)}${n.title}`, value: n.id })
      }
      walk(n.children, depth + 1)
    }
  }
  // 只在当前应用的子树里选父级——防止把节点误挂到别的应用目录下(会改变其实际所属应用)。
  walk(filteredTree.value, 0)
  return opts
})

/** 行数据 → 完整入参:openEdit 回填与 StatusSwitch 行内改状态共用(后端无独立启停端点,均走全量 update)。 */
const toInput = (r: MenuTreeNode): MenuInput => ({
  parentId: r.parentId, type: r.type, title: r.title, permission: r.permission, sort: r.sort,
  enabled: r.enabled, moduleId: r.moduleId ?? null,
  path: r.path ?? '', component: r.component ?? '', icon: r.icon ?? '', visible: r.visible,
})

function openAdd(parentId = 0) {
  editingId.value = null
  // 新建顶级目录时,自动盖上当前筛选的应用(「未分配」筛选下则留空)。
  const moduleId = parentId === 0 && moduleFilter.value !== UNASSIGNED ? moduleFilter.value : null
  Object.assign(form, blank(), { parentId, moduleId })
  show.value = true
}
function openEdit(r: MenuTreeNode) {
  editingId.value = r.id
  Object.assign(form, toInput(r))
  show.value = true
}

/** FormContainer onConfirm:校验失败 reject / API 失败 return false → 弹层不关;成功正常返回自动关。 */
async function save() {
  await formRef.value?.validate()
  try {
    const payload: MenuInput = { ...form, moduleId: isTopLevel.value ? form.moduleId : null }
    if (editingId.value === null) await menuApi.add(payload)
    else await menuApi.update(editingId.value, payload)
    message.success(t('menu.saved'))
    await Promise.all([load(), syncShell()])
  } catch (e) {
    message.error(translateError(e))
    return false
  }
}

const columns: ProTableColumn<MenuTreeNode>[] = [
  {
    title: () => t('menu.title'),
    key: 'title',
    align: 'left', // 树列左对齐,展开缩进才好读
    render: (r) =>
      h(NSpace, { align: 'center', size: 6, wrapItem: false }, () => [
        r.icon ? h(AppIcon, { icon: r.icon, size: 18 }) : null,
        r.title,
        h(NTag, { size: 'tiny', type: typeTag(r.type).type, bordered: false }, () => typeTag(r.type).text),
      ]),
  },
  { title: () => t('menu.permission'), key: 'permission', render: (r) => r.permission || '—' },
  { title: () => t('menu.sort'), key: 'sort', width: 70 },
  {
    title: () => t('common.status'),
    key: 'enabled',
    width: 84,
    render: (r) =>
      h(StatusSwitch, {
        value: r.enabled,
        // 停用是一键隐藏整棵子树的重操作,先确认;启用无副作用,跳过确认(返回 null)。
        confirm: (next: boolean) => (next ? null : t('menu.disableConfirm', { title: r.title })),
        request: (next: boolean) => menuApi.update(r.id, { ...toInput(r), enabled: next }),
        // 重拉而非往行对象上写:搜索态下的祖先行是 filterTree 的浅拷贝,写它不会回到源树(开关会弹回去)。
        // StatusSwitch 是悲观更新(请求成功才 emit),重拉一次就是最终态。
        'onUpdate:value': () => {
          load()
          syncShell() // 停用/启用即刻反映到侧边栏
        },
      }),
  },
  {
    title: () => t('common.visible'),
    key: 'visible',
    width: 84,
    render: (r) => (r.visible ? t('common.visible') : t('common.hidden')),
  },
  {
    title: () => t('common.operation'),
    key: 'op',
    width: 200,
    render: (r) =>
      h(NSpace, { size: 2, wrapItem: false }, () => [
        r.type !== MenuType.Button
          ? h(NButton, { size: 'small', quaternary: true, onClick: () => openAdd(r.id) }, () => t('menu.addChild'))
          : null,
        h(NButton, { size: 'small', quaternary: true, type: 'primary', onClick: () => openEdit(r) }, () => t('common.edit')),
        h(
          NPopconfirm,
          {
            // popconfirm 留在模板层当触发器,「执行→toast」后半段交给 useConfirm().run。
            onPositiveClick: () =>
              run(() => menuApi.remove(r.id), t('menu.deleted')).then((ok) => {
                if (ok) Promise.all([load(), syncShell()])
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
  <div class="view">
    <!-- expanded-row-keys / update:expanded-row-keys 不是 ProTable 的 prop —— 它 inheritAttrs:false + v-bind="attrs",
         未声明的 attr 原样透传给内层 n-data-table(现有的 :loading、原来的 default-expand-all 走的都是这条路)。 -->
    <ProTable
      :columns="columns"
      :data="visibleTree"
      :loading="loading"
      row-key="id"
      :pagination="false"
      storage-key="sys-menu"
      :expanded-row-keys="expandedKeys"
      @update:expanded-row-keys="(keys: number[]) => (expandedKeys = keys)"
    >
      <template #toolbar>
        <n-select
          v-model:value="moduleFilter"
          :options="filterOptions"
          style="width: 200px"
          :placeholder="t('menu.module')"
        />
        <n-input v-model:value="keyword" clearable :placeholder="t('menu.searchPlaceholder')" style="width: 220px">
          <template #prefix><AppIcon icon="ph:magnifying-glass" :size="16" /></template>
        </n-input>
        <n-button quaternary @click="toggleExpandAll">
          <template #icon>
            <AppIcon :icon="allExpanded ? 'ph:arrows-in-line-vertical' : 'ph:arrows-out-line-vertical'" :size="16" />
          </template>
          {{ allExpanded ? t('common.collapseAll') : t('common.expandAll') }}
        </n-button>
        <n-button type="primary" @click="openAdd(0)">
          <template #icon><AppIcon icon="ph:plus" :size="16" /></template>{{ t('common.add') }}
        </n-button>
      </template>
    </ProTable>

    <FormContainer
      v-model:show="show"
      :title="editingId === null ? t('menu.addTitle') : t('menu.editTitle')"
      :width="720"
      :on-confirm="save"
      :confirm-text="t('common.save')"
    >
      <!-- 两列布局:短字段两两一行、长/特殊字段整行(span 2),省纵向空间免下拉。 -->
      <n-form ref="formRef" :model="form" :rules="rules" label-placement="left" :label-width="90">
        <n-grid :cols="2" :x-gap="18">
          <n-form-item-gi :label="t('menu.parent')">
            <n-select v-model:value="form.parentId" :options="parentOptions" />
          </n-form-item-gi>
          <n-form-item-gi :label="t('menu.type')">
            <n-select v-model:value="form.type" :options="typeOptions" />
          </n-form-item-gi>
          <n-form-item-gi :label="t('menu.title')" path="title">
            <n-input v-model:value="form.title" :placeholder="t('menu.title')" />
          </n-form-item-gi>
          <n-form-item-gi :label="t('menu.sort')">
            <n-input-number v-model:value="form.sort" :min="0" style="width: 100%" />
          </n-form-item-gi>
          <!-- 权限码 = 规范化路由。从后端实时路由表里选,而不是手敲——大小写/{id}/斜杠错一个字符
               就是"明明授权了却还是 403"且无任何报错(超管测试还一切正常)。tag 保留手敲逃生口。 -->
          <n-form-item-gi :span="2" :label="t('menu.permission')">
            <n-select
              v-model:value="form.permission"
              :options="permissionOptions"
              filterable
              tag
              clearable
              :placeholder="t('menu.permissionPlaceholder')"
            />
          </n-form-item-gi>
          <n-form-item-gi v-if="isTopLevel" :span="2" :label="t('menu.module')">
            <n-select
              v-model:value="form.moduleId"
              :options="moduleOptions"
              clearable
              :placeholder="t('menu.moduleHint')"
            />
          </n-form-item-gi>
          <n-form-item-gi :label="t('menu.path')">
            <n-input v-model:value="(form.path as string)" placeholder="/system/xxx" />
          </n-form-item-gi>
          <!-- 组件路径:选项来自 import.meta.glob 的真实文件表,选得到的一定能加载;
               手敲错则该菜单只会 console.warn 后静默消失。tag 保留手敲(页面文件尚未创建时先占位)。 -->
          <n-form-item-gi :label="t('menu.component')">
            <n-select
              v-model:value="(form.component as string)"
              :options="componentOptions"
              filterable
              tag
              clearable
              placeholder="system/xxx/index"
            />
          </n-form-item-gi>
          <n-form-item-gi :span="2" :label="t('menu.icon')">
            <IconPicker :model-value="form.icon ?? ''" @update:model-value="(v: string) => (form.icon = v)" />
          </n-form-item-gi>
          <n-form-item-gi :label="t('common.status')">
            <n-switch v-model:value="form.enabled" />
          </n-form-item-gi>
          <n-form-item-gi :label="t('menu.visible')">
            <n-switch v-model:value="form.visible" />
          </n-form-item-gi>
        </n-grid>
      </n-form>
    </FormContainer>
  </div>
</template>

<style scoped>
.view {
  display: flex;
  flex-direction: column;
  gap: var(--gap-card);
}
</style>
