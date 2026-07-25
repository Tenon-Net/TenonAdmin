# ExportColumnsModal

导出「选列」弹窗。列勾选,默认按 `defaultSelected`(缺省 `true`);确认后 `emit('confirm', keys)` 把勾选 Key(按档案声明顺序)交给父级,父级负责带**当前 ProTable 筛选条件**发导出请求并触发 blob 下载。

## Props / Model

| 属性 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `show` | `boolean` (`v-model:show`) | `false` | 显隐 |
| `columns` | `ExportColumnDef[]` | — | `{ key, title, defaultSelected? }` |
| `loading` | `boolean` | `false` | 导出请求中;锁关窗 + 确认钮 spinner |

## Emits

| 事件 | 载荷 | 说明 |
|---|---|---|
| `confirm` | `string[]` | 勾选的列 Key(档案顺序) |

## 用法

```vue
<ExportColumnsModal
  v-model:show="exportShow"
  :columns="userExportColumns"
  :loading="exporting"
  @confirm="onExport"
/>
```

父级 `@confirm` 里从 `tableRef.params` 取当前筛选,拼 `columns: keys.join(',')` 调 `userApi.export` / `logApi.opExport`,拿到 blob 后 `URL.createObjectURL` 触发下载。
