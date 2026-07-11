# 前端管理页审查报告(对比 SimpleAdmin)

标 [已修复] 的已直接改代码;标 [待确认] 的需你确认后再动。

## 配置管理 config

对照:`web/src/views/system/config/index.vue`(+ `web/src/api/index.ts` 的 `configApi` + `web/src/types/api.ts` 的 `SysConfig/ConfigInput` + `web/src/locales/zh-CN.ts` 的 `config.*`)vs `D:/SimpleAdmin/web/src/views/sys/ops/config`(含 `components/otherConfig`、`sysBaseConfig.vue`、`safetyConfig/`、`mqttConfig/`)。

- **功能/易用性**:
  - [待确认] SimpleAdmin 的"配置管理"实为**分类配置中心**(Tab:系统基础配置/安全策略/MQTT 配置/文件配置(开发中)/其他配置),每个分类有专属结构化表单(如登录失败锁定次数、密码复杂度策略、站点名称等常用系统级参数直接给字段级 UI,不需要用户知道 key)。tenon 目前只有一张通用 key-value 表格(`config/index.vue`),没有任何分类化的结构表单。这是"SimpleAdmin 有、tenon 没有"的功能,且要不要做分类配置中心是产品范围决策,不是小修小补,按规则一律待确认,不动手。
  - [答复] 我觉得还是要做一个分类配置中心，我希望这个系统更通用，更可配置化，高可用，不用每次改个小配置就去修改代码或者发布。
  - [待确认] 列表的“分组编码”(`groupCode`)搜索框是自由文本输入,用户不知道系统里已有哪些分组值可选,只能盲打。可优化为下拉选择(去重已有 groupCode),但这需要新增一个“分组去重”查询能力/接口,跨文件且改变现有交互 → 待确认,不动手。
  - [答复] 不动
  - 表单必填校验(configKey/name)、编辑时 configKey 禁用、保存/删除走 `useConfirm`+`translateError` 统一错误提示,与其他已完成的 CRUD 页(如 position)范式一致,未发现额外易用性问题。
- **Bug**:未发现问题。`blank()/openAdd/openEdit/save` 空值兜底、`configValue`/`groupCode`/`remark` 的 `?? ''` 处理都到位;`ellipsis:{tooltip:true}` 避免长配置值撑破表格。
- **美观度**:与同批 ProTable+FormContainer 页面视觉一致,未发现问题。
- **菜单归属**:
  - [待确认] “配置管理”菜单(`Id=55`)挂在 `ParentId=20`(目录标题“字典配置”)下,与“字典管理”同目录。该目录名偏向“字典”,但同时承载了通用配置管理,命名与内容不完全贴合(例如叫“系统配置”或把配置/字典拆成两个目录可能更直观)。这是菜单种子(`DefaultMenuSeed.cs`)改动,且改目录名或结构可能牵动其他种子引用/反向锁测试 → 待确认,不动手。
  - [答复]目录标题应该改为系统运维，然后菜单管理和系统配置分别是两个菜单
- **可优化/新增功能**:
  - [待确认] 可参考 SimpleAdmin 把高频系统级配置(如登录失败锁定次数、密码复杂度、会话超时等)抽成结构化表单,替代/补充当前纯 key-value 表格,降低运维直接改 key-value 出错的风险。是否需要这类"预置系统参数"功能取决于产品需求,记录待你确认。
  - [需要]

## 登录日志 log/login

对照:`web/src/views/system/log/login/index.vue`(+ `logApi.loginPage/loginClear` + `SysLoginLog` 类型 + `log.*` i18n)vs `D:/SimpleAdmin/web/src/views/sys/audit/vislog`(含 `components/lineChat.vue`、`pieChat.vue`、`detail.vue`)。

