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
| QA03 | P2 | open | backend | 匿名 `POST /api/v1/auth/mfa/challenge/verify` 无前端调用，会消费登录 MFA 挑战并回 userId |
| QA04 | P2 | question | backend | 自助改密不吊销其它会话（管理员重置会吊销） |
| QA05 | P1 | fixed | backend | MFA 绑定/恢复在账号不存在时每次都 `Hash()` 陪跑，耗时可枚举 |
| QA06 | P1 | open | backend | 短信免密登录：错码 40010 vs 无码 40011，可区分「该手机号是否刚被发过真码」 |
| QA07 | P1 | fixed | backend | `[ActiveSession]` 未写入数据范围，仅登录端点上的 DataEntity 查询默认 Unrestricted |
| QA08 | P2 | question | backend | 用户/机构/岗位列表不走数据范围（设计如此）；有用户管理权即可看全库账号 |
| QA09 | P1 | question | backend | `SetRoleDataScope` 不校验调用者自身范围，持有该权限即可把角色扩成 All/任意机构 |
| QA10 | P2 | question | backend | 删机构/岗位不检查仍挂靠的用户；用户可删/停用自己 |
| QA11 | P1 | fixed | backend | 停用角色后权限码已失效，门户模块/菜单树仍按该角色授权展示 |
| QA12 | P1 | question | backend | `GET /sys/dict/items/{typeCode}` 挂 RolePermission；未授字典菜单时用户/机构表单下拉静默空白 |
| QA13 | P2 | question | backend | 种子字典类型与配置键可删；字典项 (类型,值) 无唯一；job 分组出现在「其他」 |
| QA14 | P2 | fixed | backend | 字典类型停用后 `GetItemsByTypeAsync` 仍返回启用项 |
| QA15 | P1 | question | backend | 文件分页/下载/删除不按上传人过滤；有文件管理权即可操作全库 |
| QA16 | P2 | question | backend | 签名直链只绑文件 Id、无过期；链接泄漏后直到 JWT 密钥轮换都有效 |
| QA17 | P1 | fixed | backend | `import/validate`·`commit` 不卡 `MaxImportRows`，JSON 入口可绕过文件行数上限 |
| QA18 | P1 | fixed | backend | 覆盖导入 `RoleNames` 留空会把已有角色清掉（`UpdateAsync` 全量重设） |
| QA19 | P2 | question | backend | 导入按角色名赋任意角色；覆盖按账号命中全库；机构/主管按名取第一条 |
| QA20 | P2 | fixed | backend | SQL 总闸关闭后，存量 SQL 任务连改 cron/名称也撞 47008 |
| QA21 | P2 | question | backend | `Api:DisabledModules=["Job"]` 只摘 HTTP 路由，调度器仍跑；`IsSystem` 可改 Handler/Props |
| QA22 | P2 | fixed | web+web-react | 回收站页签漏了 `job`，软删任务只能靠 API 恢复 |
| QA23 | P2 | question | backend | 软删用户/角色会级联清关联，恢复后角色/菜单授权不会回来 |
| QA24 | P1 | fixed | backend | `MarkReadAsync` 不校验可见性，任意登录用户可对任意通知 Id 写已读回执 |
| QA25 | P2 | question | backend | Hub 仅 JWT 鉴权不验会话活性；定向通知空 receiverIds 成幽灵；头像字段可任意 URL |
| QA26 | P2 | fixed | tests | `SecurityBaselinePrecheckTests` 样本 JSON 落盘路径硬编码到不可写目录，本机红 |
| QA27 | P2 | question | backend | 限流固定窗口边界可近 2× 突发；显式 `WorkerId=0` 的多副本仍可撞号 |
| QA28 | P1 | fixed | backend | Worker 实体扫描漏挂 Services 程序集，开 CodeFirst 时内核表建不全 |
| QA29 | P3 | question | tests | 多数 Replace* 用例后置 Replace，不真正锁 TryAdd；副库 DbType 默认为 Sqlite |
| QA30 | P2 | fixed | web | `vite preview` 未设 `strictPort`，5173 被占时会静默挪端口 |
| QA31 | P2 | question | web+web-react | `/personal/profile` 失败时超管被当普通用户，空权限码导致全部权限按钮消失 |
| QA32 | P3 | question | web | 登录皮肤切换器文案写死中文；`v-auth` 仅 mounted 时 remove，权限刷新不回挂 |
| QA33 | P2 | fixed | web-react | `vite preview` 未设 `strictPort`（与 Vue QA30 对偶） |
| QA34 | P3 | question | web-react | 登录皮肤切换器文案写死中文（与 Vue QA32 对偶） |
| QA35 | P3 | question | web | Vue `v-auth` 仅 mounted remove；React `Can` 可随 store 重渲（框架差，非缺功能） |

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
- 状态：open
- 复现/证据：`MfaController.VerifyChallenge` 为 `[AllowAnonymous]` `POST /api/v1/auth/mfa/challenge/verify`。两套前端 API 封装都没有这个方法（只出现在 OpenAPI `schema.d.ts`）。登录完成走的是 `POST /api/v1/auth/login/totp`（内部同样 `VerifyAndConsumeAsync`）。活探测无效挑战返回 `40019`。持有登录 40018 下发的 `challengeId` + 正确 TOTP 时，该端点会消费一次性挑战并回 `userId`，而不签发令牌。
- 风险：不能靠猜 challengeId 提权；但可作废一次进行中的 MFA 登录，并多一条披露 userId 的匿名面。属于多余攻击面。
- 建议：产品确认是否给第三方/移动端留的。若无调用方，删除或改为内部-only；至少不要在匿名响应里回 `userId`。

