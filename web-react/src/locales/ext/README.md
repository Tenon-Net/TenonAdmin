# locales/ext —— 消费者 i18n 扩展位

**这个目录是给你(消费者)的。上游不往这里写东西,所以你放的文件永远不会在 `git merge upstream` 时冲突。**

## 用法

按 `ext/<locale>/<模块>.ts` 放文件,默认导出该模块的键。文件名即顶层命名空间：

```ts
// src/locales/ext/zh-CN/sample.ts
export default {
  title: '标题',
  addTitle: '新增文档',
}
```

```ts
// src/locales/ext/en-US/sample.ts
export default {
  title: 'Title',
  addTitle: 'Add Document',
}
```

页面里照常 `const { t } = useTranslation()` 后 `t('sample.title')`。`locales/index.ts` 用 `import.meta.glob` 自动并入，**你不需要注册，也不需要改任何现成文件**。

插值占位符是**单花括号** `{name}`（`t('workbench.welcome', { name })`）——文案沿用 Vue 模板的写法（vue-i18n 风格），`locales/index.ts` 里已把 i18next 的默认 `{{name}}` 改过来了。两个模板各自自包含，这份文案是本模板自己的真源。

## 为什么不直接写进 `zh-CN.ts`

那两个文件是**上游自留地**（本模板自包含，它们就在 `src/locales/` 下，是真源）。你写进去 = 每次同步上游都在同一个文件里撞冲突。放这里 = 零冲突。同理你自己的 API 模块放 `api/<域>.ts`，自己的类型放 `types/<模块>.ts`。

## 给自己的错误码加文案 / 覆写内置文案

合并是**深合并**，所以你可以往内置命名空间的任意深度补键，而不会把兄弟键顶掉：

```ts
// src/locales/ext/zh-CN/error.ts
// 键要和后端 [MsgKey("error.doc.titleDuplicated")] 逐字对上,去掉 `error.` 前缀。
// translateError 只按 msgKey 取字,从不读数字 code —— 写成 { 50001: '...' } 是死文案,永远没人读。
export default {
  doc: { titleDuplicated: '文档标题重复' },
}
```

覆写内置文案同理，且只动你写的那一个键：

```ts
// src/locales/ext/zh-CN/error.ts —— captchaExpired 等 auth.* 兄弟键不受影响
export default {
  auth: { passwordWrong: '账号或密码不正确' },
}
```
