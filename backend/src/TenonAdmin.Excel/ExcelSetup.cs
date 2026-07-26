using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TenonAdmin.Core;

namespace TenonAdmin.Excel;

/// <summary>
/// Excel 编解码装配(可选卫星包入口)。在 <c>AddTenonAdmin()</c> <b>之前</b>调用即前置注册
/// <see cref="IExcelReader"/> / <see cref="IExcelWriter"/> / <see cref="IExcelTemplateBuilder"/>
/// 的真实现,压过内核默认的 <see cref="MissingExcelProvider"/>(内核用 <c>TryAdd</c> 注册,先到者胜,设计 §5.2)。
/// <para>不装本包 / 不调本方法 → 任意 codec 调用一律抛 <see cref="ErrorCode.ExcelProviderMissing"/>(46001),fail-loud。</para>
/// </summary>
public static class ExcelSetup
{
    /// <summary>
    /// 启用 xlsx 读写与模板生成:注册 MiniExcel 读/写 + OpenXml 模板构建。
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddTenonAdminExcel(); // 先注册,赢 TryAdd
    /// builder.Services.AddTenonAdmin(builder.Configuration);
    /// </code>
    /// </example>
    public static IServiceCollection AddTenonAdminExcel(this IServiceCollection services)
    {
        // TryAdd:与内核可替换性模型一致——本方法须在 AddTenonAdmin() 之前调用方能胜出。
        services.TryAddSingleton<IExcelReader, MiniExcelReader>();
        services.TryAddSingleton<IExcelWriter, MiniExcelWriter>();
        services.TryAddSingleton<IExcelTemplateBuilder, OpenXmlTemplateBuilder>();
        return services;
    }
}
