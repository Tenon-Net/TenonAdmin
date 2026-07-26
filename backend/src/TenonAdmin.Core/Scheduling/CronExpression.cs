using System.Globalization;

namespace TenonAdmin.Core;

/// <summary>
/// 6 段秒级 cron 表达式(<c>秒 分 时 日 月 周</c>),自研零依赖实现(docs/scheduling-ledger.md §4)。
/// <para>
/// 语法:全段支持 <c>* , - /</c>(区间支持环绕,如周 <c>5-1</c>);日/周支持 <c>?</c>(互斥占位);
/// 日段支持 <c>L</c>(月末)/<c>L-n</c>/<c>LW</c>(月末最后工作日)/<c>nW</c>(最近工作日,不跨月);
/// 周段支持 <c>nL</c>(当月最后一个周几)/<c>n#m</c>(第 m 个周几,不存在则该月无解);
/// 月/周支持 <c>JAN-DEC</c>/<c>SUN-SAT</c> 名字(大小写不敏感),周 <c>7</c> 等价 <c>0</c>(周日)。
/// 5 段输入(<c>分 时 日 月 周</c>)自动升 6 段(秒位补 0);<b>日与周不能同时受限</b>(Quartz 同款,47003 语义);
/// 不做年段。行为分歧以 Furion TimeCrontab 实测为准。
/// </para>
/// <para>
/// 时间语义:纯日历运算、与时区无关(DST 语义在调度层:春跳按 misfire 处理,秋回靠领取 CAS 不重复触发);
/// <see cref="GetNextOccurrence"/> 返回严格大于 after 的下一次,搜索上界 after + 100 年,无解返回 null——
/// 调用侧把任务置 Completed。上界不能取 4 年:`SUN#5 2月`(闰 2 月第 5 个周日)真实间隔可达 6 年以上,
/// 2100 平年附近 `29 2` 间隔 8 年,4 年窗口会把合法任务误判死;100 年仍能让真不可能的表达式(2 月 30 日)
/// 亚毫秒级判死。逼近 <see cref="DateTime.MaxValue"/> 时收口返回 null,不抛。
/// </para>
/// 全静态纯函数 + 不可变实例,无 DI、线程安全,可自由缓存复用。
/// </summary>
public sealed class CronExpression
{
    private static readonly string[] FieldNames = ["秒", "分", "时", "日", "月", "周"];
    private static readonly string[] MonthNames = ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];
    private static readonly string[] DowNames = ["SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT"];

    private enum DayMode { Bits, Last, LastWeekday, NearestWeekday }
    private enum DowMode { Bits, LastOfMonth, Nth }

    private readonly ulong _seconds;   // bit 0..59
    private readonly ulong _minutes;   // bit 0..59
    private readonly uint _hours;      // bit 0..23
    private readonly int _months;      // bit 1..12

    private readonly DayMode _dayMode;
    private readonly uint _days;       // bit 1..31(Bits 模式)
    private readonly int _dayArg;      // Last:距月末天数(L=0,L-3=3);NearestWeekday:目标日

    private readonly DowMode _dowMode;
    private readonly int _dows;        // bit 0..6,0=周日(Bits 模式)
    private readonly int _dowArg;      // LastOfMonth / Nth 的周几
    private readonly int _dowNth;      // Nth 的序数(1..5)

    /// <summary>搜索上界(年):见类注释——4 年会误杀 `SUN#5 2月` 这类跨世纪长间隔的合法表达式</summary>
    private const int SearchYears = 100;

    /// <summary>单次 <see cref="GetNextOccurrences"/> 的 count 上限(preview 用途;防大 count 预分配与长扫描)</summary>
    public const int MaxOccurrenceCount = 1000;

    /// <summary>归一化后的 6 段表达式(入库形态;5 段输入已补秒位,统一大写)</summary>
    public string Expression { get; }

    /// <summary>解析表达式;非法抛 <see cref="FormatException"/>(消息带段位与原因,供 47003 的 args)。</summary>
    public static CronExpression Parse(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            throw new FormatException("cron 表达式为空");
        var tokens = expression.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 5)
            tokens = ["0", .. tokens];
        if (tokens.Length != 6)
            throw new FormatException($"cron 需为 6 段(秒 分 时 日 月 周)或 5 段(分 时 日 月 周),实得 {tokens.Length} 段");
        return new CronExpression(tokens);
    }

    /// <summary>尝试解析;失败返回 false 且 <paramref name="cron"/> 为 null,不抛。</summary>
    public static bool TryParse(string expression, out CronExpression? cron)
    {
        try { cron = Parse(expression); return true; }
        catch (FormatException) { cron = null; return false; }
    }

    /// <summary>归一化:5 段升 6 段(秒位补 0)、压掉多余空白;合法性顺带校验(非法抛 <see cref="FormatException"/>)。</summary>
    public static string Normalize(string expression) => Parse(expression).Expression;

    /// <summary>严格大于 <paramref name="after"/> 的下一次时刻(整秒);after + 100 年内无解返回 null(逼近 DateTime.MaxValue 也收口 null,不抛)。</summary>
    public DateTime? GetNextOccurrence(DateTime after)
    {
        if (after >= MaxSearchStart) return null;   // 再 +1s 就溢出,收口

        // 起点:after 整秒截断 + 1s(严格大于;毫秒尾巴被吞进当秒)
        var candidate = new DateTime(after.Year, after.Month, after.Day, after.Hour, after.Minute, after.Second, after.Kind).AddSeconds(1);
        var limit = after > MaxFullWindowStart ? DateTime.MaxValue : after.AddYears(SearchYears);
        while (candidate <= limit)
        {
            if ((_months & (1 << candidate.Month)) == 0)
            {
                if (candidate.Year == 9999 && candidate.Month == 12) return null;   // AddMonths 会溢出
                candidate = new DateTime(candidate.Year, candidate.Month, 1, 0, 0, 0, candidate.Kind).AddMonths(1);
                continue;
            }
            if (DayMatches(candidate.Year, candidate.Month, candidate.Day) && TryFindTime(candidate, out var result))
                return result;
            if (candidate.Date == DateTime.MaxValue.Date) return null;   // AddDays 会溢出
            candidate = candidate.Date.AddDays(1);
        }
        return null;
    }

    private static readonly DateTime MaxSearchStart = DateTime.MaxValue.AddSeconds(-2);
    private static readonly DateTime MaxFullWindowStart = DateTime.MaxValue.AddYears(-SearchYears);

    /// <summary>
    /// 从 <paramref name="after"/> 起连续取 <paramref name="count"/> 次(preview 用);提前无解则截短。
    /// <paramref name="count"/> 上限 <see cref="MaxOccurrenceCount"/>,越限抛 <see cref="ArgumentOutOfRangeException"/>。
    /// </summary>
    public IReadOnlyList<DateTime> GetNextOccurrences(DateTime after, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, MaxOccurrenceCount);
        var list = new List<DateTime>(Math.Min(count, 64));
        var cursor = after;
        for (var i = 0; i < count; i++)
        {
            var next = GetNextOccurrence(cursor);
            if (next is null) break;
            list.Add(next.Value);
            cursor = next.Value;
        }
        return list;
    }

    /// <inheritdoc/>
    public override string ToString() => Expression;

    // ── 解析 ─────────────────────────────────────────────────────────

    private CronExpression(string[] f)
    {
        Expression = string.Join(' ', f).ToUpperInvariant();

        _seconds = ParseBits(f[0].ToUpperInvariant(), 0, 59, null, 0, 0);
        _minutes = ParseBits(f[1].ToUpperInvariant(), 0, 59, null, 0, 1);
        _hours = (uint)ParseBits(f[2].ToUpperInvariant(), 0, 23, null, 0, 2);
        _months = (int)ParseBits(f[4].ToUpperInvariant(), 1, 12, MonthNames, 1, 4);

        var dayToken = f[3].ToUpperInvariant();
        var dowToken = f[5].ToUpperInvariant();
        var dayRestricted = dayToken is not ("*" or "?");
        var dowRestricted = dowToken is not ("*" or "?");
        if (dayRestricted && dowRestricted)
            throw new FormatException("cron 日与周不能同时受限,其一必须为 * 或 ?(Quartz 同款语义)");

        // 日段
        if (!dayRestricted)
        {
            _dayMode = DayMode.Bits;
            _days = AllBits(1, 31);
        }
        else if (dayToken == "L")
        {
            _dayMode = DayMode.Last;
        }
        else if (dayToken == "LW")
        {
            _dayMode = DayMode.LastWeekday;
        }
        else if (dayToken.StartsWith("L-", StringComparison.Ordinal))
        {
            if (!int.TryParse(dayToken[2..], NumberStyles.None, CultureInfo.InvariantCulture, out var off) || off < 1 || off > 30)
                throw Err(3, $"L-n 的 n 需在 [1,30],实得 \"{f[3]}\"");
            _dayMode = DayMode.Last;
            _dayArg = off;
        }
        else if (dayToken.EndsWith('W'))
        {
            if (!int.TryParse(dayToken[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var d) || d < 1 || d > 31)
                throw Err(3, $"W 需形如 nW(1≤n≤31)或 LW,实得 \"{f[3]}\"");
            _dayMode = DayMode.NearestWeekday;
            _dayArg = d;
        }
        else if (dayToken.Contains('L') || dayToken.Contains('W'))
        {
            throw Err(3, "L/W 只能单独成段(L、L-n、LW、nW),不能出现在枚举或区间里");
        }
        else
        {
            _dayMode = DayMode.Bits;
            _days = (uint)ParseBits(dayToken, 1, 31, null, 0, 3);
        }

        // 周段
        if (!dowRestricted)
        {
            _dowMode = DowMode.Bits;
            _dows = 0b111_1111;
        }
        else if (dowToken == "L")
        {
            // Quartz 同款:周段孤立 L = SAT(纯周六约束,非"最后")
            _dowMode = DowMode.Bits;
            _dows = 1 << 6;
        }
        else if (dowToken.Contains('#'))
        {
            var parts = dowToken.Split('#');
            if (parts.Length != 2 || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var nth) || nth < 1 || nth > 5)
                throw Err(5, $"# 需形如 周几#n(1≤n≤5),实得 \"{f[5]}\"");
            _dowMode = DowMode.Nth;
            _dowArg = ParseDowValue(parts[0], f[5]);
            _dowNth = nth;
        }
        else if (dowToken.EndsWith('L'))
        {
            _dowMode = DowMode.LastOfMonth;
            _dowArg = ParseDowValue(dowToken[..^1], f[5]);
        }
        else
        {
            _dowMode = DowMode.Bits;
            _dows = ParseDowBits(dowToken);
        }
    }

    private static FormatException Err(int field, string reason) =>
        new($"cron 第 {field + 1} 段({FieldNames[field]}):{reason}");

    private static uint AllBits(int min, int max)
    {
        uint bits = 0;
        for (var v = min; v <= max; v++) bits |= 1u << v;
        return bits;
    }

    /// <summary>解析普通段(* , - / 与名字)为位集;区间支持环绕。<paramref name="nameBase"/>:名字表首项对应的数值。</summary>
    private static ulong ParseBits(string token, int min, int max, string[]? names, int nameBase, int field)
    {
        ulong bits = 0;
        foreach (var part in token.Split(','))
        {
            if (part.Length == 0)
                throw Err(field, "空枚举项(连续逗号?)");

            var body = part;
            var step = 1;
            var slash = part.IndexOf('/');
            if (slash >= 0)
            {
                body = part[..slash];
                var stepText = part[(slash + 1)..];
                if (!int.TryParse(stepText, NumberStyles.None, CultureInfo.InvariantCulture, out step) || step < 1)
                    throw Err(field, $"步长非法 \"{stepText}\"(需为 ≥1 的整数)");
            }

            int a, b;
            if (body == "*")
            {
                a = min;
                b = max;
            }
            else
            {
                var dash = body.IndexOf('-');
                if (dash > 0)
                {
                    a = ParseValue(body[..dash], min, max, names, nameBase, field);
                    b = ParseValue(body[(dash + 1)..], min, max, names, nameBase, field);
                }
                else
                {
                    a = ParseValue(body, min, max, names, nameBase, field);
                    b = slash >= 0 ? max : a;   // "10/3" = 从 10 到上界步进
                }
            }

            var size = max - min + 1;
            var span = a <= b ? b - a : size - (a - b);   // a>b 即环绕
            for (var i = 0; i <= span; i += step)
            {
                var v = a + i;
                if (v > max) v -= size;
                bits |= 1UL << v;
            }
        }
        return bits;
    }

    private static int ParseValue(string s, int min, int max, string[]? names, int nameBase, int field)
    {
        if (int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var v))
        {
            if (v < min || v > max)
                throw Err(field, $"值 {v} 超出范围 [{min},{max}]");
            return v;
        }
        if (names is not null)
        {
            var idx = Array.IndexOf(names, s);
            if (idx >= 0) return idx + nameBase;
        }
        throw Err(field, $"无法解析 \"{s}\"");
    }

    /// <summary>周段单值(nL / n#m 里的 n):数字 0-7(7≡0)或 SUN-SAT 名字。</summary>
    private static int ParseDowValue(string s, string original)
    {
        var raw = ParseDowValueRaw(s, original);
        return raw == 7 ? 0 : raw;
    }

    /// <summary>周段裸值:保留 7 不折(区间端点要用原值区分「0-7 整环」与「0-0 单日」)。</summary>
    private static int ParseDowValueRaw(string s, string original)
    {
        if (int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var v))
        {
            if (v < 0 || v > 7)
                throw Err(5, $"周几 {v} 超出范围 [0,7],实得 \"{original}\"");
            return v;
        }
        var idx = Array.IndexOf(DowNames, s);
        if (idx >= 0) return idx;
        throw Err(5, $"无法解析周几 \"{original}\"");
    }

    /// <summary>
    /// 周段位集解析:与通用 <see cref="ParseBits"/> 分开写,因为 7≡0 使周环只有 7 格——
    /// 若在 0..7 的 8 格轮上带步长展开,幻影格 7 会数错相位(如 6-1/2 该是{六,一}却给{六,日})。
    /// 规则:值先折 7→0,再在 7 格环上展开;区间两端折叠后相等而原文不等(0-7 / 7-0)= 绕整环一圈 = 全周。
    /// </summary>
    private static int ParseDowBits(string token)
    {
        var bits = 0;
        foreach (var part in token.Split(','))
        {
            if (part.Length == 0)
                throw Err(5, "空枚举项(连续逗号?)");

            var body = part;
            var step = 1;
            var slash = part.IndexOf('/');
            if (slash >= 0)
            {
                body = part[..slash];
                var stepText = part[(slash + 1)..];
                if (!int.TryParse(stepText, NumberStyles.None, CultureInfo.InvariantCulture, out step) || step < 1)
                    throw Err(5, $"步长非法 \"{stepText}\"(需为 ≥1 的整数)");
            }

            int a, b;
            var fullCircle = false;
            if (body == "*")
            {
                a = 0;
                b = 6;
            }
            else
            {
                var dash = body.IndexOf('-');
                if (dash > 0)
                {
                    var rawA = ParseDowValueRaw(body[..dash], token);
                    var rawB = ParseDowValueRaw(body[(dash + 1)..], token);
                    a = rawA == 7 ? 0 : rawA;
                    b = rawB == 7 ? 0 : rawB;
                    fullCircle = a == b && rawA != rawB;
                }
                else
                {
                    a = ParseDowValue(body, token);
                    b = slash >= 0 ? 6 : a;   // "5/2" = 从周五到周六步进
                }
            }

            var span = fullCircle ? 6 : a <= b ? b - a : 7 - (a - b);
            for (var i = 0; i <= span; i += step)
                bits |= 1 << (a + i) % 7;
        }
        return bits;
    }

    // ── 匹配 ─────────────────────────────────────────────────────────

    private bool DayMatches(int year, int month, int day)
    {
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var dayOk = _dayMode switch
        {
            DayMode.Last => day == daysInMonth - _dayArg,
            DayMode.LastWeekday => day == LastWeekdayOf(year, month, daysInMonth),
            // 目标日超出当月天数(如 4 月的 31W)→ 该月无解,不跨月顺延
            DayMode.NearestWeekday => _dayArg <= daysInMonth && day == NearestWeekdayOf(year, month, _dayArg, daysInMonth),
            _ => (_days & (1u << day)) != 0,
        };
        if (!dayOk) return false;

        var dow = (int)new DateTime(year, month, day).DayOfWeek;   // 0=周日
        return _dowMode switch
        {
            DowMode.LastOfMonth => dow == _dowArg && day + 7 > daysInMonth,
            DowMode.Nth => dow == _dowArg && (day - 1) / 7 + 1 == _dowNth,
            _ => (_dows & (1 << dow)) != 0,
        };
    }

    /// <summary>最近工作日:周六回退周五(越出月初改进到周一),周日顺延周一(越出月末改回退周五)——不跨月。</summary>
    private static int NearestWeekdayOf(int year, int month, int target, int daysInMonth)
    {
        var dow = (int)new DateTime(year, month, target).DayOfWeek;
        if (dow == 6) return target - 1 >= 1 ? target - 1 : target + 2;
        if (dow == 0) return target + 1 <= daysInMonth ? target + 1 : target - 2;
        return target;
    }

    private static int LastWeekdayOf(int year, int month, int daysInMonth)
    {
        for (var d = daysInMonth; ; d--)
        {
            var dow = (int)new DateTime(year, month, d).DayOfWeek;
            if (dow != 0 && dow != 6) return d;
        }
    }

    /// <summary>在 <paramref name="from"/> 的当天内找 ≥ 其时分秒的首个匹配时刻。</summary>
    private bool TryFindTime(DateTime from, out DateTime result)
    {
        for (var h = from.Hour; h < 24; h++)
        {
            if ((_hours & (1u << h)) == 0) continue;
            var mStart = h == from.Hour ? from.Minute : 0;
            for (var m = mStart; m < 60; m++)
            {
                if ((_minutes & (1UL << m)) == 0) continue;
                var sStart = h == from.Hour && m == from.Minute ? from.Second : 0;
                for (var s = sStart; s < 60; s++)
                {
                    if ((_seconds & (1UL << s)) == 0) continue;
                    result = new DateTime(from.Year, from.Month, from.Day, h, m, s, from.Kind);
                    return true;
                }
            }
        }
        result = default;
        return false;
    }
}
