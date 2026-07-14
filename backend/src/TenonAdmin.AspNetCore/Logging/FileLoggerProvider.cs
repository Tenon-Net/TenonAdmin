using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using TenonAdmin.Core;

namespace TenonAdmin.AspNetCore;

/// <summary>
/// 文件日志 provider:按<b>级别分目录</b> + 按<b>日期滚动</b> + 按<b>大小分卷</b>,写 <c>{root}/{级别}/{yyyyMMdd}.log</c>。
///
/// <para><b>为什么自己写。</b>内核的运行时依赖只准是 SqlSugarCore + Microsoft.*(见 CLAUDE.md),Serilog/NLog 进不来。
/// 而 <c>ILogger</c> 默认只有 Console provider——进程一重启,异常堆栈就没了。这两百行是那条红线内的最小解。
/// <b>它是加法不排他</b>:消费者要 JSON/远端 sink,在宿主里照常 <c>AddSerilog()</c>,两者并存。</para>
///
/// <para><b>线程模型</b>(照抄 Furion/MoYu 的 FileLogger,那部分骨架是对的):
/// <see cref="FileLogger"/> 在<b>调用线程</b>把日志格式化成一行,<c>TryWrite</c> 进有界 Channel 就返回——业务线程永不碰 IO,
/// 队列满了宁可丢日志也不阻塞(<see cref="BoundedChannelFullMode.DropWrite"/>,丢了会计数并在下一批补一行 <c>[dropped N]</c>)。
/// 消费端是<b>单个</b>后台任务(<c>SingleReader = true</c>),所以下面的 <see cref="LevelWriter"/> 内部一把锁都不需要。</para>
///
/// <para><b>ponytail: 多副本共写同一个日志卷会互相覆盖</b>(各进程各持一份文件 position)。
/// 已用 <c>TenonAdmin:Id:WorkerId</c> 给文件名加 <c>-w1</c> 后缀兜住(多副本本来就强制配唯一机器号,不新造概念);
/// 真要多副本共享一个目录,给每个副本挂各自的卷更省心。</para>
///
/// <para><b>ponytail: 跨天以本地时间为准。</b>容器默认 UTC,文件日期会比运维预期差 8 小时——部署时配 <c>TZ=Asia/Shanghai</c>。</para>
/// </summary>
[ProviderAlias("File")]     // 白送标准的 Logging:File:LogLevel:* 分类过滤(与 Console provider 同一套),故本类不自己做级别过滤
public sealed class FileLoggerProvider : ILoggerProvider
{
    private const int QueueCapacity = 10_000;
    private const int BatchSize = 100;

    private readonly string _root;
    private readonly AdminFileLogOptions _options;
    private readonly string _workerSuffix;
    private readonly TimeProvider _time;
    private readonly Channel<Entry> _channel;
    private readonly Task _consumer;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();
    private int _dropped;

    /// <param name="root">已解析为绝对路径的日志根目录</param>
    /// <param name="options">文件日志配置(保留期/单文件上限)</param>
    /// <param name="workerId">雪花机器号:&gt;0 时进文件名(<c>20260714-w1.log</c>),避免多副本共写一个文件互相覆盖</param>
    /// <param name="time">统一时间源;跨天判定按其<b>本地</b>时间</param>
    public FileLoggerProvider(string root, AdminFileLogOptions options, int workerId, TimeProvider time)
    {
        _root = root;
        _options = options;
        _workerSuffix = workerId > 0 ? $"-w{workerId}" : string.Empty;
        _time = time;
        _channel = Channel.CreateBounded<Entry>(new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,     // 队列满 → 丢日志。绝不阻塞业务线程
            SingleReader = true,                             // 单消费者 ⇒ LevelWriter 内部免锁
            SingleWriter = false,
        });
        _consumer = Task.Factory.StartNew(ConsumeAsync, CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
    }

    public ILogger CreateLogger(string categoryName)
        => _loggers.GetOrAdd(categoryName, name => new FileLogger(this, name));

    /// <summary>关写端 → 等消费任务排干队列 → 逐个 writer 落盘。三步缺一不可,否则退出时最后一批日志就丢了。</summary>
    public void Dispose()
    {
        _channel.Writer.TryComplete();
        try { _consumer.Wait(TimeSpan.FromSeconds(3)); } catch { /* 排不干就算了,总不能卡住宿主退出 */ }
        _loggers.Clear();
    }

    private void Enqueue(LogLevel level, string line)
    {
        if (!_channel.Writer.TryWrite(new Entry(level, line)))
            Interlocked.Increment(ref _dropped);
    }