### QA04 · P2 · 自助改密不吊销其它会话
- 表面：backend
- 状态：question
- 复现/证据：`UserService.ResetPasswordAsync` 会 `RevokeAllForUserAsync`（`PasswordResetSecurityTests` 锁定）。`PersonalService.ChangePasswordAsync` 只换哈希、清 `MustChangePassword`、写历史，不碰会话。双前端改密成功后本机 logout 重登，其它设备上的旧 refresh 仍可续命。
- 风险：账号已改密后，失窃的其它会话继续有效，直到 refresh 自然过期。
- 建议：若产品语义是「改密 = 不再信任其它持有者」，自助改密应吊销除当前 sid 外的会话（或全部再签发）。需产品拍板，本轮不改。

### QA05 · P1 · MFA 绑定/恢复账号不存在时每次现算陪跑哈希
- 表面：backend
- 状态：fixed
- 复现/证据：`AuthService.ValidateUserAsync` 用静态 `_dummyHash ??=` 缓存陪跑哈希，使「账号不存在」与「密码错误」代价接近。`MfaEnrollmentService.StartBindAsync` / `UseRecoveryCodeAsync` 原先对不存在账号每次 `hasher.Hash("tenon-admin.timing-dummy")` 再 `Verify`——多一轮 PBKDF2，耗时可区分，也能被拿来砸 CPU。
- 风险：TOTP 开启时，匿名绑定/恢复入口可按耗时枚举账号，并放大 PBKDF2 成本。
- 建议：已改为与登录相同的进程内缓存 `_dummyHash ??=`。`MfaEnrollmentTests` 13 项仍绿。

### QA06 · P1 · 短信免密登录可用 40010/40011 区分手机号是否有真实用户
- 表面：backend
- 状态：open
- 复现/证据：`SendSmsLoginCodeAsync` 对未知/重复/停用走 `PretendIssueAsync`（同外形、不存码、不发短信）。`LoginByPhoneAsync` 再 `VerifyAsync`：缓存无码 → `SmsCodeExpired` **40011**；有码但输错 → `SmsCodeWrong` **40010**（带 `attemptsLeft`）。`SmsLoginFlowTests` 只断言未知手机号登录是 40011，**没有**断言「已发码用户输错」不得用另一码。`AuthService` 注释写「与码过期不可区分」，与实现不符。开关默认关；打开后：发码成功后再拿任意错误码登录，40010=该号恰有一名启用用户，40011=没有。
- 风险：短信登录开启时，可枚举哪些手机号对应系统内启用账号（发码本身已防枚举，登录验码把缺口打开了）。
- 建议：免密登录验码把 40010 也归一成 40011；或 `PretendIssueAsync` 写入不可猜的陪跑码，使未知号错猜也走 40010。改时补测试：已知号错码与未知号错码必须同码。本轮不改（影响「剩余次数」文案，需产品一起定）。

