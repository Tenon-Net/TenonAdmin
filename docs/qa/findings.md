# TenonAdmin 系统 QA 发现

本文件是 `/loop` 系统测试的唯一存疑/风险账本。极小且确定的 bug 直接修代码，不记在这里（除非修完需要留痕）。

严重度：`P0` 可越权/丢数据/升级炸 · `P1` 功能错误或安全削弱 · `P2` 边界/体验 · `P3` 观感。

状态：`open` 待处理 · `fixed` 已修 · `question` 需产品/设计判断 · `wontfix` 判定非问题（附理由）。

> 禁止在此粘贴密钥、真实密码、exploit payload。安全问题只写条件与影响。

---

## 索引

| ID | 严重度 | 状态 | 表面 | 标题 |
|----|--------|------|------|------|
| QA01 | P1 | fixed | web-react | `LoginPage.spec.tsx` 全量 vitest 品牌用例偶发红（site mock / 竞态；已加固 mount） |
| QA02 | P2 | fixed | web | Vue 模板未设 `strictPort`，5173 被占时会静默挪端口连错应用 |
| QA03 | P2 | fixed | backend | 匿名 `POST /api/v1/auth/mfa/challenge/verify` 已删除 |
| QA04 | P2 | fixed | backend | 自助改密吊销除当前外的其他会话 |
| QA05 | P1 | fixed | backend | MFA 绑定/恢复在账号不存在时每次都 `Hash()` 陪跑，耗时可枚举 |
| QA06 | P1 | fixed | backend | 短信免密登录错码归一为 40011，防枚举 |
| QA07 | P1 | fixed | backend | `[ActiveSession]` 未写入数据范围，仅登录端点上的 DataEntity 查询默认 Unrestricted |
| QA08 | P2 | fixed | backend | 用户/机构按数据范围隔离；职位保持全局 |
| QA09 | P1 | fixed | backend | 角色数据范围仅超管可配置 |
| QA10 | P2 | fixed | backend | 有活跃用户时拒删机构/职位；禁止自操作 |
| QA11 | P1 | fixed | backend | 停用角色后权限码已失效，门户模块/菜单树仍按该角色授权展示 |
| QA12 | P1 | fixed | backend | 字典下拉改为 ActiveSession，登录即可读 |
| QA13 | P2 | fixed | backend | 种子数据禁删；字典值唯一；job 配置独立 Tab |
| QA14 | P2 | fixed | backend | 字典类型停用后 `GetItemsByTypeAsync` 仍返回启用项 |
| QA15 | P1 | fixed | backend | 文件管理按所有者隔离；超管全库 |
| QA16 | P2 | wontfix | backend | 签名直链为永久能力 URL（设计取舍，天花板已在 `IFileUrlSigner` 文档里写明） |
| QA17 | P1 | fixed | backend | `import/validate`·`commit` 不卡 `MaxImportRows`，JSON 入口可绕过文件行数上限 |
| QA18 | P1 | fixed | backend | 覆盖导入 `RoleNames` 留空会把已有角色清掉（`UpdateAsync` 全量重设） |
| QA19 | P2 | fixed | backend | 导入改用编码匹配 + 范围校验 + 角色策略 + 公式转义 |
| QA20 | P2 | fixed | backend | SQL 总闸关闭后，存量 SQL 任务连改 cron/名称也撞 47008 |
| QA21 | P2 | fixed | backend | 系统任务载荷锁定；API 与调度开关独立已补文档 |
| QA22 | P2 | fixed | web+web-react | 回收站页签漏了 `job`，软删任务只能靠 API 恢复 |
| QA23 | P2 | fixed | backend | 软删保留关联，彻底删除时清理；恢复后刷缓存 |
| QA24 | P1 | fixed | backend | `MarkReadAsync` 不校验可见性，任意登录用户可对任意通知 Id 写已读回执 |
| QA25 | P2 | fixed | backend | Hub 校验 sid 活性；通知目标必须有效；头像限本站签名 URL |
| QA26 | P2 | fixed | tests | `SecurityBaselinePrecheckTests` 样本 JSON 落盘路径硬编码到不可写目录，本机红 |
| QA27 | P2 | fixed | backend | 固定窗口保持并补文档；WorkerId DB 租约守卫已加 |
| QA28 | P1 | fixed | backend | Worker 实体扫描漏挂 Services 程序集，开 CodeFirst 时内核表建不全 |
| QA29 | P3 | fixed | tests | 核心服务 TryAdd 契约测试已补；副库 DbType 改为必填 |
| QA30 | P2 | fixed | web | `vite preview` 未设 `strictPort`，5173 被占时会静默挪端口 |
| QA31 | P2 | fixed | web+web-react | LoginOutput 携带 isSuperAdmin，profile 失败回退会话值 |
| QA32 | P3 | fixed | web | 皮肤名 i18n；v-auth 改为响应式 watchEffect |
| QA33 | P2 | fixed | web-react | `vite preview` 未设 `strictPort`（与 Vue QA30 对偶） |
| QA34 | P3 | fixed | web-react | 皮肤名 i18n（对偶 QA32） |
| QA35 | P3 | fixed | web | v-auth 改为响应式，与 React Can 行为对齐 |
| QA36 | P1 | fixed | backend | 角色委派策略：定义仅超管；非超管只能授予可委派角色给范围内用户 |

---

## 条目

