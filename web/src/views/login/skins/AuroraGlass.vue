<script setup lang="ts">
import { computed } from 'vue'
import { NConfigProvider, darkTheme } from 'naive-ui'
import type { GlobalThemeOverrides } from 'naive-ui'
import { useAppStore } from '@/stores/app'
import { mix, rgba } from '@/theme/mix'
import LoginForm from '../LoginForm.vue'

const app = useAppStore()

// 极光四色:accent + 派生紫/青/品红,随主题色联动。
const vars = computed(() => ({
  '--a1': rgba(app.accent, 0.55),
  '--a2': rgba(mix(app.accent, '#8B5CF6', 0.65), 0.5),
  '--a3': rgba(mix(app.accent, '#22D3EE', 0.55), 0.45),
  '--a4': rgba(mix(app.accent, '#EC4899', 0.6), 0.4),
}))

// 跟随应用明暗:深色用 darkTheme,浅色用 Naive 默认(null)。inherit=false 隔离应用根 overrides。
const naiveTheme = computed(() => (app.dark ? darkTheme : null))

// accent 派生主色两套共用。
const common = computed(() => ({
  primaryColor: app.accent,
  primaryColorHover: mix(app.accent, '#ffffff', 0.16),
  primaryColorPressed: mix(app.accent, '#000000', 0.18),
}))

// 输入框做成玻璃上的半透明观感 + accent 聚焦,深/浅两套。
// ponytail: 浅色分支硬编码了 text-primary(#1D2129)/secondary(#4E5969) 令牌值,tokens 若改需同步
const glassTheme = computed<GlobalThemeOverrides>(() =>
  app.dark
    ? {
        common: common.value,
        Input: {
          color: 'rgba(255, 255, 255, 0.06)',
          colorFocus: 'rgba(255, 255, 255, 0.1)',
          textColor: 'rgba(255, 255, 255, 0.92)',
          placeholderColor: 'rgba(255, 255, 255, 0.4)',
          iconColor: 'rgba(255, 255, 255, 0.5)',
          border: '1px solid rgba(255, 255, 255, 0.14)',
          borderHover: `1px solid ${rgba(app.accent, 0.6)}`,
          borderFocus: `1px solid ${app.accent}`,
          boxShadowFocus: `0 0 0 2px ${rgba(app.accent, 0.25)}`,
          caretColor: app.accent,
        },
        Form: { labelTextColor: 'rgba(255, 255, 255, 0.78)' },
        Checkbox: { textColor: 'rgba(255, 255, 255, 0.7)' },
      }
    : {
        common: common.value,
        Input: {
          color: 'rgba(255, 255, 255, 0.5)',
          colorFocus: 'rgba(255, 255, 255, 0.8)',
          textColor: '#1D2129',
          placeholderColor: 'rgba(0, 0, 0, 0.4)',
          iconColor: 'rgba(0, 0, 0, 0.4)',
          border: '1px solid rgba(0, 0, 0, 0.1)',
          borderHover: `1px solid ${rgba(app.accent, 0.5)}`,
          borderFocus: `1px solid ${app.accent}`,
          boxShadowFocus: `0 0 0 2px ${rgba(app.accent, 0.2)}`,
          caretColor: app.accent,
        },
        Form: { labelTextColor: '#4E5969' },
        Checkbox: { textColor: '#4E5969' },
      },
)
</script>

<template>
  <div class="aurora" :class="{ light: !app.dark }" :style="vars">
    <div class="aurora-bg" />
    <div class="stars" />
    <div class="orb orb-1" />
    <div class="orb orb-2" />
    <div class="orb orb-3" />
    <div class="aurora-content">
      <div class="halo" />
      <n-config-provider :theme="naiveTheme" :theme-overrides="glassTheme" :inherit-theme-overrides="false">
        <div class="glass-card">
          <LoginForm />
        </div>
      </n-config-provider>
    </div>
    <div class="grain" />
  </div>
</template>

