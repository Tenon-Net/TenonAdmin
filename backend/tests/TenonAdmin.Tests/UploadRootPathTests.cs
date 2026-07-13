using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TenonAdmin.Core;

namespace TenonAdmin.Tests;

/// <summary>
/// 上传根目录的解析基准:必须是 <b>ContentRoot</b>,不是进程 CWD。
/// <para>SQLite 库文件(<c>SqlSugarSetup.ResolveSqlitePath</c>)与 JWT 开发密钥(<c>JwtKeyResolver</c>)早就按
/// ContentRoot 解析;上传根一度按 CWD 解析,三条可写路径两种基准。容器里 <c>WORKDIR</c> 恰好让两者相等而掩盖问题,
/// 一旦 entrypoint 脚本 cd 过、或 k8s 覆写 workingDir,库还在原处、文件却落到别处——重启后"文件消失"。</para>
/// </summary>
public class UploadRootPathTests
{
    [Fact]
    public void UploadRoot_ResolvesAgainstContentRoot_NotCwd()
    {
        using var factory = new AdminAppFactory();
        var sp = factory.Services;

        var root = sp.GetRequiredService<AdminUploadOptions>().RootPath;
        var contentRoot = sp.GetRequiredService<IHostEnvironment>().ContentRootPath;

        Assert.True(Path.IsPathRooted(root), $"上传根应已解析为绝对路径,实际 {root}");
        Assert.StartsWith(Path.GetFullPath(contentRoot), root, StringComparison.Ordinal);

        // 测试进程的 CWD 是测试项目的 bin 目录,与被测宿主的 ContentRoot 不同 —— 按 CWD 解析会落在这里,即为回归。
        Assert.False(root.StartsWith(Path.GetFullPath(Directory.GetCurrentDirectory()), StringComparison.Ordinal),
            $"上传根不该落在进程 CWD 下,实际 {root}");
    }
}
