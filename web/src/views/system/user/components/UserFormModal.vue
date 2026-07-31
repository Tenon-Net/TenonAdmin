<script setup lang="ts">
// 用户「新增/编辑」弹窗 —— 从 user/index.vue 抽出。职位/角色/主管下拉由父页拉好经 props 传入(避免重复请求);
// 编辑回显走 userApi.detail 拿 roleIds;保存后 emit('saved') 交回父页刷新表格。
// 新增留空口令时后端生成随机强口令,经 emit('passwordGenerated') 交回父页用同一结果弹层展示(明文仅此一次)。
import { reactive, ref } from 'vue'
import {
  NButton, NAvatar, NInput, NForm, NFormItemGi, NGrid, NSelect, NSwitch,
  useMessage, type FormInst, type FormRules,
} from 'naive-ui'
import { useI18n } from 'vue-i18n'
import AppIcon from '@/components/AppIcon.vue'
import PasswordStrength from '@/components/PasswordStrength/index.vue'
import FormContainer from '@/components/FormContainer/index.vue'
import OrgTreeSelect from '@/components/OrgTreeSelect/index.vue'
import DictSelect from '@/components/DictSelect/index.vue'
import FileUpload from '@/components/FileUpload/index.vue'
import { userApi } from '@/api'
import { translateError } from '@/utils/error'
import type { AddUserInput, UpdateUserInput, UserItem } from '@/types/api'

const props = defineProps<{
  positionOptions: { label: string; value: number }[]
  roleOptions: { label: string; value: number }[]
  directorOptions: { label: string; value: number }[]
}>()
const emit = defineEmits<{
  (e: 'saved'): void
  (e: 'passwordGenerated', pwd: string): void
}>()

const { t } = useI18n()
const message = useMessage()

// 头像首字母兜底(列表页亦有一份同名工具,各自 3 字符不值得上提)。
const initial = (name?: string | null) => (name || '?').slice(0, 1)

const show = ref(false)
const formRef = ref<FormInst | null>(null)
const editingId = ref<number | null>(null)
const rules: FormRules = {
  account: { required: true, whitespace: true, message: () => t('user.accountRequired'), trigger: ['input', 'blur'] },
  name: { required: true, whitespace: true, message: () => t('user.nameRequired'), trigger: ['input', 'blur'] },
  // 手机/邮箱选填,填了才校验格式(前端信任边界;后端存自由字符串)
  phone: { validator: (_r, v: string) => !v || /^1[3-9]\d{9}$/.test(v), message: () => t('user.phoneInvalid'), trigger: ['input', 'blur'] },
  email: { validator: (_r, v: string) => !v || /^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(v), message: () => t('user.emailInvalid'), trigger: ['input', 'blur'] },
}
interface UserForm {
  account: string
  password: string
  name: string
  nickname: string
  phone: string
  email: string
  gender: string | null
  avatar: string | null
  orgId: number | null
  positionId: number | null
  directorId: number | null
  enabled: boolean
  forceTotp: boolean
  totpEnabled: boolean
  roleIds: number[]
}
const blank = (): UserForm => ({
  account: '', password: '', name: '', nickname: '', phone: '', email: '',
  gender: null, avatar: null, orgId: null, positionId: null, directorId: null,
  enabled: true, forceTotp: false, totpEnabled: false, roleIds: [],
})
const form = reactive<UserForm>(blank())
const avatarUploading = ref(false) // 头像上传中:给上传按钮加 spinner,别让用户对着空白干等

function openAdd() {
  editingId.value = null
  Object.assign(form, blank())
  show.value = true
}
/** 编辑:先取 detail(拿 roleIds + 回显),成功再开弹层;失败弹码不开。 */
async function openEdit(r: UserItem) {
  try {
    const d = await userApi.detail(r.id)
    editingId.value = r.id
    Object.assign(form, {
      account: d.account, password: '', name: d.name,
      nickname: d.nickname ?? '', phone: d.phone ?? '', email: d.email ?? '',
      gender: d.gender ?? null, avatar: d.avatar ?? null,
      orgId: d.orgId ?? null, positionId: d.positionId ?? null, directorId: d.directorId ?? null,
      enabled: d.enabled, forceTotp: !!d.forceTotp, totpEnabled: !!d.totpEnabled,
      roleIds: d.roleIds ?? [],
    })
    show.value = true
  } catch (e) {
    message.error(translateError(e))
  }
}
defineExpose({ openAdd, openEdit })

async function save() {
  await formRef.value?.validate()
  try {
    if (editingId.value === null) {
      const body: AddUserInput = {
        account: form.account,
        password: form.password || undefined, // 留空 → 省略字段 → 后端生成随机强口令(明文经出参回传)
        name: form.name,
        nickname: form.nickname || null, phone: form.phone || null, email: form.email || null,
        gender: form.gender, avatar: form.avatar,
        orgId: form.orgId, positionId: form.positionId, directorId: form.directorId, enabled: form.enabled,
        forceTotp: form.forceTotp,
        roleIds: form.roleIds,
      }
      const out = await userApi.add(body)
      // 管理员没自己指定口令时,系统生成的随机口令只有这一次机会展示——不弹出来这个号谁也登不进去。
      // 交回父页用重置密码那套只读+复制弹层(同一语义:明文仅供管理员当场转达)。
      if (!form.password) emit('passwordGenerated', out.initialPassword)
    } else {
      const body: UpdateUserInput = {
        name: form.name,
        nickname: form.nickname || null, phone: form.phone || null, email: form.email || null,
        gender: form.gender, avatar: form.avatar,
        orgId: form.orgId, positionId: form.positionId, directorId: form.directorId, enabled: form.enabled,
        forceTotp: form.forceTotp,
        roleIds: form.roleIds,
      }
      await userApi.update(editingId.value, body)
    }
    message.success(t('user.saved'))
    emit('saved')
  } catch (e) {
    message.error(translateError(e))
    return false
  }
}
</script>

