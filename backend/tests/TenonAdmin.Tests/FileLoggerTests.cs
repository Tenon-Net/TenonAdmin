using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TenonAdmin.AspNetCore;
using TenonAdmin.Core;
using Xunit;

namespace TenonAdmin.Tests;

/// <summary>
/// 文件日志(<see cref="FileLoggerProvider"/>)的行为契约:按级别分目录、跨天滚动、超限分卷、重启续写、过期清理、写失败不抛。
/// <para>不走 WebApplicationFactory——这是个纯 IO 组件,直接单测 provider,拿临时目录当靶场。</para>
/// </summary>
public class FileLoggerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tenon-filelog-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly FakeTime _time = new(new DateTime(2026, 7, 14, 10, 30, 0, DateTimeKind.Local));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* 清不掉就算了 */ }
        GC.SuppressFinalize(this);
    }

    private FileLoggerProvider Create(AdminFileLogOptions? options = null, int workerId = 0)
        => new(_root, options ?? new AdminFileLogOptions(), workerId, _time);

    private string LogFile(string level, string name = "20260714") => Path.Combine(_root, level, name + ".log");

    [Fact]
    public void 按级别分目录写入_Critical并入error()
    {
        using (var provider = Create())
        {
            var logger = provider.CreateLogger("Demo");
            logger.LogInformation("这是一条普通信息");
            logger.LogWarning("这是一条告警");
            logger.LogError("这是一条错误");
            logger.LogCritical("这是一条致命");
        }   // Dispose 里排干队列 + 落盘

        var info = File.ReadAllText(LogFile("information"));
        var warn = File.ReadAllText(LogFile("warning"));
        var error = File.ReadAllText(LogFile("error"));

        Assert.Contains("[INF] Demo[0] 这是一条普通信息", info);
        Assert.Contains("[WRN] Demo[0] 这是一条告警", warn);
        // Critical 与 Error 同文件,各自保留自己的级别标签
        Assert.Contains("[ERR] Demo[0] 这是一条错误", error);
        Assert.Contains("[CRT] Demo[0] 这是一条致命", error);

        // 每条日志只落一个文件:不双写全量,级别之间不串味
        Assert.DoesNotContain("这是一条错误", info);
        Assert.DoesNotContain("这是一条告警", info);
        Assert.False(Directory.Exists(Path.Combine(_root, "all")));
    }

    [Fact]
    public void 异常堆栈附在消息下一行()
    {
        using (var provider = Create())
            provider.CreateLogger("Demo").LogError(new InvalidOperationException("炸了"), "处理失败");

        var text = File.ReadAllText(LogFile("error"));
        Assert.Contains("[ERR] Demo[0] 处理失败", text);
        Assert.Contains("System.InvalidOperationException: 炸了", text);
    }

    [Fact]
    public void 跨天换新文件_且不截断昨天的()
    {
        using (var provider = Create())
        {
            var logger = provider.CreateLogger("Demo");
            logger.LogError("今天的日志");
            Flush(provider);

            _time.Advance(TimeSpan.FromDays(1));
            logger.LogError("明天的日志");
        }

        // MoYu 的同类实现换文件时 SetLength(0),会把已存在的日期文件清零。这里必须两天都在
        Assert.Contains("今天的日志", File.ReadAllText(LogFile("error", "20260714")));
        Assert.Contains("明天的日志", File.ReadAllText(LogFile("error", "20260715")));
    }

    [Fact]
    public void 单文件超限后分卷_重启续写不覆盖第一卷()
    {
        var options = new AdminFileLogOptions { MaxFileSizeMb = 1 };
        var padding = new string('x', 200_000);     // 每条 ~200KB,几条就压过 1MB

        using (var provider = Create(options))
        {
            var logger = provider.CreateLogger("Demo");
            logger.LogError("第一卷起始");
            for (var i = 0; i < 8; i++) logger.LogError(padding);
        }

        var first = LogFile("error");
        var second = LogFile("error", "20260714-2");
        Assert.True(File.Exists(second), "单文件超过 MaxFileSizeMb 后应续写 -2 分卷");
        var firstSizeBefore = new FileInfo(first).Length;

        // 进程重启:必须接着最大分卷写,而不是从头覆盖第一卷(否则重启一次就丢一天的日志)
        using (var provider = Create(options))
            provider.CreateLogger("Demo").LogError("重启之后");

        Assert.Contains("重启之后", File.ReadAllText(second));
        Assert.Equal(firstSizeBefore, new FileInfo(first).Length);          // 第一卷分毫未动
        Assert.Contains("第一卷起始", File.ReadAllText(first));
    }

    [Fact]
    public void 跨天时清理过期文件_保留期内的不动()
    {
        var dir = Path.Combine(_root, "error");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "20260601.log"), "很久以前");    // 43 天前,超 RetainDays=14
        File.WriteAllText(Path.Combine(dir, "20260710.log"), "四天前");      // 保留期内
        File.WriteAllText(Path.Combine(dir, "not-a-date.log"), "手工文件");  // 名字解析不出日期 → 不碰

        using (var provider = Create(new AdminFileLogOptions { RetainDays = 14 }))
            provider.CreateLogger("Demo").LogError("今天的日志");            // 首次开卷即触发清理

        Assert.False(File.Exists(Path.Combine(dir, "20260601.log")));
        Assert.True(File.Exists(Path.Combine(dir, "20260710.log")));
        Assert.True(File.Exists(Path.Combine(dir, "not-a-date.log")));
    }

    [Fact]
    public void 保留期为0时不清理()
    {
        var dir = Path.Combine(_root, "error");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "20200101.log"), "上古日志");

        using (var provider = Create(new AdminFileLogOptions { RetainDays = 0 }))
            provider.CreateLogger("Demo").LogError("今天的日志");

        Assert.True(File.Exists(Path.Combine(dir, "20200101.log")));
    }

    [Fact]
    public void 多副本机器号进文件名_避免共写同一文件()
    {
        using (var provider = Create(workerId: 3))
            provider.CreateLogger("Demo").LogError("副本 3 的日志");

        Assert.True(File.Exists(LogFile("error", "20260714-w3")));
        Assert.False(File.Exists(LogFile("error")));
    }

    [Fact]
    public void 目录不可写时不抛异常_降级为不写盘()
    {
        // 用一个已存在的文件当"目录",Directory.CreateDirectory 必失败
        var blocker = Path.Combine(Path.GetTempPath(), "tenon-filelog-blocker-" + Guid.NewGuid().ToString("N")[..8]);
        File.WriteAllText(blocker, "我是文件不是目录");
        try
        {
            using var provider = new FileLoggerProvider(blocker, new AdminFileLogOptions(), 0, _time);
            var logger = provider.CreateLogger("Demo");

            // 日志系统绝不能打崩宿主:写不进去就写不进去,业务调用方毫无感知
            logger.LogError("这条注定写不进去");
            logger.LogError("这条也是");
            Flush(provider);
        }
        finally
        {
            File.Delete(blocker);
        }
    }

    /// <summary>队列是异步消费的:Dispose 会排干,但用例中途要看文件/推时钟就得等消费任务追上来。</summary>
    private static void Flush(FileLoggerProvider provider) => Thread.Sleep(300);

    /// <summary>可推进的本地时钟。仓库没引 Microsoft.Extensions.TimeProvider.Testing,自己写个 20 行的就够。</summary>
    private sealed class FakeTime(DateTime localNow) : TimeProvider
    {
        private DateTimeOffset _now = new(localNow, TimeZoneInfo.Local.GetUtcOffset(localNow));

        public override DateTimeOffset GetUtcNow() => _now.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Local;
        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}

