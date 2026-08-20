using System.Globalization;
using System.Text.Json;

namespace TenonAdmin.Workflow;

/// <summary>
/// <see cref="IWfConditionEvaluator"/> 默认实现:结构化条件树求值,模板方法拆成一串小步,
/// 消费者可继承覆写单步(如自定义 <see cref="LooseEquals"/> 的宽松比较规则)。
/// <para>
/// 变量源 = <c>wf_instance.VariablesJson</c>,发起时由前端原样提交、后端从不校验
/// (<c>WfInstanceService.StartAsync</c> 直接透传 <c>input.VariablesJson</c>),因此本类
/// 全程对烂 JSON 免疫:任何解析失败 / 字段缺失 / 类型不确定,一律返回 <c>false</c>,不抛异常,
/// 让分支落到设计器强制配置的默认臂。
/// </para>
/// </summary>
public class WfConditionEvaluator : IWfConditionEvaluator
{
    /// <summary>递归深度上限,与 <see cref="System.Text.Json"/> 默认 <c>MaxDepth</c>(64)对齐,防止恶意/畸形条件树导致栈溢出。</summary>
    protected const int MaxDepth = 64;

    /// <inheritdoc />
    public virtual bool Evaluate(WfConditionExpr? expr, string? variablesJson)
    {
        if (expr is null) return false;

        // JsonElement 的有效期绑定其 JsonDocument;必须在整个求值过程中保活 doc,
        // 不能把 doc.RootElement 传出本方法后再 dispose。
        using var doc = ParseVariables(variablesJson);
        var root = doc?.RootElement ?? default;
        return EvaluateExpr(expr, root, depth: 0);
    }