### QA07 · P1 · `[ActiveSession]` 未写入数据范围
- 表面：backend
- 状态：fixed
- 复现/证据：`RolePermissionAttribute` 在授权阶段把 `IDataScopeProvider.ResolveAsync` 写入 `IDataScopeContext`。`ActiveSessionAttribute` 原先只验会话，不写范围。`HttpContextDataScopeContext` 未设置时返回 `Unrestricted`。内核个人中心/通知表是 `BaseEntity` 所以当时看不见；消费方在「仅登录」端点上查 `DataEntity`（或只挂 `[Authorize]`）会看到全部机构。本轮已抽 `DataScopeRequestBinder`，两过滤器共用；TestHost 增加 `GET /api/v1/sample/doc/mine`，`SampleDocScopeTests.ActiveSession_endpoint_still_filters_by_data_scope` 锁定。
- 风险：已修。修前：任意登录用户打未绑范围的 DataEntity 列表 = 跨机构读。
- 建议：已修。消费方自建端点不要只挂 `[Authorize]`。

### QA08 · P2 · 用户/机构/岗位不走通用数据范围
- 表面：backend
- 状态：question
- 复现/证据：`rebuild-design.md` §6 写明 `sys_user` 继承 `BaseEntity`，用户列表**不**走 `CreateOrgId` 过滤，「如需按机构筛用户在 UserService 显式处理」。`UserService.BuildListQuery` / `OrgService.ListAsync` / `PositionService` 均未读 `IDataScopeContext`。对比：用户导入 `UserImportProfile.IsOrgInScope` **会**挡范围外机构。持有 `GET:/api/v1/sys/user/page` 即可列出全库账号/手机/邮箱；机构树、岗位同理。
- 风险：数据范围招牌只罩业务 `DataEntity`。把用户管理菜单授给「本机构」角色时，对方仍能看/改其它机构的账号（改 `OrgId` 也无范围校验）。若产品意图是「用户管理=超管专属」，应用菜单授权约束，文档应写死。
- 建议：产品拍板。若要机构管理员管本范围用户，在 `BuildListQuery` / `Add`/`Update` 按 `OrgId ∈ scope.OrgIds`（加 IncludeSelf）显式过滤，机构/岗位同样。本轮不改（会改变现有授权语义）。

### QA09 · P1 · 配置角色数据范围不校验调用者自身范围
- 表面：backend
- 状态：question
- 复现/证据：`RbacService.SetRoleDataScopeAsync` 只验角色存在，可写成 `All` 或 `Custom` 任意机构 Id，随后 `InvalidateScopesAsync`。端点 `PUT:/api/v1/sys/role/datascope` 仅 `[RolePermission]`。持有该权限的非超管可以把**自己的角色**改成 All，或把 Custom 填成总部机构 Id，随后业务表过滤器对其失效。
- 风险：数据范围可被「有配范围权限的人」自助拆掉。与 QA08 叠加：先看全用户，再给自己扩范围。
- 建议：非 Unrestricted 调用者禁止设 All；Custom 的机构 Id 必须 ⊆ 调用者 `OrgIds`；本机构/本机构及以下可保留。需产品确认「谁允许配 All」。本轮不改。