### QA01 · P1 · `LoginPage.spec.tsx` 全量 vitest 品牌用例偶发红
- 表面：web-react
- 状态：fixed
- 复现/证据：早期全量红 2 条品牌用例、单文件绿；套件中可见 `ECONNREFUSED :3000`（happy-dom 默认源）。Round 18 全量已 **807/807** 绿。仍加固 `mount()`：`resetModules` 后先钉当前图的 `siteInfo`/`providers`，再渲染，并 `waitFor` site store 落地；`beforeEach` 补 `providersMock.mockReset` + `localStorage.clear`。LoginPage 单文件 31 绿。
- 风险：已修（测试卫生）。业务登录页未改。
- 建议：已修。若 CI 再闪红，再在 `test-setup` 给 fetch 默认拒网。

### QA02 · P2 · Vue 模板未设 `strictPort`
- 表面：web
- 状态：fixed
- 复现/证据：本机 `127.0.0.1:5173` 被另一份仓库的 VitePress 文档站占用（`C:\HuHuHu\TenonAdmin\site`）。`web-react/vite.config.ts` 已 `strictPort: true` 并写明「静默挪端口会连上另一个应用」。`web/vite.config.ts` 原先只有 `port: 5173`，无 `strictPort`——Vite 默认会改绑下一可用端口，`dev.bat`/文档写死的 5173 会打开文档站而不是管理端。
- 风险：本地对照双模板或文档站同时开时，Vue 开发者连错应用、排查成本高；极端情况下 5174 被挪来的 Vue 占掉，React 因 `strictPort` 起不来。
- 建议：已在 `web/vite.config.ts` 的 `server` 段加上 `strictPort: true`。本机要跑 Vue 管理端需先停掉占用 5173 的 VitePress，或给文档站换端口。

### QA03 · P2 · 匿名 MFA 挑战校验端点无调用方
- 表面：backend
- 状态：fixed
- 复现/证据：`MfaController.VerifyChallenge` 为 `[AllowAnonymous]` `POST /api/v1/auth/mfa/challenge/verify`。两套前端 API 封装都没有这个方法（只出现在 OpenAPI `schema.d.ts`）。登录完成走的是 `POST /api/v1/auth/login/totp`（内部同样 `VerifyAndConsumeAsync`）。活探测无效挑战返回 `40019`。持有登录 40018 下发的 `challengeId` + 正确 TOTP 时，该端点会消费一次性挑战并回 `userId`，而不签发令牌。判定为多余攻击面，**已整个删除该端点**；TOTP 登录仍走 `login/totp`，行为不变。两套模板 `schema.d.ts` 已随之重生成（路径消失即回归证据）。
- 风险：已修。修前：可作废一次进行中的 MFA 登录，并多一条披露 userId 的匿名面。
- 建议：已修。第三方/移动端若日后要独立校验挑战，应新开一条带鉴权的端点，不要复活匿名版本。

### QA04 · P2 · 自助改密不吊销其它会话
- 表面：backend
- 状态：fixed
- 复现/证据：`UserService.ResetPasswordAsync` 会 `RevokeAllForUserAsync`（`PasswordResetSecurityTests` 锁定）。`PersonalService.ChangePasswordAsync` 原先只换哈希、清 `MustChangePassword`、写历史，不碰会话。双前端改密成功后本机 logout 重登，其它设备上的旧 refresh 仍可续命。产品语义已拍板为「改密 = 不再信任其它持有者」：自助改密改为吊销除**当前 sid 外**的全部会话（当前会话保留，避免改完即被自己踢下线）。`PersonalSessionTests.ChangePassword_revokes_other_sessions_but_keeps_current` 锁定。
- 风险：已修。修前：账号已改密后，失窃的其它会话继续有效，直到 refresh 自然过期。
- 建议：已修。

### QA05 · P1 · MFA 绑定/恢复账号不存在时每次现算陪跑哈希
- 表面：backend
- 状态：fixed
- 复现/证据：`AuthService.ValidateUserAsync` 用静态 `_dummyHash ??=` 缓存陪跑哈希，使「账号不存在」与「密码错误」代价接近。`MfaEnrollmentService.StartBindAsync` / `UseRecoveryCodeAsync` 原先对不存在账号每次 `hasher.Hash("tenon-admin.timing-dummy")` 再 `Verify`——多一轮 PBKDF2，耗时可区分，也能被拿来砸 CPU。
- 风险：TOTP 开启时，匿名绑定/恢复入口可按耗时枚举账号，并放大 PBKDF2 成本。
- 建议：已改为与登录相同的进程内缓存 `_dummyHash ??=`。`MfaEnrollmentTests` 13 项仍绿。

### QA06 · P1 · 短信免密登录可用 40010/40011 区分手机号是否有真实用户
- 表面：backend
- 状态：fixed
- 复现/证据：`SendSmsLoginCodeAsync` 对未知/重复/停用走 `PretendIssueAsync`（同外形、不存码、不发短信）。`LoginByPhoneAsync` 再 `VerifyAsync`：缓存无码 → `SmsCodeExpired` **40011**；有码但输错 → `SmsCodeWrong` **40010**（带 `attemptsLeft`）。原 `SmsLoginFlowTests` 只断言未知手机号登录是 40011，**没有**断言「已发码用户输错」不得用另一码，`AuthService` 注释写的「与码过期不可区分」与实现不符。已把**免密登录**这条路径的错码归一为 40011 且不回 `attemptsLeft`——已知号错码与未知号错码从此逐字节同形。**密码登录的短信二次挑战不受影响**（调用者身份已确定，无枚举面），仍保留 40010 + 剩余次数文案。`Wrong_code_for_known_phone_normalizes_to_expired_without_attempts_left` 与 `Password_login_sms_challenge_keeps_wrong_code_with_attempts_left` 一起锁住这条分界。
- 风险：已修。修前：短信登录开启时，可枚举哪些手机号对应系统内启用账号。
- 建议：已修。代价是免密登录用户输错码时看不到剩余次数——防枚举的必要取舍；双模板 `smsCodeExpired` 文案本就是合并措辞「短信验证码错误或已失效,请重新获取」，不必改。

