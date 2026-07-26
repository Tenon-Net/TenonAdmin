# CronEditor(cron 可视化编辑器)

6 段秒级 cron(`秒 分 时 日 月 周`)的可视化编辑 + 表达式直填,语法与后端 `TenonAdmin.Core` 的
`CronExpression` 一一对应(日段 `L`/`L-n`/`LW`/`nW`、周段 `nL`/`n#m`、周 `0`=周日且 `7≡0`、
5 段输入自动升 6 段)。定时任务表单的 Cron 触发类型已落地。

## 契约

| Prop | 类型 | 说明 |
|---|---|---|
| `modelValue` | `string` | 表达式本体,`v-model` 双绑;空串 = 未填 |
| `previewCount` | `number = 5` | 预览条数(透传后端 `count`,上限 20) |

无插槽、无多余 emit——`update:modelValue` 是唯一出口,校验交给表单层(必填规则)与后端(47003)。

## 行为要点

- **7 个页签**:秒/分/时/日/月/周 六段 + 「表达式」直填。每段支持 每 · 区间 · 步长 · 指定值;
  日页签额外 月末(L / L-n)· 最近工作日(nW)· 月末工作日(LW);周页签额外 最后一个周几(nL)· 第 m 个周几(n#m)。
- **日/周互斥**:一侧编辑成受限值(非 `*`/`?`)时另一侧自动落 `?`——后端对"日周同限"直接拒 47003,
  与其让用户提交后撞墙,不如编辑时就替他落好。
- **预览**:表达式变化防抖 400ms 调 `POST /sys/job/preview-cron`(任何登录用户可用),显示归一化结果 +
  未来 N 次时刻;非法时显示后端 47003 文案;`everySecondWarning` 为真给"等效每秒执行"警告(不拦截)。
- **解析是尽力而为**:表达式直填时反解到各段页签;名字(`SUN-SAT`/`JAN-DEC`)、混写等复杂形态解析不动的,
  表达式本身不丢,继续在表达式页签编辑即可——可视化页签只覆盖常用形态,不是完备的双向映射。

## 用法

```vue
<script setup lang="ts">
import CronEditor from '@/components/CronEditor/index.vue'
const cron = ref('0 0 2 * * ?')
</script>

<template>
  <CronEditor v-model:model-value="cron" />
</template>
```

i18n 键在 `job.cron.*`(zh-CN / en-US 两份对齐)。
