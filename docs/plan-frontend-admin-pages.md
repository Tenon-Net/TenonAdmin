# 执行文档:补齐 TenonAdmin 后台管理页

> **本文件是唯一事实源与断点记录。** 用 `/loop docs/plan-frontend-admin-pages.md 按台账执行下一轮` 或 `/goal` 逐轮执行。每轮完成后按下方协议更新【进度台账】,任务中断后下次从第一个未勾轮续跑。

---

## 如何执行(每轮固定协议 + 断点续跑)

这是长任务,拆成 9 个**纵向可交付轮次**(每轮 = API+类型+页面+菜单种子+i18n 的完整切片,单独可跑、可评审、可提交)。

**每轮固定动作**:
1. 读下方【进度台账】,找**第一个未完成**的轮次;若某轮标「进行中」,读它的备注看剩什么。
2. 按该轮"详情"实现;遵守【通用配方】【核心机制】。
3. **验证**(见每轮 done 判据 + 文末【验证】):`cd web && npm run typecheck && npm run lint` 必过;改了种子重启后端,登录后手动过一遍该页主流程。
4. **更新台账**:把该轮 `[ ]` 改 `[x]`,状态列填「完成 + 日期 + commit 短哈希」;有偏差在该轮末尾追加一行 `> 备注:…`。
5. `git commit`(该轮独立提交,信息如 `feat(web): 配置管理页`)。
6. 交回控制/进入下一轮。

**断点续跑**:任务中断后,下次从台账第一个未 `[x]` 的轮开始;每轮自包含,不依赖上一轮的内存状态,只依赖已提交的代码 + 台账。

---

## 进度台账(Progress Ledger)

| # | 轮次 | 依赖 | 状态 |
|---|---|---|---|
| R1 | [x] 配置管理(`system/config`)—— 最纯 CRUD,当模板跑通配方 | — | 完成 · 2026-07-10 · 本提交 |
| R2 | [x] 登录日志(`system/log/login`)—— 只读 ProTable | — | 完成 · 2026-07-10 · 本提交 |
| R3 | [x] 操作日志(`system/log/op`)—— 只读 + 详情抽屉 | — | 完成 · 2026-07-10 · 本提交 |
| R4 | [x] 用户写侧(改造 `system/user`)+ OrgTreeSelect 组件 + orgApi.list/positionApi.page | — | 完成 · 2026-07-10 · 本提交 |
| R5 | [x] 字典管理(`system/dict`)—— 主从 + 补后端 `dict/item/page` | — | 完成 · 2026-07-10 |
| R6 | [x] 岗位管理(`system/position`)—— 普通 CRUD | R4(positionApi.page) | 完成 · 2026-07-10 |
| R7 | [x] 在线会话(`system/session`)—— 只读 + 踢人 | — | 完成 · 2026-07-10 |
| R8 | [ ] 文件管理(`system/file`)+ FileUpload 组件 —— 上传/下载/删除 | — | 待办 |
| R9 | [ ] 机构管理(`system/org`)—— 树,复用 OrgTreeSelect | R4(OrgTreeSelect) | 待办 |

> 收尾(全绿后):在 `web/COMPONENTS.md` 补 OrgTreeSelect/FileUpload 两行;若跳过某"可选"项(日志清空按钮、dict/item/page 后端、机构列名映射),在此记一行原因。

---

## Context(为什么做这个)

摸底三方向后确认:**后端内核已完工**(可替换性卖点端到端有测;记忆里的 DataEntity IDOR、3 处缓存失效缺口在 dev 分支**已真实修复**,逐行核对过)。真正缺口全在**前端**——一批模块**后端接口全齐、前端零页面**。本计划照现有样板页范式把它们补齐,让 `system/*` 侧栏从「菜单/模块/用户(只读)」变成功能完整的后台。

