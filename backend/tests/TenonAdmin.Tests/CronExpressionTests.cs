using System.Globalization;
using TenonAdmin.Core;

namespace TenonAdmin.Tests;

/// <summary>
/// Cron 引擎向量表(docs/scheduling-ledger.md §4.4,G1 验收面)。
/// 行为分歧时以 Furion TimeCrontab 实测为准;日期锚点:2026-01-01 是周四。
/// 变异判据:删掉 <c>31W</c> 的不跨月约束(NearestWeekdayOf 的越界回退),31W 向量必须变红。
/// </summary>
public class CronExpressionTests
{
    private static DateTime D(string s) => DateTime.Parse(s, CultureInfo.InvariantCulture);

    // ── 下一次时刻向量(表驱动) ─────────────────────────────────────

    [Theory]
    // L:月末(1/31、平年 2/28、闰年 2/29)
    [InlineData("0 0 0 L * ?", "2026-01-15T10:00:00", "2026-01-31T00:00:00")]
    [InlineData("0 0 0 L * ?", "2026-02-01T00:00:00", "2026-02-28T00:00:00")]
    [InlineData("0 0 0 L * ?", "2028-02-01T00:00:00", "2028-02-29T00:00:00")]
    // L-n:月末前 n 天
    [InlineData("0 0 0 L-3 * ?", "2026-01-01T00:00:00", "2026-01-28T00:00:00")]
    [InlineData("0 0 0 L-3 2 ?", "2026-01-01T00:00:00", "2026-02-25T00:00:00")]
    // nW:最近工作日。2026-08-01 是周六 → 顺延周一 3 号(不回退到 7 月)
    [InlineData("0 0 0 1W 8 ?", "2026-07-01T00:00:00", "2026-08-03T00:00:00")]
    // 31W:2026-05-31 是周日 → 回退周五 29 号(不顺延进 6 月)
    [InlineData("0 0 0 31W 5 ?", "2026-05-01T00:00:00", "2026-05-29T00:00:00")]
    // 31W 在 30 天的 4 月无解 → 跳到 5 月再按周日回退
    [InlineData("0 0 0 31W * ?", "2026-04-01T00:00:00", "2026-05-29T00:00:00")]
    // 15W 本身是工作日(2026-07-15 周三)→ 就是 15 号
    [InlineData("0 0 0 15W * ?", "2026-07-01T00:00:00", "2026-07-15T00:00:00")]
    // LW:2026-01-31 周六 → 最后工作日 30 号
    [InlineData("0 0 0 LW * ?", "2026-01-01T00:00:00", "2026-01-30T00:00:00")]
    // 5L:当月最后一个周五(2026-01 的周五:2/9/16/23/30)
    [InlineData("0 0 0 ? * 5L", "2026-01-01T00:00:00", "2026-01-30T00:00:00")]
    // 5#5:第 5 个周五。2026 年 2/3/4 月都只有 4 个周五 → 跳到 5 月 29 日
    [InlineData("0 0 0 ? * 5#5", "2026-02-01T00:00:00", "2026-05-29T00:00:00")]
    // 名字与 7≡0:2026-07-05 是周日
    [InlineData("0 0 12 ? * SUN", "2026-07-01T00:00:00", "2026-07-05T12:00:00")]
    [InlineData("0 0 12 ? * sun", "2026-07-01T00:00:00", "2026-07-05T12:00:00")]
    [InlineData("0 0 12 ? * 7", "2026-07-01T00:00:00", "2026-07-05T12:00:00")]
    [InlineData("0 0 12 ? * 0", "2026-07-01T00:00:00", "2026-07-05T12:00:00")]
    // 周段孤立 L = SAT(Quartz 同款):2026-07-04 是周六
    [InlineData("0 0 0 ? * L", "2026-07-01T00:00:00", "2026-07-04T00:00:00")]
    // 环绕区间:周五~周一,2026-07-01 是周三 → 周五 3 号
    [InlineData("0 0 12 ? * 5-1", "2026-07-01T00:00:00", "2026-07-03T12:00:00")]
    // 步长:a/s = 从 a 到上界
    [InlineData("30/10 * * * * ?", "2026-07-01T00:00:00", "2026-07-01T00:00:30")]
    [InlineData("30/10 * * * * ?", "2026-07-01T00:00:35", "2026-07-01T00:00:40")]
    [InlineData("30/10 * * * * ?", "2026-07-01T00:00:55", "2026-07-01T00:01:30")]
    // 秒段环绕 + 步长:{50,55,0,5,10}
    [InlineData("50-10/5 * * * * ?", "2026-07-01T00:00:56", "2026-07-01T00:01:00")]
    // */15 分钟
    [InlineData("0 */15 * * * ?", "2026-07-01T08:07:00", "2026-07-01T08:15:00")]
    // 时段区间跨界推进
    [InlineData("0 0 8-10 * * ?", "2026-07-01T09:30:00", "2026-07-01T10:00:00")]
    [InlineData("0 0 8-10 * * ?", "2026-07-01T10:30:00", "2026-07-02T08:00:00")]
    // 月份名字枚举
    [InlineData("0 0 0 1 JAN,JUL ?", "2026-02-01T00:00:00", "2026-07-01T00:00:00")]
    // 5 段输入自动补秒
    [InlineData("*/5 * * * *", "2026-07-01T00:00:00", "2026-07-01T00:05:00")]
    // 严格大于:正好落在时刻上 → 给下一次
    [InlineData("0 0 12 * * ?", "2026-07-01T12:00:00", "2026-07-02T12:00:00")]
    // 毫秒尾巴:整秒截断后 +1s(12:00:00.500 → 下一候选 12:00:01)
    [InlineData("*/5 * * * * ?", "2026-07-01T12:00:00.500", "2026-07-01T12:00:05")]
    // DST 固定向量:纯日历运算,与时区无关(春跳的不存在时刻由调度层按 misfire 处理)
    [InlineData("0 30 2 29 3 ?", "2026-03-29T00:00:00", "2026-03-29T02:30:00")]
    // 周环步长相位:6-1/2 = {周六,周一}——8 格轮的幻影格 7 会把它数成 {六,日}(变异面)
    [InlineData("0 0 12 ? * 6-1/2", "2026-07-01T00:00:00", "2026-07-04T12:00:00")]
    [InlineData("0 0 12 ? * 6-1/2", "2026-07-04T12:00:00", "2026-07-06T12:00:00")]
    // 0-7 / 1-7 = 绕整环 = 全周(7≡0 折叠后两端相等而原文不等)
    [InlineData("0 0 12 ? * 0-7", "2026-07-01T00:00:00", "2026-07-01T12:00:00")]
    [InlineData("0 0 12 ? * 1-7", "2026-07-05T13:00:00", "2026-07-06T12:00:00")]
    // 步长锚点 = 段最小值(Quartz/Vixie 同款;明示豁免 TimeCrontab 的 0 锚):月 */5 = {1,6,11}、日 */10 = {1,11,21,31}
    [InlineData("0 0 0 1 */5 ?", "2026-02-01T00:00:00", "2026-06-01T00:00:00")]
    [InlineData("0 0 0 */10 * ?", "2026-01-02T00:00:00", "2026-01-11T00:00:00")]
    // 长间隔合法表达式不被搜索上界误杀:闰 2 月第 5 个周日,2026 之后下一次是 2032-02-29(4 年窗口会错报无解)
    [InlineData("0 0 0 ? 2 SUN#5", "2026-01-01T00:00:00", "2032-02-29T00:00:00")]
    // 世纪尾巴:9998 年起搜也能翻年
    [InlineData("0 0 0 1 1 ?", "9998-06-01T00:00:00", "9999-01-01T00:00:00")]
    public void Next_occurrence_matches_vector(string cron, string after, string expected)
    {
        var next = CronExpression.Parse(cron).GetNextOccurrence(D(after));
        Assert.Equal(D(expected), next);
    }

