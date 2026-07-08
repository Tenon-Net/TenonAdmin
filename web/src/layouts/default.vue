<script setup lang="ts">
import { computed, h } from 'vue'
import { RouterView, useRoute, useRouter } from 'vue-router'
import { NLayout, NLayoutSider, NLayoutContent, NMenu, NScrollbar, type MenuOption } from 'naive-ui'
import { Icon } from '@iconify/vue'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '@/stores/auth'
import { useAppStore } from '@/stores/app'
import { MenuType, type MenuNode } from '@/types/menu'
import AppHeader from './AppHeader.vue'
import TenonLogo from '@/components/TenonLogo.vue'

const auth = useAuthStore()
const app = useAppStore()
const route = useRoute()
const router = useRouter()
const { t } = useI18n()

function renderIcon(name?: string) {
  return name ? () => h(Icon, { icon: name, width: 18, height: 18 }) : undefined
}

// 菜单树 → n-menu options:剥按钮、丢空目录、页面叶子 key=路由 path。
function toOptions(nodes: MenuNode[]): MenuOption[] {
  return [...nodes]
    .sort((a, b) => a.sort - b.sort)
    .filter((n) => n.type !== MenuType.Button && n.visible !== false)
    .map<MenuOption | null>((n) => {
      if (n.type === MenuType.Catalog) {
        const children = toOptions(n.children ?? [])
        if (!children.length) return null // 仅含按钮的目录被剥空 → 丢弃
        return { label: n.title, key: `cat-${n.id}`, icon: renderIcon(n.icon), children }
      }
      return { label: n.title, key: n.path ?? `menu-${n.id}`, icon: renderIcon(n.icon) }
    })
    .filter((o): o is MenuOption => o !== null)
}

const menuOptions = computed<MenuOption[]>(() => [
  { label: t('menu.workbench'), key: '/workbench', icon: renderIcon('ph:squares-four-duotone') },
  ...toOptions(auth.menuTree),
])

const activeKey = computed(() => route.path)
function onSelect(key: string) {
  if (key.startsWith('/')) router.push(key)
}
</script>

<template>
  <n-layout has-sider position="absolute">
    <n-layout-sider
      bordered
      collapse-mode="width"
      :collapsed="app.collapsed"
      :collapsed-width="76"
      :width="236"
      :native-scrollbar="false"
      class="sider"
    >
      <div class="brand">
        <TenonLogo :size="28" />
        <span v-show="!app.collapsed" class="brand-name">TenonAdmin</span>
      </div>
      <n-scrollbar style="max-height: calc(100vh - var(--header-h))">
        <n-menu
          :options="menuOptions"
          :value="activeKey"
          :collapsed="app.collapsed"
          :collapsed-width="76"
          :indent="20"
          :root-indent="20"
          @update:value="onSelect"
        />
      </n-scrollbar>
    </n-layout-sider>
    <n-layout>
      <div class="header">
        <AppHeader />
      </div>
      <n-layout-content
        :native-scrollbar="false"
        content-style="padding: var(--pad-page);"
        style="background: var(--color-bg-body)"
      >
        <router-view v-slot="{ Component }">
          <keep-alive>
            <component :is="Component" />
          </keep-alive>
        </router-view>
      </n-layout-content>
    </n-layout>
  </n-layout>
</template>

<style scoped>
.sider {
  background: var(--color-bg-container);
}
.brand {
  display: flex;
  align-items: center;
  gap: 10px;
  height: var(--header-h);
  padding: 0 22px;
  color: var(--color-text-primary);
  font-weight: 600;
  overflow: hidden;
  white-space: nowrap;
}
.brand-name {
  font-size: var(--font-size-md);
}
.header {
  height: var(--header-h);
  border-bottom: 1px solid var(--color-border);
  background: var(--color-header-bg);
  backdrop-filter: blur(12px);
  position: sticky;
  top: 0;
  z-index: 10;
}
</style>