- **功能/易用性**:
  - [待确认] SimpleAdmin 的访问日志把“登录/登出”合并成一个页面(顶部 `el-radio-group` 切类目),并有周统计折线图 + 总比例饼图,`ip` 字段还做了地理位置解析(`opAddress`)、`userAgent` 做了浏览器/操作系统解析(`opBrowser`/`opOs`)。tenon 目前只做了“登录日志”,`ip`/`userAgent` 都是后端原样透传的原始值,没有图表统计,也没有登出事件。这几项都需要后端新增数据(IP 归属地库、UA 解析、登出事件采集)和统计接口,属于产品功能范围决策而非页面小修,按规则待确认,不动手。
  
    [日志可以丰富一点，但是IP 归属地库可以不用加进去，需要加上符合后台管理系统该有的功能]
  
  - 搜索(账号/结果)、`success` 用 `tag` + 下拉筛选,交互清晰;其余字段只读展示,未发现额外易用性问题。
  
- **Bug**:未发现问题。`userId`/`ip`/`userAgent` 均有 `??`/`||` 空值兜底为 “—”,与其他只读页一致。

- **美观度**:`success` 列红/绿 tag 语义清晰,布局与其他 ProTable 只读页一致,未发现问题。

- **菜单归属**:“登录日志”(`Id=68`)挂在 `ParentId=1`(“系统管理”)目录下,与“操作日志”“在线会话”“菜单管理”“模块管理”同目录,归属合理,未发现问题。

- [答复]日志放在一个日志审计的目录下吧，和simpleadmin一样

- **可优化/新增功能**:
  - [待确认] 参考 SimpleAdmin,补充 IP 归属地解析 + UA 浏览器/系统解析,能显著提升审计可读性(比如一眼看出“异地登录”)。这属于新功能且要后端引入解析能力(归属地库/UA 解析库),记录待你确认。
  
    [答复]ip地址归属可以不做，其他的如果不破坏现有架构可以实现
  
  - [待确认] 是否需要登出事件采集与统计图表(周趋势/成功比例),取决于审计需求,记录待你确认。
  
  - [答复]你觉得呢？有必要吗

## 操作日志 log/op

对照:`web/src/views/system/log/op/index.vue`(+ `logApi.opPage/opClear` + `SysOpLog` 类型 + `log.*` i18n)vs `D:/SimpleAdmin/web/src/views/sys/audit/oplog`(含 `components/columnChat.vue`、`pieChat.vue`、`detail.vue`)。

- **功能/易用性**:
  - [待确认] SimpleAdmin 详情里除了请求参数(paramJson)还展示**返回结果**(`resultJson`/异常信息 `exeMessage`),并把“操作日志”“异常日志”合并成一个可切换类目的页面,还有周统计柱状图 + 总比例饼图。tenon 后端 `SysOpLog`(见 `web/src/types/api.ts:188-201`)**根本没有 resultJson/异常信息字段**,只有 `paramJson`,所以“看返回结果”这个能力在后端数据模型层面就缺失,不是前端能补的;异常日志分类、统计图表同理都需要新后端能力。均属于产品功能范围决策,按规则待确认,不动手。
  
  - [答复] 这个你帮我分析一下，是否需要加上
  
  - [待确认] 详情抽屉里的“操作人”只显示 `operatorId`(原始数字 ID,`log/op/index.vue:110`),不像 SimpleAdmin 显示 `opUser`/`opAccount`(已解析的用户名/账号),运维看到一串数字很难直接定位是谁操作的。要展示用户名需要后端在日志行里 join 用户名或前端另发一次用户查询,属于跨文件/接口改动 → 待确认,不动手。
  
    [答复]这个要加的，日志必须直观
  
  - 其余交互(操作名/结果可搜、详情抽屉字段完整、`elapsedMs` 直接展示为 “{v} ms”)清晰易懂,与 R3 计划文档描述(“只读 + 详情抽屉,后端无 op/{id} 详情,行数据直填”)一致,未发现额外易用性问题。
  
- **Bug**:未发现问题。`ip`/`operatorId`/`userAgent` 均有空值兜底;`prettyParam` 对非法 JSON 原样返回、空值给 “—”,健壮。

- **美观度**:`<pre class="param-json">` 等宽字体 + `white-space:pre-wrap`/`word-break:break-all` 展示 JSON,抽屉宽度 560px,与计划文档的“CodeBlock 仅此一处不落地”决策一致,未发现问题。

