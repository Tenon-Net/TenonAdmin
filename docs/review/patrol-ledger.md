# 巡检台账

last-seen: effe2c7
last-tree: 45c5d3f
dry-streak: 0

## 待扫

<!-- 后端队首(先排空后端,再轮到前端) -->
- backend/src/TenonAdmin.Services/Personal/PersonalModels.cs
- backend/src/TenonAdmin.Services/Personal/PersonalService.cs
- backend/src/TenonAdmin.Services/Security/SecurityPolicyProvider.cs
- backend/src/TenonAdmin.Services/Seed/ConfigSeed.cs
- backend/src/TenonAdmin.Services/Seed/DefaultDataScopeSeed.cs
- backend/src/TenonAdmin.Services/Seed/DefaultMenuSeed.cs
- backend/src/TenonAdmin.Services/Seed/DefaultModuleSeed.cs
- backend/src/TenonAdmin.Services/Seed/DefaultUserRoleSeed.cs
- backend/src/TenonAdmin.Services/Seed/DefaultUserSeed.cs
- backend/src/TenonAdmin.Services/User/UserService.cs
- backend/src/TenonAdmin.SqlSugar/DatabaseInitializer.cs
- backend/src/TenonAdmin.SqlSugar/Entities/SysSchemaVersion.cs
- backend/src/TenonAdmin.SqlSugar/Seed/ISeedData.cs
<!-- 前端(后端排空后轮到) -->
- web/src/api/index.spec.ts
- web/src/api/index.ts
- web/src/api/schema.d.ts
- web/src/components/ApiSelect/README.md
- web/src/components/ApiSelect/index.vue
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

## 轮次日志

### 第 1 轮 — 后端规范轴 · 扫了 PersonalController.cs / DemoModeFilter.cs / TenonAdminSetup.cs / ErrorCode.cs / TenonAdminOptions.cs · 修 TenonAdminOptions.Database 缺失 `/// <summary>`(§0.2/§1.13,11 个同级属性唯一漏标)→ 后端闸门绿(build 0 err，test 267/0/0，commit 9519ebd)。队列剩 82。NEXT: 继续排后端队首 5 个(ISecurityPolicyProvider / AuthService / SysModule / SysNotice / SysNoticeReceiver)。

### 第 2 轮 — 后端规范轴 · 扫了 ISecurityPolicyProvider.cs / AuthService.cs / SysModule.cs / SysNotice.cs / SysNoticeReceiver.cs · 四合规(SysNotice 的 ponytail 正文 2000 上限未被触达,静默);记 1 判断题(J1:密码过期特性 `DateTime.Now` 裸调 §1.11,跨 3 文件 5 处,涉改扩展点构造签名 + UTC/本地决策)→ 记账轮不跑闸门。队列剩 77。NEXT: 继续排后端队首 5 个(SysUser / ModuleModels / ModuleService / NoticeModels / NoticeService)。

### 第 3 轮 — 后端规范轴 · 扫了 SysUser.cs / ModuleModels.cs / ModuleService.cs / NoticeModels.cs / NoticeService.cs · 全合规(头像 ViewUrl + 通知未读实时/全量载入/逐条插入等多处 ponytail 上限本轮 diff 均未触达,静默;ModuleService 靠 PortalGeneration 计数器做跨节点缓存失效,coherent)。无发现,无闸门。队列剩 72。NEXT: 继续排后端队首 5 个(PersonalModels / PersonalService[J1 已覆盖其 §1.11-DateTime.Now] / SecurityPolicyProvider / ConfigSeed / DefaultDataScopeSeed)。
