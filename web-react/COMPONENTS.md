# 组件使用约定

> 约定:后台不设组件演示菜单,组件用法统一沉淀在本文件。本模板自包含(不建 per-component README 树),契约直接写在这里 —— 与 `web/COMPONENTS.md` 的定位一致,但那边每行链到目录 README,这边没有目录树可链。

## DataTable / TreeTable(封 @ant-design/pro-components)

**隔离边界**:16 个 CRUD 页只依赖 `<DataTable>`/`<TreeTable>` + `toProTable`,不直接碰 `@ant-design/pro-components`(beta)的 API。将来 pro-components 换版/换库,只改 `src/components/DataTable/` 这一处。`DataTable` 是 fetcher(分页请求)模式,`TreeTable` 是静态树(无分页、无内建搜索表单)模式,两者对称、各管一种形态。

`<DataTable columns fetcher persistKey? rowKey? toolbar? rowSelection? onRowClick? activeRowKey? params?>`(`src/components/DataTable/DataTable.tsx`):

- **fetcher 契约**:`(params) => {items, total}`,`params` = `{page, pageSize, sortField?, sortOrder?, ...搜索字段}`。`toProTable(fetcher)`(`src/components/DataTable/toProTable.ts`)把它适配成 antd ProTable 的 `request(params, sort)` → `{data, success, total}` 契约:`current`→`page`、`{字段: 'ascend'|'descend'}`→`sortField`+`sortOrder`(固定映成 `'asc'/'desc'`,不管后端 `OrderBySafe` 目前只认 `desc`);只接单字段排序(多列排序不支持,天花板非遗漏);列级 filter 不支持,筛选一律走搜索表单。
- **工具栏文案**(列设置/密度/刷新/搜索/重置)由 pro-components 自带 intl 提供,跟随 `App.tsx` 的 `ConfigProvider locale` 自动切中英 —— **不新增 `proTable.*` i18n 键**(Vue 侧 Naive ProTable 那 8 个自留文案,这里用不上)。列标题等业务文案由各页 `columns` 的 `title` 自己 `t()`。
- **persistKey → storage-key 唯一性**:给了才持久化,落 `localStorage['protable:{persistKey}']`(`columnsState.persistenceKey`);命名约定 `{模块}-{页面}`,如 `sys-user`。**同一 key 在两处用会互相污染列设置**,新页面起名前检查是否与已有 persistKey 撞名。
- **rowSelection**:受控,原样透传给内层 `Table`;不给则无勾选列。批量删除页配 `useBatchDelete` 的 `selectedKeys`/`setSelectedKeys` 绑 `rowSelection={{selectedRowKeys, onChange}}`(范例:`role`)。
- **onRowClick / activeRowKey**:主从页左栏点行 → 右栏联动。给了 `onRowClick` 才有指针手型 + 点击态;行内交互控件(开关/按钮)的 render 里须自行 `stopPropagation`,否则点它会冒泡触发行点击。`activeRowKey` 命中的行套 `.data-table-active-row`(`DataTable.css`),做选中高亮(范例:`dict`)。
- **params**:透传给 ProTable 的 `params`,变化即自动 reload 回第 1 页,给侧栏/主从筛选用(如用户页左机构树 → `params={{orgId}}`)。ProTable 深比较,传新对象字面量不会自旋。
- **句柄**:`ref` 拿到的 `DataTableHandle` 只暴露 `reload()`(增删改后手动刷新),不外泄 pro-components 的 `ActionType`。

`<TreeTable columns data loading? rowKey? expandedRowKeys onExpandedRowKeysChange toolbar? persistKey?>`(`src/components/DataTable/TreeTable.tsx`):

- **静态树模式**:`data` 是已 `buildTree`(`src/utils/tree.ts`)拼好 `children` 的树,无 `request`/无分页/`search={false}`,antd Table 按 `childrenColumnName='children'` 自动嵌套渲染;树列须排列首位(第一列自动承载展开箭头)。
- **展开受控**:`expandedRowKeys`/`onExpandedRowKeysChange` 必须由调用方管理 —— antd 的 prop 名是 `onExpandedRowsChange`(内部已对齐,外部只认这两个)。**默认全展开**由调用方自己播种 `expandableIds(tree)`;`data` 变化(搜索/切筛选)后须重算,否则命中结果藏在折叠的祖先里看不见。
- 工具栏(搜索框/展开折叠/新增)由各页自管,传入 `toolbar`;树表无分页,pro-components 自带搜索卡片会白占一整块高度,故未接。
- `persistKey` 同 `DataTable`;`options` 固定关掉 `reload`/`fullScreen`(无 `request` 时二者无意义),`density` 保留(树行高本就紧凑)。

