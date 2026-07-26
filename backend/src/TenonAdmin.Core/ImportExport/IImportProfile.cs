namespace TenonAdmin.Core;

/// <summary>
/// 导入档案:一个实体"怎么导"的全部声明。**这是消费者要实现的接口**(内核给 <c>UserImportProfile</c> 作范例)。
/// 编排在 <see cref="IImportRunner"/>,本接口只声明规则与两个业务动作(查重、落行)。
/// </summary>
public interface IImportProfile
{
    /// <summary>档案编码,用于模板文件名与日志,如 <c>sys-user</c>。</summary>
    string Code { get; }

    /// <summary>模板与预览的列定义,<b>顺序即模板列顺序</b>。</summary>
    IReadOnlyList<ImportColumn> Columns { get; }

    /// <summary>业务键列 Key(判重依据,如 <c>["Account"]</c>);空集合 = 不判重。</summary>
    IReadOnlyList<string> BusinessKeys { get; }

    /// <summary>
    /// 行级自定义校验:跨列规则、按名查外键、越权检查都在这里。返回该行的全部错误(无错返回空)。
    /// <b>Runner 已先做过</b>必填、字典值、行内重复三项通用校验,本方法不必重复。
    /// </summary>
    Task<IReadOnlyList<CellError>> ValidateRowAsync(ImportRow row, CancellationToken cancellationToken = default);

    /// <summary>
    /// 库内已存在的业务键集合。<b>实现须一次查完传进来的这批</b>(一条 <c>IN</c> 查询),不得逐行查库。
    /// <para>
    /// Runner 会<b>分批</b>调用本方法(见 <c>ImportRunner.ExistingKeyBatchSize</c>,默认 500),因为
    /// <c>keys.Contains(...)</c> 翻成 <c>IN</c> 是一个键一个参数,而 SQL Server 单语句参数上限 2100、
    /// 老版 SQLite 999 —— 一次塞五千个键在 SqlServer 上必炸。所以实现<b>不必也不该</b>自己再分批。
    /// </para>
    /// </summary>
    Task<IReadOnlySet<string>> FindExistingKeysAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default);

    /// <summary>
    /// 落一行。<paramref name="overwrite"/> 为 true 表示业务键已存在且策略是覆盖。
    /// <b>实现必须复用既有领域服务</b>(如 <c>IUserService.AddAsync</c>),不得直插实体绕过其安全不变量(§8 坑 5)。
    /// </summary>
    Task CommitRowAsync(ImportRow row, bool overwrite, CancellationToken cancellationToken = default);
}
