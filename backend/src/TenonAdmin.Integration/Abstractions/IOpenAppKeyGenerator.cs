namespace TenonAdmin.Integration;

/// <summary>
/// 一次新生成的凭据材料。<see cref="ApiKey"/> 与 <see cref="Secret"/> 只在创建/轮换响应里交给管理员一次,
/// 服务端只保存 <see cref="KeyId"/> 与 <see cref="SecretHash"/>。
/// </summary>
/// <param name="KeyId">公开凭据标识(16 位小写 hex,唯一索引)</param>
/// <param name="Secret">秘密原文(32 字节随机数的 base64url)</param>
/// <param name="ApiKey">完整凭据 <c>tna_{KeyId}.{Secret}</c></param>
/// <param name="SecretHash">秘密摘要(SHA-256 小写 hex)</param>
public sealed record OpenAppGeneratedKey(string KeyId, string Secret, string ApiKey, string SecretHash)
{
    /// <summary>防止日志或调试输出意外带出秘密:记录的字符串形式只含公开标识。</summary>
    public override string ToString() => $"OpenAppGeneratedKey {{ KeyId = {KeyId} }}";
}

/// <summary>
/// 凭据生成与摘要(实现契约 §4.1)。默认实现 <see cref="OpenAppKeyGenerator"/> 使用平台加密随机数;
/// 可前置注册替换(例如加入服务端 pepper),但校验与生成必须使用同一实现。
/// </summary>
public interface IOpenAppKeyGenerator
{
    /// <summary>生成一把新凭据。</summary>
    OpenAppGeneratedKey Generate();

    /// <summary>计算秘密原文的摘要(与 <see cref="OpenAppGeneratedKey.SecretHash"/> 同一算法)。</summary>
    string ComputeSecretHash(string secret);
}
