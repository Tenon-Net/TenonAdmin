<script setup lang="ts">
import { computed, h, onMounted, reactive, ref } from 'vue'
import { NButton, NEmpty, NInput, NTree, useMessage } from 'naive-ui'
import { ProTable, type ProTableColumn, type ProTableInst } from 'tenon-naive-pro-table'
import { useI18n } from 'vue-i18n'
import AppIcon from '@/components/AppIcon.vue'
import FormContainer from '@/components/FormContainer/index.vue'
import { orgApi, userApi } from '@/api'
import { buildTree } from '@/utils/tree'
import { translateError } from '@/utils/error'
import type { SysOrg, UserItem } from '@/types/api'

const props = defineProps<{ excludeIds?: number[] }>()
const emit = defineEmits<{ (e: 'confirm', ids: number[]): void }>()

const { t } = useI18n()
const message = useMessage()

const show = ref(false)
const tableRef = ref<ProTableInst<UserItem>>()

// ── 机构树(左面板) ──
const orgFlat = ref<SysOrg[]>([])
const orgTree = computed(() => {
  const all = { id: 0, parentId: -1, name: t('common.all'), children: buildTree(orgFlat.value) }
  return [all]
})
const selectedOrgKey = ref<number | null>(null)
const orgId = computed(() => (selectedOrgKey.value && selectedOrgKey.value !== 0 ? selectedOrgKey.value : undefined))

onMounted(async () => {
  try { orgFlat.value = await orgApi.list() } catch { /* 静默 */ }
})

function onOrgSelect(keys: Array<string | number>) {
  selectedOrgKey.value = (keys[0] as number) ?? null
  tableRef.value?.refresh()
}

// ── 可选用户表(中间面板) ──
const checkedKeys = ref<(string | number)[]>([])
const excludeSet = computed(() => new Set(props.excludeIds ?? []))

const fetcher = (params: { page: number; pageSize: number; name?: string }) =>
  userApi.page({ page: params.page, pageSize: params.pageSize, name: params.name, orgId: orgId.value })

const columns: ProTableColumn<UserItem>[] = [
  { type: 'selection' },
  { key: 'name', title: () => t('user.name'), width: 100 },
  { key: 'account', title: () => t('user.account'), width: 120, search: true },
  {
    key: 'op',
    title: () => t('common.operation'),
    width: 70,
    render: (r) => {
      const already = selectedSet.value.has(r.id) || excludeSet.value.has(r.id)
      return h(NButton, {
        size: 'tiny', type: 'primary', quaternary: true, disabled: already,
        onClick: () => addUsers([r]),
      }, () => already ? t('userPicker.added') : t('userPicker.add'))
    },
  },
]

// ── 已选用户(右面板) ──
const selected = reactive<UserItem[]>([])
const selectedSet = computed(() => new Set(selected.map((u) => u.id)))
const selectedSearch = ref('')
const filteredSelected = computed(() => {
  const q = selectedSearch.value.trim().toLowerCase()
  if (!q) return selected
  return selected.filter((u) => u.name.toLowerCase().includes(q) || u.account.toLowerCase().includes(q))
})

function addUsers(users: UserItem[]) {
  for (const u of users) {
    if (!selectedSet.value.has(u.id) && !excludeSet.value.has(u.id)) {
      selected.push(u)
    }
  }
}

function addChecked() {
  const rows = (tableRef.value as any)?.tableData?.filter((r: UserItem) => checkedKeys.value.includes(r.id)) ?? []
  addUsers(rows)
  checkedKeys.value = []
}

function removeUser(id: number) {
  const idx = selected.findIndex((u) => u.id === id)
  if (idx >= 0) selected.splice(idx, 1)
}

function clearSelected() {
  selected.splice(0, selected.length)
}

// ── 公共 API ──
async function open(ids?: number[]) {
  selected.splice(0, selected.length)
  checkedKeys.value = []
  selectedSearch.value = ''
  selectedOrgKey.value = null
  show.value = true

  if (ids?.length) {
    try {
      const { items } = await userApi.page({ page: 1, pageSize: ids.length, sortField: undefined, sortOrder: undefined })
      // ponytail: 如果已选 id 不在第一页,逐个拉详情太重;用全量 page 拉一页足够大,靠 id 过滤
      const all = items.filter((u) => ids.includes(u.id))
      // 没拉到的 id(翻页之外),退回只显示 id
      const found = new Set(all.map((u) => u.id))
      for (const u of all) selected.push(u)
      for (const id of ids) {
        if (!found.has(id)) selected.push({ id, account: String(id), name: String(id) } as UserItem)
      }
    } catch {
      for (const id of ids) selected.push({ id, account: String(id), name: String(id) } as UserItem)
    }
  }
}