/// <summary>
/// 装配路径:开了文件日志的宿主真能起来吗?
/// <para>上面那批单测把 provider 本身测得很透,却<b>一条装配都没覆盖</b>——第一次真跑宿主就撞上
/// "TryAddEnumerable 认不出实现类型" 直接启动崩。所以这一批必须存在。</para>
/// </summary>
public class FileLoggerSetupTests
{
    private static AdminAppFactory Factory(params (string key, string? value)[] settings) =>
        new() { Settings = settings.ToDictionary(s => s.key, s => s.value) };

    [Fact]
    public void 启用文件日志后宿主能正常启动_provider已挂进LoggerFactory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tenon-filelog-setup-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            using var f = Factory(
                ("TenonAdmin:Logging:File:Enabled", "true"),
                ("TenonAdmin:Logging:File:Path", dir));       // 绝对路径:别在 TestHost 目录里拉屎

            _ = f.CreateClient();
            Assert.Contains(f.Services.GetServices<ILoggerProvider>(), p => p is FileLoggerProvider);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void 默认不启用_不注册provider也不建目录()
    {
        using var f = new AdminAppFactory();

        _ = f.CreateClient();
        Assert.DoesNotContain(f.Services.GetServices<ILoggerProvider>(), p => p is FileLoggerProvider);
    }

    /// <summary>
    /// UseStaticFiles() 会把整个日志目录匿名直出(异常堆栈、请求参数、内部路径)。宁可启动就炸。
    /// <para><b>注意 TestHost 的 wwwroot 目录并不存在</b>(全新检出里没有,跑过上传用例才会长出来)——这正是要害:
    /// 守卫一度直接信 <c>IWebHostEnvironment.WebRootPath</c>,而它在 wwwroot 目录不存在时是 <c>null</c>,
    /// 于是守卫在<b>全新部署的机器上恰好失灵</b>(那儿 wwwroot 本来就还没有),等第一次上传把 wwwroot 建出来,
    /// 日志目录就被静态中间件直出了。本用例当时的表现是"看别的测试跑没跑过上传"随机红绿。</para>
    /// </summary>
    [Fact]
    public void 日志目录落在wwwroot下_启动即抛()
    {
        using var f = Factory(
            ("TenonAdmin:Logging:File:Enabled", "true"),
            ("TenonAdmin:Logging:File:Path", "wwwroot/logs"));

        var ex = Assert.ThrowsAny<Exception>(() => f.CreateClient());
        Assert.Contains("wwwroot", ex.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