    private async Task ConsumeAsync()
    {
        var writers = new Dictionary<string, LevelWriter>();     // 消费任务独占,不必用并发容器
        var batch = new List<Entry>(BatchSize);
        var touched = new HashSet<LevelWriter>();
        try
        {
            while (await _channel.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                batch.Clear();
                touched.Clear();
                while (batch.Count < BatchSize && _channel.Reader.TryRead(out var entry)) batch.Add(entry);
                if (batch.Count == 0) continue;

                // 上一轮丢了多少,在这一批的头一个文件里留个痕。静默丢日志是最坏的一种丢
                var dropped = Interlocked.Exchange(ref _dropped, 0);

                foreach (var entry in batch)
                {
                    var writer = GetWriter(writers, DirectoryOf(entry.Level));
                    if (writer is null) continue;               // 该目录不可写,已降级
                    if (dropped > 0)
                    {
                        writer.Write($"[dropped {dropped} messages: 日志队列已满({QueueCapacity}),写盘跟不上产出速度]");
                        dropped = 0;
                    }
                    writer.Write(entry.Line);
                    touched.Add(writer);
                }

                // 只在队列排空时才 flush:每条都 flush 会把吞吐拖垮
                if (_channel.Reader.Count == 0)
                    foreach (var writer in touched) writer.Flush();
            }
        }
        catch (Exception ex)
        {
            // 消费循环里逃出来的异常没人接得住(它不在任何请求上下文里),吼一声就算了——绝不能让日志系统打崩宿主
            Console.Error.WriteLine($"[TenonAdmin] 文件日志消费循环异常终止,后续日志不再写盘: {ex}");
        }
        finally
        {
            foreach (var writer in writers.Values) writer.Dispose();
        }
    }

    private LevelWriter? GetWriter(Dictionary<string, LevelWriter> writers, string dirName)
    {
        if (!writers.TryGetValue(dirName, out var writer))
        {
            writer = new LevelWriter(Path.Combine(_root, dirName), _workerSuffix, _options, _time);
            writers[dirName] = writer;
        }
        return writer.Disabled ? null : writer;
    }

    /// <summary>Critical 并入 error/:实践中没人会去单独盯一个 critical 目录,拆出来只会让人漏看。</summary>
    private static string DirectoryOf(LogLevel level) => level switch
    {
        LogLevel.Critical or LogLevel.Error => "error",
        LogLevel.Warning => "warning",
        LogLevel.Information => "information",
        LogLevel.Debug => "debug",
        _ => "trace",
    };

    private readonly record struct Entry(LogLevel Level, string Line);

