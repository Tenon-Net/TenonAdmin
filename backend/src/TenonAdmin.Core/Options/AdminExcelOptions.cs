namespace TenonAdmin.Core;

/// <summary>
/// 导入/导出配置(对应 <c>TenonAdmin:Excel</c> 节,见 excel-ledger §6.1)。
/// <para>三项上限各自独立:导入行数防内存打爆、导出行数防无界下载、导入文件大小不复用
/// <see cref="AdminUploadOptions.MaxSizeMb"/>——导入文件不进存储、生命周期完全不同,
/// 共用一个数字会让"调大头像上限"意外放开导入面。</para>
/// </summary>
public class AdminExcelOptions
{
    /// <summary>单次导入最大数据行数;超过拒收(ImportRowLimitExceeded)。防恶意大文件打爆内存。</summary>
    public int MaxImportRows { get; set; } = 5000;

    /// <summary>单次导出最大行数;超过拒绝(ExportRowLimitExceeded),提示先收窄筛选条件。</summary>
    public int MaxExportRows { get; set; } = 50000;

    /// <summary>导入文件大小上限(MB)。不复用 Upload.MaxSizeMb:导入文件不进存储、生命周期完全不同,
    /// 二者共用一个数字会让"调大头像上限"意外放开导入面。</summary>
    public int MaxImportFileSizeMb { get; set; } = 10;
}
