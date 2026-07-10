<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount } from 'vue'
import { useAppStore } from '@/stores/app'
import { mix, rgba } from '@/theme/mix'
import LoginForm from '../LoginForm.vue'

const app = useAppStore()

// 聚光位置(百分比),默认略偏上居中;鼠标移动时跟随。
const mx = ref(50)
const my = ref(38)
function onMove(e: MouseEvent) {
  mx.value = (e.clientX / window.innerWidth) * 100
  my.value = (e.clientY / window.innerHeight) * 100
}
onMounted(() => window.addEventListener('mousemove', onMove, { passive: true }))
onBeforeUnmount(() => window.removeEventListener('mousemove', onMove))

const vars = computed(() => ({
  '--mx': mx.value + '%',
  '--my': my.value + '%',
  '--s1': rgba(app.accent, 0.28),
  '--s2': rgba(mix(app.accent, '#8B5CF6', 0.6), 0.22),
  // 跟随光晕独立取色:亮色近白底下 0.28 几乎不可见,提到 0.5 才读得出;暗底 0.28 已够。
  '--spot': rgba(app.accent, app.isDark ? 0.3 : 0.5),
}))
</script>

<template>
  <div class="spotlight" :style="vars">
    <div class="mesh" />
    <div class="spot" />
    <div class="card">
      <LoginForm />
    </div>
  </div>
</template>

<style scoped>
.spotlight {
  position: relative;
  height: 100vh;
  overflow: hidden;
  display: flex;
  align-items: center;
  justify-content: center;
  background: var(--color-bg-body);
}
.mesh {
  position: absolute;
  inset: 0;
  background:
    radial-gradient(30% 40% at 15% 20%, var(--s1), transparent 60%),
    radial-gradient(30% 40% at 85% 80%, var(--s2), transparent 60%);
  opacity: 0.9;
}
.spot {
  position: absolute;
  inset: 0;
  pointer-events: none;
  background: radial-gradient(340px 340px at var(--mx) var(--my), var(--spot), transparent 60%);
  transition: background 0.12s ease-out;
}
.card {
  position: relative;
  z-index: 2;
  width: 100%;
  max-width: 460px;
  padding: 46px 46px;
  background: var(--color-bg-container);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-xl);
  box-shadow: var(--shadow-3);
  animation: fade-up 0.6s cubic-bezier(0.16, 1, 0.3, 1) both;
}
@keyframes fade-up {
  from {
    opacity: 0;
    transform: translateY(16px);
  }
  to {
    opacity: 1;
    transform: none;
  }
}
/* 窄屏:给卡片侧边留白 + 收紧内边距(默认 46px 在 ~360px 手机上挤压表单、卡片贴边) */
@media (max-width: 480px) {
  .spotlight {
    padding: 0 16px;
  }
  .card {
    padding: 32px 24px;
  }
}

@media (prefers-reduced-motion: reduce) {
  .spot {
    /* 光晕仍跟随指针(指针驱动的位移,非自发动画);仅去掉缓动过渡。
       Windows Server 等默认关动画的环境会命中此分支——原来退化为静态居中,
       表现为"光晕不跟鼠标",故此处保留跟随。 */
    transition: none;
  }
  .card {
    animation: none;
  }
}
</style>
