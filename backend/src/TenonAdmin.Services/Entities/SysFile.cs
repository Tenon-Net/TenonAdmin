using SqlSugar;
using TenonAdmin.SqlSugar;

namespace TenonAdmin.Services;

/// <summary>
/// 文件记录(设计 §4/§14)。存<b>元数据 + 重写后的存储相对路径</b>,原始文件名仅作展示/下载命名,
/// 绝不参与物理路径拼接(防路径穿越)。物理字节由 <c>IFileStorage</c> 存,本表只记账。
/// <para>继承 <see cref="BaseEntity"/> 获得雪花 Id + 上传时间(CreateTime)+ 上传人(CreateUserId,AOP)+ 软删。
/// 软删只隐藏记录,物理文件的回收留给清理任务(v1.x),本任务不删盘(ponytail:先记账,清理后置)。</para>
/// </summary>
[SugarTable("sys_file", TableDescription = "文件记录")]
[SugarIndex("idx_sys_file_hash", nameof(SysFile.Hash), OrderByType.Asc)]
public class SysFile : BaseEntity
{
    /// <summary>原始文件名(含后缀,仅展示与下载命名用)</summary>
    [SugarColumn(Length = 256, ColumnDescription = "原始文件名")]
    public string OriginalName { get; set; } = "";

    /// <summary>存储相对路径(重写后的安全名,如 <c>20260707/0193x....png</c>);相对存储根</summary>
    [SugarColumn(Length = 512, ColumnDescription = "存储相对路径")]
    public string StoragePath { get; set; } = "";

    /// <summary>后缀(含点,小写)</summary>
    [SugarColumn(Length = 16, ColumnDescription = "后缀")]
    public string Extension { get; set; } = "";

    /// <summary>上传时声明的 Content-Type(仅记录,不作安全判定依据)</summary>
    [SugarColumn(Length = 128, IsNullable = true, ColumnDescription = "Content-Type")]
    public string? ContentType { get; set; }

    /// <summary>文件大小(字节)</summary>
    [SugarColumn(ColumnDescription = "文件大小(字节)")]
    public long SizeBytes { get; set; }

    /// <summary>内容 SHA-256(hex,小写);分片上传落库,供「秒传」按内容去重。单文件上传暂不计算(留 null)。</summary>
    [SugarColumn(Length = 64, IsNullable = true, ColumnDescription = "内容哈希(SHA-256)")]
    public string? Hash { get; set; }
}
