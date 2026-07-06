namespace TenonAdmin.Core;

/// <summary>
/// 雪花算法默认实现(设计 §2.2 替代表:替换 Yitter.IdGenerator,零第三方依赖,单文件)。
/// <para>64 位布局(高位 → 低位):</para>
/// <code>
/// ┌─1 bit─┬────────41 bit────────┬───10 bit───┬───12 bit───┐
/// │ 符号 0 │ 相对纪元的毫秒时间戳    │  机器号     │  毫秒内序列  │
/// └───────┴──────────────────────┴────────────┴────────────┘
/// </code>
/// <para>容量:41 位毫秒 ≈ 69.7 年(纪元 2026-01-01 起可用到约 2095);
/// 1024 台机器;单机单毫秒 4096 个 ID(≈ 400 万/秒),超出则自旋等待下一毫秒。</para>
/// <para>时钟安全:注入 <see cref="TimeProvider"/>(可测试,设计 §12);检测到时钟回拨时,
/// 小幅(≤5ms,NTP 微调级)自旋等待追平,大幅回拨直接抛异常拒绝发号——绝不发出可能重复的 ID。</para>
/// </summary>
public class SnowflakeIdGenerator : IIdGenerator
{
    // ── 位宽与派生常量(改位宽只需动这两行,其余全部派生) ──────────────
    private const int WORKER_ID_BITS = 10;
    private const int SEQUENCE_BITS = 12;

    private const long MAX_WORKER_ID = (1L << WORKER_ID_BITS) - 1;   // 1023
    private const long SEQUENCE_MASK = (1L << SEQUENCE_BITS) - 1;    // 4095
    private const int WORKER_ID_SHIFT = SEQUENCE_BITS;               // 机器号左移 12
    private const int TIMESTAMP_SHIFT = SEQUENCE_BITS + WORKER_ID_BITS; // 时间戳左移 22

    /// <summary>时钟回拨的可容忍上限(毫秒)。NTP 微调通常在几毫秒内,超过视为异常环境。</summary>
    private const long MAX_CLOCK_DRIFT_MS = 5;

    /// <summary>自定义纪元:2026-01-01 UTC(项目立项年)。固定死——改动会导致新旧 ID 重叠,永远不要改。</summary>
    private static readonly DateTimeOffset EPOCH = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly long _workerId;
    private readonly TimeProvider _time;
    private readonly Lock _lock = new();      // .NET 9+ 专用锁类型,比 lock(object) 语义更明确

    private long _lastTimestamp = -1;         // 上次发号的毫秒时间戳
    private long _sequence;                   // 当前毫秒内已发序列

    /// <param name="workerId">
    /// 机器号(0–1023)。单机部署用默认 0 即可;多实例部署时必须为每个实例配置不同值,
    /// 否则同毫秒可能撞号(由 AspNetCore 层从 <c>TenonAdmin:Id:WorkerId</c> 配置注入)。
    /// </param>
    /// <param name="timeProvider">时间源,默认系统时钟;测试时可注入 FakeTimeProvider</param>
    public SnowflakeIdGenerator(long workerId = 0, TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(workerId);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(workerId, MAX_WORKER_ID);
        _workerId = workerId;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public long NextId()
    {
        lock (_lock)
        {
            var now = CurrentMillis();

            // 时钟回拨处理:小幅自旋追平,大幅直接拒绝(宁可不发号也不发重号)
            if (now < _lastTimestamp)
            {
                var drift = _lastTimestamp - now;
                if (drift > MAX_CLOCK_DRIFT_MS)
                    throw new InvalidOperationException(
                        $"检测到时钟回拨 {drift}ms(超过容忍上限 {MAX_CLOCK_DRIFT_MS}ms),拒绝生成 ID 以避免重复。请检查服务器时钟同步。");
                now = SpinUntil(_lastTimestamp);
            }

            if (now == _lastTimestamp)
            {
                // 同一毫秒内:序列自增;溢出(>4095)则本毫秒已发满,自旋到下一毫秒重新计数
                _sequence = (_sequence + 1) & SEQUENCE_MASK;
                if (_sequence == 0)
                    now = SpinUntil(_lastTimestamp + 1);
            }
            else
            {
                _sequence = 0; // 新的毫秒,序列归零
            }

            _lastTimestamp = now;
            return (now << TIMESTAMP_SHIFT) | (_workerId << WORKER_ID_SHIFT) | _sequence;
        }
    }

    /// <summary>当前相对纪元的毫秒数</summary>
    private long CurrentMillis() => (long)(_time.GetUtcNow() - EPOCH).TotalMilliseconds;

    /// <summary>自旋直到时间源到达目标毫秒(仅在序列打满或微小回拨时进入,持续微秒级)</summary>
    private long SpinUntil(long target)
    {
        long now;
        do { now = CurrentMillis(); } while (now < target);
        return now;
    }
}
