<script setup lang="ts">
// 机构管理 = 树表:ProTable 静态数据 + children 树模式(无分页)。
// org list 平铺 → buildTree 拼 children;上级机构用 OrgTreeSelect(剪自身子树防成环);
// 无独立启停端点,StatusSwitch 走全量 update;删除有子机构后端拒,前端照调由 translateError 弹码。
import { computed, h, onMounted, reactive, ref, watch } from 'vue'
import {
  NButton, NSpace, NForm, NFormItem, NInput, NInputNumber, NSwitch,
  NPopconfirm, NModal, NCard, useMessage, type FormInst, type FormRules,
} from 'naive-ui'
import { ProTable, type ProTableColumn } from 'tenon-naive-pro-table'
import { useI18n } from 'vue-i18n'
import AppIcon from '@/components/AppIcon.vue'
import DictSelect from '@/components/DictSelect/index.vue'
import DictTag from '@/components/DictTag/index.vue'
import FormContainer from '@/components/FormContainer/index.vue'
import OrgTreeSelect from '@/components/OrgTreeSelect/index.vue'
import StatusSwitch from '@/components/StatusSwitch/index.vue'
import { useConfirm } from '@/composables/useConfirm'
import { orgApi } from '@/api'
import { translateError } from '@/utils/error'
import { buildTree, expandableIds, filterTree, type Tree } from '@/utils/tree'
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

// ── 搜索 + 展开控制 ───────────────────────────────────────────────
// ProTable 静态 data 模式不做任何前端过滤(它只在 fetcher 模式下发请求),所以过滤得自己算。
// 关键字放 #toolbar 而不是用列的 search 配置:树表没有分页,搜索卡片会白占一整块高度。
const keyword = ref('')
const filteredTree = computed(() => {
  const kw = keyword.value.trim().toLowerCase()
  if (!kw) return tree.value
  return filterTree(tree.value, (n) => n.name.toLowerCase().includes(kw) || n.code.toLowerCase().includes(kw))
})

// 展开受控:一旦传了 expanded-row-keys,naive 就以它为准,default-expand-all 会被初始值直接覆盖成"全折叠",
// 所以"默认全展开"得自己播种。data 变了受控 keys 也不会自动跟着变 —— 搜索后要重算,否则命中结果藏在折叠的祖先里看不见。
const expandedKeys = ref<number[]>([])
watch(filteredTree, (t) => (expandedKeys.value = expandableIds(t)), { immediate: true })

const allExpanded = computed(() => expandedKeys.value.length > 0)
const toggleExpandAll = () => (expandedKeys.value = allExpanded.value ? [] : expandableIds(filteredTree.value))

// ── 弹窗表单(parentId 可空:OrgTreeSelect clearable → null,save 时归一为 0=根)──
interface OrgForm {
  parentId: number | null
  name: string
  code: string
  category: string | null
  sort: number
  enabled: boolean
}
const show = ref(false)
const formRef = ref<FormInst | null>(null)
const editingId = ref<number | null>(null)
const rules: FormRules = {
  name: { required: true, whitespace: true, message: () => t('org.nameRequired'), trigger: ['input', 'blur'] },
}
const blank = (): OrgForm => ({ parentId: 0, name: '', code: '', category: null, sort: 0, enabled: true })
const form = reactive<OrgForm>(blank())

// 行数据 → 完整入参:openEdit 回填与 StatusSwitch 行内改状态共用(后端无独立启停端点,均走全量 update)。
const toInput = (r: SysOrg): OrgInput => ({ parentId: r.parentId, name: r.name, code: r.code, category: r.category ?? null, sort: r.sort, enabled: r.enabled })

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

// ── 复制弹窗 ──────────────────────────────────────────────────────
const copyShow = ref(false)
const copyId = ref(0)
const copySourceName = ref('')
const copyName = ref('')

function openCopy(r: SysOrg) {
  copyId.value = r.id
  copySourceName.value = r.name
  copyName.value = r.name + '-副本'
  copyShow.value = true
}
async function confirmCopy() {
  const ok = await run(() => orgApi.copy(copyId.value, { name: copyName.value }), t('org.copied'))
  if (ok) { copyShow.value = false; load() }
}

