# 组件使用约定

> 约定:后台不设组件演示菜单,组件用法统一沉淀在本文件。完整 API 见各包 README。

## ProTable(tenon-naive-pro-table)

列驱动表格:`columns` 数组同时驱动搜索表单、字典渲染与列设置;`fetcher` 是唯一后端契约。
完整文档:https://github.com/Tenon-Net/tenon-naive-pro-table/blob/main/README.zh-CN.md

tenon 内接入约定:

- **fetcher**:直接传 `xxxApi.page`,api 层负责把后端 `PagedList{current,size,total,items}` 归一成 `{items,total}`、把 `{page,pageSize}` 映射成 `{Current,Size}`(见 `src/api/index.ts` 的 `userApi.page`)。
- **labels**:**全局注入,页面不再手传**。`main.ts` 用 `app.provide(PRO_TABLE_DEFAULTS, createProTableDefaults({ labels: computed(...) }))` 一次接上 i18n(键仍在 locale 的 `common.*`/`app.*`/`proTable.*`),切语言即时生效。要覆盖单页文案才传 `:labels`。
- **列标题必须函数形式** `title: () => t('...')`,切语言即时生效(教训:提交 308a361)。
- **错误处理留在视图层**:`@error="(e) => message.error(translateError(e))"`,包内不弹 UI。
- **storage-key 命名**:`{模块}-{页面}`,如 `sys-user`;列设置与密度按此键持久化到 localStorage(`protable:` 前缀)。
- **树形页(org / menu)**:静态数据模式 —— `:data="tree"`(行带 `children`)+ `row-key="id"` + `:pagination="false"`,树列设 `align:'left'`;新增/筛选/搜索控件放 `#toolbar`。
  - **列的取舍**:树列 `minWidth:220 + fixed:'left'`、操作列 `fixed:'right'`(`scrollX` 由包内按 `sum(width ?? minWidth ?? 120)` 自动算并绑给 `n-data-table`,无需手传 `scroll-x`);文本列一律 `ellipsis:{tooltip:true}`,否则长路径换行会把行撑高、行高参差。**操作最多留 2 个**(编辑 + `n-dropdown` 更多▾),4 个平铺在 260~300px 里必换行且横向滚动时够不着 —— 这是 org/menu 都踩过的坑。下拉项里的删除用 `useConfirm().confirm`(dialog),`n-popconfirm` 是内联触发器,塞不进 dropdown。
  - **别加恒空列**:菜单树剥掉按钮后只剩目录/页面,而权限码只挂按钮 → 「权限码」列 100% 是「—」。同理关键字过滤跑在剥离后的树上,写 `n.permission` 永不命中,要按权限码搜得查节点的按钮子节点(见 `menu/index.vue` 的 `buttonInfoById`)。
  - **搜索自己算**:静态 `:data` 模式下 ProTable **不做任何前端过滤**(列的 `search` 配置只渲染搜索表单 + emit),所以关键字过滤走 `computed` + `utils/tree.ts` 的 `filterTree`(命中节点保留整棵子树,未命中但有后代命中的节点作为祖先链保留)。关键字放 `#toolbar` 的 `n-input`,别用 `search` 列配置——树表没分页,搜索卡片白占一整块高度。
  - **展开要受控**:`:expanded-row-keys` + `@update:expanded-row-keys`(不是 ProTable 的 prop,靠 `inheritAttrs:false + v-bind="attrs"` 透传给 `n-data-table`,和 `:loading` 同理)。**受控后必须删掉 `default-expand-all`** —— naive 里受控值优先,两者并存会让初始 `[]` 把"默认全展开"直接覆盖成全折叠;"全展开"改由 `expandableIds(tree)` 播种。data 变了受控 keys 不会自动跟着变,搜索后要重算,否则命中结果藏在折叠的祖先里。
  - **行内写值的坑**:`filterTree` 剪枝时,"仅因后代命中而保留"的祖先是浅拷贝。搜索态下往行对象上写值(`r.enabled = v`)写的是副本,不回源树 → 开关会弹回去。行内变更后**重拉**(`load()`)而不是本地写回;`StatusSwitch` 是悲观更新(请求成功才 emit),重拉即最终态。
