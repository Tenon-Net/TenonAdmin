<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { NButton, NCheckbox, NInput, NSelect } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import AppIcon from '@/components/AppIcon.vue'
import { MenuType, type MenuTreeNode } from '@/types/menu'
import type { ModuleRow } from '@/types/api'

const UNASSIGNED = 0

interface ButtonItem { id: number; title: string; checked: boolean }
interface MenuRow { id: number; title: string; checked: boolean; buttons: ButtonItem[] }
interface CatalogGroup { id: number; title: string; checked: boolean; indeterminate: boolean; menus: MenuRow[] }

const props = defineProps<{
  tree: MenuTreeNode[]
  granted: number[]
  modules: ModuleRow[]
  defaultModuleId: number
}>()

const emit = defineEmits<{ (e: 'update:checked', ids: number[]): void }>()

const { t } = useI18n()
const search = ref('')
const moduleId = ref(props.defaultModuleId)
watch(() => props.defaultModuleId, (v) => { moduleId.value = v })

const moduleOptions = computed(() => [
  ...props.modules.map((m) => ({ label: m.title, value: m.id })),
  { label: t('menu.moduleUnassigned'), value: UNASSIGNED },
])

function toMenuRow(m: MenuTreeNode, grantedSet: Set<number>): MenuRow {
  return {
    id: m.id,
    title: m.title,
    checked: grantedSet.has(m.id),
    buttons: m.children
      .filter((b) => b.type === MenuType.Button)
      .map((b) => ({ id: b.id, title: b.title, checked: grantedSet.has(b.id) })),
  }
}

function buildGroups(tree: MenuTreeNode[], grantedSet: Set<number>): CatalogGroup[] {
  return tree.map((node) => {
    // 顶级 Menu（如工作台）：自身就是菜单行，无目录壳
    const menus: MenuRow[] = node.type === MenuType.Menu
      ? [toMenuRow(node, grantedSet)]
      : node.children.filter((m) => m.type === MenuType.Menu).map((m) => toMenuRow(m, grantedSet))
    const group: CatalogGroup = {
      id: node.id,
      title: node.title,
      checked: grantedSet.has(node.id),
      indeterminate: false,
      menus,
    }
    recomputeGroup(group)
    return group
  })
}

const allGroups = reactive<CatalogGroup[]>([])

watch(
  () => [props.tree, props.granted] as const,
  ([tree, granted]) => {
    const set = new Set(granted)
    const groups = buildGroups(
      tree.filter((n) => n.type === MenuType.Catalog || n.type === MenuType.Menu),
      set,
    )
    allGroups.splice(0, allGroups.length, ...groups)
  },
  { immediate: true },
)

const filteredByModule = computed(() =>
  allGroups.filter((g) => {
    const node = props.tree.find((n) => n.id === g.id)
    return moduleId.value === UNASSIGNED ? node?.moduleId == null : node?.moduleId === moduleId.value
  }),
)

const filteredGroups = computed(() => {
  const q = search.value.trim().toLowerCase()
  if (!q) return filteredByModule.value
  return filteredByModule.value
    .map((g) => {
      if (g.title.toLowerCase().includes(q)) return g
      const menus = g.menus.filter(
        (m) => m.title.toLowerCase().includes(q) || m.buttons.some((b) => b.title.toLowerCase().includes(q)),
      )
      return menus.length ? { ...g, menus } : null
    })
    .filter(Boolean) as CatalogGroup[]
})

function recomputeGroup(group: CatalogGroup) {
  const allIds = group.menus.flatMap((m) => [m, ...m.buttons])
  const checkedCount = allIds.filter((x) => x.checked).length
  group.checked = checkedCount > 0 && checkedCount === allIds.length
  group.indeterminate = checkedCount > 0 && checkedCount < allIds.length
}

function toggleCatalog(group: CatalogGroup, val: boolean) {
  group.checked = val
  group.indeterminate = false
  group.menus.forEach((m) => {
    m.checked = val
    m.buttons.forEach((b) => { b.checked = val })
  })
  emitChecked()
}

function toggleMenu(group: CatalogGroup, menu: MenuRow, val: boolean) {
  menu.checked = val
  menu.buttons.forEach((b) => { b.checked = val })
  recomputeGroup(group)
  emitChecked()
}

function toggleButton(group: CatalogGroup, menu: MenuRow) {
  const allChecked = menu.buttons.length > 0 && menu.buttons.every((b) => b.checked)
  if (allChecked) menu.checked = true
  recomputeGroup(group)
  emitChecked()
}

function collectChecked(): number[] {
  const ids: number[] = []
  for (const g of allGroups) {
    if (g.checked || g.indeterminate) ids.push(g.id)
    for (const m of g.menus) {
      if (m.checked) ids.push(m.id)
      for (const b of m.buttons) {
        if (b.checked) ids.push(b.id)
      }
    }
  }
  return ids
}

function emitChecked() {
  emit('update:checked', collectChecked())
}