const columns: ProTableColumn<Tree<SysOrg>>[] = [
  { title: () => t('org.name'), key: 'name', align: 'left' }, // 树列左对齐,展开缩进才好读
  { title: () => t('org.code'), key: 'code' },
  { title: () => t('org.category'), key: 'category', width: 100, render: (r) => h(DictTag, { typeCode: 'org_category', value: r.category }) },
  { title: () => t('org.sort'), key: 'sort', width: 80 },
  {
    title: () => t('common.status'),
    key: 'enabled',
    width: 90,
    render: (r) =>
      h(StatusSwitch, {
        value: r.enabled,
        request: (next: boolean) => orgApi.update(r.id, { ...toInput(r), enabled: next }),
        // 重拉而非往行对象上写:搜索态下的祖先行是 filterTree 的浅拷贝,写它不会回到源树(开关会弹回去)。
        // StatusSwitch 是悲观更新(请求成功才 emit),所以这里重拉一次就是最终态,和本页其余变更的做法一致。
        'onUpdate:value': () => load(),
      }),
  },
  {
    title: () => t('common.operation'),
    key: 'op',
    width: 260,
    render: (r) =>
      h(NSpace, { size: 2, wrapItem: false }, () => [
        h(NButton, { size: 'small', quaternary: true, onClick: () => openAdd(r.id) }, () => t('org.addChild')),
        h(NButton, { size: 'small', quaternary: true, type: 'primary', onClick: () => openEdit(r) }, () => t('common.edit')),
        h(NButton, { size: 'small', quaternary: true, onClick: () => openCopy(r) }, () => t('org.copy')),
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
    <!-- expanded-row-keys / update:expanded-row-keys 不是 ProTable 的 prop —— 它 inheritAttrs:false + v-bind="attrs",
         未声明的 attr 原样透传给内层 n-data-table(现有的 :loading、原来的 default-expand-all 走的都是这条路)。 -->
    <ProTable
      :columns="columns"
      :data="filteredTree"
      :loading="loading"
      row-key="id"
      :pagination="false"
      :toolbar="{ refresh: false }"
      storage-key="sys-org"
      :expanded-row-keys="expandedKeys"
      @update:expanded-row-keys="(keys: number[]) => (expandedKeys = keys)"
    >
      <template #toolbar>
        <n-input v-model:value="keyword" clearable :placeholder="t('org.searchPlaceholder')" style="width: 220px">
          <template #prefix><AppIcon icon="ph:magnifying-glass" :size="16" /></template>
        </n-input>
        <n-button quaternary :loading="loading" @click="load">
          <template #icon><AppIcon icon="ph:arrow-clockwise" :size="16" /></template>
        </n-button>
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
        <n-form-item :label="t('org.code')">
          <n-input v-model:value="form.code" :placeholder="t('org.codePlaceholder')" :disabled="editingId !== null" />
        </n-form-item>
        <n-form-item :label="t('org.category')">
          <DictSelect v-model:value="form.category" type-code="org_category" clearable :placeholder="t('org.categoryPlaceholder')" />
        </n-form-item>
        <n-form-item :label="t('org.sort')">
          <n-input-number v-model:value="form.sort" :min="0" style="width: 160px" />
        </n-form-item>
        <n-form-item :label="t('common.status')">
          <n-switch v-model:value="form.enabled" />
        </n-form-item>
      </n-form>
    </FormContainer>

    <n-modal v-model:show="copyShow" :mask-closable="false">
      <n-card :title="t('org.copyTitle')" style="width: 420px" :bordered="false" closable @close="copyShow = false">
        <p style="margin: 0 0 12px">{{ t('org.copyConfirm', { name: copySourceName }) }}</p>
        <n-input v-model:value="copyName" :placeholder="t('org.copyNameLabel')" />
        <template #action>
          <n-space justify="end">
            <n-button @click="copyShow = false">{{ t('common.cancel') }}</n-button>
            <n-button type="primary" @click="confirmCopy">{{ t('common.confirm') }}</n-button>
          </n-space>
        </template>
      </n-card>
    </n-modal>
  </div>
</template>

<style scoped>
.view {
  display: flex;
  flex-direction: column;
  gap: var(--gap-card);
}
</style>
