<script setup lang="ts">
// 安全策略 = 结构化表单范式(同 SysBaseConfig):字段绑定固定 config key(GroupCode='security')。
// 后端经 ISecurityPolicyProvider 读这些键强制执行(登录锁定/密码复杂度/会话令牌时长),改值即时生效、无需重发。
import { onMounted, reactive, ref } from 'vue'
import { NForm, NFormItem, NInputNumber, NSwitch, NDivider, NButton, NSpin, useMessage } from 'naive-ui'
import { useI18n } from 'vue-i18n'
import AppIcon from '@/components/AppIcon.vue'
import { configApi } from '@/api'
import { translateError } from '@/utils/error'

const { t } = useI18n()
const message = useMessage()

// 数值项(min 兜底:锁死阈值不为负、时长至少 1 分钟,避免存 0 导致永不过期/永久锁定)
const NUM_FIELDS = [
  { key: 'sys.security.loginLock.maxFailCount', min: 0 },
  { key: 'sys.security.loginLock.lockMinutes', min: 1 },
  { key: 'sys.security.password.minLength', min: 1 },
  { key: 'sys.security.session.accessMinutes', min: 1 },
  { key: 'sys.security.session.refreshMinutes', min: 1 },
  { key: 'sys.security.rateLimit.windowSeconds', min: 1 },
  { key: 'sys.security.rateLimit.permitPerWindow', min: 0 },
  { key: 'sys.security.rateLimit.authPermitPerWindow', min: 0 },
] as const

const BOOL_FIELDS = [
  'sys.security.password.requireUpper',
  'sys.security.password.requireLower',
  'sys.security.password.requireDigit',
  'sys.security.password.requireSpecial',
] as const

// 验证码开关单独成节(与密码开关分区展示,故不并入 BOOL_FIELDS 的密码循环)
const CAPTCHA_KEY = 'sys.security.captcha.enabled'
// 限流开关(单独成节;阈值走 NUM_FIELDS)
const RATELIMIT_KEY = 'sys.security.rateLimit.enabled'

const nums = reactive<Record<string, number>>({})
const bools = reactive<Record<string, boolean>>({})
const loading = ref(true)
const saving = ref(false)

// 'sys.security.loginLock.maxFailCount' → t('config.security.loginLock.maxFailCount')
const label = (key: string) => t('config.security.' + key.replace('sys.security.', ''))

onMounted(async () => {
  try {
    const rows = await configApi.listByGroup('security')
    const map = new Map(rows.map((r) => [r.configKey, r.configValue ?? '']))
    for (const f of NUM_FIELDS) nums[f.key] = Number(map.get(f.key)) || f.min
    for (const k of BOOL_FIELDS) bools[k] = map.get(k) === 'true'
    bools[CAPTCHA_KEY] = map.get(CAPTCHA_KEY) === 'true'
    bools[RATELIMIT_KEY] = map.get(RATELIMIT_KEY) === 'true'
  } catch (e) {
    message.error(translateError(e))
  } finally {
    loading.value = false
  }
})

async function save() {
  saving.value = true
  try {
    // 配置值统一以字符串落库;数值/布尔序列化为字符串
    await configApi.saveBatch([
      ...NUM_FIELDS.map((f) => ({ configKey: f.key, configValue: String(nums[f.key]) })),
      ...BOOL_FIELDS.map((k) => ({ configKey: k, configValue: String(bools[k]) })),
      { configKey: CAPTCHA_KEY, configValue: String(bools[CAPTCHA_KEY]) },
      { configKey: RATELIMIT_KEY, configValue: String(bools[RATELIMIT_KEY]) },
    ])
    message.success(t('config.saved'))
  } catch (e) {
    message.error(translateError(e))
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <n-spin :show="loading">
    <n-form label-placement="left" :label-width="150" style="max-width: 560px">
      <n-divider title-placement="left">{{ t('config.security.loginLock.title') }}</n-divider>
      <n-form-item :label="label('sys.security.loginLock.maxFailCount')">
        <n-input-number v-model:value="nums['sys.security.loginLock.maxFailCount']" :min="0" style="width: 160px" />
      </n-form-item>
      <n-form-item :label="label('sys.security.loginLock.lockMinutes')">
        <n-input-number v-model:value="nums['sys.security.loginLock.lockMinutes']" :min="1" style="width: 160px" />
      </n-form-item>

      <n-divider title-placement="left">{{ t('config.security.password.title') }}</n-divider>
      <n-form-item :label="label('sys.security.password.minLength')">
        <n-input-number v-model:value="nums['sys.security.password.minLength']" :min="1" style="width: 160px" />
      </n-form-item>
      <n-form-item v-for="k in BOOL_FIELDS" :key="k" :label="label(k)">
        <n-switch v-model:value="bools[k]" />
      </n-form-item>

      <n-divider title-placement="left">{{ t('config.security.session.title') }}</n-divider>
      <n-form-item :label="label('sys.security.session.accessMinutes')">
        <n-input-number v-model:value="nums['sys.security.session.accessMinutes']" :min="1" style="width: 160px" />
      </n-form-item>
      <n-form-item :label="label('sys.security.session.refreshMinutes')">
        <n-input-number v-model:value="nums['sys.security.session.refreshMinutes']" :min="1" style="width: 160px" />
      </n-form-item>

      <n-divider title-placement="left">{{ t('config.security.captcha.title') }}</n-divider>
      <n-form-item :label="label('sys.security.captcha.enabled')">
        <n-switch v-model:value="bools[CAPTCHA_KEY]" />
      </n-form-item>

      <n-divider title-placement="left">{{ t('config.security.rateLimit.title') }}</n-divider>
      <n-form-item :label="label('sys.security.rateLimit.enabled')">
        <n-switch v-model:value="bools[RATELIMIT_KEY]" />
      </n-form-item>
      <n-form-item :label="label('sys.security.rateLimit.windowSeconds')">
        <n-input-number v-model:value="nums['sys.security.rateLimit.windowSeconds']" :min="1" style="width: 160px" />
      </n-form-item>
      <n-form-item :label="label('sys.security.rateLimit.permitPerWindow')">
        <n-input-number v-model:value="nums['sys.security.rateLimit.permitPerWindow']" :min="0" style="width: 160px" />
      </n-form-item>
      <n-form-item :label="label('sys.security.rateLimit.authPermitPerWindow')">
        <n-input-number v-model:value="nums['sys.security.rateLimit.authPermitPerWindow']" :min="0" style="width: 160px" />
      </n-form-item>

      <n-form-item :label="' '" :show-feedback="false">
        <n-button v-auth="'PUT:/api/v1/sys/config/batch'" type="primary" :loading="saving" @click="save">
          <template #icon><AppIcon icon="ph:floppy-disk" :size="16" /></template>{{ t('common.save') }}
        </n-button>
      </n-form-item>
    </n-form>
  </n-spin>
</template>