范例页:`system/user`(标准列表 + 排序)、`system/org` / `system/menu`(TreeTable)、`system/dict`(主从 + activeRowKey/onRowClick)、`system/role`(rowSelection + 批量删除)。

## 组件索引

| 组件 | 定位 | 关键契约 |
|---|---|---|
| FormContainer | 弹窗/抽屉二合一表单容器 | `open/onOpenChange/title/variant?/width?/onConfirm?/confirmText?/cancelText?/maskClosable?`。`variant` 不传则跟随全局偏好 `app.formStyle`(设置抽屉可切)。**onConfirm 协议**(`shouldCloseAfterConfirm`):不传 → 直接关;`resolve(false)` → 不关(校验失败放 onConfirm 首行 `return false`);抛错 → 不关(错误 toast 由业务自己弹);其余(含 `resolve(true)`/`resolve(undefined)`)→ 关。确认钮自动 loading,提交中锁死 esc/遮罩/关闭钮/取消钮防中途关闭。宽度取 `min(width ?? 560, 90vw)`。 |
| StatusSwitch | 表格行内启停开关 | `value/request/confirm?/disabled?/size?/successMsg?/onChange?`。**悲观更新**:`checked` 绑父传 `value`,点击不先翻转,`request(next)` 成功才 `onChange(next)`;失败不 `onChange` = UI 保持原值即回滚,零成本。`confirm` 给了则先二次确认(函数形态按目标态出文案,返回空值本次跳过确认)。loading 期间自锁防连点。 |
| DictSelect | 字典下拉 | `{typeCode, ...rest}`,`rest` 全透传 antd `Select`。经 `stores/dict` 缓存 + 并发去重取数,过滤停用项(`toDictOptions`)。无 loading 圈(ponytail:字典通常极小且缓存命中,真遇到慢字典再从 store 引出在途态)。 |
| DictTag | 表格列字典翻译 + 语义色标签 | `{typeCode, value, typeMap?}`。空值(`null`/`undefined`/`''`)渲染 `—`;命中字典按启用项排序下标 `i%4` 取 `[processing, success, warning, error]`(`typeMap` 显式映射优先);未命中(未加载完/脏值)渲染原始 value、色 `default`,不空白不闪烁。 |
| OrgTreeSelect | 机构树下拉 | `{excludeSubtreeOf?, ...rest}`,`rest` 全透传 antd `TreeSelect`。挂载拉 `orgApi.list()`(平铺)→ `buildTree` 拼树。`excludeSubtreeOf` 剪自身子树,防把机构挂到自己后代下成环(编辑机构选上级用;用户页传 `null` 不剪)。失败静默留空(机构是配角,不打断主流程)。 |
| FileUpload | 封 antd `Upload` 的 `customRequest` | `{chunked?, onUploaded?, onLoadingChange?, ...rest}`。内部按 `chunked` 走 `utils/chunkUpload`(分片/断点续传/秒传)或 `fileApi.upload`(整文件直传,自动带 Bearer);成功 `onUploaded(out)`;`onLoadingChange` 在真正开始/结束各触发一次(try/finally 兜底),给触发器加"上传中"反馈;失败经 `App.useApp().message.error` 弹错并走 antd 的 `onError`。`accept`/`multiple`/`showUploadList` 等经 rest 透传。 |
| PasswordStrength | 密码强度条 + 规则清单 | `{value}`(密码明文,空则不渲染)。自包含:内部 `useEffect` 拉 `configApi.passwordPolicy()`,失败留默认策略(后端仍强制)。规则清单按有效策略动态构建(大小写/数字仅策略要求时作硬规则,特殊字符按策略要求分「硬规则/可选提示」);强度档位:长度不达标或字符种类数 ≤1 → 弱,种类数=2 或长度<12 → 中,否则强。改密页 / 建用户页共用。 |
| ApiSelect | 通用远程分页下拉基座 | `{fetch, remote?, immediate?, debounce?, reloadOn?, ...rest}`。`fetch(keyword)` 回归一选项(数组或 `{items}`,`normalizeOptions` 归一)。`remote`(默认 true)每次输入(防抖 `debounce` ms,默认 300)调 `fetch`;非 remote 只加载一次交 Select 客户端过滤。`immediate`(默认 true)挂载即以空关键字预取一页。`reloadOn` 变化(如部门过滤)触发重新加载。内部竞态守卫(`seq`)保证只认最新一次在途请求;失败留空、不外抛。`UserSelect` 基于此。 |
| UserSelect | 人员选择器(下拉,非弹窗) | `{orgId?, pageSize?, ...rest}`,基于 `ApiSelect`:`fetch` = `userApi.page({name: keyword, orgId})`,`reloadOn={orgId}`。选项 label 为「姓名(账号)」,value=id。 |
| UserPicker | 人员选择弹窗(左机构树/中可选用户表/右已选列表) | `forwardRef` 暴露 `open(ids?)`(命令式,预选回显)。Props:`{excludeIds?, onConfirm}`。固定 `modal` 形态(三面板不适合抽屉);中间表用 `<DataTable>`;**本版只保留每行「添加」,不做批量勾选**(ponytail:待批量删除页需要它时统一补)。`open(ids)` 回显对翻页之外找不到的用户退回只显 id 的占位行(`hydrateSelected`)。 |
| Can | 按钮级权限门 | `{code, every?, children}`,替代 Vue 侧 `v-auth` 指令(React 无指令,用组件包裹)。`code` 单码或数组;多码默认 `some`(OR),`every` 传 true 则 `every`(AND)。判定收敛在 `stores/auth` 的 `hasPerm`(超管 fail-open / 未加载 fail-closed / 精确命中),与 Vue 侧同一判定逻辑。服务端始终是权威,这里只拨显隐。 |
| MarkdownEditor / MarkdownView | 通知公告 Markdown 编辑/只读渲染(封 `md-editor-rt`) | `MarkdownEditor: {value?, onChange?}`;`MarkdownView: {value?}`。跟随应用明暗主题。`noKatex/noMermaid/noHighlight(/noPrettier)` 全关 —— md-editor-rt 默认会从 unpkg 懒加载这些扩展,破坏气隙自包含;通知正文用不到公式/图表。`MarkdownEditor` 的图片上传插的是后端签发的签名直链 `viewUrl`(不是 `storagePath`,后端默认不静态托管上传目录)。全局 XSS 过滤由 `main.tsx` 首次渲染前挂上。 |
| Chart | ECharts 基础封装 | `{option, height?, loading?, className?, style?}`。裸 `echarts/core` `init`(不引 `vue-echarts` 类 wrapper),跟随应用明暗/accent 重建主题(dispose 旧图、以新主题 init,因 echarts 主题在 init 时固化,并把当前 `loading` 态补回新实例)。图种按需注册在 `src/lib/echarts`。 |
| CodeBlock | 代码/JSON 只读展示 + 复制 | `{code, language?, wordWrap?, copyable?}`。`code` 是调用方已格式化好的文本(如 `JSON.stringify(v,null,2)`);已注册语言(当前仅 `json`)走 `hljs`,未注册语言降级为**转义后**的纯文本(`escapeHtml`,防 XSS,`dangerouslySetInnerHTML` 的安全边界)。配色走 `src/styles/code.css`,不引 hljs 自带 css。 |
| DetailPage | 详情页外壳 | `{title?, loading?, showBack?, onBack?, actions?, children}`。返回按钮 + 标题 + actions + body(`Spin` 包 loading)。`onBack` 语义交父级:路由态关标签回列表 / 就地态清详情状态。`views/**/detail.tsx` 自动注册为 `/<路径>/:id/detail`,默认 `noCache`,补偿非菜单详情路由的空面包屑。 |
| AppIcon | tenon 图标渲染器 | `{icon?, size?, fallback?, className?, style?}`。`@iconify/react/offline` 薄封装 + 空值兜底(`icon || fallback`,用 `||` 不用 `??`——空串也要退兜底,`??` 只兜 null/undefined)。离线集由 `lib/icons.setupIcons` 注册,渲染任意 iconify 串(`ph:*`/`lucide:*`/`ep:*`/`ant-design:*`)全离线、不打 `api.iconify.design`。 |
| IconPicker | 轻量内联图标选择器(离线) | `{value?, onChange?, placeholder?, clearable?}`。触发器 + 弹窗(搜索 + 集合 Tab + 图标网格)。值契约 `prefix:name`(如 `ph:folder`)或 `local:name`,空串=未选。**不做在线搜索**(气隙自包含约定)。单页可见上限 `CAP=300`,超出提示继续输入缩小范围,避免一次渲染上千 `<Icon>`。`value`/`onChange` 声明可选以便直接放进 antd `Form.Item`。 |
| TenonLogo | 品牌徽标(透榫) | `{size?}`。内联 SVG,明暗跟随 `app` store;固定品牌色 `#646CFF`,不随 accent 变。 |
| ImportWizard | 导入四步向导(`Steps`):上传 → 列映射 → 预览改错(**裸 antd `Table`**,可编辑+错误红底 tooltip,勿塞 `DataTable`)→ 结果;api 注入,用户管理已落地。错误格底色用 `--color-danger-bg`(坑 12)。**「已存在」(46010)按重复策略呈现**(警示色 `--color-warning-bg`,不是硬错误)—— 判定在 `src/utils/importDup.ts`。 |
| ExportColumnsModal | 导出选列弹窗;默认按 `defaultSelected` 勾选,确认后父级带 **当前筛选** 请求 blob 下载;用户管理 / 操作日志已落地。 |

