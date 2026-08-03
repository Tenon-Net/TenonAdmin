<script setup lang="ts">
// 外部登录品牌标:
// 1) 优先本地 assets/oauth/{code}.svg|png(彩色圆标,Gitee 风)
// 2) 再 Iconify 名
// 3) 首字母
import { computed } from 'vue'
import { OfflineIcon } from 'tenon-naive-iconify-picker'
import { resolveProviderIcon, type BrandCode } from '@/utils/oauthBrand'

const props = withDefaults(
  defineProps<{
    code: string
    icon?: string | null
    size?: number
  }>(),
  { size: 22 },
)

const resolved = computed(() => resolveProviderIcon(props.code, props.icon))

// 相对路径 glob 更稳(Windows/别名下 /src/... 有时扫不到)
const assetModules = {
  ...import.meta.glob('../../assets/oauth/*.{svg,png,webp}', {
    eager: true,
    import: 'default',
    query: '?url',
  }),
  ...import.meta.glob('/src/assets/oauth/*.{svg,png,webp}', {
    eager: true,
    import: 'default',
    query: '?url',
  }),
} as Record<string, string>

function localBadgeUrl(code: string): string | null {
  const c = code.toLowerCase()
  for (const ext of ['svg', 'png', 'webp'] as const) {
    const hit = Object.entries(assetModules).find(([path, url]) => {
      if (!url || typeof url !== 'string') return false
      const base = path.replace(/\\/g, '/').split('/').pop()?.toLowerCase() ?? ''
      return base === `${c}.${ext}`
    })
    if (hit) return hit[1]
  }
  return null
}

const badgeUrl = computed(() => {
  if (resolved.value.kind !== 'brand') return null
  return localBadgeUrl(resolved.value.code)
})

/** 无本地图时的线标回退(仅占位,真正观感靠你放的本地圆标) */
const fallbackIconify: Partial<Record<BrandCode, string>> = {
  github: 'ant-design:github-filled',
  wechat: 'ant-design:wechat-filled',
  wecom: 'ant-design:wechat-work-filled',
  dingtalk: 'ant-design:dingtalk-circle-filled',
  qq: 'ant-design:qq-circle-filled',
}

const fallbackColor: Partial<Record<BrandCode, string>> = {
  github: '#24292f',
  wechat: '#07c160',
  wecom: '#2b7de9',
  dingtalk: '#0089ff',
  qq: '#12b7f5',
  gitee: '#c71d23',
}

const fallback = computed(() => {
  if (resolved.value.kind !== 'brand') return null
  const code = resolved.value.code
  return {
    icon: fallbackIconify[code],
    color: fallbackColor[code] ?? '#666',
  }
})

// 给父级判断:是否整图圆标(用于去掉按钮灰边)
defineExpose({ isBadge: computed(() => !!badgeUrl.value) })
</script>

<template>
  <!-- 本地彩色圆标:铺满按钮,图自带圆底 -->
  <img
    v-if="badgeUrl"
    class="oauth-brand-badge"
    :src="badgeUrl"
    :width="size"
    :height="size"
    alt=""
    draggable="false"
  />
  <span
    v-else
    class="oauth-brand-icon"
    :style="{ width: size + 'px', height: size + 'px', color: fallback?.color }"
    aria-hidden="true"
  >
    <OfflineIcon
      v-if="fallback?.icon"
      :icon="fallback.icon"
      :size="size"
      fallback="ph:link-simple"
    />
    <OfflineIcon
      v-else-if="resolved.kind === 'iconify'"
      :icon="resolved.name"
      :size="size"
      fallback="ph:link-simple"
    />
    <span v-else class="oauth-brand-letter">{{ resolved.kind === 'letter' ? resolved.letter : '?' }}</span>
  </span>
</template>

<style scoped>
/* 方形素材也强制裁成圆(Gitee 风);cover 铺满避免留白变「方块」 */
.oauth-brand-badge {
  display: block;
  width: 100%;
  height: 100%;
  max-width: 100%;
  max-height: 100%;
  object-fit: cover;
  border-radius: 50%;
  overflow: hidden;
  pointer-events: none;
  user-select: none;
  flex-shrink: 0;
}
.oauth-brand-icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  line-height: 0;
}
.oauth-brand-letter {
  font-size: 12px;
  font-weight: 700;
  color: var(--color-text-secondary, #666);
  line-height: 1;
}
</style>