### QA10 · P2 · 删机构/岗位不挡挂靠用户；可删停自己
- 表面：backend
- 状态：question
- 复现/证据：`OrgService.DeleteAsync` 只挡「仍有子机构」(`OrgHasChildren`)，不查 `SysUser.OrgId`。`PositionService.DeleteAsync` 不查 `PositionId`。软删后用户仍持旧 Id，列表机构名/职位名变空（`FillOrgPositionNamesAsync` 被软删过滤器挡住），数据范围 `Org` 仍按残留 `OrgId` 解析。用户 `DeleteAsync`/`SetEnabledAsync`/`UpdateAsync` 也不拦「操作对象 = 当前登录用户」，有用户管理权即可删停自己（前端只护超管行）。
- 风险：误删叶子机构留下悬挂用户；管理员误删自己后需超管从回收站恢复。非越权。
- 建议：有用户挂靠时拒删（新错误码）；删/停当前用户直接拒。产品也可接受「软删 + 悬挂」并在 UI 提示。本轮不改。

### QA11 · P1 · 停用角色后门户仍展示其授权
- 表面：backend
- 状态：fixed
- 复现/证据：`RbacPermissionProvider` 用 `InnerJoin SysRole && Enabled` 算权限码；`MenuService.ComputeMyModulesAsync` / `ComputeMyMenuTreeAsync` 原先直接 `userRoles` 取全部角色 Id。`ModulePortalTests` 有停用**菜单**不暴露模块，没有停用**角色**。停用角色后 `GET /api/v1/ping` 已 403，但 `/personal/modules` 仍列出 system。已抽 `ResolveEnabledRoleIdsAsync` 与权限提供者同口径；补 `Disabled_role_does_not_expose_module`。
- 风险：已修。修前：侧栏入口在、点进去接口 403，或误以为仍有权。
- 建议：已修。角色启停走全量 update，`InvalidateByRoleAsync` 会 bump 门户代际，修后即时空。

### QA12 · P1 · 字典下拉热路径要单独授权
- 表面：backend
- 状态：question
- 复现/证据：`GET /api/v1/sys/dict/items/{typeCode}` 挂 `[RolePermission]`，种子按钮在「系统运维 → 字典」下。用户/机构表单的 `DictSelect`/`useDictOptions` 调这个接口，失败 `.catch(() => {})` 静默留空。只授「组织管理」的角色：用户页性别、机构分类下拉空白，导入模板服务端自读 `IDictService` 不受影响。对比 `password-policy` 已用 `[ActiveSession]`。
- 风险：非超管、未勾字典查询权时，表单看起来能建用户但性别/分类选不了；管理员以为字典坏了。
- 建议：下拉接口改 `[ActiveSession]`（只回启用项、无写面）；或把该权限码并进用户/机构菜单种子。需产品拍板。本轮不改。

### QA13 · P2 · 种子字典/配置可删；项值不唯一
- 表面：backend
- 状态：question
- 复现/证据：`gender` / `common_status` / `org_category` 无内置保护，`DeleteTypeAsync` 会物理删项。配置 `DeleteAsync` 同样可删 `sys.security.*` 种子键（策略层有 Options 兜底）。`SysDictItem` 无 `(DictTypeCode, Value)` 唯一索引，可插两条 value=1。`OtherConfig` 的 `STRUCTURED_GROUPS` 不含 `job`，任务保留天数等出现在「其他」并可当自定义行删掉。
- 风险：误删性别字典 → 用户表单下拉空；误删安全键 → 回退 Options 默认，运维以为配置中心还在管。重复字典值让导入/展示歧义。
- 建议：种子类型/键拒删（对照 `ModuleProtected`）；项值按类型唯一；`job` 进结构化 Tab 或加入排除分组。本轮不改。

### QA14 · P2 · 停用字典类型后下拉仍有项
- 表面：backend
- 状态：fixed
- 复现/证据：`GetItemsByTypeAsync` 原先只滤 `item.Enabled`，不看类型。`UpdateTypeAsync` 会失效缓存，但再查仍把启用项吐出。已改为类型不存在或 `Enabled=false` 返回空；`DictCrudTests.Items_by_type_empty_when_type_disabled` 锁定。
- 风险：已修。修前：管理端停用类型后，用户表单下拉不变。
- 建议：已修。

