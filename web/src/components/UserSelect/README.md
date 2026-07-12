# UserSelect

人员选择器。基于 [`ApiSelect`](../ApiSelect/README.md),`fetch = userApi.page`(账号/姓名远程搜索 + 可选 `orgId` 部门过滤)。
选项 `label` 为「姓名(账号)」,`value` 为用户 `id`(`number`)。单/多选、`placeholder` 等经 `$attrs` 透传。

## Props

| prop | 类型 | 默认 | 说明 |
|---|---|---|---|
| `orgId?` | `number \| null` | — | 限定部门;变化时自动重拉。 |
| `pageSize?` | `number` | `50` | 下拉一页容量(搜索命中数上限)。 |

## 用法

```vue
<!-- 单选负责人 -->
<UserSelect v-model:value="form.ownerId" :placeholder="t('...')" />

<!-- 多选 + 限定部门 -->
<UserSelect v-model:value="form.memberIds" :org-id="form.orgId" multiple />
```
