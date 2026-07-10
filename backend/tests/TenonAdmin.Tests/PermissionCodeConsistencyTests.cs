using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using TenonAdmin.AspNetCore;
using TenonAdmin.Services;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Tests;

/// <summary>
/// 权限码一致性回归锁(P2-14):DefaultMenuSeed 里手写的每个权限码,必须对应一个真实存在、
/// 挂了 [RolePermission] 的内置控制器端点。种子码与路由是两处分离的手写串,靠人肉同步;
/// 改错一个字符即"授了也匹配不上",且无编译/测试报错——本测试用反射按 BuildPermissionCode 同规则
/// 从控制器算码,锁死漂移。
/// </summary>
public class PermissionCodeConsistencyTests
{
    [Fact]
    public void Every_seeded_permission_code_maps_to_a_real_endpoint()
    {
        var endpointCodes = BuiltInEndpointCodes();
        var seededCodes = SeededPermissionCodes();

        Assert.NotEmpty(seededCodes);   // 防呆:种子真被读到
        foreach (var code in seededCodes)
            Assert.Contains(code, endpointCodes);
    }

    /// <summary>
    /// 反向锁(P2-14 续):每个挂了 [RolePermission] 的内置端点,要么有对应的种子菜单节点(普通用户可在角色-菜单
    /// UI 里被授权),要么显式登记在 <see cref="KnownUnseededEndpoints"/>(尚无菜单节点、当前仅超管可用)。二者之外
    /// 的端点会对普通用户<b>静默 403</b> 且无人察觉——本测试逼新增受权端点必须"同批加菜单种子"或"显式登记",杜绝无声漂移。
    /// </summary>
    [Fact]
    public void Every_permission_endpoint_is_seeded_or_explicitly_known_unseeded()
    {
        var unseeded = BuiltInEndpointCodes().Except(SeededPermissionCodes()).ToHashSet();

        // 新出现的"无菜单节点"受权端点:要么去 DefaultMenuSeed 补节点,要么(确属暂不开放)登记到 KnownUnseededEndpoints。
        var missing = unseeded.Except(KnownUnseededEndpoints).OrderBy(x => x).ToList();
        Assert.True(missing.Count == 0,
            "以下受权端点既无种子菜单节点、也未登记 KnownUnseededEndpoints —— 普通用户将静默 403:\n  "
            + string.Join("\n  ", missing));

        // 反向自清:清单里的端点若已被种子化(或已删除),提示从清单移除,使其随 M2 菜单树落地而缩小。
        var stale = KnownUnseededEndpoints.Except(unseeded).OrderBy(x => x).ToList();
        Assert.True(stale.Count == 0,
            "以下端点已在 DefaultMenuSeed 种子化(或已不存在),请从 KnownUnseededEndpoints 移除:\n  "
            + string.Join("\n  ", stale));
    }

    /// <summary>
    /// 挂了 [RolePermission] 但<b>暂无菜单节点</b>的内置端点(当前仅超管可用)。DefaultMenuSeed 刻意只播代表性接口,
    /// 完整菜单树随前端 M2 落地补齐;补上某端点的菜单节点后须从本清单删除(否则上面的"反向自清"断言会提示)。
    /// 新增受权端点若不打算立刻给菜单,须显式登记于此,承认其对普通用户不可授。
    /// </summary>
    private static readonly HashSet<string> KnownUnseededEndpoints =
    [
        "DELETE:/api/v1/sys/file/{id}",
        "DELETE:/api/v1/sys/org/{id}",
        "DELETE:/api/v1/sys/position/{id}",
        // GET config/{id}(配置详情):R1 刻意不放详情按钮(编辑用行数据),故此端点仍未种子化,保留登记。
        "GET:/api/v1/sys/config/{id}",
        // GET dict/type/{id}(类型详情):R5 同理不放详情按钮(编辑用行数据),保留登记。
        "GET:/api/v1/sys/dict/type/{id}",
        "GET:/api/v1/sys/file/{id}/download",
        "GET:/api/v1/sys/module/{id}",
        "GET:/api/v1/sys/org/{id}",
        "GET:/api/v1/sys/position/{id}",
        "POST:/api/v1/sys/org/add",
        "POST:/api/v1/sys/position/add",
        "PUT:/api/v1/sys/org/{id}",
        "PUT:/api/v1/sys/position/{id}",
    ];

    /// <summary>反射内置控制器,按 {大写Method}:/{小写路由模板} 生成所有 [RolePermission] 端点的权限码。</summary>
    private static HashSet<string> BuiltInEndpointCodes()
    {
        var codes = new HashSet<string>();
        var controllers = typeof(TenonAdminSetup).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(t));

        foreach (var controller in controllers)
        {
            var controllerRoute = controller.GetCustomAttribute<RouteAttribute>()?.Template ?? "";
            foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var hasPermission = method.GetCustomAttribute<RolePermissionAttribute>() is not null
                    || controller.GetCustomAttribute<RolePermissionAttribute>() is not null;
                if (!hasPermission) continue;

                foreach (var http in method.GetCustomAttributes<HttpMethodAttribute>())
                {
                    var template = Combine(controllerRoute, http.Template);
                    foreach (var verb in http.HttpMethods)
                        codes.Add($"{verb.ToUpperInvariant()}:/{template.TrimStart('/').ToLowerInvariant()}");
                }
            }
        }
        return codes;
    }

    /// <summary>取 DefaultMenuSeed 里全部非空权限码(internal 类,反射实例化后经公共 ISeedData&lt;SysMenu&gt; 取数据)。</summary>
    private static List<string> SeededPermissionCodes()
    {
        var seedType = typeof(RbacService).Assembly.GetType("TenonAdmin.Services.DefaultMenuSeed")!;
        var seed = (ISeedData<SysMenu>)Activator.CreateInstance(seedType, nonPublic: true)!;
        return seed.HasData().Select(m => m.Permission).Where(p => !string.IsNullOrEmpty(p)).ToList();
    }

    private static string Combine(string controllerRoute, string? actionTemplate) =>
        string.IsNullOrEmpty(actionTemplate)
            ? controllerRoute
            : $"{controllerRoute.TrimEnd('/')}/{actionTemplate.TrimStart('/')}";
}