### QA15 · P1 · 文件管理不按上传人/机构隔离
- 表面：backend
- 状态：question
- 复现/证据：`SysFile` 继承 `BaseEntity`（非 `DataEntity`）。`FileService.PageAsync` 无 `CreateUserId`/`CreateOrgId` 过滤；`DownloadAsync`/`DeleteAsync` 只按 Id。有 `GET:/api/v1/sys/file/page` + 下载/删除权限即可列出并取走他人头像/附件。与 QA08 同模型，文件内容更敏感。秒传按哈希建独立行（T-D7），GC 共享判定已测绿。
- 风险：文件模块若授给普通业务员，等于共享网盘。若产品认定「文件管理=超管专属」，用菜单授权约束即可。
- 建议：产品拍板。若要按人/机构隔离，列表加 `CreateUserId` 或给 `SysFile` 改 `DataEntity`（会改变现有授权语义）。本轮不改。

### QA16 · P2 · 签名预览直链无过期
- 表面：backend
- 状态：question
- 复现/证据：`FileUrlSigner.Sign` 只对 `fileId` 做 HMAC，无时间窗。`GET /sys/file/{id}/view?sig=` 匿名；`FileViewUrlTests` 锁伪造/换 Id。头像、Markdown 插图把 URL 写进 HTML/日志/Referer 后，拿到链接的人可一直拉，直到 JWT 主密钥轮换派生新子密钥。软删后 `DownloadAsync` 因过滤器 44004，直链失效。
- 风险：不能靠猜 sig 提权；泄漏面是「曾经合法的 URL」。内联图长期可缓存是产品取舍。
- 建议：若要收回访问，签名加 `exp`（或短 TTL + 刷新接口）。需产品定头像是否允许长期外链。本轮不改。

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
- 状态：question
- 复现/证据：`RoleNames` 按名 `GetFirstAsync`，不滤 `Enabled`、不校验调用者是否拥有该角色（与用户表单「有更新权即可赋任意角色」同口径）。覆盖按 `Account` 全库命中，只校验**写入**的机构名是否在范围内，不校验**被覆盖用户当前机构**——与 QA08 同一产品选择，导入把账号字符串变成更新面。机构/职位/主管均按名称取第一条，重名则静默绑错。导出单元格原样写入 xlsx，以 `=` 开头的姓名/备注在 Excel 里可能被当公式。
- 风险：把用户导入授给机构管理员时，可给任意账号改资料并赋高权角色（不能造超管，QA 已锁）。重名绑错是数据质量问题。公式注入需用户打开文件。
- 建议：产品若要「本机构管理员只能导本范围」，覆盖前检查目标 `OrgId ∈ scope`，角色名限制为调用者可授集合。重名改为精确匹配失败。公式列可对 `=+-@` 前缀加 `'`。本轮不改。

### QA20 · P2 · SQL 总闸关闭后存量任务无法改非载荷字段
- 表面：backend
- 状态：fixed
- 复现/证据：`ValidateAndSerializeProps` 对 `HandlerKind.Sql` 原先一律 `ThrowIf(!options.Sql.Enabled)`。开闸建过的任务，关闸后再改名称/cron（sql 文本不变）也会 47008。已改为：无 `storedProps`（新建）或 sql 文本相对库内变更才拒；执行侧 `SqlAdminJob` 仍拒跑。`Existing_sql_job_can_update_non_payload_while_gate_closed` 锁定。
- 风险：已修。修前：关闸运维等于把存量 SQL 任务的触发配置也锁死。
- 建议：已修。关闸后若要彻底「看不见/不能改」SQL 任务，另做列表过滤。

