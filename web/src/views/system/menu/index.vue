<script setup lang="ts">
import { computed, h, onMounted, reactive, ref, watch } from 'vue'
import {
  NButton, NSpace, NTag, NForm, NInput, NInputNumber, NSwitch,
  NSelect, NDropdown, NGrid, NFormItemGi, useMessage, type FormInst, type FormRules,
} from 'naive-ui'
import { ProTable, type ProTableColumn } from 'tenon-naive-pro-table'
import { useI18n } from 'vue-i18n'
import AppIcon from '@/components/AppIcon.vue'
import IconPicker from '@/components/IconPicker/index.vue'
import FormContainer from '@/components/FormContainer/index.vue'
import StatusSwitch from '@/components/StatusSwitch/index.vue'
import ButtonManager from './components/ButtonManager.vue'
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
const { confirm } = useConfirm()
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

// 统一淡 tag,不做三色区分:按钮已不在树里,只剩目录/页面两种,而这个区别树形缩进和
// 「有没有路由路径」已经说清楚了;每行都顶一坨颜色反而是噪音(SimpleAdmin / XiHan 同样选了无色)。
const typeText = (ty: MenuType) =>
  ty === MenuType.Catalog ? t('menu.typeCatalog')
  : ty === MenuType.Menu ? t('menu.typeMenu')
  : t('menu.typeButton')

// 目录/页面用不同色系(蓝/绿)区分 —— 树里只有这两类,按钮不进树。
const typeTagType = (ty: MenuType): 'info' | 'success' => (ty === MenuType.Catalog ? 'info' : 'success')

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

// 类型只留目录/页面:按钮统一走「权限按钮」弹窗建,主表单不再提供按钮选项。
const typeOptions = computed(() => [
  { label: t('menu.typeCatalog'), value: MenuType.Catalog },
  { label: t('menu.typeMenu'), value: MenuType.Menu },
])
const moduleOptions = computed(() => modules.value.map((m) => ({ label: m.title, value: m.id })))

/** 顶部筛选下拉:各应用 +「未分配」。 */
const filterOptions = computed(() => [
  ...moduleOptions.value,
  { label: t('menu.moduleUnassigned'), value: UNASSIGNED },
])

/** 递归剔除按钮节点 —— 按钮改由「权限按钮」弹窗单独管,不再进主树,树只剩目录/菜单。 */
function stripButtons(nodes: MenuTreeNode[]): MenuTreeNode[] {
  return nodes
    .filter((n) => n.type !== MenuType.Button)
    .map((n) => ({ ...n, children: stripButtons(n.children) }))
}

/**
 * 按所属应用过滤:moduleId 只存在顶级目录(parentId==0)上,子节点为 null 靠继承,
 * 所以只过滤顶级数组、子树自动跟随;UNASSIGNED 归拢 moduleId==null 的顶级目录。
 * 再剥掉按钮节点后交给表格渲染。
 */
const filteredTree = computed(() =>
  stripButtons(
    tree.value.filter((r) => (moduleFilter.value === UNASSIGNED ? r.moduleId == null : r.moduleId === moduleFilter.value)),
  ),
)

/**
 * 各节点直属按钮的数量 + 权限码拼串(取自未剥离的原树,无需额外请求)。
 * count 给「权限按钮」列的徽标;perms 给关键字搜索 —— 按钮已不在树里,
 * 「这个权限码归哪个页面」只能靠父页面命中来回答,否则藏起按钮就等于把它搜没了。
 */
const buttonInfoById = computed(() => {
  const m = new Map<number, { count: number; perms: string }>()
  const walk = (nodes: MenuTreeNode[]) => {
    for (const n of nodes) {
      const btns = n.children.filter((c) => c.type === MenuType.Button)
      m.set(n.id, { count: btns.length, perms: btns.map((b) => b.permission).join(' ').toLowerCase() })
      walk(n.children)
    }
  }
  walk(tree.value)
  return m
})

// ── 关键字搜索 + 展开控制 ─────────────────────────────────────────
// 内核开箱就播了 90 行菜单,全展开、没搜索、没折叠 = 第一天打开就糊脸。
// ProTable 静态 data 模式不做任何前端过滤(它只在 fetcher 模式下发请求),所以过滤得自己算;
// 关键字放 #toolbar 而不是用列的 search 配置:树表没有分页,搜索卡片会白占一整块高度。
// 只喂给表格,不喂给 parentOptions —— 父级下拉必须始终是完整的当前应用子树,不能随搜索缩水。
const keyword = ref('')
const visibleTree = computed(() => {
  const kw = keyword.value.trim().toLowerCase()
  if (!kw) return filteredTree.value
  // 注意别写成 n.permission:过滤跑在已剥掉按钮的 filteredTree 上,而目录/页面的 permission 恒为空,
  // 那样写等于永不命中。要按权限码搜,只能查该节点「按钮子节点」的码(见 buttonInfoById)。
  return filterTree(filteredTree.value, (n) =>
    n.title.toLowerCase().includes(kw) ||
    (n.path ?? '').toLowerCase().includes(kw) ||
    (buttonInfoById.value.get(n.id)?.perms ?? '').includes(kw),
  )
})