    [Fact]
    public void Feb_30_never_occurs_returns_null()
    {
        Assert.Null(CronExpression.Parse("0 0 0 30 2 ?").GetNextOccurrence(D("2026-01-01T00:00:00")));
    }

    [Fact]
    public void Near_DateTime_MaxValue_returns_null_without_throwing()
    {
        var cron = CronExpression.Parse("0 0 0 1 1 ?");
        Assert.Null(cron.GetNextOccurrence(D("9999-06-01T00:00:00")));            // 后面没有 1 月 1 日了
        Assert.Null(cron.GetNextOccurrence(D("9999-12-31T23:59:59")));            // 再 +1s 即溢出,收口
        Assert.Null(cron.GetNextOccurrence(DateTime.MaxValue));
        // 秒级表达式在 9999 年内也不抛(靠日推进守卫收口)
        Assert.NotNull(CronExpression.Parse("*/5 * * * * ?").GetNextOccurrence(D("9999-12-30T00:00:00")));
    }

    [Fact]
    public void Occurrences_count_is_capped()
    {
        var cron = CronExpression.Parse("0 0 12 * * ?");
        Assert.Throws<ArgumentOutOfRangeException>(() => cron.GetNextOccurrences(D("2026-07-01T00:00:00"), CronExpression.MaxOccurrenceCount + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => cron.GetNextOccurrences(D("2026-07-01T00:00:00"), -1));
        Assert.Empty(cron.GetNextOccurrences(D("2026-07-01T00:00:00"), 0));
    }

    [Fact]
    public void Trailing_newline_from_textarea_is_tolerated()
    {
        Assert.Equal("0 30 3 * * ?", CronExpression.Normalize("0 30 3 * * ?\n"));
        Assert.Equal("0 30 3 * * ?", CronExpression.Normalize("0 30 3 * *\r\n?"));
    }

    [Fact]
    public void Seconds_step_advances_continuously()
    {
        var cron = CronExpression.Parse("*/5 * * * * ?");
        var occurrences = cron.GetNextOccurrences(D("2026-07-01T12:00:00"), 3);
        Assert.Equal(
            [D("2026-07-01T12:00:05"), D("2026-07-01T12:00:10"), D("2026-07-01T12:00:15")],
            occurrences);
    }

    [Fact]
    public void Occurrences_are_strictly_increasing_and_capped_by_count()
    {
        var occurrences = CronExpression.Parse("0 0 12 * * ?").GetNextOccurrences(D("2026-07-01T00:00:00"), 5);
        Assert.Equal(5, occurrences.Count);
        for (var i = 1; i < occurrences.Count; i++)
            Assert.True(occurrences[i] > occurrences[i - 1]);
        Assert.Equal(D("2026-07-01T12:00:00"), occurrences[0]);
    }

    // ── 归一化 ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("*/5 * * * *", "0 */5 * * * *")]                 // 5 段升 6 段
    [InlineData("0  30 3   * * ?", "0 30 3 * * ?")]              // 压掉多余空白
    [InlineData("0 30 3 * * ?", "0 30 3 * * ?")]                 // 6 段原样
    [InlineData("0 0 12 ? * mon", "0 0 12 ? * MON")]             // 统一大写(同一日程只有一种入库形态)
    [InlineData("0 0 0 lw jan ?", "0 0 0 LW JAN ?")]
    public void Normalize_upgrades_5_fields_and_collapses_whitespace(string input, string expected)
    {
        Assert.Equal(expected, CronExpression.Normalize(input));
    }

    // ── 非法表达式(47003 语义来源) ─────────────────────────────────

    [Theory]
    [InlineData("0 0 0 1 * MON")]        // 日周同限
    [InlineData("0 0 0 L * 5L")]         // 日周同限(双特殊符)
    [InlineData("61 * * * * ?")]         // 秒越界
    [InlineData("0 0 25 * * ?")]         // 时越界
    [InlineData("0 0 0 32 * ?")]         // 日越界
    [InlineData("0 0 0 * 13 ?")]         // 月越界
    [InlineData("0 0 0 ? * 8")]          // 周越界
    [InlineData("0 0 0 ?")]              // 段数不足
    [InlineData("0 0 0 * * * *")]        // 7 段(不做年段)
    [InlineData("*/0 * * * * ?")]        // 步长为 0
    [InlineData("0 0 0 W * ?")]          // W 无目标日
    [InlineData("0 0 0 1,15W * ?")]      // W 混入枚举
    [InlineData("0 0 0 L,5 * ?")]        // L 混入枚举
    [InlineData("0 0 0 L-31 * ?")]       // L-n 越界
    [InlineData("0 0 0 ? * 5#6")]        // # 序数越界
    [InlineData("0 0 0 ? * #3")]         // # 缺周几
    [InlineData("abc * * * * ?")]        // 非数字
    [InlineData("? * * * * ?")]          // ? 只许日/周
    [InlineData("")]                     // 空串
    public void Invalid_expression_throws_FormatException(string cron)
    {
        Assert.Throws<FormatException>(() => CronExpression.Parse(cron));
    }

    [Fact]
    public void TryParse_returns_false_without_throwing_on_invalid()
    {
        Assert.False(CronExpression.TryParse("0 0 0 1 * MON", out var cron));
        Assert.Null(cron);
        Assert.True(CronExpression.TryParse("0 30 3 * * ?", out var ok));
        Assert.NotNull(ok);
    }

    [Fact]
    public void FormatException_message_carries_field_position()
    {
        var ex = Assert.Throws<FormatException>(() => CronExpression.Parse("61 * * * * ?"));
        Assert.Contains("秒", ex.Message);
    }
}