### QA21 · P2 · DisabledModules 与 IsSystem 护栏边界
- 表面：backend
- 状态：question
- 复现/证据：`Api:DisabledModules=["Job"]` 只经 `DisabledModuleConvention` 摘控制器路由；`JobSchedulerService` 仍按 `Jobs:SchedulerEnabled`（默认 true）选主扫表，种子清理任务继续跑。台账写明「没有第二个总开关」。`IsSystem` 仅禁删（47014）；`UpdateAsync` 可改 HandlerKind/Props/Name（台账写「可改触发配置」——代码比文案宽，可把清理任务改成 Http/Sql）。安全面：HTTP 围栏/CRLF/密钥掩码/Compiled 冒充内置/SQL 总闸/选主 CAS 均有测试钉死（本轮 Job* 92→Api+Security 67 绿）。
- 风险：运维以为关模块=停调度；持有任务编辑权即可改写内置任务载荷（编辑权本就≈高敏，开 SQL 闸=DBA）。
- 建议：文档/启动预检提示「DisabledModules≠SchedulerEnabled」；若产品要锁死内置任务，IsSystem 拒改 HandlerKind/Props。本轮不改。

### QA22 · P2 · 回收站 UI 漏定时任务页签
- 表面：web + web-react
- 状态：fixed
- 复现/证据：后端 `RecycleBinController` 与 `JobApiTests.Deleted_job_is_restored_as_paused` 已支持 `type=job`（恢复强制 Paused）。Vue `types` / React `RECYCLE_TYPES` 原先只有 8 项，无 `job`；软删任务只能打 API 或直改库。已在双模板页签与 zh/en `recycle.tabs.job` 补上。React recycle 单测仍 4 绿。
- 风险：已修。修前：任务页删了非系统任务后，运维在回收站找不到。
- 建议：已修。

### QA23 · P2 · 软删级联清关联，恢复变「空壳」
- 表面：backend
- 状态：question
- 复现/证据：`UserService.DeleteAsync` 软删前 `SetUserRolesAsync([],)`；`RoleService.DeleteAsync` 后 `OnRoleDeletedAsync` 物理删 user_role / role_menu / role_data_scope。回收站 `RestoreAsync` 只翻 `IsDelete`（及唯一列后缀），不重绑关联。恢复用户 = 无角色；恢复角色 = 无菜单无范围无用户。唯一冲突有 42021。日志清空为硬删全表（清后 OperationLog 再记一条清空动作）；监控仅 RolePermission；缓存管理只定向 flush、无键浏览。
- 风险：误删再恢复后需手工重配权；若产品期望「回收站 = 时光机」则语义不符。级联清关联本身防孤儿，合理。
- 建议：产品拍板。若要可逆，软删时把关联快照到旁表，恢复时写回；或 UI 明确提示「恢复后须重新授权」。本轮不改。

### QA24 · P1 · 标记已读不校验通知可见性
- 表面：backend
- 状态：fixed
- 复现/证据：`MarkAllReadAsync` 只对 `VisibleToMeAsync` 结果写回执；`MarkReadAsync` 原先只看幂等、不查可见性。任意登录用户对定向给别人的 `noticeId`（或乱猜 Id）`PUT .../read` 会插入 `SysNoticeRead`。不泄漏正文，但污染回执表；若日后该用户被加进接收范围，会直接显示已读。已在 `MarkReadAsync` 用 `VisibleToMeAsync` + `NoticeNotFound(45001)` 拦住。`Mark_read_invisible_notice_is_rejected` 锁定。NoticeTests 5 绿。
- 风险：已修。
- 建议：已修。

### QA25 · P2 · 实时 Hub / 幽灵定向 / 头像 URL
- 表面：backend
- 状态：question
- 复现/证据：`TenonHub` 仅 `[Authorize]`，不走 `[ActiveSession]`——会话被踢后，未过期 JWT 仍可连 Hub（API 会 401）；`force-logout` 依赖已连接组推送。Hub 令牌走 query `access_token`（SignalR 惯例，Referer/代理日志可能留痕）。`PublishAsync` 在 `ReceiverType≠All` 且 `ReceiverIds` 空/缺时仍落库，无人可见（管理端 page 能看见）。`UpdateProfile` 的 `Avatar` 接受任意字符串（测试写入 `/api/.../view/1?sig=x`），前端用 `img src`；外链/非签名 URL 可当跟踪像素。个人改密不吊销其它会话见 QA04。定向可见性与会话自助踢设备已有测试。
- 风险：踢设备后短窗内 Hub 仍可收推送（无业务方法可调）；幽灵通知占管理列表；头像字段信任客户端。
- 建议：Hub 连接时校验 sid 仍活跃；定向无目标拒发；Avatar 限制为本站签名 view URL。产品拍板。本轮不改。