- **菜单归属**:“操作日志”(`Id=66`)与“登录日志”“在线会话”同挂在 `ParentId=1`(“系统管理”)下,归属合理,未发现问题。

- [答复] 日志放在一个日志审计的目录下吧，和simpleadmin一样

- **可优化/新增功能**:
  
  - [待确认] 参考 SimpleAdmin 补充“返回结果/异常信息”展示、操作人用户名解析、异常日志分类和统计图表,能大幅提升审计可用性,但都需要后端数据模型/接口先行,记录待你确认,不属于本轮前端小修范围。
  - [答复] 需要提升审计可用性，可以实现

## 用户管理 user

对照:`web/src/views/system/user/index.vue`(+ `userApi`/`orgApi`/`positionApi` + `UserItem/AddUserInput/UpdateUserInput/UserDetail` 类型 + `user.*`/`en-US.ts` i18n)vs `D:/SimpleAdmin/web/src/views/sys/organization/user`(含 `components/form/index.vue`、`form_basic.vue`、`form_more.vue`)。同时对照 `docs/plan-frontend-admin-pages.md` R4 详情/备注,确认代码与文档描述一致(角色分配、机构/职位列省略、启停走专用端点、重置密码返回明文、超管保护均按文档实现)。

- **功能/易用性**:
  - [已修复] `web/src/locales/zh-CN.ts` 和 `en-US.ts` 的 `user.keyword`(“账号 / 姓名” / “Account / Name”)是死 i18n key——全仓库搜索 `user.keyword` 无任何 `.vue` 引用(tenon 页面的搜索是 `account`/`name` 两个独立列各自 `search:true`,不是 SimpleAdmin 那种合并的 `searchKey` 单框搜索)。已删除两个语言文件里这个未使用的 key,不涉及组件/类型/接口改动。`npm run lint` 通过;`npm run typecheck` 在改动前后报错完全一致(均为环境缺失 `tenon-naive-pro-table`/`tenon-naive-iconify-picker` 包导致,`git stash` 验证与本次改动无关,属预置环境问题不在本轮职责内)。
  
  - [待确认] SimpleAdmin 用户页有左侧机构树筛选(`TreeFilter`,点组织直接过滤该组织下用户)、批量勾选删除、角色/资源/权限授权入口(下拉菜单)、更丰富的档案字段(性别/手机/邮箱/生日/入职日期/员工编号/主管等)。tenon 当前无机构树筛选、无批量删除、无角色分配 UI(文档已注明是后端缺 `role/page` 的已知限制)、无额外档案字段。是否需要这些能力属于产品范围决策(而且角色分配、机构树筛选都要动后端/跨文件),按规则待确认,不动手。
  
    [答复] 从企业管理角度，你觉得是否需要加上左侧机构树等功能，我觉得是要的
  
  - 新增/编辑表单字段(账号/密码/姓名/机构/职位/启用)、超管行删除与停用置灰、重置密码走专用弹窗 + 明文结果只读展示,均与 R4 计划文档描述一致,交互清晰;未发现额外易用性问题。
  
- **Bug**:未发现问题。`openEdit` 失败时 `message.error` 且不开弹层;`resetForm`/`resetResult` 状态在 `openReset`/`doReset` 里正确重置;`isSuperAdmin` 行的删除按钮直接不渲染(`r.isSuperAdmin ? null : …`)、停用走 `StatusSwitch` 的 `disabled`,双重防自锁到位。

- **美观度**:与其他 CRUD 页风格一致,重置密码结果框用 `readonly` input + 复制按钮,视觉清晰,未发现问题。

- **菜单归属**:`user` 页菜单归属未在本轮改动范围内(菜单结构与 R1-R3 一致挂在合理目录下),未发现问题。