**显式不做(越界即返工)**:
- **用户角色分配 UI** —— 后端缺 `GET sys/role/page`(只有 role/menu、role/datascope 授权,无角色 CRUD)。用户表单提交时 `roleIds` 原样回传避免清空,不渲染角色多选。独立全栈 track,本计划不含。
- **工作台真实统计** —— 后端无统计接口,`workbench.vue` 假数据不动。
- 不改 `client.ts`(上传/下载现有封装可行);不动只读 `dictApi`(它是下拉缓存基座)。

---

## 核心机制(动手前必须懂)

**动态路由 = .vue 文件 + 后端菜单记录**(`web/src/composables/useAuthMenu.ts`):登录后拉菜单树,只有 `Type===Menu` 且带 `Path`+`Component` 的节点会 `addRoute`;`Component` 字符串经 `import.meta.glob('/src/views/**/*.vue')` 映射(`"system/config/index"` → `/src/views/system/config/index.vue`)。**每个新页面都要在后端菜单种子补一条 Menu 记录,否则页面写了也进不去。**

**菜单种子改法**(`backend/src/TenonAdmin.Services/Seed/DefaultMenuSeed.cs`):`HasData()` 追加记录,`DatabaseInitializer` 用 `Storageable` **按主键只插缺失行**,重启后端幂等插入。现占用 Id:1-8/10-15/20-25/30-32/40-49;**新记录统一 50-81**(见【菜单种子】)。权限码规范化:**大写 Method + 冒号 + 小写路由模板**(`{id}`/`{typecode}`/`{sessionid}` 全小写),否则授权管道匹配不上。

**v-auth 现状**:`directives/auth.ts` 目前 fail-open(`permissionCodes` 恒空 → 不隐藏任何按钮)。新页面按钮照写 `v-auth="'METHOD:/route'"`(码写对),当前不误伤显示;等后端补 `/personal/permissions` 后自动生效。

**⚠ 反向锁测试(每个种子权限码的强制配套,别只 build)**:`backend/tests/TenonAdmin.Tests/PermissionCodeConsistencyTests.cs` 有个 `KnownUnseededEndpoints` 清单 + 反向自清断言:**每当你在 `DefaultMenuSeed` 新种一个权限码(按钮节点),必须从该清单删掉同一条**,否则 `Every_permission_endpoint_is_seeded_or_explicitly_known_unseeded` 变红。`dotnet build` 察觉不到(编译过、测试红)——**改了种子必须跑 `dotnet test --filter PermissionCodeConsistencyTests`**(R1/R2 就因只 build 埋了雷,R3 一并清了)。剩余轮对照清单要删的条目:R4 用户 `DELETE/GET/PUT user/{id}`、`PUT user/{id}/enabled`、`PUT user/{id}/password`;R5 字典 `POST/PUT/DELETE dict/type`、`PUT/DELETE dict/item/{id}`;R6 岗位 `POST position/add`、`PUT/DELETE position/{id}`;R8 文件 `GET file/{id}/download`、`DELETE file/{id}`;R9 机构 `POST org/add`、`PUT/DELETE org/{id}`。**只删你这轮真种了码的那几条**(如 R1 没放配置详情按钮 → `GET config/{id}` 保留在清单)。

---

## 通用配方(每个 ProTable CRUD 页照抄,只写这一遍)

范式源:`web/src/views/system/module/index.vue`(手写 `blank()/toInput()/openAdd/openEdit/save` + `FormContainer` + `StatusSwitch` + `useConfirm().run` + `NPopconfirm`)。差别:本批都有服务端 `page` 端点,列表用 **ProTable**(`tenon-naive-pro-table`,分页/搜索/竞态)承接,弹窗状态仍手写。