- **主从选中(dict)**:`:active-row-key` + `@row-click` 做行高亮/选中(勿再 `:deep(> td)`);行内交互控件(开关/按钮)的 render 里要 `stopPropagation`,否则点它会冒泡触发 `@row-click`。
- **窄栏搜索(dict)**:`:search="{ layout: 'inline' }"` 无卡片单行,配合列 `search: true`。
- **排序(user)**:列写 `sorter: true` → 点表头把 `{ sortField, sortOrder }` 并进 fetcher;**api 层要把它们映射成后端 `SortField/SortOrder` query**(见 `userApi.page`/`positionApi.page`)。后端按实体列白名单安全排序(非法字段忽略回退默认,`PagedListExtensions.OrderBySafe`),字段名 = 实体属性名(大小写不敏感)。
- **行排序(position)**:岗位顺序用**可编辑 `Sort` 字段**——编辑弹窗一个 `n-input-number`,列表默认 `OrderBy(Sort)`,用户表单的岗位下拉也继承此序。ProTable 本身有 `row-draggable`(`drag-handle` + `@row-drag-sort`,sortablejs 懒加载)这个能力,但本项目**未接线**:没有 `positionApi.reorder`,后端也无 `POST /sys/position/reorder` 端点。要真拖拽排序,需先补该端点(按序赋 Sort)再启用手柄——对一个极少改动的小列表,数字框已够,故未做(见 `docs/refinement-ledger.md` E1)。
- **搜索折叠**:`:search="{ collapsible: true }"`(仅 grid 布局,搜索项多时才用)。
- **逃生口 slot**:`#toolbar-right`(工具栏右侧)、`#header-{key}`、`#empty`、`#pagination-prefix`;全局默认(align/pageSizes/emptyText/tag 等)也走 `PRO_TABLE_DEFAULTS`,免为小调整发包。
- **已能用(透传)**:列宽拖拽(列 `resizable`)、虚拟滚动(`:virtual-scroll`+`max-height`)、合计行(`:summary`)、合并单元格(列 `rowSpan/colSpan`)——经 attrs/列透传,无需新 API。
- **本地联调**:`NPT_LOCAL=1 npm run dev` 直连兄弟仓库源码(见 vite.config.ts),回路同图标包(改 → 发补丁版 → bump)。排序/拖拽/折叠这些能力需 `^0.3.1`(0.3.0 的行拖拽在 fetcher 模式下**从不生效**:Sortable 只在 onMounted 绑一次,而空表时 naive 根本没渲染 tbody);列排序依赖后端 `SortField/SortOrder`(行拖拽是纯前端能力,本项目未接线,见上),改后端后 `npm run gen:api` 重生成 schema。

范例页:`src/views/system/user/index.vue`(标准列表 + 排序)、`position`(可编辑 Sort 排序)、`org`/`menu`(树)、`dict`(主从 + inline 搜索)。

## 自研通用组件索引

**每组件详细 API 见其目录 README**;新通用组件一律「目录 + `index.vue` + `README.md`」;composable/store 的说明写在源码头注释,本文件只留索引与一句话定位。

| 组件 | 定位 | README |
|---|---|---|
| FormContainer | 弹窗/抽屉二合一表单容器;onConfirm 协议接管 loading/关闭,全局形态可在设置抽屉切换 | `src/components/FormContainer/README.md` |
| StatusSwitch | 表格行内启停开关;悲观更新,失败自动回滚 | `src/components/StatusSwitch/README.md` |
| DictSelect | 字典下拉,`$attrs` 全透传 n-select | `src/components/DictSelect/README.md` |
| DictTag | 表格列字典翻译 + 语义色标签 | `src/components/DictTag/README.md` |
| OrgTreeSelect | 机构树下拉;拉 `org/list` 平铺 → `utils/tree.buildTree` 拼树,`$attrs` 透传 n-tree-select;`excludeSubtreeOf` 剪自身子树防成环 | `src/components/OrgTreeSelect/README.md` |
| FileUpload | 封 n-upload `custom-request`;内部 `fileApi.upload` 自动带 Bearer,成功 `emit('uploaded', out)`,上传起止各 `emit('loadingChange', bool)`(try/finally 兜底,给触发器加"上传中"spinner);`$attrs` 透传(accept/multiple/show-file-list)。`chunked` 走分片/断点续传/秒传(`utils/chunkUpload`,进度回 n-upload) | `src/components/FileUpload/README.md` |
| PasswordStrength | 密码强度条 + 规则清单;自包含,`:value` 传密码明文,内部拉当前生效密码策略动态构建规则(改密页 / 建用户页共用) | `src/components/PasswordStrength/README.md` |
| ApiSelect | 通用远程分页下拉基座;`fetch(keyword)` 回归一选项,管加载/远程搜索防抖/竞态/loading,`$attrs` 透传 n-select | `src/components/ApiSelect/README.md` |
| UserSelect | 人员选择器;基于 ApiSelect,`userApi.page` 搜索 + 可选 `orgId` 部门过滤,label 为「姓名(账号)」 | `src/components/UserSelect/README.md` |
| MarkdownEditor / MarkdownView | 通知公告 Markdown 编辑/只读渲染(封 md-editor-v3);存 Markdown 纯文本,跟随明暗主题;通知页已落地 | `src/components/MarkdownEditor/README.md` |
| Chart(+ LineChart/PieChart) | ECharts 封装(封 vue-echarts);自动跟随明暗主题/accent、按需注册图种、自带 autoresize;预设传 data、BaseChart 传 option;工作台已落地 | `src/components/Chart/README.md` |
| CodeBlock | 代码/JSON 只读展示;NCode + `hljs/lib/core` 按需注册(现仅 json),复制按钮 + 自动换行,配色随 Naive 主题;操作日志详情已落地 | `src/components/CodeBlock/README.md` |
| DetailPage | 详情页外壳:返回 + 标题 + actions/body 插槽;`@back` 交父级(路由态关标签回列表 / 就地态清状态),补偿非菜单详情路由的空面包屑;配 `useTabTitle` 设动态标签标题。用法/骨架见 `skills/create-page-variant.md` 变体四 | `src/components/DetailPage/README.md` |
| ImportWizard | 导入四步向导(`n-steps`):上传 → 列映射 → 预览改错(**裸 `n-data-table`**,可编辑+错误红底 tooltip)→ 结果;api 注入,用户管理已落地 | `src/components/ImportWizard/README.md` |
| ExportColumnsModal | 导出选列弹窗;默认按 `defaultSelected` 勾选,确认后父级带 **ProTable 当前筛选** 请求 blob 下载;用户管理 / 操作日志已落地 | `src/components/ExportColumnsModal/README.md` |

