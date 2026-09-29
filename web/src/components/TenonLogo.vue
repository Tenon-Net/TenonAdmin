<script setup lang="ts">
// 品牌仓库的透明版 PNG，亮暗主题共用。

const props = withDefaults(defineProps<{ size?: number }>(), { size: 28 })
</script>

<template>
  <img src="/tenon-mark.png" :width="props.size" :height="props.size" alt="Tenon" draggable="false" />
</template>
