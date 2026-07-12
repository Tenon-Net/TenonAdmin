# 组件使用约定

> 约定:后台不设组件演示菜单,组件用法统一沉淀在本文件。完整 API 见各包 README。

## ProTable(tenon-naive-pro-table)

列驱动表格:`columns` 数组同时驱动搜索表单、字典渲染与列设置;`fetcher` 是唯一后端契约。
完整文档:https://github.com/Tenon-Net/tenon-naive-pro-table/blob/main/README.zh-CN.md

tenon 内接入约定:

- **fetcher**:直接传 `xxxApi.page`,api 层负责把后端 `PagedList{current,size,total,items}` 归一成 `{items,total}`、把 `{page,pageSize}` 映射成 `{Current,Size}`(见 `src/api/index.ts` 的 `userApi.page`)。
- **labels**:用 `useProTableLabels()`(`src/composables/useProTableLabels.ts`),查询/重置/密度复用 `common.*`/`app.*` 键,其余在 locale 的 `proTable.*`。
- **列标题必须函数形式** `title: () => t('...')`,切语言即时生效(教训:提交 308a361)。
- **错误处理留在视图层**:`@error="(e) => message.error(translateError(e))"`,包内不弹 UI。
- **storage-key 命名**:`{模块}-{页面}`,如 `sys-user`;列设置与密度按此键持久化到 localStorage(`protable:` 前缀)。
- **本地联调**:`NPT_LOCAL=1 npm run dev` 直连兄弟仓库源码(见 vite.config.ts),回路同图标包(改 → 发补丁版 → bump)。

范例页:`src/views/system/user/index.vue`。

## 自研通用组件索引

**每组件详细 API 见其目录 README**;新通用组件一律「目录 + `index.vue` + `README.md`」;composable/store 的说明写在源码头注释,本文件只留索引与一句话定位。

| 组件 | 定位 | README |
|---|---|---|
| FormContainer | 弹窗/抽屉二合一表单容器;onConfirm 协议接管 loading/关闭,全局形态可在设置抽屉切换 | `src/components/FormContainer/README.md` |
| StatusSwitch | 表格行内启停开关;悲观更新,失败自动回滚 | `src/components/StatusSwitch/README.md` |
| DictSelect | 字典下拉,`$attrs` 全透传 n-select | `src/components/DictSelect/README.md` |
| DictRadio | 字典单选按钮组(搜索栏互斥条件) | `src/components/DictRadio/README.md` |
| DictTag | 表格列字典翻译 + 语义色标签 | `src/components/DictTag/README.md` |
| OrgTreeSelect | 机构树下拉;拉 `org/list` 平铺 → `utils/tree.buildTree` 拼树,`$attrs` 透传 n-tree-select;`excludeSubtreeOf` 剪自身子树防成环 | `src/components/OrgTreeSelect/README.md` |
| FileUpload | 封 n-upload `custom-request`;内部 `fileApi.upload` 自动带 Bearer,成功 `emit('uploaded', out)`;`$attrs` 透传(accept/multiple/show-file-list)。`chunked` 走分片/断点续传/秒传(`utils/chunkUpload`,进度回 n-upload) | `src/components/FileUpload/README.md` |
| PasswordStrength | 密码强度条 + 规则清单;自包含,`:value` 传密码明文,内部拉当前生效密码策略动态构建规则(改密页 / 建用户页共用) | `src/components/PasswordStrength/README.md` |
| ApiSelect | 通用远程分页下拉基座;`fetch(keyword)` 回归一选项,管加载/远程搜索防抖/竞态/loading,`$attrs` 透传 n-select | `src/components/ApiSelect/README.md` |
| UserSelect | 人员选择器;基于 ApiSelect,`userApi.page` 搜索 + 可选 `orgId` 部门过滤,label 为「姓名(账号)」 | `src/components/UserSelect/README.md` |
| RoleSelect | 角色选择器;基于 ApiSelect,`roleApi.page` 搜索,只列启用角色,多选常用 | `src/components/RoleSelect/README.md` |
| DictCheckbox | 字典多选(复选框组);同 DictSelect/DictRadio 基座,`v-model:value` 为数组 | `src/components/DictCheckbox/README.md` |
| JsonEditor | JSON 值编辑薄版(零依赖):textarea + 实时校验 + 格式化;仅用于约定为 JSON 的字段,`expose.valid()` 供提交前校验 | `src/components/JsonEditor/README.md` |
| MarkdownEditor / MarkdownView | 通知公告 Markdown 编辑/只读渲染(封 md-editor-v3);存 Markdown 纯文本,跟随明暗主题;通知页已落地 | `src/components/MarkdownEditor/README.md` |
| Chart(+ LineChart/BarChart/PieChart) | ECharts 封装(封 vue-echarts);自动跟随明暗主题/accent、按需注册图种、自带 autoresize;预设传 data、BaseChart 传 option;工作台已落地 | `src/components/Chart/README.md` |