- **可优化/新增功能**:
  - [待确认] 机构/职位列当前只回 id、列表不展示机构/职位名称(计划文档已标注为“可选,先省”),后续如需要在列表直接看到机构/职位名,需要后端联表或前端二次映射,记录待你确认,是否现在做取决于优先级。
  
    [答复] 最好能显示机构，这样比较直观
  
  - [待确认] 参考 SimpleAdmin 加机构树侧栏筛选和批量删除,能提升大用户量场景下的操作效率,但改变现有交互设计,记录待你确认。
  
  - [答复]筛选和批量删除都要

## 字典管理 dict

对照:`web/src/views/system/dict/index.vue`(+ `dictAdminApi` + `DictTypeInput/DictItemInput/SysDictType/SysDictItem` 类型 + `dict.*` i18n)vs `D:/SimpleAdmin/web/src/views/sys/ops/dict`(含 `components/form.vue`)。同时对照 `docs/plan-frontend-admin-pages.md` R5 详情/备注核实实现(主从联动、`dict/item/page` 管理端点、缓存失效、行内点击冒泡两处修复)。

- **功能/易用性**:
  - SimpleAdmin 的字典是**单表自关联树**(`category`+`parentId`,靠单选类目切左侧列表),tenon 是**类型表(SysDictType)+ 字典项表(SysDictItem)双表模型**(左选类型、右管字典项),两者是不同的数据建模选择——tenon 这种设计更规范(避免树深度和 category 枚举硬编码耦合),不算“功能缺失”,是既有的、文档已确认的架构决策,未发现需要对齐 SimpleAdmin 的理由。
  - 左侧点击行切换选中类型 + 右侧卡片实时联动、类型/项增删改后 `useDictStore().invalidate(code)` 失效下拉缓存,交互闭环完整;删除类型有确认文案“其下字典项将一并删除”,已核实后端 `DictService.DeleteTypeAsync` 确实级联清理 `SysDictItem`(`backend/src/TenonAdmin.Services/Dict/DictService.cs:69-76`),文案与行为一致,未发现问题。
  - [待确认] SimpleAdmin 支持勾选多行批量删除(父表/子表都有),tenon 只能逐条删除。是否需要批量删除属于交互设计改动,与 R4 用户管理页提到的同类诉求一致,按规则待确认,不动手。
  - [答复] 需要批量删除
- **Bug**:未发现问题。类型行内 `StatusSwitch`/操作按钮均包了 `stopPropagation`(避免点击冒泡误切选中行),`loadItems()` 有竞态守卫(`selectedType.value?.code === code` 校验后才回写),均与 R5 计划文档记录的两处 ultracode 评审修复一致,已核实代码确实生效。
- **美观度**:选中行浅底色高亮(`--n-merged-td-color-hover` 兜底)、左右两栏 flex 布局在窄屏会换行,未发现问题。
- **菜单归属**:“字典管理”(`Id=60`)与“配置管理”同挂在 `ParentId=20`(目录标题“字典配置”)下——这与【配置管理】小节里提到的问题是同一处:目录名偏“字典”导向,但装了配置管理和字典管理两个模块,这里因为字典管理本身贴合“字典配置”这个目录名,归属没有问题;矛盾点在“配置管理”那侧,已在对应小节记录,不重复记录。
- **可优化/新增功能**:未发现新增建议;当前双表模型 + 主从联动已经覆盖字典管理的核心需求,SimpleAdmin 的单表树形反而是更受限的设计(不支持独立的类型级配置字段如 `remark`),不建议对齐。

## 岗位管理 position

对照:`web/src/views/system/position/index.vue`(+ `positionApi` + `PositionInput/SysPosition` 类型 + `position.*` i18n)vs `D:/SimpleAdmin/web/src/views/sys/organization/position`(含 `components/form.vue`)。同时对照 `docs/plan-frontend-admin-pages.md` R6 详情/备注(“纠偏:岗位与机构无关联”,确认 tenon 的 `PositionInput` 刻意不含 `orgId`)。

