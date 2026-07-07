using System.Security.Cryptography;
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
    IPasswordHasher hasher,
    IRbacService rbac,
    AdminSecurityOptions security) : IUserService
{
    // 生成随机初始口令的字符集:去掉易混字符(0/O、1/l/I),含大小写+数字+符号。
    private const string PASSWORD_CHARS = "ABCDEFGHJKMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789!@#$%^&*";

    /// <summary>
    /// 解析初始口令:显式给定 → 用之;否则用配置的默认口令;都没有 → 密码学随机强口令(安全默认,不落公开常量)。
    /// </summary>
    protected virtual string ResolveInitialPassword(string? provided) =>
        !string.IsNullOrEmpty(provided) ? provided
        : !string.IsNullOrEmpty(security.DefaultInitialPassword) ? security.DefaultInitialPassword!
        : RandomNumberGenerator.GetString(PASSWORD_CHARS, 16);

    /// <inheritdoc />
    public virtual Task<PagedList<UserItem>> PageAsync(UserPageInput input) =>
        users.AsQueryable()
            .WhereIF(!string.IsNullOrEmpty(input.Account), u => u.Account.Contains(input.Account!))
            .WhereIF(!string.IsNullOrEmpty(input.Name), u => u.Name.Contains(input.Name!))
            .WhereIF(input.OrgId.HasValue, u => u.OrgId == input.OrgId)
            .WhereIF(input.Enabled.HasValue, u => u.Enabled == input.Enabled!.Value)
            .OrderBy(u => u.Id)
            // 投影到 UserItem:SQL 层就不取 Password 列,哈希从不进内存/出接口
            .Select(u => new UserItem
            {
                Id = u.Id,
                Account = u.Account,
                Name = u.Name,
                OrgId = u.OrgId,
                PositionId = u.PositionId,
                Enabled = u.Enabled,
                IsSuperAdmin = u.IsSuperAdmin,
                CreateTime = u.CreateTime,
            })
            .ToPagedListAsync(input.Current, input.Size);

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
            OrgId = u.OrgId,
            PositionId = u.PositionId,
            Enabled = u.Enabled,
            IsSuperAdmin = u.IsSuperAdmin,
            CreateTime = u.CreateTime,
            RoleIds = roleIds,
        };
    }

    /// <inheritdoc />
    public virtual async Task<long> AddAsync(AddUserInput input)
    {
        // 查重把软删行也纳入:软删行仍占着唯一索引里的 Account,漏检会撞库唯一约束抛原生 500(P1-10)
        AdminException.ThrowIf(
            await users.AsQueryable().ClearFilter<ISoftDelete>().AnyAsync(u => u.Account == input.Account),
            ErrorCode.AccountExists);

        var user = new SysUser
        {
            Account = input.Account,
            Password = hasher.Hash(ResolveInitialPassword(input.Password)),
            Name = input.Name,
            OrgId = input.OrgId,
            PositionId = input.PositionId,
            Enabled = input.Enabled,
            IsSuperAdmin = false,       // 接口永不建超管(防提权);超管只能种子/手工建
        };
        // 用户 + 角色成对写入包事务:任一步失败整体回滚,不留"已提交但无角色"的幽灵用户(P2-18)
        await InTransactionAsync(async () =>
        {
            await users.InsertAsync(user);  // 插入后 AOP 已把雪花 Id 回填到 user.Id
            if (input.RoleIds.Count > 0) await rbac.SetUserRolesAsync(user.Id, input.RoleIds);
        });
        return user.Id;
    }

    /// <inheritdoc />
    public virtual async Task UpdateAsync(long id, UpdateUserInput input)
    {
        var user = await users.GetByIdAsync(id);
        AdminException.ThrowIf(user is null, ErrorCode.UserNotFound);
        // 超管护栏(与 SetEnabledAsync/DeleteAsync 同源):不可经普通更新面停用/降权超管——
        // 否则被授予用户更新权限码的下位者(或超管误操作)可把 Enabled 置 false + 清空角色,把最高账号锁死(P1-8)。
        AdminException.ThrowIf(user!.IsSuperAdmin && !input.Enabled, ErrorCode.SuperAdminProtected);

        // 只改资料字段;Account/Password/IsSuperAdmin 原样保留(整行更新时未改动即不变)
        user.Name = input.Name;
        user.OrgId = input.OrgId;
        user.PositionId = input.PositionId;
        user.Enabled = input.Enabled;
        await InTransactionAsync(async () =>
        {
            await users.UpdateAsync(user);
            await rbac.SetUserRolesAsync(id, input.RoleIds);   // 全量重设角色 + 失效其权限缓存
        });
    }

    /// <inheritdoc />
    public virtual async Task DeleteAsync(long id)
    {
        var user = await users.GetByIdAsync(id);
        AdminException.ThrowIf(user is null, ErrorCode.UserNotFound);
        AdminException.ThrowIf(user!.IsSuperAdmin, ErrorCode.SuperAdminProtected);

        await InTransactionAsync(async () =>
        {
            await rbac.SetUserRolesAsync(id, []);   // 先清角色(顺带失效缓存),再软删——避免残留孤儿关联
            await users.DeleteAsync(id);
        });
    }

    /// <inheritdoc />
    public virtual async Task<string> ResetPasswordAsync(long id, string? newPassword)
    {
        var user = await users.GetByIdAsync(id);
        AdminException.ThrowIf(user is null, ErrorCode.UserNotFound);

        var password = ResolveInitialPassword(newPassword);
        user!.Password = hasher.Hash(password);
        await users.UpdateAsync(user);
        return password;   // 返回明文仅供管理员当场转达;不落日志(调用方注意脱敏)
    }

    /// <inheritdoc />
    public virtual async Task SetEnabledAsync(long id, bool enabled)
    {
        var user = await users.GetByIdAsync(id);
        AdminException.ThrowIf(user is null, ErrorCode.UserNotFound);
        AdminException.ThrowIf(!enabled && user!.IsSuperAdmin, ErrorCode.SuperAdminProtected);

        user!.Enabled = enabled;
        await users.UpdateAsync(user);
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