字典三件套的数据基座是 `src/stores/dict.ts`(按 typeCode 缓存 + 并发去重;字典管理操作后调 `invalidate()`),页面拿原始选项用 `useDictOptions(typeCode)`。范例页:`src/views/system/menu/index.vue`、`module/index.vue`(FormContainer + useConfirm + StatusSwitch 完整落地)。

## useConfirm(二次确认 + 结果 toast,composable)

`src/composables/useConfirm.ts`,**仅限 setup 中调用**(依赖 Dialog/Message Provider)。`const { ask, confirm, run } = useConfirm()`:

- `run(action, successMsg?) => Promise<boolean>`:执行 → 成/败 toast。配模板层 `n-popconfirm` 用(popconfirm 当触发器,后半段交给 run):
  ```ts
  onPositiveClick: () => run(() => xxApi.remove(r.id), t('xx.deleted')).then((ok) => { if (ok) load() })
  ```
- `confirm({ content, title?, type?, action, successMsg? }) => Promise<boolean>`:先弹 dialog、**action 在 dialog 挂起期间执行**(确认钮 loading,执行中锁死取消/Esc/遮罩,防连点重复执行);给不适合内联的重操作(批量删除等)。取消/关闭/Esc/失败均 false;`successMsg: false` 关掉成功 toast。
- `ask({ content, title?, type? }) => Promise<boolean>`:仅确认不执行,给需要自管后续流程的组件用(StatusSwitch 即基于它)。Esc/遮罩/取消均 false。

## 数字动画 / 水印(用 Naive 内建,不自研)

- 数字动画:`<span class="tabular"><n-number-animation :from="0" :to="n" show-separator /></span>`;`.tabular` 防滚动抖宽(styles/index.css),前后缀直接写旁边。
- 水印:全局水印已内建(`layouts/default.vue` + 设置抽屉开关,内容默认当前用户名);局部包 `<n-watermark content="…" cross>` 即可。

## 可加但先不加(设计已备案,别提前造)

- **CodeBlock**(代码展示):Naive `NCode` + `highlight.js/lib/core` 按需注册语言(hljs 已是 naive-ui 传递依赖,显式声明零新下载),配色走 Naive 主题不引 hljs css;等出现日志详情/配置 JSON 页再落地。
- FormContainer size 档位 / `onBeforeClose`;useConfirm 返回结果值版;StatusSwitch 泛型值/乐观模式;DictSelect 展示禁用项;dict 缓存 TTL/SWR;CountTo 自研包装(NNumberAnimation 不够用再说)。

## IconPicker / AppIcon(tenon-naive-iconify-picker)

离线优先图标选择与渲染,初始化与包装见 `src/lib/icons.ts` 顶部注释、`src/components/IconPicker/index.vue`、`src/components/AppIcon.vue`。
