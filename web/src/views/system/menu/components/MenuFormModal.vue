<script setup lang="ts">
// 菜单「新增/编辑」弹窗 —— 从 menu/index.vue 抽出。父页拥有 tree/modules 数据并保持响应,
// 本弹窗只读消费,保存后 emit('saved') 交回父页重拉树 + 重建壳层。
import { computed, reactive, ref } from 'vue'
import {
  NForm, NInput, NInputNumber, NSwitch, NSelect, NGrid, NFormItemGi,
  useMessage, type FormInst, type FormRules,
} from 'naive-ui'
import { useI18n } from 'vue-i18n'
import IconPicker from '@/components/IconPicker/index.vue'
import FormContainer from '@/components/FormContainer/index.vue'
import { viewComponentPaths } from '@/composables/useAuthMenu'
import { menuApi } from '@/api'
import { translateError } from '@/utils/error'
import { MenuType, type MenuInput, type MenuTreeNode, toMenuInput } from '@/types/menu'
import type { ModuleRow } from '@/types/api'

const props = defineProps<{
  modules: ModuleRow[]
  /** 全树:subtreeIds 防环需要按钮在内的完整结构。 */
  tree: MenuTreeNode[]
  /** 当前应用、已剥按钮的树:父级下拉的候选来源(只在本应用子树里选父级,防跨应用误挂)。 */
  filteredTree: MenuTreeNode[]
  moduleFilter: number
}>()
const emit = defineEmits<{ (e: 'saved'): void }>()

const { t } = useI18n()
const message = useMessage()

/** 「未分配」「全部」哨兵,与父页一致:openAdd 顶级目录时据此决定是否自动带上当前应用。 */
const UNASSIGNED = 0
const ALL_MODULES = -1

const show = ref(false)
const formRef = ref<FormInst | null>(null)
const rules: FormRules = {
  // whitespace: 纯空白不算填写
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
const moduleOptions = computed(() => props.modules.map((m) => ({ label: m.title, value: m.id })))
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
  const exclude = editingId.value === null ? new Set<number>() : subtreeIds(props.tree, editingId.value)
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
  walk(props.filteredTree, 0)
  return opts
})

function openAdd(parentId = 0) {
  editingId.value = null
  // 新建顶级目录时,自动盖上当前筛选的应用(「未分配」「全部」筛选下则留空,交给用户手选)。
  const moduleId = parentId === 0 && props.moduleFilter !== UNASSIGNED && props.moduleFilter !== ALL_MODULES ? props.moduleFilter : null
  Object.assign(form, blank(), { parentId, moduleId })
  show.value = true
}
function openEdit(r: MenuTreeNode) {
  editingId.value = r.id
  Object.assign(form, toMenuInput(r))
  show.value = true
}
defineExpose({ openAdd, openEdit })

/** FormContainer onConfirm:校验失败 reject / API 失败 return false → 弹层不关;成功正常返回自动关。
 *  重拉树/重建壳层交回父页(emit),失败不该把已成功的保存报成失败。 */
async function save() {
  await formRef.value?.validate()
  try {
    const payload: MenuInput = { ...form, moduleId: isTopLevel.value ? form.moduleId : null }
    if (editingId.value === null) await menuApi.add(payload)
    else await menuApi.update(editingId.value, payload)
    message.success(t('menu.saved'))
    emit('saved')
  } catch (e) {
    message.error(translateError(e))
    return false
  }
}
</script>

<template>
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
</template>

<style scoped>
.link-hint {
  font-size: 12px;
  color: var(--color-text-tertiary, #999);
  line-height: 1.5;
}
</style>