## hooks 约定

`src/hooks/useConfirm.ts` —— 二次确认 + 结果 toast,**仅限组件内调用**(依赖 `App.useApp()`)。`const {ask, confirm, run} = useConfirm()`:

- `ask({content, title?}) => Promise<boolean>`:只弹确认框不执行,取消/关闭/遮罩/Esc 均 `false`。给需自管后续流程的组件用(`StatusSwitch` 即基于它)。
- `confirm({content, title?, action, successMsg?}) => Promise<boolean>`:确认后执行 `action`,成/败统一 toast。**busy 守卫**:antd `modal.confirm({onOk})` 只自动锁 OK 钮,不锁取消钮/Esc;`onOk` 一启动即 `instance.update` 禁用取消钮 + 关键盘,防止请求在途时用户取消导致「已成功但表格没刷新」。给不适合内联的重操作(批量删除等)。
- `run(action, successMsg?) => Promise<boolean>`:不弹框的后半段(执行 → toast),配触发器旁的内联确认(如 `Popconfirm`)用。
- `successMsg: false` 关掉成功 toast。

`src/hooks/useBatchDelete.ts` —— 表格批量删除样板收敛,内部用 `useConfirm`。`useBatchDelete({remove, refresh, successMsg?, content?})` 返回 `{selectedKeys, setSelectedKeys, hasSelection, run}`;`selectedKeys`/`setSelectedKeys` 绑 `<DataTable>` 的受控 `rowSelection`,`run()` 二次确认后执行 `remove(ids)`,仅成功才清选 + `refresh()`(失败保留勾选让用户重试)。`content` 可选、可异步(删前先查依赖关系再把话说清)。范例:`system/role`。

