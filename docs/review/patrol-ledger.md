# 巡检台账

last-seen: c32d29c
last-tree: 021743c
dry-streak: 0

<!-- last-tree 哈希法第 23 轮修正:旧法 `git ls-files -s <paths>|hash-object` 只反映索引/HEAD 树、对未暂存工作区改动不敏感(纯 worktree 改动会被误判无变更而漏扫)。新法内容敏感:`{ git status --porcelain=v1 -- P; git diff HEAD -- P; git ls-files -o --exclude-standard -- P|xargs git hash-object; }|git hash-object --stdin`(P=backend/src web/src)。021743c 为新法值,与旧法 1a5538c 不可比。 -->

## 待扫

<!-- 第 23 轮:SMS OTP/MFA 全栈特性在工作区落地(porcelain 21 文件,全未提交·全避让)。入队全部,本轮取后端队首 5(ISmsSender/AuthController/CacheKeys/ErrorCode/AdminSecurityOptions)扫毕移除,余 16 如下(仍全避让;真正扫描待其提交后由 committed 门控重入队)。 -->
后端(10,避让):
- backend/src/TenonAdmin.Services/Auth/AuthService.cs
- backend/src/TenonAdmin.Services/Auth/IAuthService.cs
- backend/src/TenonAdmin.Services/Auth/LoginModels.cs
- backend/src/TenonAdmin.Services/Config/ConfigModels.cs
- backend/src/TenonAdmin.Services/Config/ConfigService.cs
- backend/src/TenonAdmin.Services/Seed/ConfigSeed.cs
- backend/src/TenonAdmin.Services/ServicesSetup.cs
- backend/src/TenonAdmin.Services/Sms/ISmsOtpService.cs
- backend/src/TenonAdmin.Services/Sms/LoggingSmsSender.cs
- backend/src/TenonAdmin.Services/Sms/SmsOtpService.cs
前端(6,避让):
- web/src/api/index.ts
- web/src/api/schema.d.ts
- web/src/composables/useSite.ts
- web/src/locales/en-US.ts
- web/src/locales/zh-CN.ts
- web/src/views/login/LoginForm.vue

## 待裁决

### J1 · §1.11 · 密码过期特性用 `DateTime.Now` 裸调(未走注入的 TimeProvider)
`LastPasswordChangeTime` 的读写在 5 处直接 `DateTime.Now`:AuthService.cs:98,103(本轮扫到)、UserService.cs:179,267、PersonalService.cs:80。§1.11 要求时间统一走注入的 `TimeProvider`。
判断题(不擅改)理由:
- **成系统、跨 3 文件 5 处**:只改 AuthService 两处=症状修,留下另两个服务不一致;根因修要一起动 UserService/PersonalService(不在本轮 5 文件内)。
- **改扩展点构造签名**:AuthService 是 §5.3 子类化模板(主构造函数),加 `TimeProvider` 形参会 source-break 消费者子类(要补传基构造实参)。
- **本地 vs UTC 决策**:SessionService.cs:11 明确"统一走 UTC";本特性用 `DateTime.Now`(本地)。收敛时须定夺存本地还是 UTC——影响过期窗口判断,非纯机械替换。
建议方向:三文件一起改,注入 `TimeProvider`,并由维护者定夺 `GetUtcNow()`(与 SessionService 对齐)还是 `GetLocalNow()`(保现语义)后一次性收敛。
> 覆盖 UserService.cs / PersonalService.cs 同规则,后续扫到这两文件的 §1.11-DateTime.Now 不再重报。
> 追加(第 6 轮):DatabaseInitializer.cs:82 `AppliedTime = DateTime.Now` 同属 §1.11,但为架构版本审计戳(非密码过期特性)、且该类 internal sealed 无子类破坏顾虑,仅剩 UTC/本地一处决策——并入本条随 J1 一起定夺,不单独重报。

### J3 · §2.7 · router/routes.ts:16 `/module` 路由 meta.title 硬编码 '选择应用'
兄弟静态路由都用 i18n 键(menu.profile/password/notice/sessions),唯 `/module` 硬编码中文 '选择应用';英文环境该串不翻译。判断题(不擅改)理由:该路由为顶层(不在 layout 壳内),meta.title 可见性取决于 document.title 如何取用(未核实),修法要定键名 + 双语各加一行(触碰 606 键的共享 locale 大文件)。建议:确认 document.title 消费后改用 menu.* 键并双语补齐。

