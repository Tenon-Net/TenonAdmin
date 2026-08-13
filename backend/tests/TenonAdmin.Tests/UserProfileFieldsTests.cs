using System.Net.Http.Headers;

namespace TenonAdmin.Tests;

/// <summary>
/// 用户通用资料字段(昵称/手机/邮箱/性别/头像/直属主管)的读写往返。
/// 覆盖手写映射两侧:AddAsync→实体落库、GetAsync/PageAsync→出参投影;以及 DirectorName 的关联回填(同表按 Id 补名)。
/// </summary>
public class UserProfileFieldsTests
{
    private static async Task<HttpClient> SuperAdminClient(AdminAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await c.LoginToken("superAdmin", "Test@123456"));
        return c;
    }

    private static async Task<long> AddUser(HttpClient c, object body) =>
        (await (await c.PostJson("/api/v1/sys/user", body)).ReadEnvelope())
            .GetProperty("data").GetProperty("id").GetInt64();

    [Fact]
    public async Task Profile_fields_and_director_name_round_trip()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);

        // 先建主管,再建下属并指向他
        var directorId = await AddUser(c, new { account = "boss", password = "Test@123456", name = "老板", enabled = true, roleIds = Array.Empty<long>() });
        var userId = await AddUser(c, new
        {
            account = "staff",
            password = "Test@123456",
            name = "员工",
            nickname = "小员",
            phone = "13800138000",
            email = "staff@example.com",
            gender = "1",
            directorId,
            enabled = true,
            roleIds = Array.Empty<long>(),
        });

        // 详情:各字段原样回来
        var detail = (await (await c.GetAsync($"/api/v1/sys/user/{userId}")).ReadEnvelope()).GetProperty("data");
        Assert.Equal("小员", detail.GetProperty("nickname").GetString());
        Assert.Equal("13800138000", detail.GetProperty("phone").GetString());
        Assert.Equal("staff@example.com", detail.GetProperty("email").GetString());
        Assert.Equal("1", detail.GetProperty("gender").GetString());
        Assert.Equal(directorId, detail.GetProperty("directorId").GetInt64());

        // 列表:主管姓名由 DirectorId 关联回填(不落库字段)
        var items = (await (await c.GetAsync("/api/v1/sys/user/page?Current=1&Size=50")).ReadEnvelope())
            .GetProperty("data").GetProperty("items").EnumerateArray();
        var row = items.Single(m => m.GetProperty("account").GetString() == "staff");
        Assert.Equal("老板", row.GetProperty("directorName").GetString());
        Assert.Equal("1", row.GetProperty("gender").GetString());
    }

    [Fact]
    public async Task Update_overwrites_profile_fields()
    {
        using var f = new AdminAppFactory();
        var c = await SuperAdminClient(f);

        var id = await AddUser(c, new { account = "u1", password = "Test@123456", name = "u1", phone = "13800138000", enabled = true, roleIds = Array.Empty<long>() });

        await (await c.PutJson($"/api/v1/sys/user/{id}",
            new { name = "u1", nickname = "改后", phone = "13900139000", email = (string?)null, enabled = true, roleIds = Array.Empty<long>() })).ReadEnvelope();

        var detail = (await (await c.GetAsync($"/api/v1/sys/user/{id}")).ReadEnvelope()).GetProperty("data");
        Assert.Equal("改后", detail.GetProperty("nickname").GetString());
        Assert.Equal("13900139000", detail.GetProperty("phone").GetString());
        // 传 null 即清空(整字段覆盖,非补丁)
        Assert.True(detail.GetProperty("email").ValueKind == System.Text.Json.JsonValueKind.Null);
    }
}
