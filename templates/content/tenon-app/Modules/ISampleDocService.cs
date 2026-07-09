namespace TenonApp.Modules;

/// <summary>示例机构隔离业务服务契约:标准 CRUD,演示 <see cref="SampleDoc"/> 的数据范围读写。</summary>
public interface ISampleDocService
{
    /// <summary>新建(CreateOrgId 由审计 AOP 从当前用户机构自动回填)。</summary>
    Task<long> CreateAsync(string title);

    /// <summary>列出当前数据范围内可见的文档。</summary>
    Task<IReadOnlyList<SampleDoc>> ListAsync();

    /// <summary>改名;目标不在数据范围内(越权/不存在)返回 false。</summary>
    Task<bool> RenameAsync(long id, string title);

    /// <summary>删除;目标不在数据范围内(越权/不存在)返回 false。</summary>
    Task<bool> DeleteAsync(long id);
}