### QA07 · P1 · `[ActiveSession]` 未写入数据范围
- 表面：backend
- 状态：fixed
- 复现/证据：`RolePermissionAttribute` 在授权阶段把 `IDataScopeProvider.ResolveAsync` 写入 `IDataScopeContext`。`ActiveSessionAttribute` 原先只验会话，不写范围。`HttpContextDataScopeContext` 未设置时返回 `Unrestricted`。内核个人中心/通知表是 `BaseEntity` 所以当时看不见；消费方在「仅登录」端点上查 `DataEntity`（或只挂 `[Authorize]`）会看到全部机构。本轮已抽 `DataScopeRequestBinder`，两过滤器共用；TestHost 增加 `GET /api/v1/sample/doc/mine`，`SampleDocScopeTests.ActiveSession_endpoint_still_filters_by_data_scope` 锁定。
- 风险：已修。修前：任意登录用户打未绑范围的 DataEntity 列表 = 跨机构读。
- 建议：已修。消费方自建端点不要只挂 `[Authorize]`。

### QA08 · P2 · 用户/机构/岗位不走通用数据范围
- 表面：backend
- 状态：fixed
- 复现/证据：`rebuild-design.md` §6 写明 `sys_user` 继承 `BaseEntity`，用户列表**不**走 `CreateOrgId` 过滤，「如需按机构筛用户在 UserService 显式处理」。`UserService.BuildListQuery` / `OrgService.ListAsync` / `PositionService` 原先均未读 `IDataScopeContext`，持有 `GET:/api/v1/sys/user/page` 即可列出全库账号/手机/邮箱。产品已拍板「机构管理员管本范围用户」：用户列表与增改按 `OrgId ∈ scope.OrgIds` 显式过滤，机构树返回**范围内机构 + 其祖先**（否则树断根渲染不出来），**岗位保持全局**（岗位是公司级字典而非机构资产，刻意不收）。`UserDataScopeTests` 四例锁定：`Non_superadmin_sees_only_users_in_scope_orgs` / `Superadmin_sees_all_users` / `Non_superadmin_cannot_add_user_to_out_of_scope_org` / `Non_superadmin_org_list_returns_only_scoped_plus_ancestors`。
- 风险：已修。修前：把用户管理菜单授给「本机构」角色时，对方仍能看/改其它机构的账号。
- 建议：已修。注意这**改变了既有授权语义**：升级前把用户管理授给非超管的部署，升级后对方可见范围会收窄——这是修复方向，但需在升级说明里点名。

### QA09 · P1 · 配置角色数据范围不校验调用者自身范围
- 表面：backend
- 状态：fixed
- 复现/证据：`RbacService.SetRoleDataScopeAsync` 原先只验角色存在，可写成 `All` 或 `Custom` 任意机构 Id。端点 `PUT:/api/v1/sys/role/datascope` 仅 `[RolePermission]`，持有该权限的非超管可以把**自己的角色**改成 All，随后业务表过滤器对其失效。取的是最干脆的一档：**配置角色数据范围收归超管专属**（而不是「⊆ 调用者范围」的部分放行）——数据范围是权限体系的地基，能改地基的人本就等价于超管。`RbacSuperAdminGuardTests.NonSuperAdmin_with_route_permission_rejected_on_SetDataScope` 锁定：即便路由权限码给全了，非超管仍被拒。
- 风险：已修。修前：数据范围可被「有配范围权限的人」自助拆掉；与 QA08 叠加可先看全用户再给自己扩范围。
- 建议：已修。同批 QA36 把角色定义/菜单授权一并收归超管，四者同源，见该条。

### QA10 · P2 · 删机构/岗位不挡挂靠用户；可删停自己
- 表面：backend
- 状态：fixed
- 复现/证据：`OrgService.DeleteAsync` 原先只挡「仍有子机构」(`OrgHasChildren`)，不查 `SysUser.OrgId`；`PositionService.DeleteAsync` 不查 `PositionId`。软删后用户仍持旧 Id，列表机构名/职位名变空，数据范围 `Org` 仍按残留 `OrgId` 解析。用户 `DeleteAsync`/`SetEnabledAsync` 也不拦「操作对象 = 当前登录用户」。已按「有用户挂靠即拒删」落地（新错误码 `OrgHasUsers` / `PositionHasUsers`），并禁止删除/停用当前登录用户。六例锁定：`Delete_org_with_active_user_returns_OrgHasUsers` / `Delete_org_without_users_succeeds` / `Delete_position_with_active_user_returns_PositionHasUsers` / `Delete_position_without_users_succeeds` / `User_cannot_delete_self` / `User_cannot_disable_self`（成功路径与拒绝路径成对，防「一律拒绝」式假修）。
- 风险：已修。修前：误删叶子机构留下悬挂用户；管理员误删自己后需超管从回收站恢复。非越权。
- 建议：已修。挂靠判定只看未软删用户，所以回收站里的用户不会挡住机构删除。