    /// <summary>
    /// 解析 <c>VariablesJson</c>。variablesJson 为 null/空白/非法 JSON/根不是 object 时,
    /// 一律返回 <c>null</c>(= 视作「无任何字段」),不抛异常。
    /// </summary>
    protected virtual JsonDocument? ParseVariables(string? variablesJson)
    {
        if (string.IsNullOrWhiteSpace(variablesJson)) return null;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(variablesJson);
        }
        catch (Exception)
        {
            // 烂 JSON(发起时前端提交、后端从不校验)→ 视作无字段,不能让求值炸掉整单发起事务。
            // 这里故意接 Exception 而不是只接 JsonException:字符串含落单 UTF-16 代理项时
            // JsonDocument.Parse 抛的是 ArgumentException("Cannot transcode invalid UTF-16 string…"),
            // 不是 JsonException;求值跑在引擎的 DB 事务里,任何未捕获异常都会炸掉整单发起,
            // 接口硬契约是「求值不抛异常」,宁可捕宽一点也不能漏。
            return null;
        }

        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            // 根不是 object(如数组/标量/null)→ 视作无字段。
            doc.Dispose();
            return null;
        }

        return doc;
    }

    /// <summary>
    /// 对一个表达式节点求值(组或叶子)。<paramref name="expr"/> 为 <c>null</c> 或递归深度超过
    /// <see cref="MaxDepth"/> 层一律返回 <c>false</c>。
    /// </summary>
    protected virtual bool EvaluateExpr(WfConditionExpr? expr, JsonElement root, int depth)
    {
        if (expr is null) return false;
        if (depth >= MaxDepth) return false;

        // Children 非 null → 组(Field/Op 忽略);否则叶子。
        return expr.Children is not null
            ? EvaluateGroup(expr, root, depth)
            : EvaluateLeaf(expr, root, depth);
    }

    /// <summary>
    /// 组求值:<c>Logic</c> 缺省按 And;<c>Children</c> 为空数组 → 恒 <c>false</c>(没配就永不命中,落默认臂)。
    /// </summary>
    protected virtual bool EvaluateGroup(WfConditionExpr expr, JsonElement root, int depth)
    {
        var children = expr.Children!;
        if (children.Count == 0) return false;

        var isOr = expr.Logic == WfConditionLogic.Or;
        foreach (var child in children)
        {
            var childResult = EvaluateExpr(child, root, depth + 1);
            if (isOr && childResult) return true;
            if (!isOr && !childResult) return false;
        }

        // Or 循环完都没命中 → false;And 循环完都没被短路 → true。
        return !isOr;
    }

    /// <summary>
    /// 叶子求值。<c>Field</c>/<c>Op</c> 缺失 → false。11 个 <see cref="WfConditionOp"/> 全部有分支,
    /// 未知枚举值(<c>op</c> 支持 <c>allowIntegerValues</c>,可能是越界数字)落 <c>default</c> → false,不抛异常。
    /// </summary>
    protected virtual bool EvaluateLeaf(WfConditionExpr expr, JsonElement root, int depth)
    {
        if (string.IsNullOrEmpty(expr.Field) || expr.Op is null) return false;

        var hasField = TryGetField(root, expr.Field, out var fieldValue);

        // empty/notEmpty 是唯一能处理「字段缺失」的操作符;其余操作符字段缺失或值为 JSON null 一律 false
        // (含 ne/notIn —— 缺失既不 eq 也不 ne,失败即落默认臂)。
        var comparable = hasField && fieldValue.ValueKind != JsonValueKind.Null;

        switch (expr.Op.Value)
        {
            case WfConditionOp.Empty:
                return IsEmpty(fieldValue, hasField);
            case WfConditionOp.NotEmpty:
                return !IsEmpty(fieldValue, hasField);
            case WfConditionOp.Eq:
                return comparable && TryGetValueElement(expr, out var eqValue) && LooseEquals(fieldValue, eqValue);
            case WfConditionOp.Ne:
                return comparable && TryGetValueElement(expr, out var neValue) && !LooseEquals(fieldValue, neValue);
            case WfConditionOp.Gt:
                // gt/gte/lt/lte 仅数值:两侧都强转 decimal,任一侧转不出 → false。日期串比较不做(已知天花板,留给后续里程碑)。
                return comparable && TryGetValueElement(expr, out var gtValue) && EvaluateCompare(fieldValue, gtValue) is { } gt && gt > 0;
            case WfConditionOp.Gte:
                return comparable && TryGetValueElement(expr, out var gteValue) && EvaluateCompare(fieldValue, gteValue) is { } gte && gte >= 0;
            case WfConditionOp.Lt:
                return comparable && TryGetValueElement(expr, out var ltValue) && EvaluateCompare(fieldValue, ltValue) is { } lt && lt < 0;
            case WfConditionOp.Lte:
                return comparable && TryGetValueElement(expr, out var lteValue) && EvaluateCompare(fieldValue, lteValue) is { } lte && lte <= 0;
            case WfConditionOp.In:
                return comparable && TryGetValueElement(expr, out var inValue) && EvaluateIn(fieldValue, inValue);
            case WfConditionOp.NotIn:
                return comparable && TryGetValueElement(expr, out var notInValue) && !EvaluateIn(fieldValue, notInValue);
            case WfConditionOp.Contains:
                return comparable && TryGetValueElement(expr, out var containsValue) && EvaluateContains(fieldValue, containsValue);
            default:
                return false;
        }
    }

    /// <summary>
    /// 在 <paramref name="root"/> 顶层查找字段,平铺、大小写不敏感(对齐 <see cref="WfModelJson.Options"/>
    /// 的 <c>PropertyNameCaseInsensitive</c>);不支持 <c>a.b</c> 嵌套路径。<paramref name="root"/> 非
    /// object(含 <c>variablesJson</c> 解析失败时的 <c>default</c>)一律查不到。
    /// </summary>
    protected virtual bool TryGetField(JsonElement root, string name, out JsonElement value)
    {
        value = default;
        if (root.ValueKind != JsonValueKind.Object) return false;

        // JsonElement.TryGetProperty 大小写敏感,大小写不敏感查找要自己扫一遍。
        foreach (var prop in root.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 判空:字段缺失 / JSON <c>null</c> / 空或纯空白字符串 / 空数组 / 空对象 → true。
    /// 数字 <c>0</c> 与 <c>false</c> 不算空。
    /// </summary>
    protected virtual bool IsEmpty(JsonElement field, bool hasField)
    {
        if (!hasField) return true;

        return field.ValueKind switch
        {
            JsonValueKind.Null => true,
            JsonValueKind.String => string.IsNullOrWhiteSpace(field.GetString()),
            JsonValueKind.Array => field.GetArrayLength() == 0,
            JsonValueKind.Object => !field.EnumerateObject().Any(),
            _ => false, // Number(含 0)/True/False 均不算空
        };
    }

    /// <summary>
    /// 宽松相等:同类型精确比较;跨类型(数字↔字符串、布尔↔字符串)按规则试转,转不出就不相等。
    /// 数组/对象等未定义相等语义的组合一律不相等。
    /// </summary>
    protected virtual bool LooseEquals(JsonElement a, JsonElement b)
    {
        // 数字↔数字:decimal 比较。
        if (a.ValueKind == JsonValueKind.Number && b.ValueKind == JsonValueKind.Number)
            return TryToDecimal(a, out var da) && TryToDecimal(b, out var db) && da == db;

        // 字符串↔字符串:OrdinalIgnoreCase。
        if (a.ValueKind == JsonValueKind.String && b.ValueKind == JsonValueKind.String)
            return string.Equals(a.GetString(), b.GetString(), StringComparison.OrdinalIgnoreCase);

        // 布尔↔布尔:精确。
        if (IsBoolKind(a.ValueKind) && IsBoolKind(b.ValueKind))
            return (a.ValueKind == JsonValueKind.True) == (b.ValueKind == JsonValueKind.True);

        // 数字↔字符串(宽松):字符串按 decimal.TryParse(InvariantCulture) 试转,成功则数值比,
        // 失败则退回字符串文本比(OrdinalIgnoreCase)。
        if (a.ValueKind == JsonValueKind.Number && b.ValueKind == JsonValueKind.String)
            return LooseEqualsNumberString(a, b);
        if (a.ValueKind == JsonValueKind.String && b.ValueKind == JsonValueKind.Number)
            return LooseEqualsNumberString(b, a);

        // 布尔↔字符串:bool.TryParse。
        if (IsBoolKind(a.ValueKind) && b.ValueKind == JsonValueKind.String)
            return bool.TryParse(b.GetString(), out var pb) && (a.ValueKind == JsonValueKind.True) == pb;
        if (a.ValueKind == JsonValueKind.String && IsBoolKind(b.ValueKind))
            return bool.TryParse(a.GetString(), out var pa) && pa == (b.ValueKind == JsonValueKind.True);

        // 其余类型组合(数组/对象等)未定义相等语义,一律不相等。
        return false;
    }

    /// <summary>
    /// <see cref="LooseEquals"/> 数字↔字符串分支的具体实现:字符串按 <see cref="TryToDecimal"/> 试转,
    /// 成功则数值比;转不出(如科学计数 <c>"1e5"</c> —— <see cref="decimal.TryParse(string?, NumberStyles, IFormatProvider?, out decimal)"/>
    /// 不支持指数记法)则退回字符串文本比对(<see cref="JsonElement.ToString"/> 对 Number 保留原始 JSON 文本,OrdinalIgnoreCase)。
    /// </summary>
    protected virtual bool LooseEqualsNumberString(JsonElement number, JsonElement str)
    {
        if (TryToDecimal(str, out var parsed) && TryToDecimal(number, out var numberValue))
            return numberValue == parsed;

        // 转 decimal 失败,退回字符串文本比对(OrdinalIgnoreCase)。
        return string.Equals(number.ToString(), str.GetString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 强转 decimal(金额场景):JSON 数字走 <see cref="JsonElement.TryGetDecimal"/>;字符串走
    /// <see cref="decimal.TryParse(string?, NumberStyles, IFormatProvider?, out decimal)"/>(InvariantCulture)。
    /// 转不出(科学计数/超范围/非数字文本)→ false,不加 double 回退路径。
    /// </summary>
    protected virtual bool TryToDecimal(JsonElement element, out decimal value)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out value))
            return true;

        if (element.ValueKind == JsonValueKind.String &&
            decimal.TryParse(element.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out value))
            return true;

        value = default;
        return false;
    }

    /// <summary>
    /// gt/gte/lt/lte 用:两侧强转 decimal 后比较,返回 <see cref="decimal.CompareTo(decimal)"/> 的结果;
    /// 任一侧转不出 → <c>null</c>(调用方按不可比 = false 处理)。日期串比较不做(已知天花板)。
    /// </summary>
    protected virtual int? EvaluateCompare(JsonElement field, JsonElement value)
    {
        if (!TryToDecimal(field, out var df) || !TryToDecimal(value, out var dv)) return null;
        return df.CompareTo(dv);
    }

    /// <summary>
    /// in/notIn 用:右侧 <paramref name="value"/> 是数组时按元素逐个走 <see cref="LooseEquals"/> 宽松规则;
    /// 非数组时当作单元素列表。左侧 <paramref name="field"/> 本身是数组 → false。
    /// </summary>
    protected virtual bool EvaluateIn(JsonElement field, JsonElement value)
    {
        if (field.ValueKind == JsonValueKind.Array) return false;

        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                if (LooseEquals(field, item)) return true;
            }
            return false;
        }

        return LooseEquals(field, value);
    }

    /// <summary>
    /// contains 用:字段是字符串 → OrdinalIgnoreCase 子串(要求比较值也是字符串;
    /// 不像 <see cref="LooseEquals"/> 那样做跨类型宽松转换,所以 <c>contains("order100", 100)</c>
    /// 不匹配——value 是数字而非字符串,直接落 false);
    /// 字段是数组 → 任一元素按 <see cref="LooseEquals"/> 宽松规则等于 value;其余 → false。
    /// </summary>
    protected virtual bool EvaluateContains(JsonElement field, JsonElement value)
    {
        if (field.ValueKind == JsonValueKind.String)
        {
            if (value.ValueKind != JsonValueKind.String) return false;
            var haystack = field.GetString() ?? "";
            var needle = value.GetString() ?? "";
            return haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
        }

        if (field.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in field.EnumerateArray())
            {
                if (LooseEquals(item, value)) return true;
            }
            return false;
        }

        return false;
    }

    private static bool IsBoolKind(JsonValueKind kind) => kind is JsonValueKind.True or JsonValueKind.False;

    /// <summary>
    /// 取叶子的比较值字面量。<c>"value": null</c> 与「没有 value 键」两种情况 STJ 都可能给 C# <c>null</c>;
    /// 即便 <see cref="Nullable{T}.HasValue"/> 为真,也要再判 <see cref="JsonElement.ValueKind"/> 是否
    /// <see cref="JsonValueKind.Undefined"/>,不能只判 HasValue。
    /// </summary>
    private static bool TryGetValueElement(WfConditionExpr expr, out JsonElement value)
    {
        if (expr.Value.HasValue && expr.Value.Value.ValueKind != JsonValueKind.Undefined)
        {
            value = expr.Value.Value;
            return true;
        }

        value = default;
        return false;
    }
}
