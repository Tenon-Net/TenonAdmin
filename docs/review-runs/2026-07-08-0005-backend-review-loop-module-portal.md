# TenonAdmin 后端 review loop：module-portal

## 本轮范围

- 聚焦子域：多应用门户 / MenuService 模块访问权反推 / RBAC Enabled 口径一致性。
- 审查文件范围：
  - `backend/src/TenonAdmin.Services/Menu/MenuService.cs`
  - `backend/src/TenonAdmin.Services/Security/RbacPermissionProvider.cs`
  - `backend/tests/TenonAdmin.Tests/ModulePortalTests.cs`
- 设计约束来源：
  - `docs/rebuild-design.md`：模块访问权由菜单授权反推；权限码保持模块无关。
  - `docs/dev-plan.md`：M1.5 多应用门户后端增量已落，测试 65→73。
  - `docs/phase2-review.md`：继续保持权限/会话/信封等安全口径一致。

## 当前基线（git/build/test）

- `git status --short`：开始本轮前工作树已有大量 Phase 2 / M1.5 相关修改与未跟踪文件；本轮只在该既有工作树上追加小范围门户修复与测试。
- 开始基线：
  - `dotnet build backend/TenonAdmin.slnx -c Release --no-incremental`：通过，0 warning / 0 error。
  - 第一次并行跑 `dotnet test ... --no-build` 早于 build 产物生成完成，报找不到测试 DLL；build 完成后重跑通过。
  - `dotnet test backend/tests/TenonAdmin.Tests/TenonAdmin.Tests.csproj -c Release --no-build`：通过，73/73。
- 修复后基线：
  - `dotnet test backend/tests/TenonAdmin.Tests/TenonAdmin.Tests.csproj -c Release --filter "FullyQualifiedName~ModulePortalTests"`：通过，6/6。
  - `dotnet build backend/TenonAdmin.slnx -c Release --no-incremental`：通过，0 warning / 0 error。
  - `dotnet test backend/tests/TenonAdmin.Tests/TenonAdmin.Tests.csproj -c Release --no-build`：通过，74/74。

## 已确认并已修复的问题

### 已停用菜单仍可让门户展示模块入口

- 位置：`backend/src/TenonAdmin.Services/Menu/MenuService.cs`
- 现象：`MenuService.GetMyModulesAsync` 通过角色菜单关联反推用户可访问模块时，原先把 `sys_menu` 全表读入内存，未过滤 `Enabled=false` 的菜单。
- 影响：当角色仍残留一个已停用菜单的授权关系时：
  - `RbacPermissionProvider` 已经按 `m.Enabled && m.Permission != ""` 过滤，不再授出该菜单权限码；
  - 但门户 `/api/v1/personal/modules` 仍可能因为该停用菜单上溯到顶级目录 `ModuleId`，把对应模块展示给用户；
  - 前端会出现“看到应用入口但实际无生效权限/菜单”的错位。
- 修复：`GetMyModulesAsync` 反推模块时只把启用菜单纳入 `byId`，让门户模块访问权与 RBAC 生效权限口径一致。
- 回归：新增 `ModulePortalTests.Disabled_menu_grant_does_not_expose_module`：
  - 创建普通用户并授权内置 ping 菜单；
  - 再停用该菜单；
  - 断言 `/api/v1/personal/modules` 返回空模块列表；
  - 断言 `/api/v1/ping` 返回 403，证明权限热路径也未授出停用菜单权限码。

## 已确认但暂不修改的问题

- 父级目录停用是否应级联压制子级按钮权限，当前还不是已确认实现缺陷：
  - `RbacPermissionProvider` 当前只检查被授权菜单自身 `Enabled`，不向上检查祖先目录是否启用。
  - `MenuService.GetMyMenuTreeAsync` 构树时只读启用菜单，父目录停用会让导航树缺失父路径。
  - 是否要求“停用目录即停用整棵子树权限”属于产品/权限口径选择，影响 RBAC 热路径与授权语义，本轮不硬改，仅记录为下一轮可专项确认的问题。

## 验证证据

```text
> dotnet test backend/tests/TenonAdmin.Tests/TenonAdmin.Tests.csproj -c Release --filter "FullyQualifiedName~ModulePortalTests"
已通过! - 失败: 0，通过: 6，已跳过: 0，总计: 6，持续时间: 6 s - TenonAdmin.Tests.dll (net10.0)
```

```text
> dotnet build backend/TenonAdmin.slnx -c Release --no-incremental
已成功生成。
    0 个警告
    0 个错误
```

```text
> dotnet test backend/tests/TenonAdmin.Tests/TenonAdmin.Tests.csproj -c Release --no-build
已通过! - 失败: 0，通过: 74，已跳过: 0，总计: 74，持续时间: 9 s - TenonAdmin.Tests.dll (net10.0)
```

## 本轮改动文件

- `backend/src/TenonAdmin.Services/Menu/MenuService.cs`
- `backend/tests/TenonAdmin.Tests/ModulePortalTests.cs`
- `docs/review-runs/2026-07-08-0005-backend-review-loop-module-portal.md`

## 下一轮建议

1. 继续聚焦多应用门户：审查 `ModuleService` 的编码规范化（trim/大小写）与软删唯一键在 SQLite/MySQL 下的行为是否一致。
2. 专项确认“目录禁用是否应级联压制子节点权限”的产品口径；若确认需要级联，再补 `RbacPermissionProvider` 祖先链过滤与对应测试。
3. 审查 `PersonalService.GetMyModulesAsync` 返回的 `DefaultModuleId` 是否需要在模块被禁用/删掉/失权后降级为 `null`，避免前端拿到 stale default。
