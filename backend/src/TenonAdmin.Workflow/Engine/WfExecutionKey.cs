using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TenonAdmin.Workflow;

/// <summary>
/// <c>wf_node_execution.ExecutionKey</c> 的构造器(M3a-1 Task 3)。姿势逐字对照
/// <see cref="WfIdentityHash"/>,但<b>不复用它的 <see cref="WfIdentityHash.Compute"/></b>——那个签名被回执的
/// 六个维度和 <see cref="WfCommandType"/>/<see cref="WfTargetType"/> 焊死,追加参数会破坏已发包的回执 hash。
/// <para><b>⚠ 发包后不可逆的契约。</b>规则同样由 <c>WfExecutionKeyTests</c> 的快照用例钉死:那条测试转红
/// 意味着契约被破坏,<b>不是</b>去更新期望值,而是把改动撤回。</para>
/// <para>后续里程碑<b>只允许在末尾追加</b>参与字段(且必须同时给旧维度定义哨兵,保证既有输入的 key 不变),
/// 不得重排、不得替换分隔符、不得改哈希算法或输出格式。</para>
/// <para><see cref="Compute"/> 的 <c>nodeVisitId</c> 为 <c>null</c> 时退化为
/// <c>(scope, instanceId, tokenId, nodeId, definitionVersionId)</c> 一次——即 M3a 之前「同一 token 在同一节点
/// 只可靠执行一次」的旧语义。本里程碑内 <b>不可达</b>(调用方总能算出 <c>NodeVisitId</c>),这是语义边界,
/// 不是缺陷,<b>不做回填</b>。</para>
/// </summary>
public static class WfExecutionKey
{
    /// <summary>字段分隔符,同 <see cref="WfIdentityHash"/>。</summary>
    private const char Separator = '\n';

    /// <summary>
    /// 计算 <c>ExecutionKey</c>:六个维度按<b>固定顺序</b>
    /// <c>ScopeKey → InstanceId → TokenId → NodeVisitId → NodeId → DefinitionVersionId</c>
    /// 用 <see cref="Separator"/> 拼接 → UTF-8 → SHA-256 → 小写十六进制。
    /// </summary>
    /// <param name="scopeKey">机构/租户范围键;归一化复用 <see cref="WfIdentityHash.NormalizeScopeKey"/>。</param>
    /// <param name="instanceId">流程实例 Id,不变文化十进制。</param>
    /// <param name="tokenId">运行 token Id,不变文化十进制。</param>
    /// <param name="nodeVisitId">节点访问序号;<c>null</c> → 哨兵 <see cref="WfIdentityHash.ScopeSentinel"/>。</param>
    /// <param name="nodeId">节点 Id;<c>Trim()</c> 后参与,不得为空白或含分隔符。</param>
    /// <param name="definitionVersionId">定义版本 Id,不变文化十进制。</param>
    /// <returns>64 位小写十六进制。</returns>
    /// <exception cref="ArgumentException"><paramref name="nodeId"/> 为空白,或任一字符串含分隔符。</exception>
    public static string Compute(
        string? scopeKey,
        long instanceId,
        long tokenId,
        long? nodeVisitId,
        string nodeId,
        long definitionVersionId)
    {
        var scope = WfIdentityHash.NormalizeScopeKey(scopeKey);   // 复用,不复制归一化规则
        var node = NormalizeNodeId(nodeId);
        var visit = nodeVisitId?.ToString(CultureInfo.InvariantCulture) ?? WfIdentityHash.ScopeSentinel;

        var payload = string.Join(
            Separator,
            scope,
            instanceId.ToString(CultureInfo.InvariantCulture),
            tokenId.ToString(CultureInfo.InvariantCulture),
            visit,
            node,
            definitionVersionId.ToString(CultureInfo.InvariantCulture));

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static string NormalizeNodeId(string nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
            throw new ArgumentException("节点 Id 必填,不接受 null / 空白。", nameof(nodeId));
        var node = nodeId.Trim();
        if (node.Contains(Separator))
            throw new ArgumentException("值不得包含换行符(key 分隔符)。", nameof(nodeId));
        return node;
    }
}
