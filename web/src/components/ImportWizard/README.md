# ImportWizard

用户导入四步向导(也可注入其他档案的 api 复用):

1. **上传** — `n-upload` 拖拽 + 下载模板  
2. **列映射** — 左=文件表头,右=`n-select` 目标列(自动匹配预选)  
3. **预览改错** — **裸 `n-data-table`**(不用 ProTable):可编辑单元格、错误格红底 + `n-tooltip` 显示 `translateError(code)`、「只看错误行」、「重新校验」、重复策略  
4. **结果** — 计数;失败行可回到第③步;下载错误报告  

演示模式下 preview/validate/commit 会被 `DemoModeFilter` 拦成 41002 —— 组件经 `translateError` 给出可读提示(坑 4)。

## Props / Model

| 属性 | 类型 | 说明 |
|---|---|---|
| `show` | `boolean` (`v-model:show`) | 显隐;打开时重置状态 |
| `api` | `ImportWizardApi` | `downloadTemplate` / `preview` / `validate` / `commit` / `errorReport` |
| `templateFileName` | `string?` | 模板下载文件名,默认 `import-template.xlsx` |
| `errorReportFileName` | `string?` | 错误报告文件名,默认 `import-errors.xlsx` |

## Emits

| 事件 | 说明 |
|---|---|
| `done` | 提交有成功插入/更新时触发,父级 `refresh` 列表 |

## 用法(用户管理)

```vue
<ImportWizard
  v-model:show="importShow"
  :api="userImportApi"
  template-file-name="用户导入模板.xlsx"
  error-report-file-name="用户导入错误报告.xlsx"
  @done="() => tableRef?.refresh()"
/>
```

```ts
const userImportApi: ImportWizardApi = {
  downloadTemplate: () => userApi.importTemplate(),
  preview: (file, mapping) => userApi.importPreview(file, mapping),
  validate: (rows) => userApi.importValidate(rows),
  commit: (rows, strategy) => userApi.importCommit(rows, strategy),
  errorReport: (rows) => userApi.importErrorReport(rows),
}
```

权限:入口按钮 `v-auth="'POST:/api/v1/sys/user/import/preview'"`;模板下载走 `[ActiveSession]`,无需独立节点。
