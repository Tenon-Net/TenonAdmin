# 巡检台账

last-seen: 9519ebd
last-tree: 45c5d3f
dry-streak: 0

## 待扫

<!-- 后端队首(先排空后端,再轮到前端) -->
- backend/src/TenonAdmin.Core/Security/ISecurityPolicyProvider.cs
- backend/src/TenonAdmin.Services/Auth/AuthService.cs
- backend/src/TenonAdmin.Services/Entities/SysModule.cs
- backend/src/TenonAdmin.Services/Entities/SysNotice.cs
- backend/src/TenonAdmin.Services/Entities/SysNoticeReceiver.cs
- backend/src/TenonAdmin.Services/Entities/SysUser.cs
- backend/src/TenonAdmin.Services/Module/ModuleModels.cs
- backend/src/TenonAdmin.Services/Module/ModuleService.cs
- backend/src/TenonAdmin.Services/Notice/NoticeModels.cs
- backend/src/TenonAdmin.Services/Notice/NoticeService.cs
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

（暂无）

## 轮次日志

### 第 1 轮 — 后端规范轴 · 扫了 PersonalController.cs / DemoModeFilter.cs / TenonAdminSetup.cs / ErrorCode.cs / TenonAdminOptions.cs · 修 TenonAdminOptions.Database 缺失 `/// <summary>`(§0.2/§1.13,11 个同级属性唯一漏标)→ 后端闸门绿(build 0 err，test 267/0/0，commit 9519ebd)。队列剩 82。NEXT: 继续排后端队首 5 个(ISecurityPolicyProvider / AuthService / SysModule / SysNotice / SysNoticeReceiver)。