### QA11 · P1 · 停用角色后门户仍展示其授权
- 表面：backend
- 状态：fixed
- 复现/证据：`RbacPermissionProvider` 用 `InnerJoin SysRole && Enabled` 算权限码；`MenuService.ComputeMyModulesAsync` / `ComputeMyMenuTreeAsync` 原先直接 `userRoles` 取全部角色 Id。`ModulePortalTests` 有停用**菜单**不暴露模块，没有停用**角色**。停用角色后 `GET /api/v1/ping` 已 403，但 `/personal/modules` 仍列出 system。已抽 `ResolveEnabledRoleIdsAsync` 与权限提供者同口径；补 `Disabled_role_does_not_expose_module`。
- 风险：已修。修前：侧栏入口在、点进去接口 403，或误以为仍有权。
- 建议：已修。角色启停走全量 update，`InvalidateByRoleAsync` 会 bump 门户代际，修后即时空。

### QA12 · P1 · 字典下拉热路径要单独授权
- 表面：backend
- 状态：fixed
- 复现/证据：`GET /api/v1/sys/dict/items/{typeCode}` 原先挂 `[RolePermission]`，种子按钮在「系统运维 → 字典」下。用户/机构表单的 `DictSelect`/`useDictOptions` 调这个接口，失败 `.catch(() => {})` 静默留空——只授「组织管理」的角色，用户页性别、机构分类下拉全空。已按「下拉接口降为 `[ActiveSession]`」落地（对照 `password-policy` 的同款处理）：任何已登录用户可读，不挂专门权限码。选它而不是「把权限码并进用户/机构菜单种子」，是因为字典下拉是**跨模块热路径**，每加一个用到字典的模块就要补一次种子授权，迟早再漏一次。写面（增删改类型/项）仍是 `[RolePermission]`。端点的 XML 注释已写明这是 QA12 的有意降级，两套 `schema.d.ts` 同步。
- 风险：已修。修前：非超管、未勾字典查询权时，表单看起来能建用户但性别/分类选不了。
- 建议：已修。只回**启用项**、无写面，所以放宽到「登录即可读」不构成泄漏面；消费者若有敏感字典，应另建端点而不是往这条挂。

### QA13 · P2 · 种子字典/配置可删；项值不唯一
- 表面：backend
- 状态：fixed
- 复现/证据：`gender` / `common_status` / `org_category` 原先无内置保护，`DeleteTypeAsync` 会物理删项；配置 `DeleteAsync` 同样可删 `sys.security.*` 种子键。`SysDictItem` 无 `(DictTypeCode, Value)` 唯一约束，可插两条 value=1。`OtherConfig` 的 `STRUCTURED_GROUPS` 不含 `job`，任务保留天数等出现在「其他」并可当自定义行删掉。三条都已落地：种子数据按 **`Id < 1000`** 判定并拒删（新错误码 `SeedDataProtected`；用 Id 区间而不是逐个硬编码 code，新增种子自动纳入保护）、字典项值按类型唯一（`DictItemValueExists`）、`job` 在双模板都独立成结构化 Tab。四例锁定：`Delete_seed_config_returns_SeedDataProtected` / `Delete_seed_dict_type_returns_SeedDataProtected` / `Delete_seed_dict_item_returns_SeedDataProtected` / `Add_duplicate_dict_item_value_returns_DictItemValueExists`。
- 风险：已修。修前：误删性别字典 → 用户表单下拉空；误删安全键 → 静默回退 Options 默认。
- 建议：已修。**消费者自建种子必须落在 `Id >= 1000`**，否则会被内核当成受保护种子而删不掉；这条约定原先只存在于代码里（5 处硬编码 `Id < 1000`），已在 0.5.5 补进 `skills/create-entity.md`。

### QA14 · P2 · 停用字典类型后下拉仍有项
- 表面：backend
- 状态：fixed
- 复现/证据：`GetItemsByTypeAsync` 原先只滤 `item.Enabled`，不看类型。`UpdateTypeAsync` 会失效缓存，但再查仍把启用项吐出。已改为类型不存在或 `Enabled=false` 返回空；`DictCrudTests.Items_by_type_empty_when_type_disabled` 锁定。
- 风险：已修。修前：管理端停用类型后，用户表单下拉不变。
- 建议：已修。

### QA15 · P1 · 文件管理不按上传人/机构隔离
- 表面：backend
- 状态：fixed
- 复现/证据：`SysFile` 继承 `BaseEntity`（非 `DataEntity`）。`FileService.PageAsync` 原先无 `CreateUserId`/`CreateOrgId` 过滤，`DownloadAsync`/`DeleteAsync` 只按 Id——有分页 + 下载/删除权限即可取走他人头像/附件。已按**按人隔离**落地（而不是改成 `DataEntity` 走机构范围：头像/附件的自然归属是上传者，不是他当时所在机构）：非超管的列表按 `CreateUserId` 过滤，单个下载/删除经 `ValidateFileOwner`，批量删除**先校验全部目标**再动手；越权一律返回 `FileNotFound` 而非「无权限」，不泄漏文件是否存在。超管不受限。
- 风险：已修。修前：文件模块若授给普通业务员，等于共享网盘。
- 建议：已修。**这条的测试曾整体 `[Fact(Skip)]`，等于零覆盖**，已在 0.5.5 解封并修好两个真实卡点（账号取 `Guid.CreateVersion7()` 前 8 位＝毫秒时间戳高位，同测试内建两个用户必撞 42006；夹具依赖种子角色 2 授上传权，实则没有），现 `FileOwnerTests` 四例真实运行：`Non_superadmin_cannot_download_another_users_file` / `..._cannot_delete_...` / `Non_superadmin_can_manage_own_files` / `Superadmin_can_manage_all_files`。

