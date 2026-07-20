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

### 键名里不要有冒号

`locales/index.ts` 里设了 `nsSeparator: false`，所以含冒号的键**不会**被切成「命名空间:键」两半——权限码正是 `GET:/api/v1/x` 这个形状。但请不要依赖这一点去设计键名：这是为了让后端 msgKey 无论什么形状都取得到字，不是鼓励你用冒号分层。分层用 `.`。

### 键名指向的必须是**文案**，不能是子树

`t('error.auth')` 这种指向一整棵子树的键，i18next 会返回一句英文 debug 文本（`key 'error.auth' returned an object instead of string.`）而不是报错。错误提示那条路径用 `te()` 挡住了它（`te()` 对子树返回 false，语义对齐 Vue 侧），但你自己在页面里写 `t()` 时它挡不住你。

插值占位符是**单花括号** `{name}`（`t('workbench.welcome', { name })`）——文案沿用 Vue 模板的写法（vue-i18n 风格），`locales/index.ts` 里已把 i18next 的默认 `{{name}}` 改过来了。两个模板各自自包含，这份文案是本模板自己的真源。

解析不出值的花括号会**原样留着**，不会变空——`{0-9}`、`{}`、`.cls { color: red }` 都安全，只有能对上传入变量名的才被替换。

### 插值结果不要进 `dangerouslySetInnerHTML`

`locales/index.ts` 设了 `escapeValue: false`（i18next 默认转义是给 `innerHTML` 用的；React 自己已经转义，再来一遍会把用户名里的 `&` 显示成 `&amp;`）。代价是**插值结果本身不带任何转义**，所以它只能进 JSX 文本位置（`{t(...)}`）。别把它塞进 `dangerouslySetInnerHTML`，也别配 `<Trans shouldUnescape>`——那两条路上它就是一个 XSS 入口。

### 键值必须是字符串，不能是函数

i18next 支持函数值，但 `te()` 对函数一律返回 false（见 `locales/index.ts` 里 `te` 的注释：这是与 Vue 侧唯一有意不对齐的一格）。写成函数的话，错误提示那条路径会当作「没有文案」退回后端原文。

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
