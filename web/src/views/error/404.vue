<script setup lang="ts">
import { useRouter } from 'vue-router'
import { NButton } from 'naive-ui'
import { useAppStore } from '@/stores/app'

const router = useRouter()
const app = useAppStore()
</script>

<template>
  <div class="nf">
    <div class="code" :style="{ color: app.accent }">404</div>
    <div class="msg">页面不存在</div>
    <n-button type="primary" @click="router.replace('/')">返回工作台</n-button>
  </div>
</template>

<style scoped>
.nf {
  height: 100vh;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 16px;
  background: var(--color-bg-body);
}
.code {
  font-size: 96px;
  font-weight: 800;
  line-height: 1;
}
.msg {
  color: var(--color-text-secondary);
  font-size: var(--font-size-md);
}
</style>
