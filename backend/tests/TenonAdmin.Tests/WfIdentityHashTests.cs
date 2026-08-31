using System.Text.RegularExpressions;
using SqlSugar;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// <see cref="WfIdentityHash"/> 的契约测试(数据库评审 §五、设计规划 §15.1 #2)。
/// <para><b>本文件的快照常量是发包后不可逆的契约</b>:TenonAdmin 经 NuGet 分发,消费者库里已有按当前规则
/// 算出的回执 hash。<see cref="Snapshot_of_a_known_tuple_is_frozen"/> 转红说明 identity 规则被改动 ——
/// 正确反应是<b>撤回改动</b>,而不是更新期望值(更新等于让所有存量回执失效、幂等静默失效)。</para>
/// <para>纯单元测试:不起 Host、不连数据库。四库一致性由 Task 8 的持久化契约套件用同一组常量再钉一遍。</para>
/// </summary>
public class WfIdentityHashTests
{
    private const string SnapshotApproveTask =
        "77236123c133484f0fab941b05d5a882fd17342bdd91fd0ee4830316b87521a4";

    private const string SnapshotStartNoOrg =
        "11e370689ac9380fea28d2e4bb269942eafb404812e9c7a1c0d9c736361e7f63";

    /// <summary>#1 已知输入 → 已知 hash。任何顺序/分隔符/算法/格式的改动都会让这两条转红。</summary>
    [Fact]
    public void Snapshot_of_a_known_tuple_is_frozen()
    {
        Assert.Equal(SnapshotApproveTask, WfIdentityHash.Compute(
            "org-1001", WfCommandType.Approve, WfTargetType.Task,
            920011223344556677L, 42L, "8f1b0c2e-3a4d-4e5f-9a8b-7c6d5e4f3a2b"));

        Assert.Equal(SnapshotStartNoOrg, WfIdentityHash.Compute(
            null, WfCommandType.Start, WfTargetType.DefinitionVersion,
            700000000000000001L, 7L, "k1"));
    }

    /// <summary>#2/#3 无机构维度的三种写法(null / 空串 / 空白 / 显式哨兵)必须归一化成同一个 identity。</summary>
    [Fact]
    public void Missing_scope_key_normalizes_to_the_sentinel()
    {
        var expected = WfIdentityHash.Compute(
            WfIdentityHash.ScopeSentinel, WfCommandType.Cancel, WfTargetType.Instance, 5L, 6L, "k");

        foreach (var scope in new string?[] { null, "", "   ", "\t" })
        {
            Assert.Equal(expected, WfIdentityHash.Compute(
                scope, WfCommandType.Cancel, WfTargetType.Instance, 5L, 6L, "k"));
        }
    }

    /// <summary>
    /// #4 request key 反向处理:不归一化、直接拒。若「没传」和「传了空」共用 identity,
    /// 所有无 key 的请求都会互相命中第一条回执。
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_request_key_is_rejected(string? requestKey)
    {
        Assert.Throws<ArgumentException>(() => WfIdentityHash.Compute(
            "org-1", WfCommandType.Approve, WfTargetType.Task, 1L, 2L, requestKey!));
    }