// 展开受控:一旦传了 expanded-row-keys,naive 就以它为准,default-expand-all 会被初始值直接覆盖成"全折叠",
// 所以"默认全展开"得自己播种。data 变了受控 keys 也不会自动跟着变 —— 搜索/切应用后要重算。
const expandedKeys = ref<number[]>([])
watch(visibleTree, (t) => (expandedKeys.value = expandableIds(t)), { immediate: true })

const allExpanded = computed(() => expandedKeys.value.length > 0)
const toggleExpandAll = () => (expandedKeys.value = allExpanded.value ? [] : expandableIds(visibleTree.value))

// 当前所属应用的路由匹配前缀(sys/biz…):传给 ButtonManager,权限路由下拉据此按应用软过滤。
// 「未分配」筛选或该应用未填 apiPrefix → undefined,ButtonManager 退化为不过滤。
const currentAppPrefix = computed(() =>
  moduleFilter.value === UNASSIGNED ? undefined : modules.value.find((m) => m.id === moduleFilter.value)?.apiPrefix ?? undefined,
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

// ── 权限按钮弹窗 ────────────────────────────────────────────────
const buttonMgrRef = ref<InstanceType<typeof ButtonManager> | null>(null)
const openButtons = (r: MenuTreeNode) => buttonMgrRef.value?.open({ id: r.id, title: r.title })
/** 弹窗内增删改后:重拉树(刷新徽标)+ 重建壳层(即时反映授权变更)。 */
const onButtonsChanged = () => Promise.all([load(), syncShell()])

/** 删除:下拉菜单项不适合内联 popconfirm,走 useConfirm().confirm 的 dialog(它的设计用途)。 */
const onDelete = (r: MenuTreeNode) =>
  confirm({
    content: t('menu.deleteConfirm', { title: r.title }),
    type: 'error',
    action: () => menuApi.remove(r.id),
    successMsg: t('menu.deleted'),
  }).then((ok) => {
    if (ok) Promise.all([load(), syncShell()])
  })

// 展开图标:内核默认三角太淡、展开态朝下,辨识度低。统一成固定尺寸的「>」caret,展开时旋转 90°
// —— 观感清晰(对齐 SimpleAdmin),尺寸恒定,行高不再因默认图标忽大忽小而参差。
const renderExpandIcon = ({ expanded }: { expanded: boolean }) =>
  h(
    'span',
    { style: `display:inline-flex;transition:transform .2s;transform:rotate(${expanded ? 90 : 0}deg)` },
    h(AppIcon, { icon: 'ph:caret-right-bold', size: 13 }),
  )

const columns: ProTableColumn<MenuTreeNode>[] = [
  {
    title: () => t('menu.title'),
    key: 'title',
    align: 'left', // 树列左对齐,展开缩进才好读
    minWidth: 220,
    fixed: 'left', // 横向滚动时不能丢失「这是哪一行」
    render: (r) =>
      // inline:必须行内 —— naive 的展开箭头是 inline-flex,默认块级 NSpace 会被挤到第二行。
      h(NSpace, { inline: true, align: 'center', size: 6, wrapItem: false }, () => [
        r.title,
        // 「隐藏」是罕见状态(种子里 20/20 都是显示),只在为真时才占视觉 —— 省掉一整列同一个词。
        r.visible ? null : h(NTag, { size: 'tiny', bordered: false, type: 'warning' }, () => t('common.hidden')),
      ]),
  },
  // 图标独占一列(对齐 SimpleAdmin):留在标题列会和树形展开箭头 + 缩进挤在一起撑破首列换行。
  {
    title: () => t('menu.icon'),
    key: 'icon',
    width: 64,
    align: 'center',
    render: (r) => (r.icon ? h(AppIcon, { icon: r.icon, size: 18 }) : null),
  },
  // 类型独占一列(对齐 SimpleAdmin):不再塞在标题里。按类型着色,目录/页面一眼可辨。
  {
    title: () => t('menu.type'),
    key: 'type',
    width: 90,
    render: (r) => h(NTag, { size: 'small', bordered: false, type: typeTagType(r.type) }, () => typeText(r.type)),
  },
  // 路由/组件路径:管理员核对菜单时真正要看的东西,原先只能点开编辑弹窗才看得到。
  // 权限码列已删 —— 树里只剩目录/页面,而它俩结构上永远没有权限码(种子 20/20 为空),
  // 那一列 100% 是「—」。权限码的正主在「权限按钮」弹窗里。
  {
    title: () => t('menu.path'),
    key: 'path',
    align: 'left',
    width: 160,
    ellipsis: { tooltip: true },
    render: (r) => r.path || '—',
  },
  {
    title: () => t('menu.component'),
    key: 'component',
    align: 'left',
    width: 180,
    ellipsis: { tooltip: true },
    render: (r) => r.component || '—',
  },
  {
    title: () => t('menu.buttons'),
    key: 'buttons',
    width: 140,
    render: (r) => {
      const n = buttonInfoById.value.get(r.id)?.count ?? 0
      // 权限按钮跟着页面走:只有页面才显示「配置权限」(含 0,可加首个);目录不显示 ——
      // 唯一例外是已挂了按钮的目录(种子里仅系统运维/ping 这个无页面锚点),仍显示以便管理。
      if (r.type !== MenuType.Menu && n === 0) return null
      return h(
        NButton,
        { size: 'small', quaternary: true, type: n > 0 ? 'primary' : undefined, onClick: () => openButtons(r) },
        {
          icon: () => h(AppIcon, { icon: 'ph:shield-check', size: 15 }),
          default: () => t('menu.configPerms', { n }),
        },
      )
    },
  },
  { title: () => t('menu.sort'), key: 'sort', width: 70 },
  {
    title: () => t('common.status'),
    key: 'enabled',
    width: 84,
    render: (r) =>
      h(StatusSwitch, {
        value: r.enabled,
        disabled: !auth.hasPerm('PUT:/api/v1/sys/menu/{id}'), // 启停走全量 update,无更新权限则置灰
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
  // 操作收敛成「编辑 + 更多▾」:原先 4 个操作塞 300px 会把「删除」挤到第二行,行高参差,
  // 且没 fixed —— 横向滚动时整列被裁掉够不着。
  {
    title: () => t('common.operation'),
    key: 'op',
    width: 150,
    fixed: 'right',
    render: (r) =>
      h(NSpace, { size: 2, wrapItem: false, justify: 'center' }, () => {
        // 「更多」里两项各自按权限码保留;都无权时整个下拉不渲染,避免空触发器。
        const moreOptions = [
          auth.hasPerm('POST:/api/v1/sys/menu/add') && { key: 'addChild', label: t('menu.addChild') },
          auth.hasPerm('DELETE:/api/v1/sys/menu/{id}') && { key: 'delete', label: t('common.delete') },
        ].filter(Boolean) as { key: string; label: string }[]
        return [
          auth.hasPerm('PUT:/api/v1/sys/menu/{id}')
            ? h(NButton, { size: 'small', quaternary: true, type: 'primary', onClick: () => openEdit(r) }, () => t('common.edit'))
            : null,
          moreOptions.length
            ? h(
                NDropdown,
                {
                  trigger: 'click',
                  options: moreOptions,
                  onSelect: (key: string) => (key === 'addChild' ? openAdd(r.id) : onDelete(r)),
                },
                () => h(NButton, { size: 'small', quaternary: true }, () => t('common.more')),
              )
            : null,
        ]
      }),
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
      :indent="26"
      :render-expand-icon="renderExpandIcon"
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
        <n-button v-auth="'POST:/api/v1/sys/menu/add'" type="primary" @click="openAdd(0)">
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
          <!-- 权限码字段已移除:目录/页面结构上永远无权限码(授权靠角色拥有菜单,页面是前端路由无后端端点),
               真正的权限码只活在按钮上,统一走每行「配置权限」按钮里的 ButtonManager 配。 -->
          <n-form-item-gi v-if="isTopLevel" :span="2" :label="t('menu.module')">
            <n-select
              v-model:value="form.moduleId"
              :options="moduleOptions"
              clearable
              :placeholder="t('menu.moduleHint')"
            />
          </n-form-item-gi>
          <n-form-item-gi :label="t('menu.path')">
            <n-input v-model:value="(form.path as string)" placeholder="/system/xxx | https://..." />
          </n-form-item-gi>
          <!-- 组件路径:选项来自 import.meta.glob 的真实文件表,选得到的一定能加载;
               手敲错则该菜单只会 console.warn 后静默消失。tag 保留手敲(页面文件尚未创建时先占位)。
               外链/iframe 约定见下方 linkHint:路径填 URL = 外链新窗口打开;组件填 URL = 内嵌 iframe。 -->
          <n-form-item-gi :label="t('menu.component')">
            <n-select
              v-model:value="(form.component as string)"
              :options="componentOptions"
              filterable
              tag
              clearable
              placeholder="system/xxx/index | https://..."
            />
          </n-form-item-gi>
          <n-form-item-gi v-if="form.type === MenuType.Menu" :span="2" :show-label="false">
            <span class="link-hint">{{ t('menu.linkHint') }}</span>
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

    <ButtonManager ref="buttonMgrRef" :tree="tree" :routes="routes" :app-prefix="currentAppPrefix" @changed="onButtonsChanged" />
  </div>
</template>

<style scoped>
.link-hint {
  font-size: 12px;
  color: var(--color-text-tertiary, #999);
  line-height: 1.5;
}
.view {
  display: flex;
  flex-direction: column;
  gap: var(--gap-card);
}
</style>
