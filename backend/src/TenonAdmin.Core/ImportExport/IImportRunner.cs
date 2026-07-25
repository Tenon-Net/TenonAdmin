namespace TenonAdmin.Core;

/// <summary>
/// 导入编排(解析 → 映射 → 校验 → 判重 → 落库)。对 xlsx 一无所知,只调 <see cref="IExcelReader"/>。
/// 实现类 public、各步 <c>protected virtual</c>,消费者覆写一步即可(模板方法,§5.3)。
/// </summary>
public interface IImportRunner
{
    /// <summary>解析文件并全量校验。<paramref name="mapping"/> 为 null 时按表头模糊匹配自动生成并回传。</summary>
    Task<ImportPreview> PreviewAsync(Stream file, IReadOnlyDictionary<string, string>? mapping,
        IImportProfile profile, CancellationToken cancellationToken = default);

    /// <summary>对前端改过的行重新校验(不碰文件)。</summary>
    Task<ImportPreview> ValidateAsync(IReadOnlyList<ImportRow> rows, IImportProfile profile,
        CancellationToken cancellationToken = default);

    /// <summary>按策略落库。<b>部分提交</b>:有错的行跳过,不影响无错行;返回逐行结果。</summary>
    Task<ImportCommitResult> CommitAsync(IReadOnlyList<ImportRow> rows, IImportProfile profile,
        DuplicateStrategy strategy, CancellationToken cancellationToken = default);
}