### QA26 · P2 · Level3 预检样本落盘路径不可移植
- 表面：tests
- 状态：fixed
- 复现/证据：`SecurityBaselinePrecheckTests.Level3_ok_path_passes_phase1_criticals` 在断言预检通过后，把样本 JSON 写到 `ScratchDir`。原先默认硬编码 `C:\Users\ADMINI~1\AppData\Local\Temp\grok-goal-...\implementer`，本机无写权限 → `UnauthorizedAccessException`，冲红整条（预检逻辑本身已绿）。已改为优先 `GROK_SCRATCH`，否则 `Path.GetTempPath()/tenon-admin-tests/level3-precheck`。本轮相关套件 38 绿。
- 风险：已修。修前：CI/他人机器偶发红，误报产品安全预检坏了。
- 建议：已修。交付流水线若需固定路径继续注入 `GROK_SCRATCH`。

### QA27 · P2 · 限流窗口突发与显式同号 WorkerId
- 表面：backend
- 状态：question
- 复现/证据：限流默认 Options+种子均 `Enabled=true`（认证 20/min、全局 300/min）；计数走 `ICacheProvider.IncrementAsync`（Redis 共享、内存单机）；生产缺 JWT / Redis 未配 WorkerId / ForwardedHeaders 无受信源均 fail-fast；生产关 CodeFirst 缺表/缺列启动失败有行动信息；SecretProtector AES-GCM 往返与错钥失败已测。已知取舍：固定窗口在边界可近 2× 突发（中间件注释已写）；`WorkerId` 显式写成相同值（含都写 0）守卫放行——运维自认知情，仍可能同毫秒撞雪花。Level3 ready 探针恒 Healthy（ADR 0006）。
- 风险：边界突发对爆破略软；误配相同 WorkerId 仍静默撞主键。
- 建议：若要更严，滑动窗口或启动时可选探测「同 WorkerId 心跳已存在」。产品拍板。本轮不改。

### QA28 · P1 · Worker 实体扫描漏挂 Services 程序集
- 表面：backend
- 状态：fixed
- 复现/证据：`AddTenonAdmin` 扫描 `ServicesSetup.Assembly + ApplicationAssemblies`；`AddTenonAdminWorker` 原先只传 `ApplicationAssemblies`。默认 Worker `EnableCodeFirst=false`，测试不易暴露；一旦运维打开 CodeFirst，只会建出 SqlSugar 层的 `sys_schema_version`，任务/用户等内核表缺失，调度器启动即查无表。已与 HTTP 组合根对齐，并加 `Worker_entity_scan_includes_services_assembly`。Replaceability+Nullable+MultiConfigId+WorkerSetup **43** 绿。
- 风险：已修。修前：独立 Worker + 开 CodeFirst 的部署会 silently 缺表。
- 建议：已修。

### QA29 · P3 · TryAdd 契约与副库 DbType 默认
- 表面：tests / docs
- 状态：question
- 复现/证据：`ReplaceabilityTests` 注释写明多数用例用后置 `Replace`，即便改成 plain `Add` 仍绿；真正 pre-reg 锁 TryAdd 的主要是 Jobs（及部分 Excel/Redis）。副库 `AdminDatabaseConnectionOptions.DbType` 默认 `"Sqlite"`，复制主库 MySQL 连接串却忘改类型时易踩坑（校验只查非空，不交叉校验方言）。多库：`IRepository` 始终主库、副库无种子/无默认 CodeFirst、钩子默认关——已有测试与文档。
- 风险：回归 TryAdd→Add 可能漏检；副库配错方言启动后才炸。
- 建议：补一条核心服务（如 `IPasswordHasher`）的 pre-reg 用例；副库缺省 DbType 改为必填或与连接串启发式警告。产品/测试债，本轮不改。