1. `fetcher = xxxApi.page`,`labels = useProTableLabels()`,`storage-key = "sys-xxx"`,`tableRef = ref<ProTableInst<Row>>()`(拿 `refresh()`)。
2. 列:`search:true` 生成搜索项;`enabled` 列用 `StatusSwitch`;操作列 `hideInSetting:true`、`h(NSpace,…)` 放编辑/删除,删除 = `NPopconfirm` + `run(()=>xxxApi.remove(id)).then(ok=>ok && tableRef.value?.refresh())`。列标题一律 `title:()=>t(...)`。
3. `#toolbar` 插槽放「新增」(`v-auth` + `openAdd`)。
4. `FormContainer` + `n-form`:`onConfirm=save`;`save()` 校验后 add/update,成功 `message.success` + `await tableRef.value?.refresh()`(刷当前页,不回第1页),失败 `translateError` + `return false`(弹层不关)。
5. 唯一键(code/configKey)编辑时 `:disabled="editingId !== null"`。

API 封装照 `web/src/api/index.ts` 现有写法:`unwrap<T>()` 解包,`page` 把 `{page,pageSize}` → `{Current,Size}`(**PascalCase 查询参**)、`PagedList<T>` 归一成 `{items,total}`。领域类型加 `web/src/types/api.ts`,照 schema.d.ts 同名 DTO 抄字段,int64/int32 收敛成 `number`。

> 已装依赖 `useTableCrud`(包内)可省弹窗样板,但仓库无先例。**决策:R1 用手写范式(与 module 一致、好评审);后续轮若嫌样板多再统一切 `useTableCrud`,不要两套并存。**

---

## 各轮详情

### R1 · 配置管理 `web/src/views/system/config/index.vue`(模板轮)
跑通配方 + `configApi` + 类型 + 种子 + i18n + 路由一条龙。
- `configApi`:`page/add/update/remove`(`sys/config/*`);列表返回完整 `SysConfig`,编辑用行数据,`detail` 可省。
- 表单 `ConfigInput`:`configKey`(必填,编辑禁用)、`configValue`(textarea)、`name`(必填)、`groupCode`、`sort`、`remark`。无 enabled → 无 StatusSwitch;删除普通 popconfirm。
- 搜索:`ConfigKey`/`Name`/`GroupCode`。
- **done**:侧栏「字典配置」目录出现「配置管理」,增/改/删/搜索/分页全通。
> 备注(R1 完成):模板范式 = ProTable(列表/搜索/分页/竞态,`ref<ProTableInst>` 拿 refresh)+ `#toolbar` 插槽新增(`v-auth`)+ 手写 FormContainer 弹窗 + NPopconfirm/`run` 删除;后续 CRUD 轮照抄。detail 用行数据,未放 `GET config/{id}` 按钮码(菜单种子跳过 Id 56)。种子加 55(页)/57/58/59(写端点码)。验证:typecheck+lint+后端 Services 编译全绿;交互式登录走查未在 loop 环境执行,留待人工 `dev.bat` 过一遍。

### R2 · 登录日志 `web/src/views/system/log/login/index.vue`(只读)
- `logApi.loginPage`(+ 可选 `loginClear`)。只读,无表单。
- 列:`account`、`success`(tag,**显式红/绿 typeMap 保证"失败=红"**)、`resultCode`、`userId`、`ip`、`userAgent`(ellipsis tooltip)、`createTime`(datetime)。搜索:`Account`/`Success`。
- **done**:侧栏出现,搜索/分页/时间格式正常。
> 备注(R2 完成):只读 ProTable(account/success 可搜),success 列 tag + 红/绿 typeMap(失败=红);**采纳可选清空按钮**(参 simpleadmin 日志页,useConfirm type:error 二次确认,硬删不可恢复)。新增共用 `log.*` i18n 命名空间(R3 复用)。种子加 68(页)/69(清空码);分页码复用已有按钮 8。i18n `log.userId` 等 int64 仅展示,类型收敛 number。验证:typecheck+lint+后端编译全绿;交互走查留待人工。

