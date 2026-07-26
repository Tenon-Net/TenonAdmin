namespace TenonAdmin.Core;

/// <summary>触发来源(<c>sys_job_log.FireMode</c>,docs/scheduling-ledger.md §3.3)</summary>
public enum JobFireMode
{
    /// <summary>正常调度触发</summary>
    Schedule = 1,

    /// <summary>手动「执行一次」(在收到请求的副本本机执行,不经选主)</summary>
    Manual = 2,

    /// <summary>错过后按 FireOnceNow 策略的补跑(错过再多也只补一次)</summary>
    Misfire = 3,

    /// <summary>错过且策略为 Skip:仅记账不执行,把错过合并记一行</summary>
    MissedSkipped = 4,
}