- **功能/易用性**:
  - [已修复] `web/src/views/system/position/index.vue:30` 的“岗位编码”列写了 `search: true`,但后端 `PositionPageInput`(`backend/src/TenonAdmin.Services/Position/PositionModels.cs:22-26`)只有 `Name` 一个过滤字段,`PositionService.PageAsync` 的 `WhereIF` 也只按 `Name` 过滤(`PositionService.cs:15`),整条链路都不支持按编码搜索。UI 上却渲染了一个“岗位编码”搜索框,用户输入后请求实际只带 `Name`,编码搜索静默失效,是明显的功能性 bug(看着能搜,实际搜不到)。已把该列的 `search: true` 去掉(改为普通展示列,不再提供搜索框),`positionApi.page` 本就只接受 `name`,未改动接口/类型;`npm run lint` 通过,`npm run typecheck` 改动前后该文件报错集合完全一致(4 条,均为环境缺包噪音)。
  - SimpleAdmin 的职位与机构强绑定(`orgId` 必填 + 组织树侧栏筛选 + “职位分类”字典枚举),tenon 按计划文档"纠偏"为职位与机构无关联的独立列表,这是已记录在案的刻意设计决策(避免过度耦合),不算缺陷,未发现需要对齐的理由。
  - 名称搜索、启停(`StatusSwitch` 走全量 `update`)、编辑禁用 `code` 等交互与其他 CRUD 页一致,未发现额外易用性问题。
- **Bug**:除上面已修复的“编码搜索不生效”外,未发现其他问题。
- **美观度**:与其他 CRUD 页风格一致,未发现问题。
- **菜单归属**:岗位管理菜单归属未在本轮改动范围内,与其他系统管理类菜单结构一致,未发现问题。
- **可优化/新增功能**:
  - [待确认] 若后续确有“按编码搜索职位”的需求,需要后端 `PositionPageInput` 加 `Code` 字段 + service 层加 `WhereIF`,这是接口改动,记录待你确认是否需要,现状是前端已去掉了这个不起作用的搜索框(功能范围回到“仅按名称搜索”)。
  - [答复] 不需要

## 在线会话 session

SimpleAdmin 无对应页面(未做在线会话管理),按 GOAL 指引改为:核对 `web/src/views/system/session/index.vue`(+ `sessionApi` + `OnlineSessionItem` 类型 + `session.*` i18n)与后端 `SysSessionController`/`SessionService`/`SessionPageInput` 的实现是否一致,并与 tenon 自身其他只读页(登录/操作日志)的交互规范做一致性评估;同时对照 `docs/plan-frontend-admin-pages.md` R7 详情/备注核实。

- **功能/易用性**:
  - 页面注释“后端仅按 UserId 过滤,故不设业务搜索列”——已核实 `backend/src/TenonAdmin.Services/Session/SessionModels.cs:27-31` 的 `SessionPageInput` 确实只有 `UserId` 一个可选过滤字段,`SessionService.ListOnlineAsync`(`SessionService.cs:158-162`)也只 `WhereIF(input.UserId.HasValue, …)`。列表的 `account` 列因此**没有**标 `search: true`,与 R6 岗位管理那次“搜索框标了但后端不支持”的错误正好相反——这里是正确的做法(不给用户一个看着能用实际不起作用的搜索框),值得作为其他模块的参照写法。
  - 踢人走 `useConfirm({type:'warning'})` 二次确认、踢自己的会话按钮置灰并显示“当前会话”(`r.userId === userStore.userInfo?.userId`),防误踢自己下线,体验上比大多数后台管理系统更细致。
  - 与 R7 计划文档描述(“只读 + 踢人,无搜索列,踢自己置灰”)完全一致,未发现额外易用性问题。
- **Bug**:未发现问题。`ip` 有 `|| '—'` 兜底;`kick` 失败时 `confirm()` 内部走 `action` 的 catch(与其他页 `useConfirm` 用法一致),成功才 `refresh()`。
- **美观度**:与登录/操作日志等只读 ProTable 页风格一致,未发现问题。
- **菜单归属**:“在线会话”页节点(`Id=81`)与登录/操作日志、菜单/模块管理同挂 `ParentId=1`(“系统管理”),归属合理;在线码(`Id=5`)、强退码(`Id=6`)复用已有按钮,未重复种权限码,未发现问题。
- **可优化/新增功能**:
  - [待确认] `OnlineSessionItem`(`web/src/types/api.ts:166-173`)没有设备/浏览器字段(不像登录日志至少还有 `userAgent`),同一账号多端登录时管理员无法从列表分辨要踢的是哪台设备,只能看 IP。补充设备信息需要后端在会话记录里落一份 UA/设备摘要,属于数据模型改动,记录待你确认是否需要;不属于本轮前端小修范围。
  - [答复] 可以要