### R3 · 操作日志 `web/src/views/system/log/op/index.vue`(只读 + 详情)
- `logApi.opPage`(+ 可选 `opClear`)。**后端无 `op/{id}` 详情** → 详情抽屉直接用行数据(分页项已含全字段)。
- 列:`title`/`httpMethod`/`path`/`success`(tag)/`resultCode`/`elapsedMs`(`{v} ms`)/`ip`/`createTime`/操作「详情」。
- 详情:`n-drawer`(或 FormContainer drawer)+ `n-descriptions`;`paramJson` 用 `<pre style="white-space:pre-wrap;word-break:break-all">{{ pretty }}</pre>`(`pretty`=`JSON.parse→stringify(_,2)`,失败原样)或 `n-code language="json"`(hljs 已是 naive 传递依赖,零新增)。
- **CodeBlock 决策**:COMPONENTS.md 约定"第二个 JSON 展示消费点再归纳"。本波仅此一处 → **不落地 CodeBlock 组件**,用上面兜底,PR 里写明。
- **done**:侧栏出现,详情抽屉展示 paramJson 可读。
> 备注(R3 完成):只读 ProTable(操作名/结果可搜)+ 原生 `n-drawer`+`n-descriptions` 详情(行数据直填,后端无 op/{id});paramJson 走 `<pre>` 兜底美化(parse→stringify(2),失败原样),未落地 CodeBlock(仅此一处,COMPONENTS.md 约定);清空按钮同 R2。种子加 66(页)/67(清空码),分页码复用已有按钮 7。**多智能体评审(ultracode)逮到反向锁雷**:R1/R2/R3 种了权限码却没删 `KnownUnseededEndpoints`,一致性测试自 R1 起就红(只 build 没 test 埋的);R3 一并清 5 条(config POST/PUT/DELETE、log/login DELETE、log/op DELETE),`dotnet test --filter PermissionCodeConsistencyTests` 2/2 绿。已把反向锁规则写入【核心机制】+【验证】。

### R4 · 用户写侧(改造 `web/src/views/system/user/index.vue`)+ OrgTreeSelect 组件
最复杂轮。先建组件(R9 复用),再改用户页。
- **OrgTreeSelect** `web/src/components/OrgTreeSelect/index.vue`(YAGNI,几十行,照 `DictSelect/index.vue` 的 `inheritAttrs:false` + `v-bind="$attrs"` 骨架):封 `n-tree-select`,`onMounted` 拉 `orgApi.list()`(**平铺数组,前端 `buildTree` 按 parentId 拼树**),`key-field=id label-field=name children-field=children`;prop `excludeSubtreeOf?:number`(编辑时剪自身子树防成环);value 经 `$attrs` 直穿。
- API 补:`userApi.detail/add/update/remove/resetPassword/setEnabled`;新增 `orgApi.list()`、`positionApi.page()`(供下拉,R6/R9 复用)。
- 新增 `AddUserInput`:`account`(必填唯一,建后不可改)、`password`(留空=后端默认初始密码)、`name`、`orgId`(OrgTreeSelect,可空)、`positionId`(下拉,可空)、`enabled`。**提交 `roleIds:[]`**。
- 编辑 `UpdateUserInput`:先 `userApi.detail(id)` 取回 `orgId/positionId/enabled/name` + **`roleIds`**,表单只展示前 4 项,**提交把 detail 的 roleIds 原样带回**避免清空角色。无 account/password。
- **启停**:`StatusSwitch` 的 `request` 走**专用端点** `userApi.setEnabled(id,next)`(非全量 update),`onUpdate:value` 回写 `r.enabled`(悲观更新,不必 refresh)。
- **重置密码弹框**:操作列「重置密码」→ 小 FormContainer 单个「新密码」(留空=默认初始);接口**返回实际生效的初始密码字符串**,成功后只读对话框展示(可复制)。
- **超管行保护**:`isSuperAdmin` 行删除/停用置灰(照 module `isBuiltin`),避免自锁。
- 机构/岗位**列名**:`UserItem` 只回 id → 列可先省机构列,或加载 `org/list`+`position/page` 做 id→name 映射(可选)。
- **done**:新增/编辑/删除/重置密码/启停全通;编辑不清空角色;超管行受保护。
> 备注(R4 完成):新增 `utils/tree.ts`(`buildTree`+`collectSubtreeIds`,R9 复用)。OrgTreeSelect 照 DictSelect 骨架(`inheritAttrs:false`+`$attrs` 透传 n-tree-select,`excludeSubtreeOf` 剪自身子树)。用户页:新增有 account/password、编辑无(先 `detail` 取 `roleIds` 原样带回避免清空角色);启停走专用 `setEnabled`(悲观回写);重置密码返回实际密码 → 只读结果弹层(navigator.clipboard 复制);超管行删除/停用置灰。**职位/机构名列先省**(UserItem 只回 id,ledger 标可选);职位下拉拉一页 200(ponytail,量超再分页)。种子加按钮 50-54 + 反向锁清 5 条用户端点。COMPONENTS.md 的 OrgTreeSelect 行已写入工作区,但**不并入本提交**(该文件已有他人未提交的 ModuleSwitcher 改动,避免搅入)——随【收尾】统一提交。5 维对抗评审 0 确认问题;typecheck+lint+一致性测试(2/2)+全量后端测试(99/99)全绿。交互走查留待人工。