async function handleConfirm() {
  const ids = selected.map((u) => u.id)
  emit('confirm', ids)
}

defineExpose({ open })
</script>

<template>
  <FormContainer
    v-model:show="show"
    :title="t('userPicker.title')"
    :width="960"
    :on-confirm="handleConfirm"
    :confirm-text="t('common.confirm')"
  >
    <div class="user-picker">
      <!-- 左面板:机构树 -->
      <div class="picker-tree">
        <div class="panel-header">{{ t('org.title') }}</div>
        <n-tree
          :data="orgTree"
          key-field="id"
          label-field="name"
          children-field="children"
          selectable
          block-line
          default-expand-all
          :selected-keys="selectedOrgKey != null ? [selectedOrgKey] : []"
          @update:selected-keys="onOrgSelect"
        />
      </div>

      <!-- 中间面板:可选用户 -->
      <div class="picker-table">
        <div class="panel-header">
          <span>{{ t('userPicker.available') }}</span>
          <n-button size="tiny" type="primary" :disabled="checkedKeys.length === 0" @click="addChecked">
            {{ t('userPicker.addChecked') }}({{ checkedKeys.length }})
          </n-button>
        </div>
        <ProTable
          ref="tableRef"
          :columns="columns"
          :fetcher="fetcher"
          :pagination="{ pageSize: 10 }"
          size="small"
          storage-key="user-picker"
          :tool-button="false"
          :checked-row-keys="checkedKeys"
          @update:checked-row-keys="(keys: (string | number)[]) => (checkedKeys = keys)"
          @error="(e) => message.error(translateError(e))"
        />
      </div>

      <!-- 右面板:已选用户 -->
      <div class="picker-selected">
        <div class="panel-header">
          <span>{{ t('userPicker.selected') }}({{ selected.length }})</span>
          <n-button size="tiny" quaternary type="error" :disabled="selected.length === 0" @click="clearSelected">
            {{ t('userPicker.clear') }}
          </n-button>
        </div>
        <n-input
          v-model:value="selectedSearch"
          size="small"
          clearable
          :placeholder="t('common.search')"
          style="margin-bottom: 8px"
        />
        <div class="selected-list">
          <div v-for="u in filteredSelected" :key="u.id" class="selected-item">
            <span class="selected-name">{{ u.name }}<span class="selected-account">({{ u.account }})</span></span>
            <n-button size="tiny" quaternary type="error" @click="removeUser(u.id)">
              <template #icon><AppIcon icon="ph:x" :size="14" /></template>
            </n-button>
          </div>
          <n-empty v-if="filteredSelected.length === 0" :description="t('common.noData')" style="padding: 24px 0" />
        </div>
      </div>
    </div>
  </FormContainer>
</template>

<style scoped>
.user-picker {
  display: flex;
  gap: 12px;
  min-height: 400px;
}
.panel-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  font-size: 13px;
  font-weight: 600;
  padding: 0 0 8px;
  border-bottom: 1px solid var(--n-border-color, #e0e0e6);
  margin-bottom: 8px;
}
.picker-tree {
  width: 180px;
  flex-shrink: 0;
  overflow: auto;
  border-right: 1px solid var(--n-border-color, #e0e0e6);
  padding-right: 12px;
}
.picker-table {
  flex: 1;
  min-width: 0;
  overflow: hidden;
}
.picker-selected {
  width: 220px;
  flex-shrink: 0;
  border-left: 1px solid var(--n-border-color, #e0e0e6);
  padding-left: 12px;
  display: flex;
  flex-direction: column;
}
.selected-list {
  flex: 1;
  overflow: auto;
  max-height: 380px;
}
.selected-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 4px 0;
  font-size: 13px;
  border-bottom: 1px solid var(--n-border-color, #e0e0e6);
}
.selected-item:last-child { border-bottom: none; }
.selected-name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.selected-account { color: var(--n-text-color-disabled, #999); font-size: 12px; margin-left: 2px; }
</style>