### J4 · §2.6 · 权限按钮弹窗(ButtonManager)整链路无客户端权限门
menu/index.vue 主表对每个动作严格 v-auth/hasPerm(增/改/删/启停,line 364/387-391/443),但:①打开 ButtonManager 的「配置权限」入口按钮(menu/index.vue:348)无门;②ButtonManager 内「新增按钮/编辑/删除/批量从路由添加」(ButtonManager.vue:243/253/271/272)全无 hasPerm。→ 只读用户(仅 GET menu/tree)能看到这些写按钮,点击才被服务端 403;仅 UX 不一致、非越权([RolePermission] 兜底)。判断题(不擅改)理由:修法要为每按钮定权限码映射(入口/新增→POST menu/add、编辑/删除→PUT/DELETE menu/{id}),跨 menu/index.vue + ButtonManager.vue 两文件,属 UX 设计取舍;建议与主表对齐后统一加门。

### J2 · 文档准确性 · DefaultMenuSeed.cs 取号台账注释自相矛盾
类注释两处"当前最大 Id"打架:行 12「现已用到 113」vs 行 15「新增行取当前最大号 +1(现为 116)」;而 `HasData()` 实际最大 Id 已是 115(114 角色-取用户 / 115 角色-授权用户)。行 12 陈旧,行 15 正确。
非机械修理由:非 §1.* 硬约束(仅取号台账注释陈旧),撞号会被 DatabaseInitializer/SeedIdRangeTests 当场拒(非静默);属文档准确性,建议下次动此文件时把行 12 一并改为「现已用到 115」批量收敛,不单独为一行注释跑后端闸门。

## 轮次日志

### 第 1 轮 — 后端规范轴 · 扫了 PersonalController.cs / DemoModeFilter.cs / TenonAdminSetup.cs / ErrorCode.cs / TenonAdminOptions.cs · 修 TenonAdminOptions.Database 缺失 `/// <summary>`(§0.2/§1.13,11 个同级属性唯一漏标)→ 后端闸门绿(build 0 err，test 267/0/0，commit 9519ebd)。队列剩 82。NEXT: 继续排后端队首 5 个(ISecurityPolicyProvider / AuthService / SysModule / SysNotice / SysNoticeReceiver)。

### 第 2 轮 — 后端规范轴 · 扫了 ISecurityPolicyProvider.cs / AuthService.cs / SysModule.cs / SysNotice.cs / SysNoticeReceiver.cs · 四合规(SysNotice 的 ponytail 正文 2000 上限未被触达,静默);记 1 判断题(J1:密码过期特性 `DateTime.Now` 裸调 §1.11,跨 3 文件 5 处,涉改扩展点构造签名 + UTC/本地决策)→ 记账轮不跑闸门。队列剩 77。NEXT: 继续排后端队首 5 个(SysUser / ModuleModels / ModuleService / NoticeModels / NoticeService)。

### 第 3 轮 — 后端规范轴 · 扫了 SysUser.cs / ModuleModels.cs / ModuleService.cs / NoticeModels.cs / NoticeService.cs · 全合规(头像 ViewUrl + 通知未读实时/全量载入/逐条插入等多处 ponytail 上限本轮 diff 均未触达,静默;ModuleService 靠 PortalGeneration 计数器做跨节点缓存失效,coherent)。无发现,无闸门。队列剩 72。NEXT: 继续排后端队首 5 个(PersonalModels / PersonalService[J1 已覆盖其 §1.11-DateTime.Now] / SecurityPolicyProvider / ConfigSeed / DefaultDataScopeSeed)。