## 文件管理 file

对照:`web/src/views/system/file/index.vue`(+ `fileApi` + `FileUpload` 组件 + `SysFile/FileUploadOutput` 类型 + `file.*` i18n)vs `D:/SimpleAdmin/web/src/views/sys/dev/file`(含 `components/fileInfoCell.vue`、`fileToolbar.vue`)。同时对照 `docs/plan-frontend-admin-pages.md` R8 详情/备注(FileUpload 组件设计、下载走 blob、`bodySerializer` 手写 FormData)。

- **功能/易用性**:
  - [已修复] `formatSize()`(`web/src/views/system/file/index.vue:22-26`)只到 MB 档,没有 GB 分支——系统未配置任何上传大小限制(已核实后端无 `MaxFileSize`/`MaxRequestBodySize` 类配置),超过 1GB 的文件会显示成不直观的四位数 MB(如“5000.0 MB”)。已加一档 GB 判断(`bytes < 1024**3` 才用 MB,否则 `/1024/1024/1024` 转 GB),纯前端展示函数、几行、不改类型/接口。`npm run lint` 通过;`npm run typecheck` 该文件报错集合前后一致(5 条,均为环境缺包噪音)。
  - “文件名”列 `search:true` 已核实与 `fileApi.page` 的 `originalName→FileName` 映射一致,搜索确实生效,不是 R6 那类假搜索框问题。
  - [待确认] SimpleAdmin 支持多存储引擎(LOCAL/MINIO 等,列展示存储桶/路径/引擎标签)、上传人列、批量删除;tenon 当前是单一本地存储、只读列表无上传人列、逐条删除。核实后端 `SysFile` 实体(`backend/src/TenonAdmin.Services/Entities/SysFile.cs`)确实只有单一 `StoragePath`,没有引擎/桶概念——tenon 是内核项目刻意做的简化存储模型(见文件头注释"物理字节由 IFileStorage 存,本表只记账"),不算缺陷;但 `SysFile` 继承 `BaseEntity` 其实**已经有 `CreateUserId`**(上传人,AOP 自动填充),后端分页接口也是整实体序列化返回,只是前端 `SysFile` 类型(`web/src/types/api.ts:147-155`)和列表都没有把它取出来展示。是否要加“上传人”列(以及要不要顺带解析成用户名,类似 R3 操作日志提到的同类问题)属于产品范围决策,记录待你确认,不动手。
  - [答复]暂时不要，这个上传文件功能暂时还没找到有什么意义
  - 其余交互(上传即用 `FileUpload` 组件触发刷新、下载走 blob + `<a download>` + `revokeObjectURL`、删除走二次确认)与 R8 计划文档一致,未发现额外易用性问题。
  
- **Bug**:除上面已修复的大文件展示问题外,未发现其他问题。`extension`/`contentType` 都有 `|| '—'` 空值兜底。

- **美观度**:与其他只读 + 操作列的 ProTable 页风格一致,未发现问题。

- **菜单归属**:文件管理页菜单归属未在本轮改动范围内,与其他系统管理类菜单结构一致,未发现问题。

- **可优化/新增功能**:
  - [待确认] 出于安全考虑,`SysFile.storagePath`(存储相对路径)刻意没有在前端展示——这是合理的默认(避免暴露服务器内部路径结构),不建议对齐 SimpleAdmin 展示该字段,仅记录说明未被漏掉,不需要处理。
  
    [答复]可以
  
  - [待确认] 是否需要批量删除,与 R4/R5 已提到的同类诉求一致,记录待你确认。
  
  - [答复] 需要

