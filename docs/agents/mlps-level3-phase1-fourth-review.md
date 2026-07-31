# 等保三级一期第四轮复审

基线：工作区相对 `HEAD` 的未提交改动。范围仅限内核的等保三级一期应用安全能力；本文不构成“通过等保三级”结论。

## 结论

上一轮的 TOTP 登录已改回 `openapi-fetch`，邀请撤销也已补上服务层授权，高敏权限追加项已有管理闭环。本轮复审曾发现以下阻断项，现已按“修复状态”完成处理；记录保留原始问题和证据以便追溯。

## 修复状态

本记录所列问题已在本轮修复并通过定向验证：

- `DefaultMenuSeed` 已补齐 MFA、高敏权限和安全基线端点的权限锚点；子管理员可在既有角色菜单中被授权。
- 带参数删除端点统一使用 `{id:long}`，并新增默认高敏集合回归断言。
- `PermissionCodeConsistencyTests` 改为复用 `PermissionCode.Build`，且正确处理绝对动作路由。
- 两端已由运行中的后端 OpenAPI 重新生成 `schema.d.ts`，`highSensApi` 不再使用弱类型断言。

定向后端测试 49/49、两端类型检查和 `git diff --check HEAD` 均通过。完整后端测试进程在外层输出窗口超时后退出，结果没有被可靠回收，不能据此宣称全量套件已通过。

## 问题

### [高] 新增受权端点没有权限种子，子管理员无法被正常授权

`PermissionCodeConsistencyTests.Every_permission_endpoint_is_seeded_or_explicitly_known_unseeded` 失败，列出了以下既未写入 `DefaultMenuSeed`、也未登记为仅超管端点的路由：

- `POST:/api/v1/sys/mfa/invite`
- `DELETE:/api/v1/sys/mfa/invite/{id:long}`
- `POST:/api/v1/sys/mfa/reset`
- `GET:/api/v1/sys/mfa/high-sensitivity`
- `POST:/api/v1/sys/mfa/high-sensitivity`
- `DELETE:/api/v1/sys/mfa/high-sensitivity/{id:long}`
- `GET:/api/v1/sys/security/baseline`

位置：`backend/src/TenonAdmin.AspNetCore/Controllers/MfaController.cs`、`backend/src/TenonAdmin.AspNetCore/Controllers/SecurityController.cs`、`backend/tests/TenonAdmin.Tests/PermissionCodeConsistencyTests.cs:35`。

影响：`[RolePermission]` 要求角色持有与路由一致的权限码；没有菜单种子，管理端无法在既有角色-菜单界面向子管理员授予这些端点，调用将静默得到 403。补齐对应菜单权限种子（包括正确的 `{id:long}` 模板）及前端可达入口；只有确实计划仅供超管的端点才登记 `KnownUnseededEndpoints`，不能用登记掩盖本应可委派的管理能力。

### [中] 权限一致性测试错误合并绝对路由，且未反映运行时权限码

失败输出还包含 `GET:/api/v1/sys/security/api/v1/sys/level3/precheck`。这不是 `SecurityController` 的实际 URL：`[HttpGet("/api/v1/sys/level3/precheck")]` 是绝对路由，运行时 `RolePermissionAttribute` 也直接以动作模板生成 `GET:/api/v1/sys/level3/precheck`。但测试的 `Combine` 无条件把控制器前缀拼到动作模板前，见 `backend/tests/TenonAdmin.Tests/PermissionCodeConsistencyTests.cs:105`。

影响：该回归锁既让当前测试套件错误失败，也无法正确覆盖绝对路由的真实权限码。让测试使用与运行时相同的 `PermissionCode.Build`/路由合并规则，或者在 `Combine` 中对以 `/` 或 `~/` 开头的动作模板返回其绝对模板；并将实际别名端点按产品决策种子化或登记为仅超管。

### [高] 带路由约束的删除端点与服务层/高敏默认权限码不一致

控制器使用 `{id:long}`：

- `DELETE api/v1/sys/mfa/invite/{id:long}`，见 `MfaController.cs:36`
- `DELETE api/v1/sys/mfa/high-sensitivity/{id:long}`，见 `MfaController.cs:117`

但 `HighSensitivityPermissions` 使用 `{id}`：

- `MfaInviteRevoke`，见 `backend/src/TenonAdmin.Services/Mfa/HighSensitivityPermissions.cs:18`
- `HighSensDelete`，见同文件第 27 行。

`PermissionCode.Build` 只做大小写和前导斜杠归一化，不会去除参数约束，见 `backend/src/TenonAdmin.AspNetCore/Security/PermissionCode.cs:14`。因此角色授权管道校验 `{id:long}`，而 `MfaEnrollmentService.RevokeBindInviteAsync` 和 `HighSensitivityPermissionService.DeleteAsync` 又校验 `{id}`；普通管理员即使从路由清单取得正确权限，也会在服务层再次被拒绝。与此同时，默认高敏集合也无法识别真正的删除权限，导致该权限不会触发登录阶段的 MFA 强制策略。

修复方式二选一并保持全链路一致：将常量与默认集合改成 `{id:long}`，或移除控制器约束改为 `{id}`。随后增加覆盖“非超管、持菜单实际权限码”的撤销邀请和删除自定义高敏项的集成测试，并断言该权限被 `IMfaPolicyService` 识别为高敏。

### [中] 高敏权限管理 API 未进入两端 OpenAPI 合同，前端以弱类型断言绕过生成类型

两份 `schema.d.ts` 只包含新登录端点 `/api/v1/auth/login/totp`，没有 `/api/v1/sys/mfa/high-sensitivity` 三个端点；但 API 包装通过 `client as unknown as { GET/POST/DELETE... }` 和字符串插值调用它们：

- `web/src/api/index.ts:186`
- `web-react/src/api/index.ts:200`

这违反 `docs/coding-standards.md` 的 OpenAPI 合同要求，类型检查无法发现服务端的请求、响应或路径变更。启动后端后对两个前端执行 `npm run gen:api`，删除弱类型包装，使用生成的字面量路径与 `params.path`。

## 验证

- `git diff --check HEAD`：通过。
- `npm run typecheck`：`web/` 通过。
- `npm run typecheck`：`web-react/` 通过。
- `dotnet test backend\\TenonAdmin.slnx --filter "FullyQualifiedName~MfaEnrollmentTests|FullyQualifiedName~Level3SessionCsrfTests|FullyQualifiedName~Level3PolicyFloorTests|FullyQualifiedName~ExternalAuthTests|FullyQualifiedName~PermissionCodeConsistencyTests" --no-restore`：失败，49 项中 48 通过、1 失败；失败即上述权限种子一致性测试。
