namespace TenonAdmin.Core;

/// <summary>xlsx 写出(codec 层)。同 <see cref="IExcelReader"/> 的替换模型。</summary>
public interface IExcelWriter
{
    /// <summary>把一张表写成 xlsx 字节。返回可直接交给 <c>FileResult</c> 的流(定位在 0)。</summary>
    Task<Stream> WriteAsync(ExportSheet sheet, CancellationToken cancellationToken = default);
}
