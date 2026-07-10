<script setup lang="ts">
import { computed, h, onMounted, reactive, ref } from 'vue'
import {
  NCard, NButton, NSpace, NDataTable, NTag, NForm, NFormItem, NInput, NInputNumber, NSwitch,
  NSelect, NPopconfirm, useMessage, type DataTableColumns, type FormInst, type FormRules,
} from 'naive-ui'
import { useI18n } from 'vue-i18n'
import AppIcon from '@/components/AppIcon.vue'
import IconPicker from '@/components/IconPicker/index.vue'
import FormContainer from '@/components/FormContainer/index.vue'
import StatusSwitch from '@/components/StatusSwitch/index.vue'
import { useConfirm } from '@/composables/useConfirm'
import { menuApi, moduleApi } from '@/api'
import { translateError } from '@/utils/error'
import { MenuType, type MenuInput, type MenuTreeNode } from '@/types/menu'
import type { ModuleRow } from '@/types/api'

const { t } = useI18n()
const message = useMessage()
const { run } = useConfirm()

const loading = ref(false)
const tree = ref<MenuTreeNode[]>([])
const modules = ref<ModuleRow[]>([])

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
onMounted(async () => {
  await load()
  try {
    modules.value = await moduleApi.list()
  } catch {
    // 模块列表仅供「所属应用」下拉;拉取失败不阻塞菜单管理主流程。
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
  walk(tree.value, 0)
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
  Object.assign(form, blank(), { parentId })
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
    await load()
  } catch (e) {
    message.error(translateError(e))
    return false
  }
}

const columns: DataTableColumns<MenuTreeNode> = [
  {
    title: () => t('menu.title'),
    key: 'title',
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
        'onUpdate:value': (v: boolean) => {
          r.enabled = v
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
                if (ok) load()
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
  <div class="page">
    <n-card :bordered="true">
      <div class="bar">
        <h3>{{ t('menu.manage') }}</h3>
        <n-button type="primary" @click="openAdd(0)">
          <template #icon><AppIcon icon="ph:plus" :size="16" /></template>{{ t('common.add') }}
        </n-button>
      </div>
      <n-data-table
        :columns="columns"
        :data="tree"
        :loading="loading"
        :row-key="(r: MenuTreeNode) => r.id"
        default-expand-all
      />
    </n-card>

    <FormContainer
      v-model:show="show"
      :title="editingId === null ? t('menu.addTitle') : t('menu.editTitle')"
      :on-confirm="save"
      :confirm-text="t('common.save')"
    >
      <n-form ref="formRef" :model="form" :rules="rules" label-placement="left" :label-width="90">
        <n-form-item :label="t('menu.parent')">
          <n-select v-model:value="form.parentId" :options="parentOptions" />
        </n-form-item>
        <n-form-item :label="t('menu.type')">
          <n-select v-model:value="form.type" :options="typeOptions" />
        </n-form-item>
        <n-form-item :label="t('menu.title')" path="title">
          <n-input v-model:value="form.title" :placeholder="t('menu.title')" />
        </n-form-item>
        <n-form-item :label="t('menu.permission')">
          <n-input v-model:value="form.permission" :placeholder="t('menu.permissionPlaceholder')" />
        </n-form-item>
        <n-form-item v-if="isTopLevel" :label="t('menu.module')">
          <n-select
            v-model:value="form.moduleId"
            :options="moduleOptions"
            clearable
            :placeholder="t('menu.moduleHint')"
          />
        </n-form-item>
        <n-form-item :label="t('menu.path')">
          <n-input v-model:value="(form.path as string)" placeholder="/system/xxx" />
        </n-form-item>
        <n-form-item :label="t('menu.component')">
          <n-input v-model:value="(form.component as string)" placeholder="system/xxx/index" />
        </n-form-item>
        <n-form-item :label="t('menu.icon')">
          <IconPicker :model-value="form.icon ?? ''" @update:model-value="(v: string) => (form.icon = v)" />
        </n-form-item>
        <n-form-item :label="t('menu.sort')">
          <n-input-number v-model:value="form.sort" :min="0" style="width: 160px" />
        </n-form-item>
        <n-form-item :label="t('common.status')">
          <n-switch v-model:value="form.enabled" />
        </n-form-item>
        <n-form-item :label="t('menu.visible')">
          <n-switch v-model:value="form.visible" />
        </n-form-item>
      </n-form>
    </FormContainer>
  </div>
</template>

<style scoped>
.page {
  display: flex;
  flex-direction: column;
  gap: var(--gap-card);
}
.bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 12px;
}
.bar h3 {
  font-size: var(--font-size-md);
  font-weight: 600;
  color: var(--color-text-primary);
}
</style>