### 第 4 轮 — 后端规范轴 · 扫了 PersonalModels.cs / PersonalService.cs / SecurityPolicyProvider.cs / ConfigSeed.cs / DefaultDataScopeSeed.cs · 全合规(PersonalService.cs:80 §1.11-DateTime.Now 归 J1 不重报;SecurityPolicyProvider 逐键读 ponytail 上限未触达;ConfigSeed 22 行 Id 全落 [1,999] 无碰撞、默认值与 provider 兜底一致;DefaultDataScopeSeed 类摘要缺失但 internal、非 §0.2 公共成员硬约束,按噪声跳过)。无闸门。队列剩 67。NEXT: 继续排后端队首 5 个(DefaultMenuSeed / DefaultModuleSeed / DefaultUserRoleSeed / DefaultUserSeed / UserService[J1 已覆盖其 §1.11])。

### 第 5 轮 — 后端规范轴 · 扫了 DefaultMenuSeed.cs / DefaultModuleSeed.cs / DefaultUserRoleSeed.cs / DefaultUserSeed.cs / UserService.cs · 四合规(权限码=规范化路由对齐;Id 全落 [1,999];UserService 三条安全不变量齐全、DateTime.Now×2 归 J1 不重报、LIKE 元字符/IN 列表两处 ponytail 上限未触达);记 1 判断题(J2:DefaultMenuSeed 取号台账注释 113 vs 116 自相矛盾、实际 max=115,文档准确性)→ 记账轮不跑闸门。队列剩 62。NEXT: 后端只剩 3 个(DatabaseInitializer / SysSchemaVersion / ISeedData),下轮扫完后端;之后轮到前端(§2.* + 契约轴)。

### 第 6 轮 — 后端规范轴 · 扫了 DatabaseInitializer.cs / SysSchemaVersion.cs / ISeedData.cs(后端队列排空)· 两合规(SysSchemaVersion 置于 SqlSugar 层是被迫的正确——同层 DatabaseInitializer 需引用其 Current 做版本闸门,放 Services 会致 SqlSugar→Services 逆向依赖;ISeedData 契约文档详尽);DatabaseInitializer.cs:82 §1.11-DateTime.Now 并入 J1 覆盖(schema 版本审计戳,internal sealed 无子类顾虑)→ 记账轮不跑闸门。队列剩 59(纯前端)。NEXT: 后端已排空,轮到前端 §2.* + 契约轴,队首 5 个(api/index.spec.ts / api/index.ts / api/schema.d.ts / components/ApiSelect/README.md / components/ApiSelect/index.vue)。

### 第 7 轮 — 前端规范轴 + 契约轴 · 扫了 api/index.spec.ts / api/index.ts / api/schema.d.ts(6485 行生成物)/ ApiSelect/README.md / ApiSelect/index.vue · 全合规(api/index.ts §2.1 自留地接缝完整、§2.2 域分组 + toPage 归一 + PascalCase 查询参;ApiSelect 竞态守卫/防抖/无硬编码文案;契约轴:api↔schema 由 typecheck 兜住,抽查 password-policy/unread-count/read-all/role-users 4 最新端点均在 schema.d.ts,无漂移;recycle 的 as any 为动态 {type} 路由已知逃生)。无闸门。队列剩 54。NEXT: 前端队首 5 个(Chart/BarChart.vue / Chart/README.md / CodeBlock/README.md / CodeBlock/index.vue / DictCheckbox/README.md)。

### 第 8 轮 — 前端规范轴 + 契约轴 · 批次 Chart/BarChart.vue(已删)/ Chart/README.md / CodeBlock/README.md / CodeBlock/index.vue / DictCheckbox/README.md(已删)· 3 存活全合规(CodeBlock §2.7 用 t('common.copy'/'common.copied') 且双语 en/zh 均在、§2.9 scoped + 主题变量;两 README 文档准确)。查明批次含 2 个 + 队列另 7 个同族零消费者组件(DictCheckbox/index、DictRadio×2、JsonEditor×2、RoleSelect×2、Chart/BarChart)已被 05d0a42「remove zero-consumer components」删除 → 一并清出队列(队列卫生,非扫描)。无闸门。队列剩 42。NEXT: 前端队首 5 个(DictSelect/README.md / UserPicker/index.vue / composables/useModule.spec.ts / composables/useModule.ts / directives/auth.ts)。