    /// <summary>格式化 + 入队,仅此而已。跑在业务线程上,所以这里一行 IO 都不能有。</summary>
    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;   // 不渲染 Scope

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;                 // 级别过滤交给 [ProviderAlias] + 框架

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.None) return;

            var message = formatter(state, exception);
            if (string.IsNullOrEmpty(message) && exception is null) return;

            var sb = new StringBuilder(256)
                .Append(provider._time.GetLocalNow().ToString("yyyy-MM-dd HH:mm:ss.fff"))
                .Append(" [").Append(ShortName(logLevel)).Append("] ")
                .Append(category).Append('[').Append(eventId.Id).Append("] ")
                .Append(message);
            if (exception is not null) sb.AppendLine().Append(exception);

            provider.Enqueue(logLevel, sb.ToString());
        }

        private static string ShortName(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            _ => "CRT",
        };
    }

    /// <summary>
    /// 一个级别目录一个,只被消费任务碰 ⇒ 内部无锁。负责:跨天换文件、超限分卷、过期清理。
    /// <para><b>永远 append,绝不截断。</b>MoYu 的同类实现换文件时走 <c>append: false</c> → <c>SetLength(0)</c>,
    /// 跨天重启/多实例/手工恢复时会把已存在的日期文件<b>清零</b>。这里不提供"覆盖"这个脚枪。</para>
    /// </summary>
    private sealed class LevelWriter(string dir, string suffix, AdminFileLogOptions options, TimeProvider time) : IDisposable
    {
        private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);   // 带 BOM 的话每个新文件头都是 EF BB BF,cat 出来一段乱码

        private DateOnly _date;
        private int _seq = 1;
        private FileStream? _stream;
        private StreamWriter? _writer;
        private bool _initialized;
        private bool _warned;

        /// <summary>目录/文件打不开(容器非 root、卷权限不对)→ 降级为不写盘。已在 stderr 吼过一声,不会静默消失。</summary>
        internal bool Disabled { get; private set; }

        public void Write(string line)
        {
            if (Disabled) return;

            if (!_initialized)
            {
                _initialized = true;
                try { Directory.CreateDirectory(dir); }
                catch (Exception ex) { Fail($"日志目录 {dir} 创建失败", ex, fatal: true); return; }
                Roll(Today());
            }

            var today = Today();
            if (today != _date)
                Roll(today);                                            // 跨天:换文件 + 顺手清过期
            else if (options.MaxFileSizeMb > 0 && _stream is { } s && s.Length >= (long)options.MaxFileSizeMb * 1024 * 1024)
                Open(_seq + 1);                                         // 超限:续写下一卷,序号单调递增

            if (Disabled || _writer is null) return;

            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    _writer.WriteLine(line);
                    return;
                }
                catch (IOException) when (attempt < 2)
                {
                    Thread.Sleep(50 * (attempt + 1));                   // 瞬时 IO 抖动(杀毒软件扫描、网络盘),退避重试
                }
                catch (Exception ex)
                {
                    Fail("日志写入失败,丢弃该条", ex, fatal: false);     // 丢这一条,但不永久禁用——下一条还有机会
                    return;
                }
            }
        }

        public void Flush()
        {
            try { _writer?.Flush(); } catch { /* flush 失败没什么可做的 */ }
        }

        public void Dispose()
        {
            Close();
            Disabled = true;
        }

        private DateOnly Today() => DateOnly.FromDateTime(time.GetLocalNow().DateTime);

        private void Roll(DateOnly date)
        {
            _date = date;
            Open(ScanMaxSeq(date));     // 接着已有的最大分卷写。不扫的话,进程一重启就从 .log 头上覆盖当天日志
            Cleanup();
        }

        private void Open(int seq)
        {
            Close();
            _seq = seq;
            try
            {
                // Append:永不截断已有内容。FileShare.ReadWrite|Delete:不挡运维的 tail/less/rm
                _stream = new FileStream(FileNameOf(_date, seq), FileMode.Append, FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete);
                _writer = new StreamWriter(_stream, Utf8NoBom) { AutoFlush = false };
            }
            catch (Exception ex)
            {
                Fail($"日志文件 {FileNameOf(_date, seq)} 打开失败", ex, fatal: true);
            }
        }

        private void Close()
        {
            try { _writer?.Flush(); _writer?.Dispose(); } catch { /* ignore */ }
            _writer = null;
            _stream = null;      // StreamWriter.Dispose 已经把底层流关了
        }

        private string FileNameOf(DateOnly date, int seq)
        {
            var name = seq <= 1 ? $"{date:yyyyMMdd}{suffix}" : $"{date:yyyyMMdd}{suffix}-{seq}";
            return Path.Combine(dir, name + ".log");
        }

        /// <summary>扫出该日期已有的最大分卷号(重启/跨天后续写它,而不是从头覆盖)。</summary>
        private int ScanMaxSeq(DateOnly date)
        {
            var prefix = $"{date:yyyyMMdd}{suffix}";
            var max = 1;
            try
            {
                foreach (var path in Directory.EnumerateFiles(dir, prefix + "*.log"))
                {
                    var name = Path.GetFileNameWithoutExtension(path);
                    if (name.Length <= prefix.Length + 1 || name[prefix.Length] != '-') continue;
                    if (int.TryParse(name.AsSpan(prefix.Length + 1), out var seq) && seq > max) max = seq;
                }
            }
            catch { /* 扫不动就从 1 开始,Append 模式下最坏也只是把新日志接在旧文件末尾 */ }
            return max;
        }

        /// <summary>删掉本目录里早于 今天-RetainDays 的日志。跨天滚动时顺手跑,低频,不在热路径上。</summary>
        private void Cleanup()
        {
            if (options.RetainDays <= 0) return;
            var earliest = _date.AddDays(-options.RetainDays);
            try
            {
                foreach (var path in Directory.EnumerateFiles(dir, "*.log"))
                {
                    var name = Path.GetFileNameWithoutExtension(path);
                    if (name.Length < 8 || !DateOnly.TryParseExact(name[..8], "yyyyMMdd", out var date)) continue;
                    if (date >= earliest) continue;
                    try { File.Delete(path); } catch { /* 删不掉(被占用)下次再说 */ }
                }
            }
            catch { /* 目录扫不动就跳过这轮清理 */ }
        }

        /// <summary>
        /// 走 stderr 而不是 ILogger——在日志 provider 里打日志会绕回自己,轻则递归重则死锁。
        /// 且每个 writer 只吼一次,免得一秒刷屏几千行。
        /// </summary>
        private void Fail(string what, Exception ex, bool fatal)
        {
            if (fatal) Disabled = true;
            if (_warned) return;
            _warned = true;
            var tail = fatal ? "该级别日志不再写盘(进程内不再重试)。" : "";
            Console.Error.WriteLine($"[TenonAdmin] {what}: {ex.Message} {tail}");
        }
    }
}