### QA16 · P2 · 签名预览直链无过期
- 表面：backend
- 状态：wontfix
- 复现/证据：`FileUrlSigner.Sign` 只对 `fileId` 做 HMAC，无时间窗。`GET /sys/file/{id}/view?sig=` 匿名；`FileViewUrlTests` 锁伪造/换 Id。头像、Markdown 插图把 URL 写进 HTML/日志/Referer 后，拿到链接的人可一直拉，直到 JWT 主密钥轮换派生新子密钥。软删后 `DownloadAsync` 因过滤器 44004，直链失效。
- 风险：不能靠猜 sig 提权；泄漏面是「曾经合法的 URL」。内联图长期可缓存是产品取舍。
- 建议：**判定 wontfix（设计取舍，非缺陷）**。这是能力链接（capability URL）模型，与 S3 presigned URL 同源；无过期是刻意的：直链会被存进公告正文这类**持久内容**，一条 30 分钟失效的 URL 等于「发布半小时后所有图一起坏」。要过期语义只能改成「正文只存 Id、渲染时现签」，那是另一件事。天花板与两条撤销手段（删文件 / 轮换 `Jwt:SecretKey`）**早已写在 `IFileUrlSigner` 的 XML 文档里**（非本轮新增），不再重复补文档。

### QA17 · P1 · Validate/Commit 绕过导入行数上限
- 表面：backend
- 状态：fixed
- 复现/证据：`AdminExcelOptions.MaxImportRows`（默认 5000）只在 `ImportRunner.PreviewAsync` 流式读 xlsx 时计数。`ValidateAsync` / `CommitAsync` 收前端 JSON 行，原先不检查。向导诚实用户会先 preview；直接打 `POST import/validate|commit` 可一次送超过上限的行。错误报告端点同样按行写 xlsx。已在 Runner 抽 `EnsureWithinRowLimit`，validate/commit 入口调用；`UserController.ImportErrorReport` 同样卡。`ValidateAndCommit_ExceedsMaxImportRows_Throws` 锁定。
- 风险：已修。修前：有导入提交权即可用超大 JSON 打内存/CPU，配置项形同虚设。
- 建议：已修。消费者自建档案若绕过 `IImportRunner` 直写，需自己卡行数。

### QA18 · P1 · 覆盖导入空角色列清空已有角色
- 表面：backend
- 状态：fixed
- 复现/证据：`UserService.UpdateAsync` 对 `RoleIds` 全量 `SetUserRolesAsync`。`UserImportProfile.CommitRowAsync` 覆盖路径把未填的 `RoleNames` 编成空列表传入。可选列语义应是「不改」不是「清空」。已改为留空时 `GetUserRoleIdsAsync` 回填原角色。`Overwrite_BlankRoleNames_KeepsExistingRoles` 锁定。
- 风险：已修。修前：用覆盖策略改姓名/机构且不填角色列，目标用户全部角色丢失（含超管资料行，超管登录靠 `IsSuperAdmin` 标志仍能进，但普通管理员会丢权）。
- 建议：已修。若产品要「空白=清空」，应在模板把角色列标必填，而不是默默清。

### QA19 · P2 · 导入赋角/覆盖账号/按名取第一条
- 表面：backend
- 状态：fixed
- 复现/证据：`RoleNames` 原先按名 `GetFirstAsync`，不滤 `Enabled`、不校验调用者是否拥有该角色。覆盖按 `Account` 全库命中，只校验**写入**的机构名是否在范围内，不校验**被覆盖用户当前机构**。机构/职位/主管均按名称取第一条，重名则静默绑错。导出单元格原样写入 xlsx，以 `=` 开头的姓名/备注在 Excel 里可能被当公式。四条一起收口：导入列改用**稳定编码**（`OrgCode` / `PositionCode` / `RoleCodes` / `DirectorAccount`）而不是显示名，取不到即 `RefNotFound` 报错（重名静默绑错这个类别整个消失，而不是"改成精确匹配"）；覆盖前校验**目标用户当前机构**在调用者范围内；赋角色走 QA36 的 `IRoleGrantPolicy` 同一套委派策略；公式转义统一下沉到导出 writer（`=+-@` 前缀加 `'`），所有导出路径一次性覆盖。三例锁定：`Import_OrgCode_ResolvesId_Or_RefNotFound` / `Import_NonExistentRoleCode_Rejected` / `Import_Overwrite_OutOfScopeOrg_Rejected`。
- 风险：已修。修前：把用户导入授给机构管理员时，可给任意账号改资料并赋高权角色（不能造超管，QA 已锁）。
- 建议：已修。**导入模板的列语义变了**（名称 → 编码），存量用户手里的旧模板会整列解析失败——升级说明需点名，模板下载接口已同步出新列。

### QA20 · P2 · SQL 总闸关闭后存量任务无法改非载荷字段
- 表面：backend
- 状态：fixed
- 复现/证据：`ValidateAndSerializeProps` 对 `HandlerKind.Sql` 原先一律 `ThrowIf(!options.Sql.Enabled)`。开闸建过的任务，关闸后再改名称/cron（sql 文本不变）也会 47008。已改为：无 `storedProps`（新建）或 sql 文本相对库内变更才拒；执行侧 `SqlAdminJob` 仍拒跑。`Existing_sql_job_can_update_non_payload_while_gate_closed` 锁定。
- 风险：已修。修前：关闸运维等于把存量 SQL 任务的触发配置也锁死。
- 建议：已修。关闸后若要彻底「看不见/不能改」SQL 任务，另做列表过滤。

