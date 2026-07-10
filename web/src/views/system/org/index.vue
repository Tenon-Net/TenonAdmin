<script setup lang="ts">
// 机构管理 = 树表(照 menu 页范式:裸 n-data-table 树 + FormContainer + StatusSwitch + useConfirm)。
// ProTable 不支持树形行,故用裸 n-data-table。org list 平铺 → buildTree 拼树;上级机构用 OrgTreeSelect
// (剪自身子树防成环);无独立启停端点,StatusSwitch 走全量 update;删除有子机构后端拒,前端照调由 translateError 弹码。
import { h, onMounted, reactive, ref } from 'vue'
import {
  NCard, NButton, NSpace, NDataTable, NForm, NFormItem, NInput, NInputNumber, NSwitch,
  NPopconfirm, useMessage, type DataTableColumns, type FormInst, type FormRules,
} from 'naive-ui'
import { useI18n } from 'vue-i18n'
import AppIcon from '@/components/AppIcon.vue'
import FormContainer from '@/components/FormContainer/index.vue'
import OrgTreeSelect from '@/components/OrgTreeSelect/index.vue'
import StatusSwitch from '@/components/StatusSwitch/index.vue'
import { useConfirm } from '@/composables/useConfirm'
import { orgApi } from '@/api'
import { translateError } from '@/utils/error'
import { buildTree, type Tree } from '@/utils/tree'
import type { OrgInput, SysOrg } from '@/types/api'

const { t } = useI18n()
const message = useMessage()
const { run } = useConfirm()

const loading = ref(false)
const tree = ref<Tree<SysOrg>[]>([])

async function load() {
  loading.value = true
  try {
    tree.value = buildTree(await orgApi.list())
  } catch (e) {
    message.error(translateError(e))
  } finally {
    loading.value = false
  }
}
onMounted(load)

// ── 弹窗表单(parentId 可空:OrgTreeSelect clearable → null,save 时归一为 0=根)──
interface OrgForm {
  parentId: number | null
  name: string
  code: string
  sort: number
  enabled: boolean
}
const show = ref(false)
const formRef = ref<FormInst | null>(null)
const editingId = ref<number | null>(null)
const rules: FormRules = {
  name: { required: true, whitespace: true, message: () => t('org.nameRequired'), trigger: ['input', 'blur'] },
  code: { required: true, whitespace: true, message: () => t('org.codeRequired'), trigger: ['input', 'blur'] },
}
const blank = (): OrgForm => ({ parentId: 0, name: '', code: '', sort: 0, enabled: true })
const form = reactive<OrgForm>(blank())

// 行数据 → 完整入参:openEdit 回填与 StatusSwitch 行内改状态共用(后端无独立启停端点,均走全量 update)。
const toInput = (r: SysOrg): OrgInput => ({ parentId: r.parentId, name: r.name, code: r.code, sort: r.sort, enabled: r.enabled })

function openAdd(parentId = 0) {
  editingId.value = null
  Object.assign(form, blank(), { parentId })
  show.value = true
}
function openEdit(r: SysOrg) {
  editingId.value = r.id
  Object.assign(form, toInput(r))
  show.value = true
}

/** FormContainer onConfirm:校验失败 reject / API 失败 return false → 弹层不关;成功正常返回自动关。 */
async function save() {
  await formRef.value?.validate()
  try {
    const payload: OrgInput = { ...form, parentId: form.parentId ?? 0 }
    if (editingId.value === null) await orgApi.add(payload)
    else await orgApi.update(editingId.value, payload)
    message.success(t('org.saved'))
    await load()
  } catch (e) {
    message.error(translateError(e))
    return false
  }
}

const columns: DataTableColumns<Tree<SysOrg>> = [
  { title: () => t('org.name'), key: 'name' },
  { title: () => t('org.code'), key: 'code' },
  { title: () => t('org.sort'), key: 'sort', width: 80 },
  {
    title: () => t('common.status'),
    key: 'enabled',
    width: 90,
    render: (r) =>
      h(StatusSwitch, {
        value: r.enabled,
        request: (next: boolean) => orgApi.update(r.id, { ...toInput(r), enabled: next }),
        'onUpdate:value': (v: boolean) => {
          r.enabled = v
        },
      }),
  },
  {
    title: () => t('common.operation'),
    key: 'op',
    width: 200,
    render: (r) =>
      h(NSpace, { size: 2, wrapItem: false }, () => [
        h(NButton, { size: 'small', quaternary: true, onClick: () => openAdd(r.id) }, () => t('org.addChild')),
        h(NButton, { size: 'small', quaternary: true, type: 'primary', onClick: () => openEdit(r) }, () => t('common.edit')),
        h(
          NPopconfirm,
          {
            onPositiveClick: () =>
              run(() => orgApi.remove(r.id), t('org.deleted')).then((ok) => {
                if (ok) load()
              }),
          },
          {
            trigger: () => h(NButton, { size: 'small', quaternary: true, type: 'error' }, () => t('common.delete')),
            default: () => t('org.deleteConfirm', { name: r.name }),
          },
        ),
      ]),
  },
]
</script>

<template>
  <div class="view">
    <n-card :bordered="true">
      <div class="bar">
        <h3>{{ t('org.title') }}</h3>
        <n-button type="primary" @click="openAdd(0)">
          <template #icon><AppIcon icon="ph:plus" :size="16" /></template>{{ t('common.add') }}
        </n-button>
      </div>
      <n-data-table
        :columns="columns"
        :data="tree"
        :loading="loading"
        :row-key="(r: Tree<SysOrg>) => r.id"
        default-expand-all
      />
    </n-card>

    <FormContainer
      v-model:show="show"
      :title="editingId === null ? t('org.addTitle') : t('org.editTitle')"
      :width="480"
      :on-confirm="save"
      :confirm-text="t('common.save')"
    >
      <n-form ref="formRef" :model="form" :rules="rules" label-placement="left" :label-width="80">
        <n-form-item :label="t('org.parent')">
          <OrgTreeSelect
            v-model:value="form.parentId"
            clearable
            :exclude-subtree-of="editingId"
            :placeholder="t('org.parentPlaceholder')"
          />
        </n-form-item>
        <n-form-item :label="t('org.name')" path="name">
          <n-input v-model:value="form.name" :placeholder="t('org.name')" />
        </n-form-item>
        <n-form-item :label="t('org.code')" path="code">
          <n-input v-model:value="form.code" :placeholder="t('org.code')" :disabled="editingId !== null" />
        </n-form-item>
        <n-form-item :label="t('org.sort')">
          <n-input-number v-model:value="form.sort" :min="0" style="width: 160px" />
        </n-form-item>
        <n-form-item :label="t('common.status')">
          <n-switch v-model:value="form.enabled" />
        </n-form-item>
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