字典三件套的数据基座是 `src/stores/dict.ts`(按 typeCode 缓存 + 并发去重;字典管理操作后调 `invalidate()`),页面拿原始选项用 `useDictOptions(typeCode)`。范例页:`src/views/system/menu/index.vue`、`module/index.vue`(FormContainer + useConfirm + StatusSwitch 完整落地)。

## useConfirm(二次确认 + 结果 toast,composable)

`src/composables/useConfirm.ts`,**仅限 setup 中调用**(依赖 Dialog/Message Provider)。`const { ask, confirm, run } = useConfirm()`:

- `run(action, successMsg?) => Promise<boolean>`:执行 → 成/败 toast。配模板层 `n-popconfirm` 用(popconfirm 当触发器,后半段交给 run):
  ```ts
  onPositiveClick: () => run(() => xxApi.remove(r.id), t('xx.deleted')).then((ok) => { if (ok) load() })
  ```
- `confirm({ content, title?, type?, action, successMsg? }) => Promise<boolean>`:先弹 dialog、**action 在 dialog 挂起期间执行**(确认钮 loading,执行中锁死取消/Esc/遮罩,防连点重复执行);给不适合内联的重操作(批量删除等)。取消/关闭/Esc/失败均 false;`successMsg: false` 关掉成功 toast。
- `ask({ content, title?, type? }) => Promise<boolean>`:仅确认不执行,给需要自管后续流程的组件用(StatusSwitch 即基于它)。Esc/遮罩/取消均 false。

## useTabTitle(详情页动态标签标题,composable)

`src/composables/useTabTitle.ts`,**仅限 setup 中调用**。`const setTabTitle = useTabTitle()`,详情页数据加载后 `setTabTitle(记录名)` 把当前标签标题改成记录名(如「张三」);内部走 `tabsStore.setTitle` → 置 `titleFixed`,`addTab` 复访不再用静态 `meta.title` 覆盖,标题随 tab 持久化、F5 无闪复原。**就地态(列表页内切换详情)别调用**——那时 `route.path` 是列表标签,会改错标签。

## 外链 / iframe 菜单(约定式,零后端字段新增)

菜单节点(`Type=Menu`)复用现有 `path`/`component` 字段承载,判据是 `isHttpUrl`(`src/utils/url.ts`):
- **外链**:`path` 填 `http(s)://…`、`component` 留空 → 不建路由(`useAuthMenu.buildRoutesForModule` 跳过),点击时 `window.open` 新窗口(`useLayoutMenu.onSelect/onSelectL1` + `MenuSearch.go` 各有 `isHttpUrl` 分支)。
- **内嵌 iframe**:`path` 填内部路径(如 `/embed/docs`)、`component` 填 `http(s)://…` → 注册通用视图 `views/embed/iframe.vue`,URL 进 `meta.iframeSrc`,keep-alive 顺带保住 iframe 状态。
- 菜单管理表单在页面类型下给出 `menu.linkHint` 说明;seed 里同理(`path`/`component` 填 URL 即可)。后端实体/枚举/种子结构一概未动。

## 数字动画 / 水印(用 Naive 内建,不自研)

- 数字动画:`<span class="tabular"><n-number-animation :from="0" :to="n" show-separator /></span>`;`.tabular` 防滚动抖宽(styles/index.css),前后缀直接写旁边。
- 水印:全局水印已内建(`layouts/default.vue` + 设置抽屉开关,内容默认当前用户名);局部包 `<n-watermark content="…" cross>` 即可。

## 可加但先不加(设计已备案,别提前造)

- FormContainer size 档位 / `onBeforeClose`;useConfirm 返回结果值版;StatusSwitch 泛型值/乐观模式;DictSelect 展示禁用项;dict 缓存 TTL/SWR;CountTo 自研包装(NNumberAnimation 不够用再说)。

## IconPicker / AppIcon(tenon-naive-iconify-picker)

离线优先图标选择与渲染,初始化与包装见 `src/lib/icons.ts` 顶部注释、`src/components/IconPicker/index.vue`、`src/components/AppIcon.vue`。
