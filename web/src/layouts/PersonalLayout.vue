<script setup lang="ts">
// 个人中心二级壳:主布局内容区内左侧子导航 + 右侧 RouterView。
// 路径仍为 /personal/*;强制改密时只露出「修改密码」导航项。
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { NMenu, NSelect } from 'naive-ui'
import { useBreakpoints, breakpointsTailwind } from '@vueuse/core'
import { useI18n } from 'vue-i18n'
import { useUserStore } from '@/stores/user'

const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const user = useUserStore()
const isMobile = useBreakpoints(breakpointsTailwind).smaller('md')

const allNav = [
  { key: '/personal/profile', labelKey: 'menu.profile' },
  { key: '/personal/password', labelKey: 'menu.password' },
  { key: '/personal/security', labelKey: 'menu.security' },
  { key: '/personal/sessions', labelKey: 'menu.sessions' },
  { key: '/personal/bindings', labelKey: 'menu.bindings' },
] as const

const mustChange = computed(() => !!user.userInfo?.mustChangePassword)

const navItems = computed(() => {
  const list = mustChange.value
    ? allNav.filter((x) => x.key === '/personal/password')
    : [...allNav]
  return list.map((x) => ({ key: x.key, label: t(x.labelKey) }))
})

const activeKey = computed(() => {
  const hit = navItems.value.find((x) => route.path === x.key || route.path.startsWith(x.key + '/'))
  return hit?.key ?? navItems.value[0]?.key ?? '/personal/profile'
})

const menuOptions = computed(() => navItems.value.map((x) => ({ key: x.key, label: x.label })))

function go(key: string) {
  if (key !== route.path) void router.push(key)
}
</script>

<template>
  <div class="personal-layout" :class="{ 'is-mobile': isMobile }">
    <aside v-if="!isMobile" class="personal-sider">
      <n-menu
        :value="activeKey"
        :options="menuOptions"
        :indent="18"
        @update:value="(k: string) => go(k)"
      />
    </aside>
    <div class="personal-main">
      <div v-if="isMobile" class="personal-mobile-nav">
        <n-select
          :value="activeKey"
          :options="menuOptions.map((o) => ({ label: o.label, value: o.key }))"
          @update:value="(k: string) => go(k)"
        />
      </div>
      <div class="personal-content">
        <RouterView />
      </div>
    </div>
  </div>
</template>

<style scoped>
.personal-layout {
  display: flex;
  gap: 16px;
  align-items: stretch;
  min-height: 100%;
}
.personal-sider {
  width: 200px;
  flex-shrink: 0;
  background: var(--color-bg-container, #fff);
  border: 1px solid var(--color-border, #e5e7eb);
  border-radius: 8px;
  padding: 8px 0 12px;
}
.personal-main {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.personal-mobile-nav {
  max-width: 360px;
}
.personal-content {
  min-width: 0;
}
.personal-layout.is-mobile {
  flex-direction: column;
}
</style>
