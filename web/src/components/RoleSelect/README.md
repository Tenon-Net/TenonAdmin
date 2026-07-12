# RoleSelect

角色选择器。基于 [`ApiSelect`](../ApiSelect/README.md),`fetch = roleApi.page`(角色名远程搜索)。只列**启用**角色,`label` = 角色名,`value` = 角色 `id`(`number`)。
多选常用(用户分配角色);`multiple`、`placeholder` 等经 `$attrs` 透传。

## Props

| prop | 类型 | 默认 | 说明 |
|---|---|---|---|
| `pageSize?` | `number` | `100` | 下拉一页容量。 |

## 用法

```vue
<RoleSelect v-model:value="form.roleIds" multiple :placeholder="t('user.rolePlaceholder')" />
```