    /// <summary>#5 换位不撞车:动词、目标类型、两个 Id 互换都必须产生不同 identity。</summary>
    [Fact]
    public void Different_dimensions_do_not_collide()
    {
        var approve = WfIdentityHash.Compute("o", WfCommandType.Approve, WfTargetType.Task, 1L, 2L, "k");
        var reject = WfIdentityHash.Compute("o", WfCommandType.Reject, WfTargetType.Task, 1L, 2L, "k");
        Assert.NotEqual(approve, reject);

        var onTask = WfIdentityHash.Compute("o", WfCommandType.Cancel, WfTargetType.Task, 1L, 2L, "k");
        var onInstance = WfIdentityHash.Compute("o", WfCommandType.Cancel, WfTargetType.Instance, 1L, 2L, "k");
        Assert.NotEqual(onTask, onInstance);

        var straight = WfIdentityHash.Compute("o", WfCommandType.Approve, WfTargetType.Task, 1L, 2L, "k");
        var swapped = WfIdentityHash.Compute("o", WfCommandType.Approve, WfTargetType.Task, 2L, 1L, "k");
        Assert.NotEqual(straight, swapped);

        // 拼接歧义防线:相邻字段的值挪一位不得算出同一个 hash(分隔符必须真的分隔)。
        var split = WfIdentityHash.Compute("ab", WfCommandType.Approve, WfTargetType.Task, 1L, 2L, "cd");
        var shifted = WfIdentityHash.Compute("a", WfCommandType.Approve, WfTargetType.Task, 1L, 2L, "bcd");
        Assert.NotEqual(split, shifted);
    }

    /// <summary>#6 前后空白归一化,但大小写<b>保留</b>(评审 §五:trim 后保持原大小写)。</summary>
    [Fact]
    public void Values_are_trimmed_but_case_sensitive()
    {
        var plain = WfIdentityHash.Compute("org", WfCommandType.Return, WfTargetType.Task, 3L, 4L, "key-A");
        var padded = WfIdentityHash.Compute("  org ", WfCommandType.Return, WfTargetType.Task, 3L, 4L, " key-A  ");
        Assert.Equal(plain, padded);

        var upper = WfIdentityHash.Compute("ORG", WfCommandType.Return, WfTargetType.Task, 3L, 4L, "key-A");
        Assert.NotEqual(plain, upper);

        var otherCaseKey = WfIdentityHash.Compute("org", WfCommandType.Return, WfTargetType.Task, 3L, 4L, "key-a");
        Assert.NotEqual(plain, otherCaseKey);
    }

    /// <summary>#7/#8 输出格式与列宽自洽:64 位小写 hex,正好装进实体的 <c>Length = 64</c>。</summary>
    [Fact]
    public void Output_is_64_char_lowercase_hex()
    {
        var hash = WfIdentityHash.Compute("org", WfCommandType.Resubmit, WfTargetType.Instance, 9L, 9L, "k");
        Assert.Equal(64, hash.Length);
        Assert.Matches(new Regex("^[0-9a-f]{64}$"), hash);
    }

    /// <summary>含分隔符的输入必须显式拒绝,不能悄悄拼出歧义 identity。</summary>
    [Fact]
    public void Values_containing_the_separator_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => WfIdentityHash.Compute(
            "or\ng", WfCommandType.Approve, WfTargetType.Task, 1L, 2L, "k"));
        Assert.Throws<ArgumentException>(() => WfIdentityHash.Compute(
            "org", WfCommandType.Approve, WfTargetType.Task, 1L, 2L, "k\n2"));
    }

    /// <summary>未定义的枚举值会让 <c>ToString()</c> 退化成数字,把数值混进本该是名字的契约里。</summary>
    [Fact]
    public void Undefined_enum_values_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WfIdentityHash.Compute(
            "org", (WfCommandType)99, WfTargetType.Task, 1L, 2L, "k"));
        Assert.Throws<ArgumentOutOfRangeException>(() => WfIdentityHash.Compute(
            "org", WfCommandType.Approve, (WfTargetType)99, 1L, 2L, "k"));
    }

    /// <summary>回执表的三个字符串列宽都是 64,hash 列必须刚好放得下(防止将来改算法后静默截断)。</summary>
    [Fact]
    public void Receipt_entity_hash_column_fits_the_hash()
    {
        var column = typeof(WfOperationReceipt).GetProperty(nameof(WfOperationReceipt.IdentityHash))!
            .GetCustomAttributes(typeof(SugarColumn), false)
            .Cast<SugarColumn>()
            .Single();
        Assert.Equal(64, column.Length);
    }
}
