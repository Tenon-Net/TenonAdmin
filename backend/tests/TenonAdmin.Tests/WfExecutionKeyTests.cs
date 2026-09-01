using System.Text.RegularExpressions;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// <see cref="WfExecutionKey"/> 的契约测试(M3a-1 Task 3),仿 <see cref="WfIdentityHashTests"/>。
/// <para><b>本文件的快照常量是发包后不可逆的契约</b>:转红说明 key 规则被改动 —— 正确反应是<b>撤回改动</b>,
/// 而不是更新期望值。</para>
/// </summary>
public class WfExecutionKeyTests
{
    private const string SnapshotWithVisit =
        "aacd834bc82204931aeae946f0ada9b8b4edc391ed9eb4a415cbf7dc0938fbe3";

    private const string SnapshotNoScopeNoVisit =
        "09ce15c107365e54ed5c6b51f0f639ab3c4dc75a05cda61bfcbfce0a2faf27c5";

    /// <summary>#1 已知输入 → 已知 hash(硬编码快照值,锁死契约)。</summary>
    [Fact]
    public void Snapshot_of_a_known_tuple_is_frozen()
    {
        Assert.Equal(SnapshotWithVisit, WfExecutionKey.Compute(
            "org-1001", 920011223344556677L, 42L, 100L, "node-1", 7L));

        Assert.Equal(SnapshotNoScopeNoVisit, WfExecutionKey.Compute(
            null, 700000000000000001L, 7L, null, "node-start", 1L));
    }

    /// <summary>#2 <c>NodeVisitId = null</c> 归一化为哨兵,且与真实 visitId 算出不同 hash。</summary>
    [Fact]
    public void Missing_node_visit_id_normalizes_to_the_sentinel_and_differs_from_a_real_one()
    {
        var withoutVisit = WfExecutionKey.Compute("org", 1L, 2L, null, "node", 3L);
        var withRealVisit = WfExecutionKey.Compute("org", 1L, 2L, 999L, "node", 3L);
        Assert.NotEqual(withoutVisit, withRealVisit);
    }

    /// <summary>#3 <c>ScopeKey</c> null / 空串 / 纯空白三者同 hash,且等于显式传 <c>"-"</c>。</summary>
    [Fact]
    public void Missing_scope_key_normalizes_to_the_sentinel()
    {
        var expected = WfExecutionKey.Compute("-", 1L, 2L, 3L, "node", 4L);

        foreach (var scope in new string?[] { null, "", "   ", "\t" })
        {
            Assert.Equal(expected, WfExecutionKey.Compute(scope, 1L, 2L, 3L, "node", 4L));
        }
    }

    /// <summary>#4 字段顺序生效:交换 <c>instanceId</c> 与 <c>tokenId</c> 的取值 → 不同 hash。</summary>
    [Fact]
    public void Different_field_positions_do_not_collide()
    {
        var straight = WfExecutionKey.Compute("org", 1L, 2L, 3L, "node", 4L);
        var swapped = WfExecutionKey.Compute("org", 2L, 1L, 3L, "node", 4L);
        Assert.NotEqual(straight, swapped);
    }

    /// <summary>#5 <c>NodeId</c> 含 <c>'\n'</c> → <see cref="ArgumentException"/>;<c>NodeId</c> 空白 → 同样抛出。</summary>
    [Fact]
    public void Invalid_node_id_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => WfExecutionKey.Compute("org", 1L, 2L, 3L, "no\nde", 4L));
        Assert.Throws<ArgumentException>(() => WfExecutionKey.Compute("org", 1L, 2L, 3L, "   ", 4L));
    }

    /// <summary>#6 输出恒为 64 位、全小写十六进制。</summary>
    [Fact]
    public void Output_is_64_char_lowercase_hex()
    {
        var hash = WfExecutionKey.Compute("org", 1L, 2L, 3L, "node", 4L);
        Assert.Equal(64, hash.Length);
        Assert.Matches(new Regex("^[0-9a-f]{64}$"), hash);
    }
}
