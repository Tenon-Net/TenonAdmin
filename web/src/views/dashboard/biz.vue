<script setup lang="ts">
// 业务系统的工作台。每个应用一个首页组件——这里和 dashboard/workbench.vue 是两个独立页面,
// 由各自应用的「工作台」菜单(component 字段)指向,切应用即换首页。
// 全部真数据拼装:欢迎卡(user store)+ 快捷入口(当前应用菜单树的可见叶子)+ 我的通知(notice/mine)。
// 消费者拿模板后把后两张卡换成自己的业务统计;别造写死的假数字。
import { computed, onMounted, ref } from 'vue'
import { NCard, NButton, NList, NListItem, NEmpty } from 'naive-ui'
import { Icon } from '@iconify/vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { useUserStore } from '@/stores/user'
import { useAuthStore } from '@/stores/auth'
import { useAppStore } from '@/stores/app'
import { btnGrad } from '@/theme/mix'
import { noticeApi } from '@/api'
import type { NoticeMineItem } from '@/types/api'
import type { MenuNode } from '@/types/menu'
import AppIcon from '@/components/AppIcon.vue'

const { t } = useI18n()
const user = useUserStore()
const auth = useAuthStore()
const app = useAppStore()
const route = useRoute()
const router = useRouter()

const avatarStyle = computed(() => ({ background: btnGrad(app.accent) }))

/** 当前应用菜单树的可见叶子(带 path)= 快捷入口;排除本页自身。 */
function leaves(tree: MenuNode[]): MenuNode[] {
  const out: MenuNode[] = []
  for (const n of tree) {
    if (!n.visible) continue
    if (n.children?.length) out.push(...leaves(n.children))
    else if (n.path) out.push(n)
  }
  return out
}
const quick = computed(() => leaves(auth.menuTree).filter((n) => n.path !== route.path))

const notices = ref<NoticeMineItem[]>([])
onMounted(async () => {
  try {
    notices.value = (await noticeApi.mine({ page: 1, pageSize: 5 })).items
  } catch {
    // 首页不因一张卡挂了糊用户一脸红(同 workbench):通知取不到就空态,其余照常可用。
  }
})
const fmtTime = (s?: string | null) => (s ?? '').replace('T', ' ').slice(0, 16)
</script>

<template>
  <div class="view">
    <!-- 欢迎横幅(与 workbench 同语法:btnGrad 头像 + 主副标题) -->
    <div class="banner">
      <div class="avatar" :style="avatarStyle">
        <Icon icon="ph:user" :width="26" color="#fff" />
      </div>
      <div>
        <div class="hi">{{ t('biz.welcome', { name: user.userInfo?.name ?? user.userInfo?.account ?? '' }) }}</div>
        <div class="tip">{{ t('biz.subtitle') }}</div>
      </div>
    </div>

    <n-card :title="t('biz.quick')" :bordered="true">
      <div v-if="quick.length" class="quick">
        <n-button v-for="m in quick" :key="m.id" secondary @click="router.push(m.path!)">
          <template #icon><AppIcon :icon="m.icon || 'ph:squares-four'" :size="16" /></template>
          {{ m.title }}
        </n-button>
      </div>
      <n-empty v-else :description="t('biz.noMenus')" />
    </n-card>

    <n-card :title="t('biz.notices')" :bordered="true">
      <template #header-extra>
        <n-button text type="primary" @click="router.push('/personal/notice')">{{ t('biz.viewAll') }}</n-button>
      </template>
      <n-list v-if="notices.length" :show-divider="false">
        <n-list-item v-for="n in notices" :key="n.id">
          <div class="notice-row" @click="router.push('/personal/notice')">
            <span class="notice-title">
              <span v-if="!n.isRead" class="dot" />{{ n.title }}
            </span>
            <span class="notice-time">{{ fmtTime(n.publishTime) }}</span>
          </div>
        </n-list-item>
      </n-list>
      <n-empty v-else />
    </n-card>
  </div>
</template>

<style scoped>
.view {
  display: flex;
  flex-direction: column;
  gap: var(--gap-card);
}
.banner {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 24px;
  border-radius: var(--radius-lg);
  background: var(--color-primary-light);
}
.avatar {
  width: 52px;
  height: 52px;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
}
.hi {
  font-size: var(--font-size-lg);
  font-weight: 600;
  color: var(--color-text-primary);
}
.tip {
  color: var(--color-text-secondary);
  margin-top: 4px;
}
.quick {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
}
.notice-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  cursor: pointer;
}
.notice-title {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: var(--color-text-primary);
}
.dot {
  flex-shrink: 0;
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: var(--color-primary);
}
.notice-time {
  flex-shrink: 0;
  color: var(--color-text-secondary);
  font-size: 12px;
}
</style>
