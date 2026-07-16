<script setup lang="ts">
// 个人资料:自己能改的字段全在这(姓名/昵称/性别/手机/邮箱/头像);机构/职位由管理员维护,只读展示。
// 提交是全量替换语义(后端 UpdateProfileInput):整表单一把梭,别做单字段 PATCH。
// 手机/邮箱直接改不做二次验证——内核无短信/邮件通道,管理员本可在用户管理任改,操作日志留痕(后端注释同款理由)。
import { onMounted, reactive, ref } from 'vue'
import {
  NCard, NForm, NFormItem, NInput, NButton, NSpin, NAvatar, useMessage,
  type FormInst, type FormRules,
} from 'naive-ui'
import { useI18n } from 'vue-i18n'
import { personalApi } from '@/api'
import { useUserStore } from '@/stores/user'
import { translateError } from '@/utils/error'
import DictSelect from '@/components/DictSelect/index.vue'
import FileUpload from '@/components/FileUpload/index.vue'
import AppIcon from '@/components/AppIcon.vue'

const { t } = useI18n()
const message = useMessage()
const user = useUserStore()

const loading = ref(true)
const saving = ref(false)
const avatarUploading = ref(false)
const formRef = ref<FormInst | null>(null)
const model = reactive({
  account: '',
  name: '',
  nickname: '',
  phone: '',
  email: '',
  gender: null as string | null,
  avatar: null as string | null,
  org: '',
  position: '',
})

// 手机/邮箱选填,填了才校验格式(与管理端 user 页同规则;后端存自由字符串)
const rules: FormRules = {
  phone: { validator: (_r, v: string) => !v || /^1[3-9]\d{9}$/.test(v), message: () => t('profile.phoneInvalid'), trigger: ['input', 'blur'] },
  email: { validator: (_r, v: string) => !v || /^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(v), message: () => t('profile.emailInvalid'), trigger: ['input', 'blur'] },
}

const initial = (name: string) => name.trim().charAt(0).toUpperCase() || '?'

onMounted(async () => {
  try {
    const p = await personalApi.profile()
    model.account = p.account
    model.name = p.name
    model.nickname = p.nickname ?? ''
    model.phone = p.phone ?? ''
    model.email = p.email ?? ''
    model.gender = p.gender ?? null
    model.avatar = p.avatar ?? null
    // 展示名称而非 Id:雪花主键是 14 位数字,甩给用户看没有任何意义。
    // 名称由 /personal/profile 一并返回(该端点是 [ActiveSession],人人可读);机构/职位被删则回落占位符。
    model.org = p.orgName ?? '—'
    model.position = p.positionName ?? '—'
  } catch (e) {
    message.error(translateError(e))
  } finally {
    loading.value = false
  }
})

async function save() {
  try {
    await formRef.value?.validate()
  } catch {
    return // 校验失败:表单项已就地标红,不再叠 toast
  }
  saving.value = true
  try {
    await personalApi.updateProfile({
      name: model.name,
      nickname: model.nickname || null,
      phone: model.phone || null,
      email: model.email || null,
      gender: model.gender,
      avatar: model.avatar,
    })
    if (user.userInfo) {
      user.userInfo.name = model.name
      user.userInfo.avatar = model.avatar // 顶栏头像即时同步,不等下次 enterInitial
    }
    message.success(t('profile.saved'))
  } catch (e) {
    message.error(translateError(e))
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <n-card :bordered="true" :title="t('profile.title')" style="max-width: 560px">
    <n-spin :show="loading">
      <n-form ref="formRef" :model="model" :rules="rules" label-placement="left" :label-width="80">
        <n-form-item :label="t('profile.avatar')">
          <div class="avatar-field">
            <n-avatar round :size="48" style="flex-shrink: 0" :src="model.avatar || undefined">
              <template #fallback>{{ initial(model.name) }}</template>
              <template v-if="!model.avatar">{{ initial(model.name) }}</template>
            </n-avatar>
            <FileUpload
              accept="image/*"
              :show-file-list="false"
              @loading-change="(v) => (avatarUploading = v)"
              @uploaded="(o) => (model.avatar = o.viewUrl ?? null)"
            >
              <n-button size="small" :loading="avatarUploading" :disabled="avatarUploading">
                <template #icon><AppIcon icon="ph:upload-simple" :size="14" /></template>
                {{ avatarUploading ? t('profile.avatarUploading') : t('profile.avatar') }}
              </n-button>
            </FileUpload>
          </div>
        </n-form-item>
        <n-form-item :label="t('profile.account')">
          <n-input :value="model.account" disabled />
        </n-form-item>
        <n-form-item :label="t('profile.name')" required>
          <n-input v-model:value="model.name" :placeholder="t('profile.name')" />
        </n-form-item>
        <n-form-item :label="t('profile.nickname')">
          <n-input v-model:value="model.nickname" :placeholder="t('profile.nickname')" />
        </n-form-item>
        <n-form-item :label="t('profile.gender')">
          <DictSelect v-model:value="model.gender" type-code="gender" clearable :placeholder="t('profile.gender')" />
        </n-form-item>
        <n-form-item :label="t('profile.phone')" path="phone">
          <n-input v-model:value="model.phone" :placeholder="t('profile.phone')" />
        </n-form-item>
        <n-form-item :label="t('profile.email')" path="email">
          <n-input v-model:value="model.email" :placeholder="t('profile.email')" />
        </n-form-item>
        <n-form-item :label="t('profile.org')">
          <n-input :value="model.org" disabled />
        </n-form-item>
        <n-form-item :label="t('profile.position')">
          <n-input :value="model.position" disabled />
        </n-form-item>
        <n-form-item :label="' '">
          <n-button type="primary" :loading="saving" :disabled="!model.name.trim()" @click="save">{{ t('common.save') }}</n-button>
        </n-form-item>
      </n-form>
    </n-spin>
  </n-card>
</template>

<style scoped>
.avatar-field {
  display: flex;
  align-items: center;
  gap: 12px;
}
</style>