### 第 9 轮 — 前端规范轴 + 契约轴 · 扫了 DictSelect/README.md / UserPicker/index.vue / useModule.spec.ts / useModule.ts / directives/auth.ts · 全合规(UserPicker §2.7 文本全走 t()、userPicker.* 7 键 en/zh 完全对齐、§2.9 scoped + CSS 变量、9999 页 ponytail 上限未触达;useModule 门户逻辑 Naive 无关且失败安全默认;auth.ts v-auth fail-closed/open 收敛于 hasPerm,合 D2)。无闸门。队列剩 37。NEXT: 前端队首 5 个(AppHeader.vue / NoticeBell.vue / locales/en-US.ts / locales/zh-CN.ts / router/routes.ts)。

### 第 10 轮 — 前端规范轴 + 契约轴 · 扫了 AppHeader.vue / NoticeBell.vue / locales/en-US.ts / locales/zh-CN.ts / router/routes.ts · 契约轴亮点:脚本结构化比对两 locale → en/zh 各 606 键、双向零缺口、完全对齐无漂移;AppHeader/NoticeBell 合规(文本走 t()、aria-label a11y、ponytail 上限未触达、原生语言名故意不译)。记 1 判断题(J3:routes.ts:16 /module meta.title 硬编码 '选择应用',§2.7 潜在缺口,可见性/键名待定)→ 记账轮不跑闸门。队列剩 32。NEXT: 前端队首 5 个(stores/app.spec.ts / auth.spec.ts / auth.ts / dict.spec.ts / user.ts)。

### 第 11 轮 — 前端规范轴 + 契约轴 · 扫了 stores/app.spec.ts / auth.spec.ts / auth.ts / dict.spec.ts / user.ts · 全合规(auth.ts §2.4 仅持久化 currentModuleId、hasPerm 超管/未加载/命中三态合 D2、reset 连清 tabs;user.ts 全持久化保登录;三 spec 覆盖 homePath/hasPerm/isDark/afterHydrate 迁移/字典缓存并发去重与失效竞态)。无契约/i18n 触点,无闸门。队列剩 27。NEXT: 前端队首 5 个(theme/mix.ts / types/api.ts / utils/error.spec.ts / utils/tree.spec.ts / views/dashboard/biz.vue)。

### 第 12 轮 — 前端规范轴 + 契约轴 · 扫了 theme/mix.ts / types/api.ts / utils/error.spec.ts / utils/tree.spec.ts / views/dashboard/biz.vue · 全合规(mix.ts 纯色值函数、findings 提的 demo() 已删;error/tree spec 覆盖到位;types/api.ts 手写 DTO 镜像抽查 UserProfile/MySessionItem/NoticePublishInput/AddUserInput 与后端一致无漂移;biz.vue 文本走 t()、biz.* 6 键在且 en==zh 全对齐、真数据无假数字)。无闸门。队列剩 22。NEXT: 前端队首 5 个(module/index.vue / personal/profile.vue / personal/sessions.vue / config/OtherConfig.vue / config/SecurityConfig.vue)。

### 第 13 轮 — 前端规范轴 + 契约轴 · 扫了 module/index.vue / personal/profile.vue / personal/sessions.vue / config/OtherConfig.vue / config/SecurityConfig.vue · 全合规(文本走 t()、scoped+CSS 变量、module 卡片 role/tabindex/键盘激活 a11y、v-auth+hasPerm 双重门)。契约轴重点:SecurityConfig 硬编码 config key 逐一核对后端权威常量——rateLimit.* = AdminRateLimitOptions(AdminSecurityOptions.cs:60-63)、captcha.enabled/type = CaptchaService.cs:16/19、loginLock/password/session.* = SecurityPolicyProvider,全字匹配零漂移;动态构造 config.security.* 24 个 i18n 标签全在且 en==zh。无闸门。队列剩 17。NEXT: 前端队首 5 个(system/dict/index.vue / file/index.vue / log/op/index.vue / menu/ButtonManager.vue / menu/index.vue)。

