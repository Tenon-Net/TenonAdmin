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
    IDataScopeContext dataScope) : IImportProfile
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
            Key = "OrgName", Title = "所属机构", Required = true, Width = 18,
            Hint = "填机构名称,如:技术部",
        },
        new() { Key = "PositionName", Title = "职位", Width = 14, Hint = "填职位名称,如:专员" },
        new() { Key = "DirectorName", Title = "直属主管", Width = 14, Hint = "填主管姓名" },
        new()
        {
            Key = "RoleNames", Title = "角色", Width = 22,
            Hint = "多个角色用逗号分隔,如:系统管理员,全部数据",
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

        // 机构按名查 + 越权
        if (Cell(row, "OrgName") is { Length: > 0 } orgName)
        {
            var org = await orgs.GetFirstAsync(o => o.Name == orgName.Trim());
            if (org is null)
                errors.Add(new CellError("OrgName", ErrorCode.ImportCellRefNotFound));
            else if (!IsOrgInScope(org.Id))
                errors.Add(new CellError("OrgName", ErrorCode.ImportOrgOutOfScope));
        }

        // 职位按名查
        if (Cell(row, "PositionName") is { Length: > 0 } posName)
        {
            var pos = await positions.GetFirstAsync(p => p.Name == posName.Trim());
            if (pos is null)
                errors.Add(new CellError("PositionName", ErrorCode.ImportCellRefNotFound));
        }

        // 主管按姓名查
        if (Cell(row, "DirectorName") is { Length: > 0 } dirName)
        {
            var dir = await userRepo.GetFirstAsync(u => u.Name == dirName.Trim());
            if (dir is null)
                errors.Add(new CellError("DirectorName", ErrorCode.ImportCellRefNotFound));
        }

        // 角色按名查(逗号分隔)
        if (Cell(row, "RoleNames") is { Length: > 0 } roleNames)
        {
            foreach (var name in SplitNames(roleNames))
            {
                var role = await roles.GetFirstAsync(r => r.Name == name);
                if (role is null)
                {
                    errors.Add(new CellError("RoleNames", ErrorCode.ImportCellRefNotFound));
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
        if (Cell(row, "OrgName") is { Length: > 0 } orgName)
        {
            var org = await orgs.GetFirstAsync(o => o.Name == orgName.Trim())
                ?? throw new AdminException(ErrorCode.ImportCellRefNotFound);
            // 提交路径再守一次越权(Validate 已查,防绕过)
            AdminException.ThrowIf(!IsOrgInScope(org.Id), ErrorCode.ImportOrgOutOfScope);
            orgId = org.Id;
        }

        long? positionId = null;
        if (Cell(row, "PositionName") is { Length: > 0 } posName)
        {
            var pos = await positions.GetFirstAsync(p => p.Name == posName.Trim())
                ?? throw new AdminException(ErrorCode.ImportCellRefNotFound);
            positionId = pos.Id;
        }

        long? directorId = null;
        if (Cell(row, "DirectorName") is { Length: > 0 } dirName)
        {
            var dir = await userRepo.GetFirstAsync(u => u.Name == dirName.Trim())
                ?? throw new AdminException(ErrorCode.ImportCellRefNotFound);
            directorId = dir.Id;
        }

        var roleIds = new List<long>();
        if (Cell(row, "RoleNames") is { Length: > 0 } roleNames)
        {
            foreach (var rn in SplitNames(roleNames))
            {
                var role = await roles.GetFirstAsync(r => r.Name == rn)
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
            // UpdateAsync 走 Id;软删行 GetById 会 miss——此处若已软删则拒绝覆盖(需管理员先恢复)
            AdminException.ThrowIf(entity!.IsDelete, ErrorCode.UserNotFound);

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
            // 坑 5:必须走 IUserService.AddAsync,不直插实体
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
                RoleIds = roleIds,
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
