# 巡检台账

last-seen: f1a7e82
last-tree: 45c5d3f
dry-streak: 0

## 待扫

<!-- 后端已排空;以下为前端(§2.* + 契约轴) -->
- web/src/components/Chart/BarChart.vue
- web/src/components/Chart/README.md
- web/src/components/CodeBlock/README.md
- web/src/components/CodeBlock/index.vue
- web/src/components/DictCheckbox/README.md
- web/src/components/DictCheckbox/index.vue
- web/src/components/DictRadio/README.md
- web/src/components/DictRadio/index.vue
- web/src/components/DictSelect/README.md
- web/src/components/JsonEditor/README.md
- web/src/components/JsonEditor/index.vue
- web/src/components/RoleSelect/README.md
- web/src/components/RoleSelect/index.vue
- web/src/components/UserPicker/index.vue
- web/src/composables/useModule.spec.ts
- web/src/composables/useModule.ts
- web/src/directives/auth.ts
- web/src/layouts/AppHeader.vue
- web/src/layouts/NoticeBell.vue
- web/src/locales/en-US.ts
- web/src/locales/zh-CN.ts
- web/src/router/routes.ts
- web/src/stores/app.spec.ts
- web/src/stores/auth.spec.ts
- web/src/stores/auth.ts
- web/src/stores/dict.spec.ts
- web/src/stores/user.ts
- web/src/theme/mix.ts
- web/src/types/api.ts
- web/src/utils/error.spec.ts
- web/src/utils/tree.spec.ts
- web/src/views/dashboard/biz.vue
- web/src/views/module/index.vue
- web/src/views/personal/profile.vue
- web/src/views/personal/sessions.vue
- web/src/views/system/config/components/OtherConfig.vue
- web/src/views/system/config/components/SecurityConfig.vue
- web/src/views/system/dict/index.vue
- web/src/views/system/file/index.vue
- web/src/views/system/log/op/index.vue
- web/src/views/system/menu/components/ButtonManager.vue
- web/src/views/system/menu/index.vue
- web/src/views/system/module/index.vue
- web/src/views/system/notice/index.vue
- web/src/views/system/org/index.vue
- web/src/views/system/position/index.vue
- web/src/views/system/recycle/index.vue
- web/src/views/system/role/components/GrantMenuTable.vue
- web/src/views/system/role/index.vue
- web/src/views/system/session/index.vue
- web/src/views/system/user/index.vue
<!-- 工作区变更(用户正在改 → 扫到时降级记账,不动代码) -->
- web/src/locales/index.ts
- web/src/locales/ext/
- web/src/locales/index.spec.ts

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