<style scoped>
.aurora {
  position: relative;
  height: 100vh;
  overflow: hidden;
  display: flex;
  align-items: center;
  justify-content: center;
  background:
    radial-gradient(140% 120% at 50% 0%, #12162b 0%, #0a0b12 55%, #06060c 100%);
}
/* 细网格,中心遮罩淡出 */
.aurora::before {
  content: '';
  position: absolute;
  inset: 0;
  z-index: 0;
  background-image:
    linear-gradient(rgba(255, 255, 255, 0.045) 1px, transparent 1px),
    linear-gradient(90deg, rgba(255, 255, 255, 0.045) 1px, transparent 1px);
  background-size: 46px 46px;
  -webkit-mask-image: radial-gradient(circle at 50% 45%, #000 0%, transparent 72%);
  mask-image: radial-gradient(circle at 50% 45%, #000 0%, transparent 72%);
}
/* 暗角,聚焦中心 */
.aurora::after {
  content: '';
  position: absolute;
  inset: 0;
  z-index: 1;
  pointer-events: none;
  background: radial-gradient(120% 120% at 50% 42%, transparent 52%, rgba(0, 0, 0, 0.6) 100%);
}
.aurora-bg {
  position: absolute;
  inset: -20%;
  z-index: 0;
  background:
    radial-gradient(38% 38% at 18% 22%, var(--a1), transparent 68%),
    radial-gradient(42% 42% at 82% 18%, var(--a2), transparent 70%),
    radial-gradient(48% 48% at 60% 88%, var(--a3), transparent 72%),
    radial-gradient(34% 34% at 88% 68%, var(--a4), transparent 66%);
  filter: blur(28px);
  animation: aurora-move 18s ease-in-out infinite alternate;
}
@keyframes aurora-move {
  0% {
    transform: translate3d(-3%, -2%, 0) rotate(0deg) scale(1.05);
  }
  50% {
    transform: translate3d(3%, 2%, 0) rotate(4deg) scale(1.16);
  }
  100% {
    transform: translate3d(-2%, 3%, 0) rotate(-3deg) scale(1.08);
  }
}
/* 星点微闪 */
.stars {
  position: absolute;
  inset: 0;
  z-index: 0;
  background-image:
    radial-gradient(1.5px 1.5px at 20% 30%, rgba(255, 255, 255, 0.7), transparent 60%),
    radial-gradient(1px 1px at 70% 62%, rgba(255, 255, 255, 0.5), transparent 60%),
    radial-gradient(1.5px 1.5px at 42% 82%, rgba(255, 255, 255, 0.6), transparent 60%),
    radial-gradient(1px 1px at 85% 22%, rgba(255, 255, 255, 0.5), transparent 60%),
    radial-gradient(1px 1px at 55% 14%, rgba(255, 255, 255, 0.55), transparent 60%),
    radial-gradient(1.5px 1.5px at 12% 66%, rgba(255, 255, 255, 0.5), transparent 60%),
    radial-gradient(1px 1px at 92% 48%, rgba(255, 255, 255, 0.45), transparent 60%);
  animation: twinkle 5s ease-in-out infinite;
}
@keyframes twinkle {
  0%,
  100% {
    opacity: 0.45;
  }
  50% {
    opacity: 0.9;
  }
}
.orb {
  position: absolute;
  z-index: 0;
  border-radius: 50%;
  filter: blur(55px);
  opacity: 0.55;
}
.orb-1 {
  width: 340px;
  height: 340px;
  background: var(--a1);
  top: -60px;
  left: -40px;
  animation: float-y 14s ease-in-out infinite;
}
.orb-2 {
  width: 280px;
  height: 280px;
  background: var(--a4);
  bottom: -80px;
  right: -40px;
  animation: float-y 16s ease-in-out infinite reverse;
}
.orb-3 {
  width: 200px;
  height: 200px;
  background: var(--a3);
  top: 38%;
  right: 18%;
  animation: float-y 20s ease-in-out infinite;
}
@keyframes float-y {
  0%,
  100% {
    transform: translateY(0);
  }
  50% {
    transform: translateY(-30px);
  }
}
.aurora-content {
  position: relative;
  z-index: 3;
  width: 100%;
  max-width: 520px;
  padding: 24px;
}
/* 卡后旋转极光光环(略大于卡片,溢出成辉光环) */
.halo {
  position: absolute;
  z-index: 0;
  inset: 8px;
  border-radius: 36px;
  background: conic-gradient(from 0deg, var(--a1), var(--a2), var(--a3), var(--a4), var(--a1));
  filter: blur(48px);
  opacity: 0.6;
  animation: spin 16s linear infinite;
}
@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}
.glass-card {
  /* 覆盖共享表单文字色为浅色(玻璃是深的) */
  --lf-title: #fff;
  --lf-hint: rgba(255, 255, 255, 0.5);
  position: relative;
  z-index: 1;
  width: 100%;
  padding: 46px 46px;
  border-radius: 20px;
  background: rgba(255, 255, 255, 0.07);
  border: 1px solid rgba(255, 255, 255, 0.14);
  backdrop-filter: blur(26px) saturate(150%);
  -webkit-backdrop-filter: blur(26px) saturate(150%);
  box-shadow:
    0 30px 80px rgba(0, 0, 0, 0.55),
    0 0 60px -12px var(--a2),
    inset 0 1px 0 rgba(255, 255, 255, 0.2);
  overflow: hidden;
}
/* accent 渐变发光边框 */
.glass-card::before {
  content: '';
  position: absolute;
  inset: 0;
  border-radius: 20px;
  padding: 1px;
  background: linear-gradient(135deg, var(--a1), transparent 45%, var(--a2));
  -webkit-mask:
    linear-gradient(#000 0 0) content-box,
    linear-gradient(#000 0 0);
  -webkit-mask-composite: xor;
  mask-composite: exclude;
  pointer-events: none;
  opacity: 0.9;
}
/* 玻璃表面缓慢流光 */
.glass-card::after {
  content: '';
  position: absolute;
  inset: 0;
  z-index: 2;
  pointer-events: none;
  background: linear-gradient(120deg, transparent 36%, rgba(255, 255, 255, 0.1) 48%, transparent 60%);
  background-size: 220% 100%;
  background-repeat: no-repeat;
  animation: sheen 8s ease-in-out infinite;
}
@keyframes sheen {
  0% {
    background-position: 180% 0;
  }
  55%,
  100% {
    background-position: -60% 0;
  }
}
/* 胶片颗粒,消除色带 + 质感 */
.grain {
  position: absolute;
  inset: 0;
  z-index: 5;
  pointer-events: none;
  opacity: 0.05;
  mix-blend-mode: overlay;
  background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='120' height='120'%3E%3Cfilter id='n'%3E%3CfeTurbulence type='fractalNoise' baseFrequency='0.9' numOctaves='2' stitchTiles='stitch'/%3E%3C/filter%3E%3Crect width='100%25' height='100%25' filter='url(%23n)'/%3E%3C/svg%3E");
}
/* ── 浅色变体(应用浅色主题时)──────────────────────── */
.aurora.light {
  background:
    radial-gradient(140% 120% at 50% 0%, #eef1fb 0%, #f6f7fc 55%, #ffffff 100%);
}
/* 网格线改深(浅底上可见) */
.aurora.light::before {
  background-image:
    linear-gradient(rgba(0, 0, 0, 0.05) 1px, transparent 1px),
    linear-gradient(90deg, rgba(0, 0, 0, 0.05) 1px, transparent 1px);
}
/* 暗角柔化为冷色微晕(黑暗角在浅底像脏) */
.aurora.light::after {
  background: radial-gradient(120% 120% at 50% 42%, transparent 55%, rgba(120, 130, 180, 0.12) 100%);
}
/* 白星点在浅底不可见,隐藏 */
.aurora.light .stars {
  display: none;
}
/* 光环稍收敛 */
.aurora.light .halo {
  opacity: 0.5;
}
/* 玻璃卡:浅色玻璃 + 深色文字 */
.aurora.light .glass-card {
  --lf-title: var(--color-text-primary);
  --lf-hint: var(--color-text-tertiary);
  background: rgba(255, 255, 255, 0.62);
  border: 1px solid rgba(214, 218, 235, 0.9);
  box-shadow:
    0 30px 80px rgba(80, 90, 160, 0.18),
    0 0 60px -12px var(--a2),
    inset 0 1px 0 rgba(255, 255, 255, 0.9);
}
/* 浅色下关掉 accent 遮罩渐变环:mask-composite 在圆角处有细毛边,深色靠辉光盖住、浅色会暴露成"一层毛边" */
.aurora.light .glass-card::before {
  display: none;
}
/* 白色流光在浅玻璃上几乎不可见,浅色下关掉 */
.aurora.light .glass-card::after {
  display: none;
}

@media (prefers-reduced-motion: reduce) {
  .aurora-bg,
  .orb,
  .stars,
  .halo,
  .glass-card::after {
    animation: none;
  }
  .glass-card::after {
    display: none;
  }
}
</style>
