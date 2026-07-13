<script setup lang="ts">
import { computed } from 'vue'
import { Icon } from '@iconify/vue'
import { useI18n } from 'vue-i18n'
import { useAppStore } from '@/stores/app'
import { useSite } from '@/composables/useSite'
import { mix, rgba } from '@/theme/mix'
import TenonLogo from '@/components/TenonLogo.vue'
import LoginForm from '../LoginForm.vue'

const { t } = useI18n()
const app = useAppStore()
const { site, appVersion } = useSite()
const year = new Date().getFullYear()

// 卖点随语言切换(t 在 computed 内对 locale 响应)。
const points = computed(() => [t('login.featRbac'), t('login.featScope'), t('login.featPortal')])

const vars = computed(() => ({
  '--a1': rgba(app.accent, 0.5),
  '--a2': rgba(mix(app.accent, '#8B5CF6', 0.6), 0.4),
}))
</script>

<template>
  <div class="split" :style="vars">
    <!-- 左:品牌面板(窄屏隐藏)-->
    <div class="panel">
      <div class="panel-aurora" />
      <div class="panel-inner">
        <div class="logo">
          <TenonLogo :size="40" />
          <span>{{ site.title }}</span>
        </div>
        <div class="bar" :style="{ background: app.accent }" />
        <!-- 标题拆 pre/accent/post 三段以支持 i18n,中间词染 accent 色(整句硬编码中文切不了英文) -->
        <h1 class="headline">{{ t('login.headlinePre') }}<span :style="{ color: app.accent }">{{ t('login.headlineAccent') }}</span>{{ t('login.headlinePost') }}</h1>
        <p class="sub">{{ site.subtitle || t('login.subtitle') }}</p>
        <ul class="points">
          <li v-for="p in points" :key="p">
            <Icon icon="ph:check-circle-duotone" :width="18" :style="{ color: app.accent }" />{{ p }}
          </li>
        </ul>
      </div>
    </div>

    <!-- 右:全高工作区(居中表单 + 版权页脚);主题/语言切换在外壳全局工具药丸 -->
    <div class="form-wrap">
      <div class="mid">
        <div class="form-inner">
          <p class="greet">{{ t('login.welcome') }}</p>
          <p class="greet-sub">{{ t('login.welcomeSub') }}</p>
          <LoginForm :show-logo="false" :show-title="false" :show-footer="false" />
        </div>
      </div>

      <p class="foot">
        © {{ year }}
        <a v-if="site.copyrightUrl" :href="site.copyrightUrl" target="_blank" rel="noopener">{{ site.copyright || site.title }}</a>
        <template v-else>{{ site.copyright || site.title }}</template>
        <span v-if="appVersion" class="foot-ver">v{{ appVersion }}</span>
      </p>
    </div>
  </div>
</template>

<style scoped>
.split {
  height: 100vh;
  display: grid;
  grid-template-columns: 1.15fr 0.85fr;
  background: var(--color-bg-body);
}
/* 左栏 = recessed 品牌面(body 底 + 极光),与右栏 clean container 拉开图/底关系 */
.panel {
  position: relative;
  overflow: hidden;
  background: var(--color-bg-body);
  border-right: 1px solid var(--color-border);
}
/* 细网格纹理,左上淡出 */
.panel::before {
  content: '';
  position: absolute;
  inset: 0;
  background-image:
    linear-gradient(rgba(127, 127, 127, 0.08) 1px, transparent 1px),
    linear-gradient(90deg, rgba(127, 127, 127, 0.08) 1px, transparent 1px);
  background-size: 40px 40px;
  -webkit-mask-image: radial-gradient(circle at 30% 40%, #000, transparent 72%);
  mask-image: radial-gradient(circle at 30% 40%, #000, transparent 72%);
}
.panel-aurora {
  position: absolute;
  inset: -20%;
  background:
    radial-gradient(40% 40% at 25% 20%, var(--a1), transparent 68%),
    radial-gradient(45% 45% at 80% 78%, var(--a2), transparent 70%);
  filter: blur(45px);
  opacity: 0.9;
  animation: panel-move 22s ease-in-out infinite alternate;
}
@keyframes panel-move {
  0% {
    transform: translate3d(-3%, -2%, 0) scale(1.05);
  }
  100% {
    transform: translate3d(3%, 3%, 0) scale(1.12);
  }
}
.panel-inner {
  position: relative;
  z-index: 1;
  padding: 64px 56px;
  display: flex;
  flex-direction: column;
  height: 100%;
  justify-content: center;
}
.logo {
  display: flex;
  align-items: center;
  gap: 12px;
  font-size: 22px;
  font-weight: 700;
  color: var(--color-text-primary);
}
.bar {
  width: 40px;
  height: 3px;
  border-radius: 2px;
  margin: 28px 0 20px;
}
.headline {
  font-size: 42px;
  line-height: 1.2;
  font-weight: 800;
  color: var(--color-text-primary);
  margin: 0;
}
.sub {
  color: var(--color-text-secondary);
  margin: 16px 0 28px;
}
.points {
  list-style: none;
  padding: 0;
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: 14px;
}
.points li {
  display: flex;
  align-items: center;
  gap: 10px;
  color: var(--color-text-secondary);
}

/* 右栏 = 全高工作区:居中表单 + 页脚(顶栏切换已上移到外壳全局工具) */
.form-wrap {
  position: relative;
  display: flex;
  flex-direction: column;
  background: var(--color-bg-container);
}
.mid {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 24px;
}
.form-inner {
  width: 100%;
  max-width: 340px;
  animation: fade-up 0.6s cubic-bezier(0.16, 1, 0.3, 1) both;
}
.greet {
  font-size: 26px;
  font-weight: 700;
  letter-spacing: -0.01em;
  color: var(--color-text-primary);
  margin: 0 0 6px;
}
.greet-sub {
  font-size: var(--font-size-sm);
  color: var(--color-text-tertiary);
  margin: 0 0 26px;
}
.foot {
  margin: 0;
  padding: 18px 24px 22px;
  text-align: center;
  font-size: var(--font-size-sm);
  color: var(--color-text-tertiary);
}
.foot a {
  color: inherit;
  text-decoration: none;
}
.foot a:hover {
  color: var(--color-primary);
}
.foot-ver {
  margin-left: 8px;
  opacity: 0.75;
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
@media (max-width: 900px) {
  .split {
    grid-template-columns: 1fr;
  }
  .panel {
    display: none;
  }
}
@media (prefers-reduced-motion: reduce) {
  .panel-aurora,
  .form-inner {
    animation: none;
  }
}
</style>