### QA30 · P2 · Vite preview 未设 strictPort
- 表面：web
- 状态：fixed
- 复现/证据：`web/vite.config.ts` 的 `server.strictPort: true` 已在 Round 1（QA02）补上；`preview` 块原先只有 `port: 5173`，`vite preview` 在端口占用时会静默挪端口，与 QA02 同类风险。已给 preview 补 `strictPort: true`。本轮 `web` vitest **90/90** 绿。
- 风险：已修。
- 建议：已修。

### QA31 · P2 · profile 失败时超管按钮全藏
- 表面：web + web-react
- 状态：question
- 复现/证据：双模板 `enterInitial` / `clearClientCache` 并行拉 permissions + profile。超管权限码常为空集；`isSuperAdmin` 只来自 profile。profile 失败时注释刻意「按普通用户处理」→ `isSuperAdmin=false` + 空码 → Vue `hasPerm`/`v-auth` 与 React `hasPerm`/`Can` 全部隐藏，直到刷新且 profile 恢复。permissions 成功但 profile 失败的窗口里，超管几乎看不到写按钮（服务端仍 sadm 绕过，直接打 API 仍可）。Round 19 对等抽查确认两侧同行为。
- 风险：短暂/持续 UX 残废，非越权；安全侧偏保守。
- 建议：profile 失败时重试一次，或 JWT `sadm` claim 客户端旁路（需防伪造）。产品拍板。双模板一起改。
### QA32 · P3 · 皮肤中文硬编码与 v-auth 一次性移除
- 表面：web
- 状态：question
- 复现/证据：`LOGIN_SKINS` 标签 `极光`/`双栏`/`聚光` 写死，切换器 aria 走 i18n 但按钮文案不跟 locale。`v-auth` 只在 `mounted` 调 `el.remove()`，无 `updated`；`clearClientCache` 刷新权限后已删节点不会回来、仍可见节点也不会立刻藏，需路由重挂。跨应用深链按 remembered `currentModuleId` 重建属设计。
- 风险：英文本地化皮肤名不对；权限刷新后按钮态可能短暂错位。
- 建议：皮肤名 i18n；关键操作改 `v-if="hasPerm"` 或 CSS 隐藏并监听 store。本轮不改。

### QA33 · P2 · React Vite preview 未设 strictPort
- 表面：web-react
- 状态：fixed
- 复现/证据：`server.strictPort: true` 已在；无 `preview` 块。`npm run preview` 端口占用时会静默挪走。已补 `preview: { port: 5174, strictPort: true, proxy… }`，对齐 Vue QA30。
- 风险：已修。
- 建议：已修。

### QA34 · P3 · React 皮肤切换器中文硬编码
- 表面：web-react
- 状态：question
- 复现/证据：`LOGIN_SKINS` 标签 `极光`/`双栏`/`聚光` 写死；tablist aria 走 i18n。与 Vue QA32 对偶。Can/zustand 选择器纪律良好（`useHasPerm` 不回新闭包）；`recycle.tabs.job` 中英均在；F5 `enterInitial`+`RequireAuth` 对齐。
- 风险：英文本地化皮肤名不对。
- 建议：皮肤名 i18n（双模板一起改）。本轮不改。

### QA35 · P3 · v-auth 一次性移除 vs Can 可重渲
- 表面：web（对比 web-react）
- 状态：question
- 复现/证据：Round 19 对等抽查。Vue `v-auth` 只在 `mounted` 调 `el.remove()`（见 QA32）；React `<Can>` 订阅 store，权限刷新后可再显隐。不是「一侧缺功能」，是指令 vs 组件的框架差。抽查其余面（权限规则、门户 F5、mustChangePassword、recycle+job、Import Skip、datascope、setEnabled、hub/force-logout、strictPort）两侧对齐。
- 风险：Vue 侧 `clearClientCache` 后按钮态可能短暂错位直至路由重挂。
- 建议：关键操作改 `v-if="hasPerm"` 或 CSS 隐藏。产品/UX 债，本轮不改。
