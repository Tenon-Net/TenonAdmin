using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using TenonAdmin.Core;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// QA15: file management is owner-only for non-superadmin.
/// Non-superadmin can only see/download/delete their own files; superadmin manages all.
/// Assertions go through the HTTP API; the non-superadmin fixtures are built at the
/// service layer (same pattern as <see cref="UserDataScopeTests"/>).
/// </summary>
public class FileOwnerTests
{
    /// <summary>文件模块的种子按钮:31 上传 / 32 分页 / 79 下载 / 80 删除。</summary>
    private static readonly long[] FILE_MENU_IDS = [31, 32, 79, 80];

    private static async Task<HttpClient> SuperAdmin(AdminAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        return c;
    }

    /// <summary>
    /// 建一个只挂文件四个按钮的角色 + 属于它的非超管用户,返回已登录的客户端。
    /// 账号用 v4 GUID:<c>CreateVersion7</c> 前 8 位是毫秒时间戳高位,同一测试里连建两个必然撞 42006。
    /// </summary>
    private static async Task<(HttpClient Client, string Account)> CreateUser(AdminAppFactory f, string suffix)
    {
        var account = "fuser-" + Guid.NewGuid().ToString("N")[..8];

        using (var scope = f.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var role = new SysRole
            {
                Name = "file-role-" + suffix,
                Code = "file-" + Guid.NewGuid().ToString("N")[..8],
                Enabled = true,
            };
            await sp.GetRequiredService<IRepository<SysRole>>().InsertAsync(role);
            await sp.GetRequiredService<IRbacService>().SetRoleMenusAsync(role.Id, FILE_MENU_IDS);

            await sp.GetRequiredService<IUserService>().AddAsync(new AddUserInput
            {
                Account = account,
                Password = "File@123456",
                Name = "FileUser" + suffix,
                Enabled = true,
                OrgId = 3,   // 技术部(种子机构)
                RoleIds = [role.Id],
            });
        }

        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await c.LoginToken(account, "File@123456"));
        return (c, account);
    }

    private static async Task<long> UploadFile(HttpClient c, string name = "test.png")
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent("fake-png-bytes"u8.ToArray());
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", name);
        var env = await (await c.PostAsync("/api/v1/sys/file/upload", content)).ReadEnvelope();
        Assert.Equal(0, env.GetProperty("code").GetInt32());
        return env.GetProperty("data").GetProperty("id").GetInt64();
    }

    [Fact]
    public async Task Non_superadmin_cannot_download_another_users_file()
    {
        using var f = new AdminAppFactory();
        var (clientA, _) = await CreateUser(f, "a");
        var (clientB, _) = await CreateUser(f, "b");

        var fileByA = await UploadFile(clientA, "a-file.png");

        var dl = await clientB.GetAsync($"/api/v1/sys/file/{fileByA}/download");
        var env = await dl.ReadEnvelope();
        Assert.Equal((int)ErrorCode.FileNotFound, env.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Non_superadmin_cannot_delete_another_users_file()
    {
        using var f = new AdminAppFactory();
        var (clientA, _) = await CreateUser(f, "c");
        var (clientB, _) = await CreateUser(f, "d");

        var fileByA = await UploadFile(clientA, "del-file.png");

        var del = await (await clientB.DeleteAsync($"/api/v1/sys/file/{fileByA}")).ReadEnvelope();
        Assert.Equal((int)ErrorCode.FileNotFound, del.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Non_superadmin_can_manage_own_files()
    {
        using var f = new AdminAppFactory();
        var (client, _) = await CreateUser(f, "own");

        var fileId = await UploadFile(client, "my-file.png");

        var page = await (await client.GetAsync("/api/v1/sys/file/page?Current=1&Size=100")).ReadEnvelope();
        Assert.Equal(0, page.GetProperty("code").GetInt32());
        var items = page.GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(items, i => i.GetProperty("id").GetInt64() == fileId);

        var dl = await client.GetAsync($"/api/v1/sys/file/{fileId}/download");
        Assert.True(dl.IsSuccessStatusCode);

        var del = await (await client.DeleteAsync($"/api/v1/sys/file/{fileId}")).ReadEnvelope();
        Assert.Equal(0, del.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Superadmin_can_manage_all_files()
    {
        using var f = new AdminAppFactory();
        var admin = await SuperAdmin(f);
        var (userClient, _) = await CreateUser(f, "e");

        var fileByUser = await UploadFile(userClient, "user-file.png");

        var page = await (await admin.GetAsync("/api/v1/sys/file/page?Current=1&Size=100")).ReadEnvelope();
        Assert.Equal(0, page.GetProperty("code").GetInt32());
        var items = page.GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(items, i => i.GetProperty("id").GetInt64() == fileByUser);

        var dl = await admin.GetAsync($"/api/v1/sys/file/{fileByUser}/download");
        Assert.True(dl.IsSuccessStatusCode);

        var del = await (await admin.DeleteAsync($"/api/v1/sys/file/{fileByUser}")).ReadEnvelope();
        Assert.Equal(0, del.GetProperty("code").GetInt32());
    }
}