### 第 14 轮 — 前端规范轴 + 契约轴 · 扫了 dict/index.vue / file/index.vue / log/op/index.vue / menu/ButtonManager.vue / menu/index.vue · 四合规(dict 主从竞态守卫 + dictStore.invalidate 全覆盖 + switch/按钮 stopPropagation;file blob 下载 createObjectURL+revoke;log/op UserSelect 精确筛 operatorId + CodeBlock + daterange;menu/index 每动作 v-auth/hasPerm 严格门、menu.* 全键在且 en==zh)。记 1 判断题(J4:ButtonManager 整链路无客户端权限门,与主表不一致,UX-only、服务端兜底)。无闸门。队列剩 12。NEXT: 前端队首 5 个(system/module/index.vue / notice/index.vue / org/index.vue / position/index.vue / recycle/index.vue)。

### 第 15 轮 — 前端规范轴 + 契约轴 · 扫了 module/index.vue / notice/index.vue / org/index.vue / position/index.vue / recycle/index.vue · 全合规(每动作 v-auth/hasPerm 严格门:module 无权退化只读状态标签、org 每操作按码显隐 + OrgTreeSelect 剪子树防成环、recycle restore/purge 双确认;i18n:recycle.tabs.* 8 动态键全在、module/notice/org/position/recycle 命名空间齐备且 en==zh)。无闸门。队列剩 7。NEXT: 前端队首(role/GrantMenuTable.vue / role/index.vue / session/index.vue / user/index.vue + 工作区 locales/index.ts〔避让记账〕)。

### 第 16 轮 — 前端规范轴 + 契约轴 · 扫了 role/GrantMenuTable.vue / role/index.vue / session/index.vue / user/index.vue / locales/index.ts〔工作区·避让记账〕· 全合规(role/index 每动作 + 更多下拉逐项 hasPerm、删角色前查持有人数警示;user/index 每动作门 + 超管删/停置灰自锁保护、findings 已记 515 行重构不重提;GrantMenuTable 三级勾选 indeterminate 逻辑;session 踢人二确认 + 自踢置灰;locales/index.ts ext 深合并接缝干净,工作区文件仅记账未改)。i18n:role.scope.* 5 动态键全在、role/user/session 命名空间齐备且 en==zh。无闸门。队列剩 2(均工作区:locales/ext/、locales/index.spec.ts)。NEXT: 扫最后两个工作区文件(避让记账),之后队列空→dry-streak 起。

### 第 17 轮 — 前端规范轴 + 契约轴 · 扫了 locales/ext/(README.md 真接缝文档 + zh/en sampleDoc.ts 样例)+ locales/index.spec.ts〔均工作区·避让记账〕· 全合规——消费者扩展接缝验证有效:ext/README 文档准、sampleDoc en/zh 键平行、index.spec.ts 覆盖 withExt 深合并 7 场景(新命名空间/错误码深合并/兄弟键不连坐/locale 隔离/不改入参)。发现工作区正在跑扩展接缝验收测试:api/sample.ts、types/sample.ts、views/sample/、ext.integration.spec.ts 等 SCRATCH 文件(标注"测试后删除")在两次 git 调用间漂移增删——入队但标注避让·可能已删,非合入门控目标。last-tree 推进 405a70b。无闸门。队列剩 4(全 SCRATCH 避让)。NEXT: SCRATCH 若已删按幽灵移除;worktree churn 属正常(用户测试中),真实代码已全扫完(后端 28 + 前端全部)。

### 第 18 轮 — 工作区门控 · 用户扩展接缝验收测试已收尾:api/sample.ts、types/sample.ts、views/sample/、ext.integration.spec.ts 4 个 SCRATCH 全删 → 幽灵移除出队。worktree 退回原始已扫稳定态(hash 45c5d3f;index.ts/ext//index.spec.ts 已扫且未变,不重入队)。last-seen→127412d、last-tree→45c5d3f、dry-streak 保持 0(本轮队列非空、做了幽灵清理)。队列清空,无代码扫描、无闸门。NEXT: 队列空 → 下轮 dry-streak 起(退避 min(3600,900·2^n));真实代码零待扫,等新提交或新工作区变更。

