using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TenonAdmin.Workflow;

/// <summary>
/// <c>wf_operation_receipt.IdentityHash</c> 的构造器(数据库评审 §五、设计规划 §15.1 #2)。
/// <para><b>⚠ 发包后不可逆的契约。</b>TenonAdmin 经 NuGet 分发,消费者库里会留下按当前规则算出的 hash;
/// 任何字段顺序、分隔符、大小写、哨兵或算法的调整,都会让同一个请求算出不同 identity —— 旧回执再也命不中,
/// 幂等<b>静默</b>失效(不报错、只是重复推进状态)。规则由 <c>WfIdentityHashTests</c> 的快照用例钉死:
/// 那条测试转红意味着契约被破坏,<b>不是</b>去更新期望值,而是把改动撤回。</para>
/// <para>后续里程碑<b>只允许在末尾追加</b>参与字段(且必须同时给旧维度定义哨兵,保证既有输入的 hash 不变),
/// 不得重排、不得替换分隔符、不得改哈希算法或输出格式。</para>
/// <para>做成静态类而非接口是有意的:可替换性是 TenonAdmin 的第一原则,但「不可逆契约」恰恰不能可替换——
/// 消费者换掉算法就等于把自己库里已有的回执全部作废。Seam 留在上层的回执服务,不在这里。</para>
/// </summary>
public static class WfIdentityHash
{
    /// <summary>无机构用户的 <c>ScopeKey</c> 哨兵。null、空串、纯空白一律归一化为它。</summary>
    public const string ScopeSentinel = "-";

    /// <summary>字段分隔符。参与值里出现它会让拼接产生歧义,故 <see cref="Compute"/> 直接拒绝含它的输入。</summary>
    private const char Separator = '\n';

    /// <summary>
    /// 计算 identity hash:六个维度按<b>固定顺序</b>
    /// <c>ScopeKey → CommandType → TargetType → TargetId → ActorUserId → RequestKey</c>
    /// 用 <see cref="Separator"/> 拼接 → UTF-8 → SHA-256 → 小写十六进制。
    /// </summary>
    /// <param name="scopeKey">机构/租户范围键;null / 空白 → <see cref="ScopeSentinel"/>。</param>
    /// <param name="commandType">写命令类型;以<b>枚举名</b>参与拼接(数值可能因追加而变,名字不会)。</param>
    /// <param name="targetType">目标类型;同样以枚举名参与。</param>
    /// <param name="targetId">实例 / 待办 / 定义版本 Id,不变文化十进制。</param>
    /// <param name="actorUserId">操作者用户 Id,不变文化十进制。</param>
    /// <param name="requestKey">
    /// 客户端 request key。<b>必填</b>——「没传 key」与「传了空 key」若共用 identity,所有无 key 请求会互相
    /// 命中,比不幂等更危险,故此处抛异常而非归一化。
    /// </param>
    /// <returns>64 位小写十六进制。</returns>
    /// <exception cref="ArgumentException"><paramref name="requestKey"/> 为空/空白,或任一字符串含分隔符。</exception>
    /// <exception cref="ArgumentOutOfRangeException">枚举取值未定义(会让枚举名退化成数字,污染契约)。</exception>
    public static string Compute(
        string? scopeKey,
        WfCommandType commandType,
        WfTargetType targetType,
        long targetId,
        long actorUserId,
        string requestKey)
    {
        if (!Enum.IsDefined(commandType))
            throw new ArgumentOutOfRangeException(nameof(commandType), commandType, "未定义的写命令类型。");
        if (!Enum.IsDefined(targetType))
            throw new ArgumentOutOfRangeException(nameof(targetType), targetType, "未定义的目标类型。");

        var scope = NormalizeScopeKey(scopeKey);
        var request = NormalizeRequestKey(requestKey);

        var payload = string.Join(
            Separator,
            scope,
            commandType.ToString(),
            targetType.ToString(),
            targetId.ToString(CultureInfo.InvariantCulture),
            actorUserId.ToString(CultureInfo.InvariantCulture),
            request);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    /// <summary>
    /// 归一化 <c>ScopeKey</c>:null / 空串 / 纯空白 → <see cref="ScopeSentinel"/>,否则 <c>Trim()</c>(保留大小写)。
    /// <para><b>落库的 <c>ScopeKey</c> 必须用本方法的返回值</b> —— 一边存原值、一边用归一化值算 hash,
    /// 排查时诊断列就与 identity 对不上。<see cref="Compute"/> 与 <c>WfOperationIdentity.Create</c>
    /// 共用这一份实现,避免规则分叉。</para>
    /// </summary>
    public static string NormalizeScopeKey(string? scopeKey)
    {
        var scope = string.IsNullOrWhiteSpace(scopeKey) ? ScopeSentinel : scopeKey.Trim();
        EnsureNoSeparator(scope, nameof(scopeKey));
        return scope;
    }

    /// <summary>
    /// 归一化 <c>RequestKey</c>:<c>Trim()</c> 后返回。**空白不归一化而是抛异常** —— 理由见
    /// <see cref="Compute"/> 的 <c>requestKey</c> 参数说明。落库同样必须用本方法的返回值。
    /// </summary>
    /// <exception cref="ArgumentException">为 null / 空白,或含分隔符。</exception>
    public static string NormalizeRequestKey(string requestKey)
    {
        if (string.IsNullOrWhiteSpace(requestKey))
            throw new ArgumentException("request key 必填,不接受 null / 空白。", nameof(requestKey));
        var request = requestKey.Trim();
        EnsureNoSeparator(request, nameof(requestKey));
        return request;
    }

    private static void EnsureNoSeparator(string value, string paramName)
    {
        if (value.Contains(Separator))
            throw new ArgumentException("值不得包含换行符(identity 分隔符)。", paramName);
    }
}