### QA21 · P2 · DisabledModules 与 IsSystem 护栏边界
- 表面：backend
- 状态：fixed
- 复现/证据：`Api:DisabledModules=["Job"]` 只经 `DisabledModuleConvention` 摘控制器路由；`JobSchedulerService` 仍按 `Jobs:SchedulerEnabled`（默认 true）选主扫表，种子清理任务继续跑。`IsSystem` 原先仅禁删（47014），`UpdateAsync` 可改 HandlerKind/Props/Name——代码比台账文案宽，能把内置清理任务改成 Http/Sql。已按「锁载荷、放触发」落地：系统任务的 `HandlerKind` / `HandlerName` / `Props` / `Name` 拒改（47014），**cron、生效窗口、重试超时这些触发与运行配置仍可改**（运维要调频次是正当需求，不该被一起锁死）。两例成对锁定边界：`System_job_handler_change_is_rejected_47014` 与 `System_job_cron_change_succeeds`。「关模块 ≠ 停调度」判定为**文档问题不是代码问题**（两个开关本就管两件事），核查后确认 `site/zh/guide/scheduled-jobs.md` 早已把两者分别写清，无需补写。
- 风险：已修。修前：持有任务编辑权即可改写内置任务载荷。
- 建议：已修。`DisabledModules` 摘的是 API 面，`Jobs:SchedulerEnabled` 停的是调度循环——要「彻底停任务」两个都得关。

### QA22 · P2 · 回收站 UI 漏定时任务页签
- 表面：web + web-react
- 状态：fixed
- 复现/证据：后端 `RecycleBinController` 与 `JobApiTests.Deleted_job_is_restored_as_paused` 已支持 `type=job`（恢复强制 Paused）。Vue `types` / React `RECYCLE_TYPES` 原先只有 8 项，无 `job`；软删任务只能打 API 或直改库。已在双模板页签与 zh/en `recycle.tabs.job` 补上。React recycle 单测仍 4 绿。
- 风险：已修。修前：任务页删了非系统任务后，运维在回收站找不到。
- 建议：已修。

### QA23 · P2 · 软删级联清关联，恢复变「空壳」
- 表面：backend
- 状态：fixed
- 复现/证据：`UserService.DeleteAsync` 原先软删前 `SetUserRolesAsync([],)`；`RoleService.DeleteAsync` 后 `OnRoleDeletedAsync` 物理删 user_role / role_menu / role_data_scope。回收站 `RestoreAsync` 只翻 `IsDelete`，不重绑关联，于是恢复用户 = 无角色、恢复角色 = 无菜单无范围无用户。产品语义定为「回收站 = 时光机」，但实现取的是比「快照到旁表」更省的一档：**软删不再清关联**（关联行留在原表，本就被软删过滤器挡住，不会造成孤儿可见），**彻底删除（purge）时才清**。恢复因此天然回到原状态，无需回写。两例锁定：`Soft_delete_user_preserves_role_association` / `Soft_delete_role_preserves_associations_purge_cleans`。
- 风险：已修。修前：误删再恢复后需手工重配权。
- 建议：已修。日志清空仍是硬删全表（不进回收站，有意为之——审计清空是运维动作，且清后会再记一条清空操作日志）。

### QA24 · P1 · 标记已读不校验通知可见性
- 表面：backend
- 状态：fixed
- 复现/证据：`MarkAllReadAsync` 只对 `VisibleToMeAsync` 结果写回执；`MarkReadAsync` 原先只看幂等、不查可见性。任意登录用户对定向给别人的 `noticeId`（或乱猜 Id）`PUT .../read` 会插入 `SysNoticeRead`。不泄漏正文，但污染回执表；若日后该用户被加进接收范围，会直接显示已读。已在 `MarkReadAsync` 用 `VisibleToMeAsync` + `NoticeNotFound(45001)` 拦住。`Mark_read_invisible_notice_is_rejected` 锁定。NoticeTests 5 绿。
- 风险：已修。
- 建议：已修。

### QA25 · P2 · 实时 Hub / 幽灵定向 / 头像 URL
- 表面：backend
- 状态：fixed
- 复现/证据：`TenonHub` 原先仅 `[Authorize]`，不走 `[ActiveSession]`——会话被踢后未过期 JWT 仍可连 Hub（API 已 401）。`PublishAsync` 在 `ReceiverType≠All` 且 `ReceiverIds` 空/缺时仍落库，无人可见。`UpdateProfile` 的 `Avatar` 接受任意字符串，前端用 `img src`，外链可当跟踪像素。三条都已落地：Hub **连接时**校验 `sid` 仍活跃，缺失或已失效直接 `Abort()` 且不加入任何组（不是连上再踢——不入组才谈得上收不到推送）；定向通知无有效目标拒发；`Avatar` 经新的 `IAvatarUrlValidator` 限定为本站签名 view URL 形状，否则 `AvatarUrlInvalid`（42026）。`TenonHubTests` 三例锁定：`Missing_sid_aborts_and_joins_no_group` / `Inactive_sid_aborts_and_joins_no_group` / `Active_sid_joins_user_and_session_groups_without_aborting`。
- 风险：已修。修前：踢设备后短窗内 Hub 仍可收推送；幽灵通知占管理列表；头像字段信任客户端。
- 建议：已修。Hub 令牌仍走 query `access_token`（SignalR 的既定惯例，浏览器 WebSocket 不能带自定义头），代理日志留痕这一条无代码可改，靠令牌短时效兜。`IAvatarUrlValidator` 走 `TryAdd`，消费者要允许外部头像床可整体替换。

