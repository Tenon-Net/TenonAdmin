namespace TenonAdmin.Services;

/// <summary>字典变更事件(某字典类型的类型或项被增删改)。订阅者可据此做跨节点失效、前端推送等。</summary>
public record DictChangedEvent(string TypeCode);

/// <summary>系统配置变更事件(某配置键被增删改)。</summary>
public record ConfigChangedEvent(string Key);
