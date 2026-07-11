<script setup lang="ts">
// 密码强度提示条 + 规则清单。自包含:传入密码字符串即可,内部 onMounted 拉后端当前
// 生效策略(超管在「安全策略」可改),精确同步不漂移。改密页与建用户页共用。
// ponytail: i18n 沿用 changePassword.* 键(既有,零迁移);它们只是键路径,重命名是纯 churn。
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import AppIcon from '@/components/AppIcon.vue'
import { configApi } from '@/api'

const props = defineProps<{ value: string }>()
const { t } = useI18n()

// 默认 = 后端默认策略,作 fallback(拉取失败仍可用;后端始终强制,弱口令以 passwordTooWeak 兜底)。
const policy = ref({ minLength: 8, requireUpper: true, requireLower: true, requireDigit: true, requireSpecial: false })
onMounted(async () => {
  try {
    policy.value = await configApi.passwordPolicy()
  } catch { /* 保留默认;后端仍强制 */ }
})

const checks = computed(() => {
  const p = props.value
  return {
    minLength: p.length >= policy.value.minLength,
    upper: /[A-Z]/.test(p),
    lower: /[a-z]/.test(p),
    digit: /\d/.test(p),
    special: /[^A-Za-z0-9]/.test(p),
  }
})
// 强度:长度达标 + 字符种类数 → 弱(1)/中(2)/强(3);空则 0
const strength = computed(() => {
  const p = props.value
  if (!p) return 0
  const c = checks.value
  const variety = [c.upper, c.lower, c.digit, c.special].filter(Boolean).length
  if (!c.minLength || variety <= 1) return 1
  if (variety === 2 || p.length < 12) return 2
  return 3
})
const strengthText = computed(() => ['', t('changePassword.strength.weak'), t('changePassword.strength.fair'), t('changePassword.strength.strong')][strength.value])
const strengthColor = computed(() => ['', '#e88080', '#e0a458', '#5aa86e'][strength.value])
// 规则清单按有效策略动态构建:恒显最小长度;大小写/数字仅在策略要求时作硬规则;
// 特殊字符——策略要求时作硬规则,否则作可选提示。
const ruleList = computed(() => {
  const pol = policy.value
  const c = checks.value
  const rows = [{ key: 'minLength', ok: c.minLength, text: t('changePassword.rules.minLength', { n: pol.minLength }) }]
  if (pol.requireUpper) rows.push({ key: 'upper', ok: c.upper, text: t('changePassword.rules.upper') })
  if (pol.requireLower) rows.push({ key: 'lower', ok: c.lower, text: t('changePassword.rules.lower') })
  if (pol.requireDigit) rows.push({ key: 'digit', ok: c.digit, text: t('changePassword.rules.digit') })
  rows.push({
    key: 'special',
    ok: c.special,
    text: pol.requireSpecial ? t('changePassword.rules.special') : t('changePassword.rules.specialOptional'),
  })
  return rows
})
</script>

<template>
  <div v-if="value" class="pw-meter">
    <div class="pw-bar">
      <span v-for="i in 3" :key="i" :style="{ background: i <= strength ? strengthColor : 'var(--color-border)' }" />
    </div>
    <div class="pw-level" :style="{ color: strengthColor }">
      {{ t('changePassword.strength.label') }}:{{ strengthText }}
    </div>
    <ul class="pw-rules">
      <li v-for="r in ruleList" :key="r.key" :class="{ ok: r.ok }">
        <AppIcon :icon="r.ok ? 'ph:check-circle-fill' : 'ph:circle'" :size="14" />{{ r.text }}
      </li>
    </ul>
  </div>
</template>

<style scoped>
.pw-meter {
  margin: -8px 0 14px;
}
.pw-bar {
  display: flex;
  gap: 6px;
}
.pw-bar span {
  flex: 1;
  height: 4px;
  border-radius: 2px;
  transition: background var(--transition-fast);
}
.pw-level {
  margin-top: 6px;
  font-size: 12px;
}
.pw-rules {
  margin: 8px 0 0;
  padding: 0;
  list-style: none;
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 2px 12px;
}
.pw-rules li {
  display: flex;
  align-items: center;
  gap: 4px;
  font-size: 12px;
  color: var(--color-text-tertiary);
}
.pw-rules li.ok {
  color: #5aa86e;
}
</style>
