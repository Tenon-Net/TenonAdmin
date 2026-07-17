<script setup lang="ts">
// 内嵌外部页面:URL 由菜单约定放进 route.meta.iframeSrc(component 字段为 URL 时,见 useAuthMenu)。
// keep-alive 缓存本页 → 切走再回来 iframe 状态(滚动/表单)得以保留。
import { computed } from 'vue'
import { useRoute } from 'vue-router'

const route = useRoute()
const src = computed(() => route.meta.iframeSrc as string | undefined)
</script>

<template>
  <div class="embed-wrap">
    <iframe
      v-if="src"
      :src="src"
      class="embed"
      referrerpolicy="no-referrer"
      sandbox="allow-same-origin allow-scripts allow-forms allow-popups"
    />
  </div>
</template>

<style scoped>
.embed-wrap {
  height: 100%;
  min-height: calc(100vh - 160px);
}
.embed {
  display: block;
  width: 100%;
  height: 100%;
  min-height: calc(100vh - 160px);
  border: 0;
}
</style>
