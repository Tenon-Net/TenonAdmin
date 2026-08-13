namespace TenonAdmin.Services;

/// <summary>
/// 角色授予策略(QA36 角色委派)——集中"当前用户能否把这些角色新授予给这个用户"的判定,
/// 是<b>唯一</b>合法出口:<see cref="IRbacService.SetUserRolesAsync"/> / <see cref="IRbacService.SetRoleUsersAsync"/>
/// (二者是 <see cref="IUserService"/> Add/Update、<see cref="UserImportProfile"/> RoleNames 等全部路径最终收口处)
/// 都必须经它,不可各自实现一套等价逻辑(否则策略可被绕过)。
/// <para>
/// 判定规则:超管(<see cref="ICurrentUser.IsSuperAdmin"/>)或系统/未认证上下文(与
/// <see cref="IDataScopeContext"/> 同一"未显式设置=可信"约定)不受限;其余用户只能把
/// "启用中 + 未删除 + <c>IsDelegatable == true</c>"的角色,授予其当前数据范围内的用户,
/// 违反分别抛 <see cref="ErrorCode.RoleNotDelegatable"/> / <see cref="ErrorCode.UserOutOfDataScope"/>。
/// </para>
/// <para>
/// 只作用于<b>新增</b>的角色关联(调用方需自行算出"本次相对已有关联新增了哪些"再传入),
/// 不检查被保留/被移除的既有关联——全量替换语义下,若把"整份目标集合"都拿去校验,
/// 非超管编辑一个"早先由超管授过某不可转授角色"的用户时,即便一个角色都没多加,也会被误挡。
/// </para>
/// <para>类 public、方法 virtual,注册用 TryAdd:消费者可接入外部角色治理系统整体替换判定规则。</para>
/// </summary>
public interface IRoleGrantPolicy
{
    /// <summary>
    /// 校验当前用户能否把 <paramref name="addedRoleIds"/>(相对已有关联<b>新增</b>的角色)授予
    /// 机构为 <paramref name="targetOrgId"/> 的用户 <paramref name="targetUserId"/>(新建用户可传 null)。
    /// 不满足直接抛 <see cref="AdminException"/>;<paramref name="addedRoleIds"/> 为空时恒通过(无新增即无需校验)。
    /// </summary>
    Task EnsureGrantableAsync(IReadOnlyCollection<long> addedRoleIds, long? targetUserId, long? targetOrgId);
}