## 外链 / iframe 菜单(约定式,零后端字段新增)

判据 `isHttpUrl`(`src/utils/url.ts`),逻辑落在 `menuToRouteDescriptors`(`src/router/menuRoutes.ts`),与 Vue 侧 `useAuthMenu.buildRoutesForModule` 规则对齐:

- **外链**:节点 `path` 为 `http(s)://…`(`component` 空)→ 不建路由,点击时 `window.open`(布局侧菜单处理)。
- **内嵌 iframe**:节点 `component` 为 `http(s)://…`(`path` 为内部路径)→ 产出 `kind:'iframe'` 描述符,`iframeSrc` 落 `node.component`;`buildRoutes.tsx` 把它渲染成通用视图 `src/views/embed/iframe.tsx`(`<iframe src>` 撑满容器)。
- 组件缺失(`component` 指向不存在的视图键)不会静默消失菜单项,而是产出 `kind:'missing'` 描述符 + `console.warn`。

## 水印(antd 内建,不自研)

`src/layouts/LayoutShell.tsx` 用原生 antd `<Watermark content={...}>` 包内容区,`content` 由 `app.watermark` 开关 + `app.watermarkText` 拼「用户名 · 自定义文本」;`content` 恒挂、开关关闭时传空串(不 remount 内容区,否则 KeepAlive 缓存全丢)。局部水印同法直接用 `<Watermark>`。

## 可加但先不加(设计已备案,别提前造)

- ApiSelect/DictSelect/UserSelect 的 loading 圈;UserPicker 中间表批量勾选(待批量删除页需要时统一补);FormContainer 的 `onBeforeClose`;StatusSwitch 泛型值/乐观模式。
