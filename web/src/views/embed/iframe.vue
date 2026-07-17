<script setup lang="ts">
// 内嵌外部页面:URL 由菜单约定放进 route.meta.iframeSrc(component 字段为 URL 时,见 useAuthMenu)。
// 每个内嵌菜单经 namedPage 有独立组件身份、meta 恒定 → src 取一次快照即可,**不能**跟全局当前路由响应式:
// 否则被 keep-alive 缓存的实例会在切到别的路由时把 src 重算成 undefined(卸载 iframe → 回访白重载)
// 或另一内嵌页的 URL(后台悄悄加载对方站点)。setup 只跑一次,快照天然正确。
import { useRoute } from 'vue-router'

const src = useRoute().meta.iframeSrc as string | undefined
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