### QA26 · P2 · Level3 预检样本落盘路径不可移植
- 表面：tests
- 状态：fixed
- 复现/证据：`SecurityBaselinePrecheckTests.Level3_ok_path_passes_phase1_criticals` 在断言预检通过后，把样本 JSON 写到 `ScratchDir`。原先默认硬编码 `C:\Users\ADMINI~1\AppData\Local\Temp\grok-goal-...\implementer`，本机无写权限 → `UnauthorizedAccessException`，冲红整条（预检逻辑本身已绿）。已改为优先 `GROK_SCRATCH`，否则 `Path.GetTempPath()/tenon-admin-tests/level3-precheck`。本轮相关套件 38 绿。
- 风险：已修。修前：CI/他人机器偶发红，误报产品安全预检坏了。
- 建议：已修。交付流水线若需固定路径继续注入 `GROK_SCRATCH`。

### QA27 · P2 · 限流窗口突发与显式同号 WorkerId
- 表面：backend
- 状态：fixed（WorkerId 部分）/ wontfix（固定窗口部分）
- 复现/证据：两个独立取舍被并在一条里，处置也分两半。**固定窗口保持不变**：边界近 2× 突发是固定窗口的固有特性，中间件注释早已写明；换滑动窗口要为每个分区留时间戳序列，内存与 Redis 两套实现都变重，而限流在本项目的定位是「挡住脚本级爆破」，不是精确配额——不值这个复杂度。**WorkerId 改了**：原先显式写成相同值（含都写 0）守卫放行，横向扩容时同毫秒撞雪花且静默。新增 `SysWorkerLease` 表 + `WorkerIdLeaseGuard`（启动争抢租约、周期续租、停止释放，TTL = 心跳 ×3），第二个实例拿同一 WorkerId 启动会**直接抛可读的启动错误**，把静默的主键冲突换成起不来。两例锁定：`Second_instance_with_same_worker_id_throws` / `Stop_releases_lease_allowing_new_instance`。
- 风险：固定窗口边界突发对爆破略软（接受）；WorkerId 误配已从静默撞主键变为 fail-fast。
- 建议：租约释放是尽力而为，进程被 kill 不会释放，租约按 TTL 自然过期后 WorkerId 才可复用——容器快速重启时若撞上未过期的旧租约会起不来，把 `Jobs:HeartbeatSeconds` 调小即可缩短这个窗口。

### QA28 · P1 · Worker 实体扫描漏挂 Services 程序集
- 表面：backend
- 状态：fixed
- 复现/证据：`AddTenonAdmin` 扫描 `ServicesSetup.Assembly + ApplicationAssemblies`；`AddTenonAdminWorker` 原先只传 `ApplicationAssemblies`。默认 Worker `EnableCodeFirst=false`，测试不易暴露；一旦运维打开 CodeFirst，只会建出 SqlSugar 层的 `sys_schema_version`，任务/用户等内核表缺失，调度器启动即查无表。已与 HTTP 组合根对齐，并加 `Worker_entity_scan_includes_services_assembly`。Replaceability+Nullable+MultiConfigId+WorkerSetup **43** 绿。
- 风险：已修。修前：独立 Worker + 开 CodeFirst 的部署会 silently 缺表。
- 建议：已修。

### QA29 · P3 · TryAdd 契约与副库 DbType 默认
- 表面：tests / docs
- 状态：fixed
- 复现/证据：`ReplaceabilityTests` 多数用例用**后置 `Replace`**，即便把注册从 `TryAdd` 退化成 plain `Add` 仍然绿——「六件套」当契约用，却漏了它要守的那件事本身。真正 pre-reg 锁 TryAdd 的原先只有 Jobs（及部分 Excel/Redis）。副库 `AdminDatabaseConnectionOptions.DbType` 默认 `"Sqlite"`，复制主库 MySQL 连接串却忘改类型时，校验只查非空、不交叉校验方言，启动后才炸。两条都已修：补 `PreRegisteredCoreServices_ShouldWinOverBuiltIns`——**在 `AddTenonAdmin()` 之前**注册核心服务替身，断言内核不覆盖它（TryAdd 退化成 Add 这条用例就会红）；副库 `DbType` 默认值改为 `""` 即必填，配置缺失在启动校验阶段就报，而不是等到第一次查询。
- 风险：已修。修前：回归 TryAdd→Add 可能漏检；副库配错方言启动后才炸。
- 建议：已修。**这是破坏性变更**：既有部署若在 `AdditionalDatabases` 里省略了 `DbType`（此前默认 Sqlite），升级后会启动失败——需在 CHANGELOG 点名。

### QA30 · P2 · Vite preview 未设 strictPort
- 表面：web
- 状态：fixed
- 复现/证据：`web/vite.config.ts` 的 `server.strictPort: true` 已在 Round 1（QA02）补上；`preview` 块原先只有 `port: 5173`，`vite preview` 在端口占用时会静默挪端口，与 QA02 同类风险。已给 preview 补 `strictPort: true`。本轮 `web` vitest **90/90** 绿。
- 风险：已修。
- 建议：已修。

