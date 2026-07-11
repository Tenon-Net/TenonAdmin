# PasswordStrength

密码强度提示条 + 规则清单。**自包含**:传入密码字符串即可,组件内 `onMounted` 拉后端当前生效密码策略(`configApi.passwordPolicy`,超管在「安全策略」可改),据此动态构建规则清单,精确同步不漂移。空密码时不渲染。

## 用法

```vue
<n-input v-model:value="model.password" type="password" />
<PasswordStrength :value="model.password" />
```

| Prop | 类型 | 说明 |
|---|---|---|
| `value` | `string` | 待评估的密码明文;为空则整体不渲染 |

## 说明

- 强度:字符种类数 + 长度达标 → 弱/中/强三档色条。
- 规则清单按策略动态:恒显最小长度;大小写/数字仅策略要求时作硬规则;特殊字符按策略在「硬规则/可选提示」间切换。
- 拉策略失败静默回退默认策略(后端始终强制,弱口令以 `passwordTooWeak` 兜底)。
- i18n 键沿用 `changePassword.strength.*` / `changePassword.rules.*`。