### R5 · 字典管理 `web/src/views/system/dict/index.vue`(主从)
- 左=类型 ProTable(`dictAdminApi.typePage`),右=选中类型的项。左行选中设 `selectedTypeCode`,`watch` 触发右侧 `dictAdminApi.items(typeCode)`。
- **不能复用 `dictApi.items`**(投影**丢 id**)→ 新增 `dictAdminApi.items` 取原始 `SysDictItem[]`(带 id)。右侧非分页,用 `n-data-table` 或 ProTable 静态 `:data`。
- 类型表单 `DictTypeInput`:`code`(必填,编辑禁用)、`name`、`sort`、`enabled`、`remark`;删除提示级联删项。项表单 `DictItemInput`:`dictTypeCode`(=当前类型,隐藏)、`label`、`value`、`sort`、`enabled`。
- 增删改后 `useDictStore().invalidate(typeCode)` 失效下拉缓存。
- **⚠ 后端限制**:唯一项列表端点 `GET dict/items/{typeCode}` **只回启用项** → 管理端看不到停用项。**决策(推荐)**:补小后端端点 `GET sys/dict/item/page?typeCode=`(含停用,照 `dict/type/page` 写)+ `gen:api` 重生成;不补则接受"只管启用项",在台账收尾记一行。
- **done**:类型 CRUD + 主从联动 + 项 CRUD + 缓存失效可见。
> 备注(R5 完成):**补了后端** `GET dict/item/page?TypeCode=`(含停用项、带 id 的管理端端点;`DictItemPageInput`+`PageItemsAsync` 冷路径不走缓存,区别于只回启用项的缓存源 `dict/items/{code}`)+ `gen:api` 重生成 schema。前端 `dictAdminApi`(typePage/typeAdd/typeUpdate/typeRemove + items/itemAdd/itemUpdate/itemRemove)。主从页:左类型 ProTable **行点击选中**(`:row-props` 经 ProTable `mergeProps($attrs)` 透传到内层 NDataTable),右字典项裸 `n-data-table` CRUD;增删改/启停后 `useDictStore().invalidate(code)` 失效下拉缓存。类型 code 编辑禁用,项表单 dictTypeCode 隐藏(取自选中类型)。种子加页 60 + 按钮 61-65 + 82(item/page),反向锁清 5 条(POST dict/type、PUT/DELETE dict/type/{id}、PUT/DELETE dict/item/{id}),保留 `GET dict/type/{id}`(详情用行数据不放按钮)。**ultracode 5 维对抗评审确认 2 项 minor 并已修**:①行内按钮/StatusSwitch 点击冒泡到行 onClick 误切选中 → status/op 列包 `stopPropagation`;②`loadItems` 缺竞态守卫 → 捕获 code、await 后 `selectedType.code===code` 才回写。typecheck+lint+后端全量 99/99 全绿。交互走查留待人工。

