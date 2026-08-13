using System.Text.RegularExpressions;
using TenonAdmin.Core;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// 用户导入档案(excel-ledger §9 G3):演示字典翻译 + 按名查外键 + 机构越权检查。
/// <para><see cref="CommitRowAsync"/> 复用 <see cref="IUserService.AddAsync"/>/<see cref="IUserService.UpdateAsync"/>,
/// 不直插实体(坑 5)——守住账号查重、密码策略、MustChangePassword、角色事务等不变量。</para>
/// </summary>
public partial class UserImportProfile(
    IUserService users,
    IRepository<SysUser> userRepo,
    IRepository<SysOrg> orgs,
    IRepository<SysPosition> positions,
    IRepository<SysRole> roles,
    IRbacService rbac,
    IDataScopeContext dataScope,
    IRoleGrantPolicy? roleGrantPolicy = null) : IImportProfile
{
    /// <inheritdoc />
    public virtual string Code => "sys-user";

    /// <inheritdoc />
    public virtual IReadOnlyList<string> BusinessKeys { get; } = ["Account"];

    /// <inheritdoc />
    public virtual IReadOnlyList<ImportColumn> Columns { get; } =
    [
        new() { Key = "Account", Title = "登录账号", Required = true, Width = 18, Hint = "唯一登录账号" },
        new() { Key = "Name", Title = "姓名", Required = true, Width = 14 },
        new() { Key = "Nickname", Title = "昵称", Width = 14 },
        new() { Key = "Phone", Title = "手机号", Width = 14, Hint = "11 位手机号" },
        new() { Key = "Email", Title = "邮箱", Width = 22 },
        new()
        {
            Key = "Gender", Title = "性别", DictTypeCode = "gender", Width = 10, Hint = "下拉选择",
        },
        new()
        {
            Key = "OrgCode", Title = "所属机构编码", Required = true, Width = 18,
            Hint = "填机构编码,如:tech",
        },
        new() { Key = "PositionCode", Title = "职位编码", Width = 14, Hint = "填职位编码,如:specialist" },
        new() { Key = "DirectorAccount", Title = "直属主管账号", Width = 14, Hint = "填主管登录账号" },
        new()
        {
            Key = "RoleCodes", Title = "角色编码", Width = 22,
            Hint = "多个角色编码用逗号分隔,如:admin,data_all",
        },
        new()
        {
            // 复用内核已种的 common_status(启用=1 / 停用=0)。表单里这个字段是开关,是个闭合二态,
            // 导入自然也该是下拉而不是自由文本(用户实测反馈,§12 第 11 轮)。挂上字典即可,
            // 模板下拉与向导下拉都是现成机制;Runner 把 label「启用」译成 value「1」,
            // ParseEnabled 正好认 1/0,不必再加一套非字典候选值的平行机制。
            Key = "Enabled", Title = "启用状态", DictTypeCode = DictTypeSeed.COMMON_STATUS_CODE, Width = 12,
            Hint = "下拉选择",
        },
    ];

    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<CellError>> ValidateRowAsync(
        ImportRow row, CancellationToken cancellationToken = default)
    {
        var errors = new List<CellError>();

        // 手机/邮箱格式(空值跳过;必填由 Runner 通用校验管)
        if (Cell(row, "Phone") is { Length: > 0 } phone && !PhoneRegex().IsMatch(phone.Trim()))
            errors.Add(new CellError("Phone", ErrorCode.ImportCellFormatInvalid));
        if (Cell(row, "Email") is { Length: > 0 } email && !EmailRegex().IsMatch(email.Trim()))
            errors.Add(new CellError("Email", ErrorCode.ImportCellFormatInvalid));

        // 启用状态:字典列,值由 Runner 的字典校验先把关(label→value)。这里是纵深防御——
        // Commit 走的是前端回传的 Cells,不能只信上游。已被字典判过错的格子不再重复报,
        // 免得同一个单元格在错误报告里出现两条。
        if (Cell(row, "Enabled") is { Length: > 0 } en
            && ParseEnabled(en) is null
            && !row.Errors.Any(e => e.ColumnKey == "Enabled"))
            errors.Add(new CellError("Enabled", ErrorCode.ImportCellFormatInvalid));

        // 机构按编码查 + 越权
        if (Cell(row, "OrgCode") is { Length: > 0 } orgCode)
        {
            var org = await orgs.GetFirstAsync(o => o.Code == orgCode.Trim());
            if (org is null)
                errors.Add(new CellError("OrgCode", ErrorCode.ImportCellRefNotFound));
            else if (!IsOrgInScope(org.Id))
                errors.Add(new CellError("OrgCode", ErrorCode.ImportOrgOutOfScope));
        }

        // 职位按编码查
        if (Cell(row, "PositionCode") is { Length: > 0 } posCode)
        {
            var pos = await positions.GetFirstAsync(p => p.Code == posCode.Trim());
            if (pos is null)
                errors.Add(new CellError("PositionCode", ErrorCode.ImportCellRefNotFound));
        }

        // 主管按账号查
        if (Cell(row, "DirectorAccount") is { Length: > 0 } dirAccount)
        {
            var dir = await userRepo.GetFirstAsync(u => u.Account == dirAccount.Trim());
            if (dir is null)
                errors.Add(new CellError("DirectorAccount", ErrorCode.ImportCellRefNotFound));
        }

        // 角色按编码查(逗号分隔)
        if (Cell(row, "RoleCodes") is { Length: > 0 } roleCodes)
        {
            foreach (var code in SplitNames(roleCodes))
            {
                var role = await roles.GetFirstAsync(r => r.Code == code);
                if (role is null)
                {
                    errors.Add(new CellError("RoleCodes", ErrorCode.ImportCellRefNotFound));
                    break;
                }
            }
        }

        return errors;
    }

    /// <inheritdoc />
    public virtual async Task<IReadOnlySet<string>> FindExistingKeysAsync(
        IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default)
    {
        if (keys.Count == 0) return new HashSet<string>();
        var list = keys.ToList();
        // 软删行也占唯一索引 Account(与 UserService.AddAsync 查重口径一致)
        var existing = await userRepo.AsQueryable()
            .ClearFilter<ISoftDelete>()
            .Where(u => list.Contains(u.Account))
            .Select(u => u.Account)
            .ToListAsync();
        return existing.ToHashSet(StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public virtual async Task CommitRowAsync(
        ImportRow row, bool overwrite, CancellationToken cancellationToken = default)
    {
        var account = Cell(row, "Account")?.Trim()
            ?? throw new AdminException(ErrorCode.ImportCellRequired);
        var name = Cell(row, "Name")?.Trim() ?? "";
        var nickname = NullIfEmpty(Cell(row, "Nickname")?.Trim());
        var phone = NullIfEmpty(Cell(row, "Phone")?.Trim());
        var email = NullIfEmpty(Cell(row, "Email")?.Trim());
        // Gender:Runner 已把 label 换成 value(如"1")
        var gender = NullIfEmpty(Cell(row, "Gender"));
        var enabled = ParseEnabled(Cell(row, "Enabled")) ?? true;

        long? orgId = null;
        if (Cell(row, "OrgCode") is { Length: > 0 } orgCode)
        {
            var org = await orgs.GetFirstAsync(o => o.Code == orgCode.Trim())
                ?? throw new AdminException(ErrorCode.ImportCellRefNotFound);
            AdminException.ThrowIf(!IsOrgInScope(org.Id), ErrorCode.ImportOrgOutOfScope);
            orgId = org.Id;
        }

        long? positionId = null;
        if (Cell(row, "PositionCode") is { Length: > 0 } posCode)
        {
            var pos = await positions.GetFirstAsync(p => p.Code == posCode.Trim())
                ?? throw new AdminException(ErrorCode.ImportCellRefNotFound);
            positionId = pos.Id;
        }

        long? directorId = null;
        if (Cell(row, "DirectorAccount") is { Length: > 0 } dirAccount)
        {
            var dir = await userRepo.GetFirstAsync(u => u.Account == dirAccount.Trim())
                ?? throw new AdminException(ErrorCode.ImportCellRefNotFound);
            directorId = dir.Id;
        }

        List<long>? roleIds = null;
        if (Cell(row, "RoleCodes") is { Length: > 0 } roleCodes)
        {
            roleIds = [];
            foreach (var rc in SplitNames(roleCodes))
            {
                var role = await roles.GetFirstAsync(r => r.Code == rc)
                    ?? throw new AdminException(ErrorCode.ImportCellRefNotFound);
                roleIds.Add(role.Id);
            }
        }

        if (overwrite)
        {
            // 软删行也要找得到:与 FindExistingKeys 口径一致
            var existing = await userRepo.AsQueryable()
                .ClearFilter<ISoftDelete>()
                .Where(u => u.Account == account)
                .Take(1)
                .ToListAsync();
            var entity = existing.FirstOrDefault();
            AdminException.ThrowIf(entity is null, ErrorCode.UserNotFound);
            AdminException.ThrowIf(entity!.IsDelete, ErrorCode.UserNotFound);
            AdminException.ThrowIf(!IsOrgInScope(entity.OrgId ?? 0), ErrorCode.ImportOrgOutOfScope);

            roleIds ??= (await rbac.GetUserRoleIdsAsync(entity.Id)).ToList();

            if (roleGrantPolicy is not null && roleIds is { Count: > 0 })
            {
                var existingRoleIds = (await rbac.GetUserRoleIdsAsync(entity.Id)).ToHashSet();
                var addedRoleIds = roleIds.Where(r => !existingRoleIds.Contains(r)).ToList();
                await roleGrantPolicy.EnsureGrantableAsync(addedRoleIds, entity.Id, orgId ?? entity.OrgId);
            }

            await users.UpdateAsync(entity.Id, new UpdateUserInput
            {
                Name = name,
                Nickname = nickname,
                Phone = phone,
                Email = email,
                Gender = gender,
                OrgId = orgId,
                PositionId = positionId,
                DirectorId = directorId,
                Enabled = enabled,
                RoleIds = roleIds,
            });
        }
        else
        {
            if (roleGrantPolicy is not null && roleIds is { Count: > 0 })
                await roleGrantPolicy.EnsureGrantableAsync(roleIds, null, orgId);

            await users.AddAsync(new AddUserInput
            {
                Account = account,
                Name = name,
                Nickname = nickname,
                Phone = phone,
                Email = email,
                Gender = gender,
                OrgId = orgId,
                PositionId = positionId,
                DirectorId = directorId,
                Enabled = enabled,
                RoleIds = roleIds ?? [],
            });
        }
    }

    /// <summary>机构是否在当前用户数据范围内。不受限(超管/系统)恒 true。</summary>
    protected virtual bool IsOrgInScope(long orgId)
    {
        var scope = dataScope.Current;
        if (scope.IsUnrestricted) return true;
        return scope.OrgIds.Contains(orgId);
    }

    /// <summary>启用状态:启用/停用/是/否/1/0/true/false;无法识别返回 null。</summary>
    protected virtual bool? ParseEnabled(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return true;
        return raw.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "是" or "启用" or "y" => true,
            "0" or "false" or "no" or "否" or "停用" or "n" => false,
            _ => null,
        };
    }

    protected static string? Cell(ImportRow row, string key) =>
        row.Cells.TryGetValue(key, out var v) ? v : null;

    protected static string? NullIfEmpty(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s;

    protected static IEnumerable<string> SplitNames(string raw) =>
        raw.Split([',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    [GeneratedRegex(@"^1\d{10}$")]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();
}
