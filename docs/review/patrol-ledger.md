# 巡检台账

last-seen: 127412d
last-tree: 45c5d3f
dry-streak: 0

## 待扫

<!-- 空:真实代码(后端 28 文件 + 整个 web/src)已全扫完;worktree 已退回已扫的稳定态(45c5d3f,= 轮 16/17 所扫内容,index.ts/ext//index.spec.ts 未变不重入队)。等新提交(last-seen 之后)或新工作区变更(last-tree 变化)入队。 -->

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