// ── 折叠/展开 ──
const collapsed = reactive(new Set<number>())
const allCollapsed = computed(() => filteredGroups.value.length > 0 && filteredGroups.value.every((g) => collapsed.has(g.id)))

function toggleCollapse(id: number) {
  if (collapsed.has(id)) collapsed.delete(id)
  else collapsed.add(id)
}
function toggleCollapseAll() {
  if (allCollapsed.value) {
    collapsed.clear()
  } else {
    for (const g of filteredGroups.value) collapsed.add(g.id)
  }
}

defineExpose({ collectChecked })
</script>

<template>
  <div class="grant-menu-table">
    <div class="grant-toolbar">
      <n-select v-model:value="moduleId" :options="moduleOptions" :placeholder="t('menu.module')" size="small" style="width: 180px" />
      <n-input v-model:value="search" :placeholder="t('common.search')" clearable size="small" style="flex: 1" />
      <n-button size="small" quaternary @click="toggleCollapseAll">
        <template #icon><AppIcon :icon="allCollapsed ? 'ph:arrows-out-line-vertical' : 'ph:arrows-in-line-vertical'" :size="16" /></template>
        {{ allCollapsed ? t('common.expandAll') : t('common.collapseAll') }}
      </n-button>
    </div>
    <div class="grant-grid">
      <div class="grant-header">
        <div class="col-catalog">{{ t('role.catalog') }}</div>
        <div class="col-menu">{{ t('role.menuCol') }}</div>
        <div class="col-buttons">{{ t('role.buttonsCol') }}</div>
      </div>
      <template v-for="group in filteredGroups" :key="group.id">
        <div class="grant-group" :style="{ '--rows': group.menus.length }">
          <div class="col-catalog" @click="toggleCollapse(group.id)">
            <span class="collapse-arrow" :class="{ 'is-collapsed': collapsed.has(group.id) }">&#9662;</span>
            <n-checkbox :checked="group.checked" :indeterminate="group.indeterminate" @click.stop @update:checked="toggleCatalog(group, $event)">
              {{ group.title }}
            </n-checkbox>
          </div>
          <div v-show="!collapsed.has(group.id)" class="group-rows">
            <div v-for="menu in group.menus" :key="menu.id" class="grant-row">
              <div class="col-menu">
                <n-checkbox :checked="menu.checked" @update:checked="toggleMenu(group, menu, $event)">
                  {{ menu.title }}
                </n-checkbox>
              </div>
              <div class="col-buttons">
                <n-checkbox
                  v-for="btn in menu.buttons" :key="btn.id"
                  :checked="btn.checked"
                  @update:checked="(v: boolean) => { btn.checked = v; toggleButton(group, menu) }"
                >
                  {{ btn.title }}
                </n-checkbox>
                <span v-if="!menu.buttons.length" class="no-buttons">—</span>
              </div>
            </div>
          </div>
        </div>
      </template>
      <div v-if="filteredGroups.length === 0" class="grant-empty">—</div>
    </div>
  </div>
</template>

<style scoped>
.grant-menu-table {
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.grant-toolbar {
  display: flex;
  gap: 8px;
}
.grant-grid {
  border: 1px solid var(--n-border-color, #e0e0e6);
  border-radius: 4px;
  font-size: 13px;
  overflow: hidden;
}
.grant-header {
  display: flex;
  background: var(--n-th-color, #fafafc);
  font-weight: 600;
  font-size: 12px;
  border-bottom: 1px solid var(--n-border-color, #e0e0e6);
}
.grant-header > div {
  padding: 8px 12px;
}
.grant-group {
  display: flex;
  border-bottom: 1px solid var(--n-border-color, #e0e0e6);
}
.grant-group:last-child {
  border-bottom: none;
}
.grant-group > .col-catalog {
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 8px 12px;
  border-right: 1px solid var(--n-border-color, #e0e0e6);
  cursor: pointer;
  user-select: none;
}
.collapse-arrow {
  display: inline-block;
  font-size: 10px;
  transition: transform 0.2s;
  color: var(--n-text-color-disabled, #999);
}
.collapse-arrow.is-collapsed {
  transform: rotate(-90deg);
}
.group-rows {
  flex: 1;
  min-width: 0;
}
.grant-row {
  display: flex;
  border-bottom: 1px solid var(--n-border-color, #e0e0e6);
}
.grant-row:last-child {
  border-bottom: none;
}
.col-catalog {
  width: 130px;
  flex-shrink: 0;
}
.col-menu {
  width: 140px;
  flex-shrink: 0;
  display: flex;
  align-items: center;
  padding: 6px 12px;
  border-right: 1px solid var(--n-border-color, #e0e0e6);
}
.col-buttons {
  flex: 1;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 4px 12px;
  padding: 6px 12px;
  min-width: 0;
}
.no-buttons {
  color: var(--n-text-color-disabled, #c0c0c0);
}
.grant-empty {
  text-align: center;
  padding: 24px;
  color: var(--n-text-color-disabled, #c0c0c0);
}
</style>
