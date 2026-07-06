namespace TenonAdmin.Core;

/// <summary>统一返回模型(骨架版,后续补错误码/msgKey,见设计 §6/§13)</summary>
public class Result<T>
{
    public int Code { get; set; }
    public string? Msg { get; set; }
    public T? Data { get; set; }

    public static Result<T> Ok(T data) => new() { Code = 0, Data = data };
}
