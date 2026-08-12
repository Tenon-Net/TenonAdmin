using System.Security.Cryptography;
using SqlSugar;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// <see cref="IUserService"/> 默认实现。角色分配复用 <see cref="IRbacService"/>(单一出口,顺带失效权限缓存)。
/// 守住三条安全不变量:密码哈希不出接口(出参用不含 Password 的 <see cref="UserItem"/>)、
/// 接口不建/不改超管标志(<c>IsSuperAdmin</c> 恒 false,不从入参取)、超管不可删/停/降权(防自锁死、防提权面被破坏)。
/// 用户+角色成对写入包事务(半写不留无角色幽灵用户)。
/// </summary>
public class UserService(
    IRepository<SysUser> users,
    IRepository<SysOrg> orgs,
    IRepository<SysPosition> positions,
    IPasswordHasher hasher,
    IRbacService rbac,
    ISessionService sessions,
    ILoginLockService loginLock,
    ISecurityPolicyProvider policy,
    AdminSecurityOptions security,
    // 可选参数:默认 DI 注入;消费者子类省略也能编译(§5.3)。externalBindings 保留为构造参数(消费者子类可能依赖它);
    // 软删不再清绑定(QA23),真正清理由回收站 Purge 经 DI 取 ISysUserExternalService 完成。
    IPasswordHistoryService? passwordHistory = null,
#pragma warning disable CS9113
    ISysUserExternalService? externalBindings = null,
#pragma warning restore CS9113
    // 统一时间源(§1.11):尾随可选参数,DI 正常注入;消费者子类省略也能编译(§5.3)
    TimeProvider? time = null,
    // 导出行数上限(excel-ledger §6.1);可选尾参,未注入时用默认 50000
    AdminExcelOptions? excel = null,
    // QA25.3:头像 URL 校验(默认只放行 null/空白或本地签名直链);未注入(纯 Services 宿主)时跳过校验
    IAvatarUrlValidator? avatarValidator = null,
    // QA08:数据范围(非超管只能管理范围内机构的用户)+ 当前用户上下文
    IDataScopeContext? dataScope = null,
    ICurrentUser? currentUser = null) : IUserService
{
    // 生成随机初始口令的字符集:去掉易混字符(0/O、1/l/I),含大小写+数字+符号。
    private const string PASSWORD_CHARS = "ABCDEFGHJKMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789!@#$%^&*";

    // LastPasswordChangeTime 是与审计字段同类的持久化业务时间戳,走本地时钟(与 SqlSugarSetup 的 GetLocalNow 审计口径一致)
    private DateTime Now => (time ?? TimeProvider.System).GetLocalNow().DateTime;

    private AdminExcelOptions Excel => excel ?? new AdminExcelOptions();

    /// <summary>
    /// 解析初始口令:显式给定 → 用之;否则用配置的默认口令;都没有 → 密码学随机强口令(安全默认,不落公开常量)。
    /// </summary>
    protected virtual string ResolveInitialPassword(string? provided) =>
        !string.IsNullOrEmpty(provided) ? provided
        : !string.IsNullOrEmpty(security.DefaultInitialPassword) ? security.DefaultInitialPassword!
        : RandomNumberGenerator.GetString(PASSWORD_CHARS, 16);

    /// <inheritdoc />
    // ponytail(P2-19): .Contains 生成参数化 LIKE(无注入风险),但未转义 LIKE 元字符 % _——
    //   搜 "%"/"_" 会被当通配、命中面偏大。属功能精确性,后台搜索普遍接受此行为;要精确字面匹配
    //   再引 EscapeLike + ESCAPE 子句。全部 Page 方法同此约定。
    public virtual async Task<PagedList<UserItem>> PageAsync(UserPageInput input)
    {
        var holders = await ResolveRoleHolderIdsAsync(input.RoleId);
        // 该角色一个人都没有:空 IN 列表在各方言下行为不一,显式短路,别把它交给 SQL
        if (holders is { Count: 0 })
            return new PagedList<UserItem> { Current = input.Current, Size = input.Size, Total = 0, Items = [] };

        var page = await BuildListQuery(input, holders).ToPagedListAsync(input.Current, input.Size);
        return new PagedList<UserItem>
        {
            Current = page.Current,
            Size = page.Size,
            Total = page.Total,
            Items = await FillOrgPositionNamesAsync(page.Items),
        };
    }

    /// <inheritdoc />
    // 坑 1:不得走 PageAsync/ToPagedListAsync——MAX_SIZE=200 会静默截断导出。
    // 与 PageAsync 共用 BuildListQuery,保证导出筛选与列表一致。
    public virtual async Task<IReadOnlyList<UserItem>> ExportAsync(UserPageInput input)
    {
        var holders = await ResolveRoleHolderIdsAsync(input.RoleId);
        if (holders is { Count: 0 }) return [];

        var max = Excel.MaxExportRows;
        var items = await BuildListQuery(input, holders).Take(max + 1).ToListAsync();
        AdminException.ThrowIf(items.Count > max, ErrorCode.ExportRowLimitExceeded);
        return await FillOrgPositionNamesAsync(items);
    }

    /// <summary>
    /// 用户列表/导出共用的查询骨架(过滤 + 排序 + 投影)。
    /// 抽出来是为了 PageAsync 与 ExportAsync 不复制过滤条件(坑 1:两条链漂移 = 导出数据范围与列表不一致)。
    /// <b>不改</b> <see cref="PageAsync"/> 的 public 签名(它是 virtual,消费者可能已覆写)。
    /// </summary>
    protected virtual ISugarQueryable<UserItem> BuildListQuery(UserPageInput input, List<long>? holders)
    {
        var scope = dataScope?.Current;
        var isSuperAdmin = currentUser?.IsSuperAdmin == true;
        var scopeOrgIds = scope is not null && !scope.IsUnrestricted && !isSuperAdmin
            ? scope.OrgIds.ToList() : null;
        var selfId = currentUser?.UserId ?? 0;
        var includeSelf = scope?.IncludeSelf == true;

        return users.AsQueryable()
            .WhereIF(!string.IsNullOrEmpty(input.Account), u => u.Account.Contains(input.Account!))
            .WhereIF(!string.IsNullOrEmpty(input.Name), u => u.Name.Contains(input.Name!))
            .WhereIF(input.OrgId.HasValue, u => u.OrgId == input.OrgId)
            .WhereIF(holders != null, u => holders!.Contains(u.Id))
            .WhereIF(input.Enabled.HasValue, u => u.Enabled == input.Enabled!.Value)
            // QA08: non-superadmin sees only users in their org scope (+ self if IncludeSelf)
            // 布尔标记写成 `== true` 而非裸布尔:SqlServer 的谓词上下文不接受裸标量(裸 1/0 →
            // "非布尔类型的表达式"),必须渲染成比较式。同 SqlSugarSetup 的全局数据范围过滤器。
            .WhereIF(scopeOrgIds != null, u =>
                (u.OrgId != null && scopeOrgIds!.Contains(u.OrgId.Value))
                || (includeSelf == true && u.Id == selfId))
            // 客户端排序(按 SysUser 实体列安全校验)优先,否则默认按 Id;必须在 Select 投影前,按实体列排序
            .OrderBySafe(input, q => q.OrderBy(u => u.Id))
            // 投影到 UserItem:SQL 层就不取 Password 列,哈希从不进内存/出接口
            .Select(u => new UserItem
            {
                Id = u.Id,
                Account = u.Account,
                Name = u.Name,
                Nickname = u.Nickname,
                Phone = u.Phone,
                Email = u.Email,
                Gender = u.Gender,
                Avatar = u.Avatar,
                OrgId = u.OrgId,
                PositionId = u.PositionId,
                DirectorId = u.DirectorId,
                Enabled = u.Enabled,
                IsSuperAdmin = u.IsSuperAdmin,
                ForceTotp = u.ForceTotp,
                TotpEnabled = u.TotpEnabled,
                CreateTime = u.CreateTime,
            });
    }

    /// <summary>
    /// 角色 → 持有者 Id 列表(未按角色筛选时返回 null,调用方据此跳过该条件)。
    /// <para>刻意用"预取 Id + Contains"而不是 SqlFunc 子查询(EXISTS):内核对外承诺支持 4 种数据库,
    /// 而 CI 只跑 SQLite / MySQL 两条腿,SqlServer 与 PostgreSQL 上的表达式翻译无人验证;
    /// 这里只用到最朴素的 Where/Select,四方言必然可移植(同 <see cref="FillOrgPositionNamesAsync"/> 的跨表套路)。
    /// 走 <c>users.Db</c> 逃生舱口而非注入 <c>IRepository&lt;SysUserRole&gt;</c>:给本类主构造器加参数
    /// 对继承它的消费者是源码破坏性变更(替换性契约由 ReplaceabilityTests 守住)。</para>
    /// ponytail: 持有者多时 IN 列表会很长;真撞上再换 EXISTS 子查询并补齐四方言测试。
    /// </summary>
    protected virtual async Task<List<long>?> ResolveRoleHolderIdsAsync(long? roleId) =>
        roleId.HasValue
            ? await users.Db.Queryable<SysUserRole>().Where(x => x.RoleId == roleId.Value).Select(x => x.UserId).ToListAsync()
            : null;

    /// <summary>按本页出现的机构/职位/主管 Id 各去重批量查一次名称回填(避免逐行 N+1)。已删/未分配则名称留 null。</summary>
    protected virtual async Task<IReadOnlyList<UserItem>> FillOrgPositionNamesAsync(IReadOnlyList<UserItem> items)
    {
        var orgIds = items.Where(x => x.OrgId.HasValue).Select(x => x.OrgId!.Value).Distinct().ToList();
        var posIds = items.Where(x => x.PositionId.HasValue).Select(x => x.PositionId!.Value).Distinct().ToList();
        var dirIds = items.Where(x => x.DirectorId.HasValue).Select(x => x.DirectorId!.Value).Distinct().ToList();
        var orgName = (orgIds.Count == 0
            ? []
            : await orgs.AsQueryable().Where(o => orgIds.Contains(o.Id)).Select(o => new { o.Id, o.Name }).ToListAsync())
            .ToDictionary(o => o.Id, o => o.Name);
        var posName = (posIds.Count == 0
            ? []
            : await positions.AsQueryable().Where(p => posIds.Contains(p.Id)).Select(p => new { p.Id, p.Name }).ToListAsync())
            .ToDictionary(p => p.Id, p => p.Name);
        // 主管也是 sys_user 行:同表按 Id 批量取姓名(全局软删过滤器已排除已删主管 → 名称留 null)
        var dirName = (dirIds.Count == 0
            ? []
            : await users.AsQueryable().Where(u => dirIds.Contains(u.Id)).Select(u => new { u.Id, u.Name }).ToListAsync())
            .ToDictionary(u => u.Id, u => u.Name);
        return items.Select(u => u with
        {
            OrgName = u.OrgId is { } oid && orgName.TryGetValue(oid, out var on) ? on : null,
            PositionName = u.PositionId is { } pid && posName.TryGetValue(pid, out var pn) ? pn : null,
            DirectorName = u.DirectorId is { } did && dirName.TryGetValue(did, out var dn) ? dn : null,
        }).ToList();
    }

    /// <inheritdoc />
    public virtual async Task<UserDetail> GetAsync(long id)
    {
        var u = await users.GetByIdAsync(id);
        AdminException.ThrowIf(u is null, ErrorCode.UserNotFound);
        var roleIds = await rbac.GetUserRoleIdsAsync(id);
        return new UserDetail
        {
            Id = u!.Id,
            Account = u.Account,
            Name = u.Name,
            Nickname = u.Nickname,
            Phone = u.Phone,
            Email = u.Email,
            Gender = u.Gender,
            Avatar = u.Avatar,
            OrgId = u.OrgId,
            PositionId = u.PositionId,
            DirectorId = u.DirectorId,
            Enabled = u.Enabled,
            IsSuperAdmin = u.IsSuperAdmin,
            ForceTotp = u.ForceTotp,
            TotpEnabled = u.TotpEnabled,
            CreateTime = u.CreateTime,
            RoleIds = roleIds,
        };
    }

    /// <inheritdoc />
    public virtual async Task<AddUserOutput> AddAsync(AddUserInput input)
    {
        // QA08: non-superadmin cannot add user to out-of-scope org
        ValidateOrgInScope(input.OrgId);

        // 查重把软删行也纳入:软删行仍占着唯一索引里的 Account,漏检会撞库唯一约束抛原生 500(P1-10)
        AdminException.ThrowIf(
            await users.AsQueryable().ClearFilter<ISoftDelete>().AnyAsync(u => u.Account == input.Account),
            ErrorCode.AccountExists);
        AdminException.ThrowIf(
            avatarValidator is not null && !avatarValidator.IsValid(input.Avatar),
            ErrorCode.AvatarUrlInvalid);

        // 仅校验管理员显式提供的口令;未提供时走随机/默认强口令,不套策略(生成的随机口令无特殊字符,避免误伤)
        if (!string.IsNullOrEmpty(input.Password)) await policy.ValidatePasswordAsync(input.Password);

        // 先算出明文再哈希:留空时这是一个随机口令,不回传给管理员就没人知道它 → 建出来即死号
        var initialPassword = ResolveInitialPassword(input.Password);

        var user = new SysUser
        {
            Account = input.Account,
            Password = hasher.Hash(initialPassword),
            Name = input.Name,
            Nickname = input.Nickname,
            Phone = input.Phone,
            Email = input.Email,
            Gender = input.Gender,
            Avatar = input.Avatar,
            OrgId = input.OrgId,
            PositionId = input.PositionId,
            DirectorId = input.DirectorId,
            Enabled = input.Enabled,
            ForceTotp = input.ForceTotp,
            IsSuperAdmin = false,       // 接口永不建超管(防提权);超管只能种子/手工建
            MustChangePassword = true,  // 管理员建号:初始口令由管理员/系统设定,强制用户首登改密(§14)
            LastPasswordChangeTime = Now,   // 密码过期窗口从建号起算
        };
        // 用户 + 角色成对写入包事务:任一步失败整体回滚,不留"已提交但无角色"的幽灵用户(P2-18)
        await InTransactionAsync(async () =>
        {
            await users.InsertAsync(user);  // 插入后 AOP 已把雪花 Id 回填到 user.Id
            if (input.RoleIds.Count > 0) await rbac.SetUserRolesAsync(user.Id, input.RoleIds);
        });
        await (passwordHistory?.AppendAsync(user.Id, user.Password) ?? Task.CompletedTask);   // 记录初始口令(建号只记录不校验;策略关/未注入时空操作)
        return new AddUserOutput { Id = user.Id, InitialPassword = initialPassword };
    }

    /// <inheritdoc />
    public virtual async Task UpdateAsync(long id, UpdateUserInput input)
    {
        var user = await users.GetByIdAsync(id);
        AdminException.ThrowIf(user is null, ErrorCode.UserNotFound);
        // 超管护栏(与 SetEnabledAsync/DeleteAsync 同源):不可经普通更新面停用/降权超管——
        // 否则被授予用户更新权限码的下位者(或超管误操作)可把 Enabled 置 false + 清空角色,把最高账号锁死(P1-8)。
        AdminException.ThrowIf(user!.IsSuperAdmin && !input.Enabled, ErrorCode.SuperAdminProtected);
        // QA10: cannot update self (prevent admin from accidentally disabling/modifying own account)
        AdminException.ThrowIf(currentUser?.UserId == id, ErrorCode.CannotOperateSelf);
        // QA08: non-superadmin cannot move user to out-of-scope org
        ValidateOrgInScope(input.OrgId);
        AdminException.ThrowIf(
            avatarValidator is not null && !avatarValidator.IsValid(input.Avatar),
            ErrorCode.AvatarUrlInvalid);

        // 只改资料字段;Account/Password/IsSuperAdmin 原样保留(整行更新时未改动即不变)
        user.Name = input.Name;
        user.Nickname = input.Nickname;
        user.Phone = input.Phone;
        user.Email = input.Email;
        user.Gender = input.Gender;
        user.Avatar = input.Avatar;
        user.OrgId = input.OrgId;
        user.PositionId = input.PositionId;
        user.DirectorId = input.DirectorId;
        user.Enabled = input.Enabled;
        user.ForceTotp = input.ForceTotp;
        await InTransactionAsync(async () =>
        {
            await users.UpdateAsync(user);
            await rbac.SetUserRolesAsync(id, input.RoleIds);   // 全量重设角色 + 失效其权限缓存
            // 经通用更新面停用 → 也要下线其会话(否则原访问令牌到期前仍可用;刷新已被 RefreshAsync 挡)
            if (!input.Enabled) await sessions.RevokeAllForUserAsync(id);
        });
    }

    /// <inheritdoc />
    public virtual async Task DeleteAsync(long id)
    {
        var user = await users.GetByIdAsync(id);
        AdminException.ThrowIf(user is null, ErrorCode.UserNotFound);
        AdminException.ThrowIf(user!.IsSuperAdmin, ErrorCode.SuperAdminProtected);
        // QA10: cannot delete self
        AdminException.ThrowIf(currentUser?.UserId == id, ErrorCode.CannotOperateSelf);

        await InTransactionAsync(async () =>
        {
            // 软删不清角色/外部绑定:关联保留,恢复即可用;真正清理在回收站 Purge(QA23)
            await users.DeleteAsync(id);
            await sessions.RevokeAllForUserAsync(id);   // 删除用户即下线其全部会话(原令牌不再可用)
        });
    }

    /// <inheritdoc />
    public virtual async Task DeleteBatchAsync(IReadOnlyCollection<long> ids)
    {
        if (ids.Count == 0) return;
        var idList = ids.ToList();
        var targets = await users.AsQueryable().Where(u => idList.Contains(u.Id)).ToListAsync();
        // 超管护栏先于自操作护栏:批次含超管时返回更具体的 SuperAdminProtected(与单删同源语义)。
        AdminException.ThrowIf(targets.Any(u => u.IsSuperAdmin), ErrorCode.SuperAdminProtected);
        // QA10: cannot delete self via batch
        AdminException.ThrowIf(currentUser?.UserId is long self && ids.Contains(self), ErrorCode.CannotOperateSelf);

        // 整批包一个事务:任一步失败全回滚。软删不清角色/外部绑定(QA23:关联保留,恢复即可用;真正清理在 Purge)。
        await InTransactionAsync(async () =>
        {
            foreach (var u in targets)
            {
                await users.DeleteAsync(u.Id);
                await sessions.RevokeAllForUserAsync(u.Id);
            }
        });
    }

    /// <inheritdoc />
    public virtual async Task<string> ResetPasswordAsync(long id, string? newPassword)
    {
        var user = await users.GetByIdAsync(id);
        AdminException.ThrowIf(user is null, ErrorCode.UserNotFound);

        // 仅校验显式提供的口令;未提供时走随机/默认强口令(同 AddAsync 约定)
        if (!string.IsNullOrEmpty(newPassword)) await policy.ValidatePasswordAsync(newPassword);

        var password = ResolveInitialPassword(newPassword);
        user!.Password = hasher.Hash(password);
        user.MustChangePassword = true;   // 管理员重置:强制用户下次登录后改密(§14)
        user.LastPasswordChangeTime = Now;   // 密码过期窗口从本次重置重新起算
        await users.UpdateAsync(user);
        await (passwordHistory?.AppendAsync(id, user.Password) ?? Task.CompletedTask);   // 记录重置口令(策略关/未注入时空操作),使用户不能改回此口令

        // 重置密码的语义是"这个账号我不再信任现有持有者"——旧口令派生出的会话必须一并作废,
        // 否则盗号者手里的 access/refresh 纹丝不动(refresh 不校验密码版本且滑动续期 → 可无限续命),
        // 管理员按下的第一个按钮对攻击者实际影响为 0。与停用/删除/改角色三条 kill-switch 对齐。
        await sessions.RevokeAllForUserAsync(id);
        // 顺带解除登录失败锁定:锁定判定在账密校验之前(锁定期正确口令也进不来),
        // 不清计数则"管理员重置了密码但用户仍登不上",管理员只能让用户干等锁定窗口过期。
        await loginLock.ResetAsync(user.Account);

        return password;   // 返回明文仅供管理员当场转达;不落日志(调用方注意脱敏)
    }

    /// <inheritdoc />
    public virtual async Task SetEnabledAsync(long id, bool enabled)
    {
        var user = await users.GetByIdAsync(id);
        AdminException.ThrowIf(user is null, ErrorCode.UserNotFound);
        AdminException.ThrowIf(!enabled && user!.IsSuperAdmin, ErrorCode.SuperAdminProtected);
        // QA10: cannot enable/disable self
        AdminException.ThrowIf(currentUser?.UserId == id, ErrorCode.CannotOperateSelf);

        user!.Enabled = enabled;
        await users.UpdateAsync(user);
        // 停用即下线其全部会话 → 原访问令牌下次请求即 401(不等自然过期);启用则不动会话
        if (!enabled) await sessions.RevokeAllForUserAsync(id);
    }

    /// <summary>
    /// QA08: validate that the given org is within the caller's data scope.
    /// Superadmin and unrestricted scope bypass; null orgId is allowed (unassigned user).
    /// </summary>
    protected virtual void ValidateOrgInScope(long? orgId)
    {
        if (orgId is null) return;
        if (currentUser?.IsSuperAdmin == true) return;
        var scope = dataScope?.Current;
        if (scope is null || scope.IsUnrestricted) return;
        AdminException.ThrowIf(!scope.OrgIds.Contains(orgId.Value), ErrorCode.OrgOutOfScope);
    }

    /// <summary>
    /// 在数据库事务内执行成对写入(用户行 + 角色关联)。所有仓储共享同一 SqlSugarScope 单例,
    /// 内层 <see cref="IRbacService.SetUserRolesAsync"/> 的写与缓存失效随本事务;失败整体回滚。
    /// (缓存失效即便发生在最终回滚前也无害:清缓存后下次读从库回填未变数据。)
    /// </summary>
    protected virtual async Task InTransactionAsync(Func<Task> work)
    {
        var result = await users.Db.Ado.UseTranAsync(work);
        if (!result.IsSuccess) throw result.ErrorException;
    }
}