### 第 19 轮 — 前端规范轴 + 契约轴 · 扫了 locales/index.ts / locales/index.spec.ts / locales/ext/README.md(i18n 扩展接缝,第 17 轮曾以工作区避让态验过,现于 a87da64 提交后以已提交态复核)· 全合规(index.ts:withExt/deepMerge glob 接缝——§2.1 上游自留地基础设施、导出 withExt 有站内调用方非死代码、深合并 spread 不改入参;§2.7 无可见文本;契约轴:接缝只合并不新增键、ext/ 现仅 README〔sampleDoc 已删〕→ glob 空、withExt 原样返回、606 键零漂移;index.spec.ts 8 例覆盖已绿;ext/README `[MsgKey]` 去前缀对齐指引准确)。触发源:用户令我先提交工作区那批避让文件(feat a87da64 前端接缝 + docs bb199f6 文件归属文档),门控把其中 3 个 web/src 文件入队。全合规无发现、无机械修。闸门:提交前已跑 npm test 39/39 + typecheck 无错(绿)。last-seen→bb199f6、last-tree→c658ff1、dry-streak 归 0(本轮队列非空、扫了真实代码)。队列出清。NEXT: 队列空 → 下轮 dry-streak 起(退避 min(3600,900·2^n));真实代码零待扫,等新提交或新工作区变更。

### 第 20 轮 — 空转轮 · 门控:bb199f6..HEAD 对 backend/src·web/src 差异为空、工作区干净、哈希仍 c658ff1。区间内唯一新提交 85cf36e 为纯文档站(site/guide getting-started/sync-fork degit 快照上手方式),不碰代码故无入队。队列空 → dry-streak 0→1,last-seen→85cf36e、last-tree 保持 c658ff1。不扫代码、不跑闸门。退避 min(3600,900·2¹)=1800s。队列剩 0。NEXT: 1800s 后重跑门控;仍无 backend/src·web/src 新增则 dry-streak→2(退避 3600s 封顶);等真实代码提交或工作区变更破空转。

### 第 21 轮 — 空转轮 · 门控:85cf36e..HEAD(=b322c10)对 backend/src·web/src 差异为空、工作区干净、哈希仍 c658ff1。区间三提交均不碰代码:67741fe(round-20 台账)、7b298cb(chore(release) 0.1.2 发版准备——changelog + web 版本号,版本号走 package.json 经 Vite define 注入前端、非 web/src 源码)、b322c10(发版 runbook 文档)。队列空 → dry-streak 1→2,last-seen→b322c10、last-tree 保持 c658ff1。不扫代码、不跑闸门。退避 min(3600,900·2²)=3600s(已封顶)。队列剩 0。NEXT: 3600s 后重跑门控;dry-streak≥2 起退避恒封顶 3600s(streak 仍会 3、4… 递增,退避不变);等真实代码提交或工作区变更即归 0 破空转。注:0.1.2 已切版但零代码改动(纯文档 + 版本元数据),巡检面无新增。