### R6 · 岗位管理 `web/src/views/system/position/index.vue`(普通 CRUD)
**纠偏:岗位与机构无关联**(`PositionInput={name,code,sort,enabled}`,无 orgId),**不接 OrgTreeSelect**。
- API `positionApi` 补 `add/update/remove`(`page` 已在 R4)。`enabled` 用 StatusSwitch。表单 `code`(编辑禁用)/`name`/`sort`/`enabled`。
- **done**:普通 CRUD + 启停全通。
> 备注(R6 完成):config/module 范式克隆。`PositionInput={name,code,sort,enabled}`(无 orgId,不接 OrgTreeSelect)。positionApi 补 add(POST position/**add**)/update/remove(PUT/DELETE position/{id});page 已在 R4。StatusSwitch 无独立端点走全量 update(同 module)。code 编辑禁用。种子加页 74 + 按钮 75-77,反向锁清 3 条写端点,留 `GET position/{id}`(详情用行数据)。ultracode 3 维对抗评审 0 发现。typecheck+lint+后端全量 99/99 全绿。

### R7 · 在线会话 `web/src/views/system/session/index.vue`(只读 + 踢人)
- `sessionApi.online/kick`,无表单。列:`account`/`ip`/`loginTime`/`expiresAt`/操作「强制下线」。
- 踢人 = `useConfirm().confirm({type:'warning',action:()=>sessionApi.kick(r.sessionId)}).then(ok=>ok && refresh())`。可选:踢自己的会话行 `disabled`(`r.userId===userStore.userId`)。
- **done**:列表显示 + 踢人二次确认 + 踢后刷新。
> 备注(R7 完成):只读 ProTable(无搜索列——后端仅按 UserId 过滤)。sessionApi 补 online(GET session/online 归一 {items,total})/kick(DELETE session/{sessionId})。踢人走 `useConfirm().confirm({type:'warning', action, successMsg})` 二次确认,成功后 refresh。**踢自己的会话置灰**(`r.userId === userStore.userInfo?.userId`),显示「当前会话」。种子仅补页 81(在线码 5、强退码 6 已在,**无需清反向锁**)。ultracode 2 维对抗评审 0 发现。typecheck+lint+后端全量 99/99 全绿。

### R8 · 文件管理 `web/src/views/system/file/index.vue` + FileUpload 组件
- **FileUpload** `web/src/components/FileUpload/index.vue`(几十行):封 `n-upload` 的 `:custom-request`,内部 `await fileApi.upload(file.file)`(走 api 层自动带 Bearer),`emit('uploaded', out)`;props 最小(accept/multiple 经 `$attrs`),默认 slot 触发器。
- API `fileApi`:`page`、`upload`(`bodySerializer` 返回 `FormData`,字段名 `file`;openapi-fetch 对 FormData 不注入 json header,浏览器自动补 boundary——**client.ts 无需改**)、`download`(`parseAs:'blob'` → 取 `r.data as Blob`,**不套 unwrap**)、`remove`。
- 工具栏放 `<FileUpload :show-file-list="false" @uploaded="refresh"/>`。列:`originalName`(search=FileName)/`extension`/`sizeBytes`(格式化 KB/MB)/`contentType`/`createTime`/操作(下载/删除)。
- **下载带 Bearer**:`fileApi.download(id)` → `URL.createObjectURL` + `<a download>` + `revokeObjectURL`,包 try/catch。
- **done**:上传→列表→下载(文件正确)→删除全通。

### R9 · 机构管理 `web/src/views/system/org/index.vue`(树,复用 OrgTreeSelect)
**照抄 `menu/index.vue`**(裸 `n-data-table` 树 + FormContainer + StatusSwitch + useConfirm)。**ProTable 不支持树形行,必须用裸 n-data-table。**
- API `orgApi` 补 `add/update/remove`(`list` 已在 R4)。**org list 平铺 → 前端 `buildTree`**(`:data=tree row-key=id default-expand-all`,行内 `children`)。
- 表单:上级机构用 `OrgTreeSelect`(clearable,`:exclude-subtree-of="editingId"`,save 时 `parentId ?? 0`)、`name`/`code`(编辑禁用)/`sort`/`enabled`。
- 删除后端会拒(有子机构),前端照常调,失败由 `translateError` 弹码。
- **done**:树展开 + 加下级 + 编辑(上级树选)+ 删除 + 启停全通。

---

## API 封装增量汇总(`web/src/api/index.ts` + 类型 `web/src/types/api.ts`)

每轮只加该轮需要的部分,全照现有写法:
- `userApi` 写侧:`detail/add/update/remove/resetPassword/setEnabled`(R4)。
- `configApi`:`page/add/update/remove`(R1)。
- `dictAdminApi`(与只读 `dictApi` 并存):`typePage/typeAdd/typeUpdate/typeRemove` + `items`(原始 `SysDictItem[]` 带 id)`/itemAdd/itemUpdate/itemRemove`(R5)。
- `logApi`:`opPage/opClear/loginPage/loginClear`(R2/R3)。
- `orgApi`:`list`(R4)`/add/update/remove`(R9)。`positionApi`:`page`(R4)`/add/update/remove`(R6)。
- `fileApi`:`page/upload/download/remove`(R8)。`sessionApi`:`online/kick`(R7)。

类型:`SysConfig/ConfigInput`、`SysDictType/DictTypeInput`、`SysDictItem(带id)/DictItemInput`、`SysOpLog/SysLoginLog`、`AddUserInput/UpdateUserInput/UserDetail`、`SysOrg/OrgInput`、`SysPosition/PositionInput`、`SysFile/FileUploadOutput`、`OnlineSessionItem`。

---

## 菜单种子(`DefaultMenuSeed.cs`,统一 Id 50-81)

页面(Menu 类型,`Enabled=Visible=true`,Icon 照现有风格,携 Path/Component):

| Id | ParentId | Title | Path | Component | 轮 |
|---|---|---|---|---|---|
| 55 | 20 字典配置 | 配置管理 | /system/config | system/config/index | R1 |
| 60 | 20 | 字典管理 | /system/dict | system/dict/index | R5 |
| 66 | 1 系统管理 | 操作日志 | /system/log/op | system/log/op/index | R3 |
| 68 | 1 | 登录日志 | /system/log/login | system/log/login/index | R2 |
| 70 | 10 组织管理 | 机构管理 | /system/org | system/org/index | R9 |
| 74 | 10 | 岗位管理 | /system/position | system/position/index | R6 |
| 78 | 30 文件管理 | 文件管理 | /system/file | system/file/index | R8 |
| 81 | 1 | 在线会话 | /system/session | system/session/index | R7 |

按钮(Button 类型,补各轮写端点授权码,`Permission=规范化路由`,Id 连号):
- 用户(parent=10,R4):50 `GET user/{id}`、51 `PUT user/{id}`、52 `DELETE user/{id}`、53 `PUT user/{id}/password`、54 `PUT user/{id}/enabled`(POST user 已 Id 12)。
- 配置(parent=20,R1):56 `GET config/{id}`(若用 detail)、57 `POST config`、58 `PUT config/{id}`、59 `DELETE config/{id}`(GET config/page 已 24)。
- 字典(parent=20,R5):61 `POST dict/type`、62 `PUT dict/type/{id}`、63 `DELETE dict/type/{id}`、64 `PUT dict/item/{id}`、65 `DELETE dict/item/{id}`(type/page 21、items 22、POST item 23 已在)。
- 日志(parent=1):67 `DELETE log/op`(R3 可选)、69 `DELETE log/login`(R2 可选);分页码 7/8 已在。
- 机构(parent=10,R9):71 `POST org/add`、72 `PUT org/{id}`、73 `DELETE org/{id}`(list 已 13)。
- 岗位(parent=10,R6):75 `POST position/add`、76 `PUT position/{id}`、77 `DELETE position/{id}`(page 已 14)。
- 文件(parent=30,R8):79 `GET file/{id}/download`、80 `DELETE file/{id}`(upload 31、page 32 已在)。
- 会话(parent=1,R7):online GET 5、kick 6 已在,只需页面节点 81。

> 「字典配置」目录(Id 20)之前无可见页,加 55/60 后才现身侧栏。不想重启后端也可在「菜单管理」页手工加同样 Menu 记录;改种子是标准可复现路径,推荐。

---

## i18n(`web/src/locales/zh-CN.ts` + `en-US.ts`,两文件对称)

每轮补该页的段:`config.*`/`dict.*`/`log.*`/`org.*`/`position.*`/`file.*`/`session.*`,并给 `user.*` 补写侧键。通用键(add/edit/delete/save/status/enabled/disabled/operation/createTime/success)已在 `common.*` 复用。**导航标题来自后端种子 `Title`,不走 i18n。**

---

## 后端缺口 / 决策清单

1. **`GET sys/dict/item/page`(含停用项)** —— 不补则字典页管不了停用项。**推荐补**(小,照 type/page)。R5 处理。
2. **`GET sys/role/page`** —— 缺 → 用户角色分配不做(独立 track)。
3. `GET sys/log/op/{id}` 无 → 详情用行数据(不阻塞)。
4. `UserItem` 无机构/岗位名 → 列表显示名需前端映射(可选)。

---

## 验证(端到端,别只靠 typecheck)

- **静态**:`cd web && npm run typecheck && npm run lint` 必过。
- **改了种子/权限码**:`dotnet test backend/TenonAdmin.slnx --filter PermissionCodeConsistencyTests` 必过(反向锁,见【核心机制】;`dotnet build` 过 ≠ 测试过)。稳妥起见跑一遍全量 `dotnet test backend/TenonAdmin.slnx`。
- **跑起来**:根目录 `dev.bat`(后端 :5000 + 前端 :5173),或 `dotnet run --project backend/samples/MinimalHost` + `cd web && npm run dev`。首启控制台打印随机超管密码,用它登录。
- **改了种子必须重启后端**让新菜单落库;登录后确认侧栏出现该轮页面、CRUD/搜索/分页/启停走通。
- **动了后端 DTO/新端点**(如 dict/item/page):`npm run gen:api`(后端在跑)重生成 `schema.d.ts` 再改前端类型。
- 每轮非平凡处(用户 roleIds round-trip、org buildTree、file 下载 blob、踢人)手动过一遍主流程,观察真实行为。

---

## 关键文件

- `web/src/api/index.ts`(所有 api 增量)、`web/src/types/api.ts`(类型)
- `web/src/views/system/module/index.vue`(手写 CRUD 范式源)、`menu/index.vue`(树表范式源)
- `web/src/components/DictSelect/index.vue`(组件 `$attrs` 透传骨架)
- `web/src/api/schema.d.ts`(endpoint/DTO 唯一权威)
- `backend/src/TenonAdmin.Services/Seed/DefaultMenuSeed.cs`(菜单种子 + 权限码)
- `web/src/composables/useAuthMenu.ts`(component→.vue 映射)、`web/COMPONENTS.md`(组件索引)