## 机构管理 org

对照:`web/src/views/system/org/index.vue`(+ `orgApi` + `OrgInput/SysOrg` 类型 + `OrgTreeSelect` 组件 + `utils/tree.ts` + `org.*` i18n)vs `D:/SimpleAdmin/web/src/views/sys/organization/org`(含 `components/form.vue`、`copy.vue`)。同时对照 `docs/plan-frontend-admin-pages.md` R9 详情/备注(“照抄 menu/index.vue 树表范式”)核实实现。

- **功能/易用性**:
  
  - tenon 用**裸 `n-data-table` 树表**(`buildTree` 平铺转树 + `default-expand-all`)展示机构层级,SimpleAdmin 用**扁平分页表 + 左侧组织树筛选侧栏**(`TreeFilter`)。前者是 ProTable 组件本身不支持树形行导致的必然选择(文件头注释已写明),照抄了本仓库 `menu/index.vue` 的既有范式,浏览整棵机构树时比"选中一个节点再看其直接子节点"的侧栏筛选模式更直观,不算缺陷,未发现需要对齐 SimpleAdmin 的理由。
  - [待确认] SimpleAdmin 的机构表单还有“组织全称”“分类”(字典枚举,如公司/部门)“指定主管”(用户选择器绑定负责人)、以及批量选择后的“复制组织”(整支子树克隆)。tenon 目前只有 name/code/sort/enabled 四个基础字段,没有这些。“指定主管”和“分类”与【用户管理】小节里提到的“SimpleAdmin 有更丰富 HR 档案字段、tenon 更简"是同一类差异,是否需要对齐属于产品范围决策;“复制组织”是纯粹的新功能,对批量搭建机构树很有用但要新写接口和交互,均记录待你确认,不动手。
  - [答复] 作为一个用户来说你觉得有必要吗？
  - 新增下级(`openAdd(r.id)`)、编辑时上级机构选择器用 `OrgTreeSelect :exclude-subtree-of` 防止选到自己或子孙(防成环)、`code` 编辑禁用、启停走全量 `update`,均与 R9 计划文档一致,未发现额外易用性问题。
  
- **Bug**:未发现问题。删除有子机构的节点时,已核实后端 `OrgService.DeleteAsync`(`backend/src/TenonAdmin.Services/Org/OrgService.cs:72-78`)确实会因 `AnyAsync(o => o.ParentId == id)` 抛 `ErrorCode.OrgHasChildren`(42004),且该错误码标了 `[MsgKey("error.org.hasChildren")]`,前端 `zh-CN.ts:398` 也确实有 `hasChildren: '存在子机构,不可删除'` 对应翻译,整条链路(后端校验 → msgKey → i18n → `translateError` 弹出)是通的,不是只有代码注释声称却没验证的“假设成立”。

- **美观度**:与 `menu/index.vue` 树表风格一致,`n-card` + 顶部标题栏 + 树表主体,未发现问题。

- **菜单归属**:
  - “机构管理”(`Id=70`)与“用户管理”“岗位管理”同挂 `ParentId=10`(目录标题“组织管理”),目录命名准确贴合内容,是本轮 9 个模块里菜单目录命名最合适的一个(相比【配置管理】小节提到的“字典配置”目录命名有点勉强)。
  - [待确认] 三个菜单节点的 `Sort` 依次是 用户管理=0、岗位管理=10、机构管理=14,即侧栏顺序是“用户→岗位→机构”。机构在概念上是用户/岗位的归属主体,放在最后略反直觉(通常期望先建机构、再建岗位、再建用户)。调整顺序只需要改 `Sort` 数值,风险很小,但仍然是菜单种子改动,按规则待确认,不动手。
  - [答复] 按你想得来
  
- **可优化/新增功能**:
  - [待确认] “复制组织”(克隆整支子树)对批量搭建相似机构结构(如连锁网点)有实际价值,是否要做取决于产品是否有这类场景,记录待你确认。
  
    [答复] 这是要给企业级的后台管理，应该有这个场景