### 第 22 轮 — 后端规范轴 + ponytail · 扫了 BaseEntity.cs / SqlSugarSetup.cs〔已提交 5273588〕/ CacheKeys.cs / ErrorCode.cs / AdminSecurityOptions.cs〔工作区·避让记账〕· 全合规。门控破空转:b322c10..5273588 已提交 2(PrimaryId 特性)+ 工作区 4(SMS/MFA 半成品),dry-streak 2→0、last-seen→5273588、last-tree→1a5538c。
  · 已提交 PrimaryId 基类(#8):抽出仅雪花主键基类给无审计明细/子表,BaseEntity:PrimaryId,AOP 匹配 BaseEntity.Id→PrimaryId.Id(同串 "Id",BaseEntity:PrimaryId 故老实体全覆盖 + 明细表插入自动填号,无回归);类级 XML + 取舍(IRepository/ISeedData 仍约束 BaseEntity、明细经 ISqlSugarClient 随主表读写)完整。PrimaryId.Id 无 XML summary 但沿本文件既有列注释惯例(全部列走 SugarColumn.ColumnDescription、零 XML summary)→ 与 5 兄弟列一致非 round-1 式孤例,合规不报。ponytail「暂放 SqlSugar 层待 Core POCO 化再迁」天花板本 diff 未触达、静默。
  · 工作区(避让·不改):短信 OTP/MFA 登录加固半成品——CacheKeys 加 sms/mfa 键(冒号命名空间 + 日期编键自过期,合 §1.8)、ErrorCode 加 40009-40012(smsCodeRequired/Wrong/Expired/smsLoginDisabled 均带 [MsgKey])、AdminSecurityOptions 加 AdminSmsOtpOptions(DB 键 mfa.enabled/smsLogin.enabled 兜底 + 部署期数值,合验证码成法);孤立看均合规,但服务/控制器/前端 i18n 均未落。
  · 无机械修、无新判断题 → 记账轮不跑闸门。队列剩 1(ISmsSender.cs 未扫,避让)。
  · NEXT【契约轴 WATCH·SMS/MFA】特性一旦提交,复扫 ErrorCode/AdminSecurityOptions 时必验:① 4 个新错误码 error.auth.{smsCodeRequired,smsCodeWrong,smsCodeExpired,smsLoginDisabled} 的 zh-CN+en-US i18n 键补齐(ErrorCodeLocaleConsistencyTests 会当场红,漏则机械题);② 新 DB 配置键 sys.security.mfa.enabled / sys.security.smsLogin.enabled 若前端 SecurityConfig.vue 加了开关须字对齐(参轮 13 成法)。下轮:扫 ISmsSender.cs + 届时新提交入队的 SMS 服务/控制器文件。

### 第 23 轮 — 后端规范轴 + ponytail(全避让·记账轮)· 扫了 ISmsSender.cs / AuthController.cs / CacheKeys.cs / ErrorCode.cs / AdminSecurityOptions.cs〔全工作区·避让〕· SMS OTP/MFA 全栈特性在工作区落地(porcelain 21 文件全未提交)。
  · **哈希法修正**(重要):发现旧 last-tree 法 `git ls-files -s|hash-object` 只反映索引/HEAD 树、对未暂存工作区改动不敏感——本轮正是纯 worktree 变更(自 5273588 无新代码提交、仅台账提交 c32d29c),旧法读数不变(1a5538c)会把整个 SMS 特性误判为「无变更」而漏扫。改用内容敏感法(见头部注释),新值 last-tree→021743c。以 porcelain 文件清单为权威入队(规则原文「把结果里的文件路径追加进待扫」),规避该盲区。
  · **门控**:committed 5273588..c32d29c 对代码空(仅台账);worktree porcelain 21 文件全入队。last-seen→c32d29c、last-tree→021743c、dry-streak 保持 0。
  · **本轮 5 文件(避让·记账)**:ISmsSender.cs = 教科书可替换性(抽象在 Core、零厂商 SDK 守运行时依赖纪律、TryAdd 前置替换有文档、LoggingSmsSender 兜底),合规;AuthController 加 4 个 [AllowAnonymous] 端点(login/sms、login/sms/resend、sms/send、sms/login);CacheKeys/ErrorCode/AdminSecurityOptions 见轮 22 已录(sms/mfa 键、40009-40012、AdminSmsOtpOptions)。均合规但半成品。
  · **WATCH 收敛/新增**(供提交后 committed 复扫):
     - [已判 N/A] 4 端点全 [AllowAnonymous] → 绕过 [RolePermission],无需菜单种子权限码(同现有 /auth/login)。
     - [新] ConfigSeed 加种子 Id 23/24(sys.security.mfa.enabled / smsLogin.enabled,Security 组默认 false)→ 验无撞号(SeedIdRangeTests 兜底)。
     - [进行中] i18n:zh-CN 已加 login.{smsLogin,smsCode,smsCodePlaceholder,smsRequired,smsSent,mfaTitle,mfaSub} + error.auth.{smsCodeRequired,smsCodeWrong,smsCodeExpired,smsLoginDisabled}。committed 复扫验 en-US 双语对齐 + error 键与 [MsgKey] 字面一致。
     - [新] 4 匿名发信端点是费用/滥用面 → 验 SmsCooldown/SmsDailyCount/MaxAttempts/RateLimit 守卫齐全(CacheKeys 已备键)。
  · 无机械修、无新判断题 → 记账轮不跑闸门。队列剩 16(后端 10 + 前端 6,全避让)。NEXT: 特性仍在工作区churn,继续按边扫队首后端 5(全避让·记账);待作者提交后 committed 门控自动重入队做真正扫描 + 逐条核 WATCH。