### QA31 · P2 · profile 失败时超管按钮全藏
- 表面：web + web-react
- 状态：fixed
- 复现/证据：双模板 `enterInitial` / `clearClientCache` 并行拉 permissions + profile。超管权限码常为空集，`isSuperAdmin` 只来自 profile；profile 失败时刻意「按普通用户处理」→ `isSuperAdmin=false` + 空码 → Vue `hasPerm`/`v-auth` 与 React `hasPerm`/`Can` 全部隐藏。已改为**从登录响应带下来**：`LoginOutput` 增加 `isSuperAdmin`，两个模板的 user store 在 `setSession` 时落库（缺字段归一为 `false`，不留 `undefined`），profile 失败时回退到这个会话值而不是回退成「普通用户」。没走「客户端解 JWT `sadm` claim」那条路——那等于让前端信任自己解出来的令牌内容。`web-react/src/stores/user.spec.ts` 补了归一用例。
- 风险：已修。修前：permissions 成功但 profile 失败的窗口里，超管几乎看不到写按钮（服务端仍 sadm 绕过，非越权）。
- 建议：已修。这只是**显示态**回退；真正的授权判定始终在服务端，前端拿到 `isSuperAdmin=true` 也变不出权限。

### QA32 · P3 · 皮肤中文硬编码与 v-auth 一次性移除
- 表面：web
- 状态：fixed
- 复现/证据：`LOGIN_SKINS` 标签 `极光`/`双栏`/`聚光` 写死，切换器 aria 走 i18n 但按钮文案不跟 locale。`v-auth` 原先只在 `mounted` 调 `el.remove()`，无 `updated`；`clearClientCache` 刷新权限后已删节点不会回来、仍可见节点也不会立刻藏，需路由重挂。两条都已修：皮肤名改走 i18n 键；`v-auth` 改用 `watchEffect` 订阅权限 store，权限刷新后能重新显隐（不再是 `mounted` 时一次性 `el.remove()`）。跨应用深链按 remembered `currentModuleId` 重建属设计，不动。
- 风险：已修。修前：英文本地化皮肤名不对；权限刷新后按钮态错位到路由重挂为止。
- 建议：已修。指令改成响应式后与 React 侧 `<Can>` 行为对齐，见 QA35。

### QA33 · P2 · React Vite preview 未设 strictPort
- 表面：web-react
- 状态：fixed
- 复现/证据：`server.strictPort: true` 已在；无 `preview` 块。`npm run preview` 端口占用时会静默挪走。已补 `preview: { port: 5174, strictPort: true, proxy… }`，对齐 Vue QA30。
- 风险：已修。
- 建议：已修。

### QA34 · P3 · React 皮肤切换器中文硬编码
- 表面：web-react
- 状态：fixed
- 复现/证据：`LOGIN_SKINS` 标签 `极光`/`双栏`/`聚光` 写死；tablist aria 走 i18n。与 Vue QA32 对偶，已随同一提交改走 i18n 键（两个模板各自维护自己那份文案，符合零共享约定）。Can/zustand 选择器纪律良好（`useHasPerm` 不回新闭包）；`recycle.tabs.job` 中英均在；F5 `enterInitial`+`RequireAuth` 对齐。
- 风险：已修。
- 建议：已修。

### QA35 · P3 · v-auth 一次性移除 vs Can 可重渲
- 表面：web（对比 web-react）
- 状态：fixed
- 复现/证据：Round 19 对等抽查。Vue `v-auth` 原先只在 `mounted` 调 `el.remove()`；React `<Can>` 订阅 store，权限刷新后可再显隐。不是「一侧缺功能」，是指令 vs 组件的框架差。已把 `v-auth` 改成 `watchEffect` 订阅权限 store（见 QA32），两侧行为对齐——选它而不是「关键操作逐个改 `v-if="hasPerm"`」，是因为后者要改的是**每一个调用点**，漏一个就回到原状。抽查其余面（权限规则、门户 F5、mustChangePassword、recycle+job、Import Skip、datascope、setEnabled、hub/force-logout、strictPort）两侧对齐。
- 风险：已修。修前：Vue 侧 `clearClientCache` 后按钮态错位直至路由重挂。
- 建议：已修。

### QA36 · P1 · 角色授予无委派边界
- 表面：backend
- 状态：fixed
- 复现/证据：角色的定义面（新建/改名/删除/配菜单/配数据范围）与授予面（把角色挂到用户身上）原先共用同一套路由权限码——拿到「角色管理」菜单的非超管，既能造角色也能把任意角色授给任意用户，等于可以自己给自己造一个高权角色再授上。已按「定义收归超管、授予受策略约束」拆成两层：**定义面（Create/Update/Delete/SetRoleMenus/SetRoleDataScope，后者见 QA09）一律超管专属**；**授予面**新增 `SysRole.IsDelegatable` 标记与 `IRoleGrantPolicy`，非超管只能把**标记为可委派**的角色授给**自己数据范围内**的用户。`IsDelegatable` 是**可空 bool**：数据库 `NULL`（功能上线前的存量角色）与显式 `false` 同判定为「不可转授」，只有显式 `true` 才放行——安全默认，升级不会静默放宽；可空还因为 MSSQL 无法对有数据的表 ADD 无 DEFAULT 的 NOT NULL 列（同 `SysUser.ForceTotp` 的成法，见 `skills/create-entity.md`）。`RoleDelegationTests` 七例锁定四条拒绝路径（create/update/delete/SetRoleMenus）、两条授予边界（可委派且在范围内放行 / 不可委派拒绝）和一条超管全通。
- 风险：已修。修前：持有角色管理菜单的非超管可自造高权角色并自授，是本轮最深的一条越权。
- 建议：已修。**升级后既有角色全部按「不可转授」处理**，需要委派的角色必须由超管在角色表单里显式勾选——这会让「升级前靠非超管管理员分配角色」的部署突然分不动角色，CHANGELOG 需点名。QA19 的导入赋角走同一套策略，不存在绕行口子。