<template>
  <FormContainer
    v-model:show="show"
    :title="editingId === null ? t('user.addTitle') : t('user.editTitle')"
    :width="720"
    :on-confirm="save"
    :confirm-text="t('common.save')"
  >
    <!-- 两列栅格:相关字段成对排,头像整行(span 2);账号编辑时禁改并占整行 -->
    <n-form ref="formRef" :model="form" :rules="rules" label-placement="left" :label-width="96">
      <n-grid :cols="2" :x-gap="16">
        <n-form-item-gi :span="editingId === null ? 1 : 2" :label="t('user.account')" path="account">
          <n-input v-model:value="form.account" :disabled="editingId !== null" :placeholder="t('user.account')" />
        </n-form-item-gi>
        <n-form-item-gi v-if="editingId === null" :label="t('user.password')">
          <div class="pw-field">
            <n-input v-model:value="form.password" type="password" show-password-on="click" :placeholder="t('user.passwordHint')" />
            <PasswordStrength :value="form.password" />
          </div>
        </n-form-item-gi>
        <n-form-item-gi :label="t('user.name')" path="name">
          <n-input v-model:value="form.name" :placeholder="t('user.name')" />
        </n-form-item-gi>
        <n-form-item-gi :label="t('user.nickname')">
          <n-input v-model:value="form.nickname" :placeholder="t('user.nicknamePlaceholder')" />
        </n-form-item-gi>
        <n-form-item-gi :label="t('user.gender')">
          <DictSelect v-model:value="form.gender" type-code="gender" clearable :placeholder="t('user.genderPlaceholder')" />
        </n-form-item-gi>
        <n-form-item-gi :label="t('user.phone')" path="phone">
          <n-input v-model:value="form.phone" :placeholder="t('user.phonePlaceholder')" />
        </n-form-item-gi>
        <n-form-item-gi :label="t('user.email')" path="email">
          <n-input v-model:value="form.email" :placeholder="t('user.emailPlaceholder')" />
        </n-form-item-gi>
        <n-form-item-gi :label="t('user.org')">
          <OrgTreeSelect v-model:value="form.orgId" :placeholder="t('user.orgPlaceholder')" />
        </n-form-item-gi>
        <n-form-item-gi :label="t('user.position')">
          <n-select v-model:value="form.positionId" :options="props.positionOptions" clearable :placeholder="t('user.positionPlaceholder')" />
        </n-form-item-gi>
        <n-form-item-gi :label="t('user.director')">
          <n-select v-model:value="form.directorId" :options="props.directorOptions" clearable filterable :placeholder="t('user.directorPlaceholder')" />
        </n-form-item-gi>
        <n-form-item-gi :label="t('user.roles')">
          <n-select v-model:value="form.roleIds" :options="props.roleOptions" multiple clearable :placeholder="t('user.rolesPlaceholder')" />
        </n-form-item-gi>
        <n-form-item-gi :label="t('common.status')">
          <n-switch v-model:value="form.enabled" />
        </n-form-item-gi>
        <n-form-item-gi :label="t('user.forceTotp')">
          <n-switch v-model:value="form.forceTotp" />
        </n-form-item-gi>
        <n-form-item-gi v-if="editingId !== null" :label="t('user.totpBound')">
          <n-switch :value="form.totpEnabled" disabled />
        </n-form-item-gi>
        <n-form-item-gi :span="2" :label="t('user.avatar')">
          <div class="avatar-field">
            <n-avatar round :size="48" style="flex-shrink: 0" :src="form.avatar || undefined">
              <template #fallback>{{ initial(form.name) }}</template>
              <template v-if="!form.avatar">{{ initial(form.name) }}</template>
            </n-avatar>
            <FileUpload
              accept="image/*"
              :show-file-list="false"
              @loading-change="(v) => (avatarUploading = v)"
              @uploaded="(o) => (form.avatar = o.viewUrl ?? null)"
            >
              <n-button size="small" :loading="avatarUploading" :disabled="avatarUploading">
                <template #icon><AppIcon icon="ph:upload-simple" :size="14" /></template>
                {{ avatarUploading ? t('user.avatarUploading') : t('user.avatar') }}
              </n-button>
            </FileUpload>
          </div>
        </n-form-item-gi>
      </n-grid>
    </n-form>
  </FormContainer>
</template>

<style scoped>
/* 密码框 + 强度条竖排(表单为 label-left 横排,此格内需纵向堆叠) */
.pw-field {
  flex: 1;
  min-width: 0;
}
/* 头像:当前预览 + 上传按钮横排 */
.avatar-field {
  display: flex;
  align-items: center;
  gap: 12px;
}
</style>
