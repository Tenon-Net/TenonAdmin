using System.Text.Json;
using TenonAdmin.Workflow;

namespace TenonAdmin.Tests;

/// <summary>
/// <see cref="WfConditionEvaluator"/> 纯单测——不建 host,直接 <c>new</c>。
/// 覆盖设计定案表:11 个操作符各一真一假、组合逻辑(含嵌套)、字段缺失、烂 <c>variablesJson</c> 失败安全、
/// 大小写不敏感字段名、跨类型宽松比较,以及锁住设计器 camelCase 枚举串的反序列化回归。
/// </summary>
public class WfConditionEvaluatorTests
{
    private readonly WfConditionEvaluator _sut = new();

    private bool Eval(WfConditionExpr? expr, string? variablesJson) => _sut.Evaluate(expr, variablesJson);

    private static WfConditionExpr Leaf(string field, WfConditionOp op, object? value) => new()
    {
        Field = field,
        Op = op,
        Value = value is null ? null : JsonSerializer.SerializeToElement(value),
    };

    private static WfConditionExpr Group(WfConditionLogic logic, params WfConditionExpr[] children) => new()
    {
        Logic = logic,
        Children = children.ToList(),
    };

    private static string Vars(object value) =>
        JsonSerializer.Serialize(new Dictionary<string, object> { ["v"] = value });

    private static string VarsMulti(params (string Key, object Value)[] pairs)
    {
        var dict = new Dictionary<string, object>();
        foreach (var (key, value) in pairs) dict[key] = value;
        return JsonSerializer.Serialize(dict);
    }

    // ── 11 个操作符,各一真一假 ──────────────────────────────

    [Fact]
    public void Eq_matches_and_mismatches()
    {
        Assert.True(Eval(Leaf("v", WfConditionOp.Eq, 100), Vars(100)));
        Assert.False(Eval(Leaf("v", WfConditionOp.Eq, 200), Vars(100)));
    }

    [Fact]
    public void Ne_matches_and_mismatches()
    {
        Assert.True(Eval(Leaf("v", WfConditionOp.Ne, 200), Vars(100)));
        Assert.False(Eval(Leaf("v", WfConditionOp.Ne, 100), Vars(100)));
    }

    [Fact]
    public void Gt_matches_and_mismatches()
    {
        Assert.True(Eval(Leaf("v", WfConditionOp.Gt, 50), Vars(100)));
        Assert.False(Eval(Leaf("v", WfConditionOp.Gt, 200), Vars(100)));
    }

    [Fact]
    public void Gte_matches_and_mismatches()
    {
        Assert.True(Eval(Leaf("v", WfConditionOp.Gte, 100), Vars(100)));
        Assert.False(Eval(Leaf("v", WfConditionOp.Gte, 101), Vars(100)));
        Assert.False(Eval(Leaf("v", WfConditionOp.Gte, 100), Vars("N/A"))); // 转不出 decimal → 不可比,false(不能翻成 true)
    }

    [Fact]
    public void Lt_matches_and_mismatches()
    {
        Assert.True(Eval(Leaf("v", WfConditionOp.Lt, 100), Vars(50)));
        Assert.False(Eval(Leaf("v", WfConditionOp.Lt, 50), Vars(100)));
    }

    [Fact]
    public void Lte_matches_and_mismatches()
    {
        Assert.True(Eval(Leaf("v", WfConditionOp.Lte, 100), Vars(100)));
        Assert.False(Eval(Leaf("v", WfConditionOp.Lte, 100), Vars(200)));
    }

    [Fact]
    public void In_matches_and_mismatches()
    {
        Assert.True(Eval(Leaf("v", WfConditionOp.In, new[] { "A", "B" }), Vars("A")));
        Assert.False(Eval(Leaf("v", WfConditionOp.In, new[] { "C", "D" }), Vars("A")));
    }

    [Fact]
    public void NotIn_matches_and_mismatches()
    {
        Assert.True(Eval(Leaf("v", WfConditionOp.NotIn, new[] { "C", "D" }), Vars("A")));
        Assert.False(Eval(Leaf("v", WfConditionOp.NotIn, new[] { "A", "B" }), Vars("A")));
    }

    [Fact]
    public void Contains_matches_and_mismatches()
    {
        Assert.True(Eval(Leaf("v", WfConditionOp.Contains, "world"), Vars("hello world")));
        Assert.False(Eval(Leaf("v", WfConditionOp.Contains, "planet"), Vars("hello world")));
    }

    [Fact]
    public void Empty_matches_and_mismatches()
    {
        Assert.True(Eval(Leaf("v", WfConditionOp.Empty, null), Vars("")));
        Assert.False(Eval(Leaf("v", WfConditionOp.Empty, null), Vars("x")));
    }

    [Fact]
    public void NotEmpty_matches_and_mismatches()
    {
        Assert.True(Eval(Leaf("v", WfConditionOp.NotEmpty, null), Vars("x")));
        Assert.False(Eval(Leaf("v", WfConditionOp.NotEmpty, null), Vars("")));
    }

    // ── 组合逻辑:and / or / 嵌套组 ──────────────────────────

    [Fact]
    public void And_group_requires_all_children_true()
    {
        var expr = Group(WfConditionLogic.And, Leaf("a", WfConditionOp.Eq, 1), Leaf("b", WfConditionOp.Eq, 2));
        Assert.True(Eval(expr, VarsMulti(("a", 1), ("b", 2))));
        Assert.False(Eval(expr, VarsMulti(("a", 1), ("b", 3))));
    }

    [Fact]
    public void Or_group_requires_any_child_true()
    {
        var expr = Group(WfConditionLogic.Or, Leaf("a", WfConditionOp.Eq, 1), Leaf("b", WfConditionOp.Eq, 2));
        Assert.True(Eval(expr, VarsMulti(("a", 1), ("b", 999))));
        Assert.False(Eval(expr, VarsMulti(("a", 999), ("b", 888))));
    }

    [Fact]
    public void Nested_group_evaluates_recursively()
    {
        // (a==1 且 b==2) 或 c==3
        var inner = Group(WfConditionLogic.And, Leaf("a", WfConditionOp.Eq, 1), Leaf("b", WfConditionOp.Eq, 2));
        var expr = Group(WfConditionLogic.Or, inner, Leaf("c", WfConditionOp.Eq, 3));

        Assert.True(Eval(expr, VarsMulti(("a", 1), ("b", 2), ("c", 999)))); // 内层组命中
        Assert.True(Eval(expr, VarsMulti(("a", 999), ("b", 999), ("c", 3)))); // 外层 c 命中
        Assert.False(Eval(expr, VarsMulti(("a", 999), ("b", 999), ("c", 999))));
    }

    [Fact]
    public void Empty_children_group_returns_false()
    {
        var expr = new WfConditionExpr { Logic = WfConditionLogic.And, Children = [] };
        Assert.False(Eval(expr, Vars(100)));
    }

    // ── 字段缺失:对每类操作符的行为 ──────────────────────────

    [Fact]
    public void Missing_field_behaves_per_operator()
    {
        const string vars = "{}"; // 无任何字段

        Assert.False(Eval(Leaf("missing", WfConditionOp.Eq, 1), vars));
        Assert.False(Eval(Leaf("missing", WfConditionOp.Ne, 1), vars)); // 缺失既不 eq 也不 ne
        Assert.False(Eval(Leaf("missing", WfConditionOp.Gt, 1), vars));
        Assert.False(Eval(Leaf("missing", WfConditionOp.NotIn, new object[] { 1, 2 }), vars)); // 缺失字段 notIn 也 false
        Assert.True(Eval(Leaf("missing", WfConditionOp.Empty, null), vars)); // 唯独 empty/notEmpty 把缺失当空
    }

    // ── 烂 variablesJson:不抛异常,一律 false(empty 例外为 true) ──

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{ 烂 JSON")]
    [InlineData("[1,2]")] // 根不是 object
    public void Malformed_variables_json_never_throws_and_non_empty_ops_return_false(string? variablesJson)
    {
        var expr = Leaf("v", WfConditionOp.Eq, 100);
        var exception = Record.Exception(() => Eval(expr, variablesJson));
        Assert.Null(exception);
        Assert.False(Eval(expr, variablesJson));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{ 烂 JSON")]
    [InlineData("[1,2]")]
    public void Malformed_variables_json_empty_op_returns_true(string? variablesJson)
    {
        var expr = Leaf("v", WfConditionOp.Empty, null);
        Assert.True(Eval(expr, variablesJson));
    }

    // ── 大小写不敏感字段名 ────────────────────────────────

    [Fact]
    public void Field_lookup_is_case_insensitive()
    {
        var vars = JsonSerializer.Serialize(new Dictionary<string, object> { ["amount"] = 100 });
        Assert.True(Eval(Leaf("AMOUNT", WfConditionOp.Eq, 100), vars));

        // 不支持 a.b 嵌套路径:平铺查找找不到嵌套字段。
        Assert.False(Eval(Leaf("a.b", WfConditionOp.Eq, 1), """{"a":{"b":1}}"""));
    }

    // ── 跨类型宽松比较 ────────────────────────────────────

    [Fact]
    public void Loose_compare_numeric_string_against_gt()
    {
        var vars = JsonSerializer.Serialize(new Dictionary<string, object> { ["amount"] = "12000" });
        Assert.True(Eval(Leaf("amount", WfConditionOp.Gt, 10000), vars));
    }

    [Fact]
    public void Loose_compare_bool_against_string_eq()
    {
        var vars = JsonSerializer.Serialize(new Dictionary<string, object> { ["flag"] = true });
        Assert.True(Eval(Leaf("flag", WfConditionOp.Eq, "true"), vars));
    }

    // ── expr 为 null ─────────────────────────────────────

    [Fact]
    public void Null_expr_returns_false()
    {
        Assert.False(Eval(null, Vars(100)));
    }

    // ── 防协议漂移:锁住设计器发来的 camelCase 枚举串(notIn ↔ NotIn) ──

    [Fact]
    public void Deserialized_camel_case_op_string_is_recognized()
    {
        const string json = """{"field":"amount","op":"notIn","value":[1,2]}""";
        var expr = JsonSerializer.Deserialize<WfConditionExpr>(json, WfModelJson.Options);

        Assert.NotNull(expr);
        Assert.Equal(WfConditionOp.NotIn, expr!.Op);

        var amount3 = JsonSerializer.Serialize(new Dictionary<string, object> { ["amount"] = 3 });
        var amount1 = JsonSerializer.Serialize(new Dictionary<string, object> { ["amount"] = 1 });
        Assert.True(Eval(expr, amount3));  // amount=3 不在 [1,2] → notIn 为 true
        Assert.False(Eval(expr, amount1)); // amount=1 在 [1,2] → notIn 为 false
    }

    // ── P1:落单 UTF-16 代理项 → JsonDocument.Parse 抛 ArgumentException(非 JsonException),不能穿透 ──

    [Fact]
    public void Malformed_variables_json_with_lone_utf16_surrogate_never_throws()
    {
        const string variablesJson = "{\"v\":\"\ud83d\"}"; // 落单高位代理项,不是合法 UTF-16 文本
        var expr = Leaf("v", WfConditionOp.Eq, "x");

        var exception = Record.Exception(() => Eval(expr, variablesJson));
        Assert.Null(exception);
        Assert.False(Eval(expr, variablesJson));
    }

    // ── LooseEqualsNumberString 零覆盖补齐:数字↔字符串宽松比较、字符串↔字符串、布尔↔布尔 ──

    [Theory]
    [InlineData(100, "100", true)]   // 数字↔字符串,字符串能转 decimal → 数值比
    [InlineData(100, "N/A", false)]  // 数字↔字符串,字符串转不出 decimal → 退回文本比,不等
    [InlineData("abc", "ABC", true)] // 字符串↔字符串:OrdinalIgnoreCase
    [InlineData(true, true, true)]   // 布尔↔布尔:精确
    [InlineData("100", 100, true)]   // 字符串↔数字(反方向):字符串能转 decimal → 数值比
    [InlineData("N/A", 100, false)]  // 字符串↔数字(反方向):字符串转不出 decimal → 退回文本比,不等
    [InlineData(true, false, false)] // 布尔↔布尔:不等
    [InlineData("true", true, true)] // 字符串↔布尔(反方向):bool.TryParse
    public void Eq_covers_loose_and_exact_type_pairs(object fieldValue, object exprValue, bool expected)
    {
        Assert.Equal(expected, Eval(Leaf("v", WfConditionOp.Eq, exprValue), Vars(fieldValue)));
    }

    // ── 数字↔字符串宽松比较的文本回退 true 路径:decimal.TryParse("1e5", NumberStyles.Number, ...) 不支持
    // 指数记法会失败,退回文本比对;JsonElement.ToString() 对 Number 保留原始 JSON 文本 "1E5"。
    // 该形状(字段侧要求保留科学计数法原文)装不进上面 Theory 的 InlineData,单独写一个 Fact。
    [Fact]
    public void Eq_number_string_falls_back_to_text_compare_when_decimal_parse_fails()
    {
        Assert.True(Eval(Leaf("v", WfConditionOp.Eq, "1e5"), """{"v":1E5}"""));
    }

    // ── IsEmpty 全 arm 覆盖:数字 0 / 布尔 false 不算空,空数组/空对象/JSON null/纯空白字符串算空 ──

    [Fact]
    public void Empty_covers_number_zero_bool_false_array_object_null_and_whitespace()
    {
        Assert.False(Eval(Leaf("v", WfConditionOp.Empty, null), Vars(0)));                                 // 数字 0 不算空
        Assert.False(Eval(Leaf("v", WfConditionOp.Empty, null), Vars(false)));                             // 布尔 false 不算空
        Assert.True(Eval(Leaf("v", WfConditionOp.Empty, null), Vars(Array.Empty<int>())));                 // 空数组
        Assert.True(Eval(Leaf("v", WfConditionOp.Empty, null), Vars(new Dictionary<string, object>())));   // 空对象
        Assert.False(Eval(Leaf("v", WfConditionOp.Empty, null), Vars(new[] { 1 })));                       // 非空数组不算空
        Assert.False(Eval(Leaf("v", WfConditionOp.Empty, null), Vars(new Dictionary<string, object> { ["x"] = 1 }))); // 非空对象不算空
        Assert.True(Eval(Leaf("v", WfConditionOp.Empty, null), """{"v":null}"""));                         // JSON null
        Assert.True(Eval(Leaf("v", WfConditionOp.Empty, null), Vars("   ")));                              // 纯空白字符串
    }

    // ── 字段值为 JSON null,与字段缺失分开测(EvaluateLeaf 的 comparable 判断需要两者都堵住) ──

    [Fact]
    public void Json_null_field_value_differs_from_missing_field()
    {
        const string vars = """{"v":null}"""; // 字段存在,但值是 JSON null(区别于完全没有这个键)

        Assert.False(Eval(Leaf("v", WfConditionOp.Eq, 1), vars));
        Assert.False(Eval(Leaf("v", WfConditionOp.Ne, 1), vars));                                  // null 不参与 ne 比较,一律 false
        Assert.False(Eval(Leaf("v", WfConditionOp.NotIn, new object[] { 1, 2 }), vars));
        Assert.True(Eval(Leaf("v", WfConditionOp.Empty, null), vars));                             // 唯独 empty 把 null 当空
    }

    // ── 组语义:Logic 缺省按 And;Children 非 null 时 Field/Op 被忽略 ──

    [Fact]
    public void Group_logic_defaults_to_and()
    {
        var expr = new WfConditionExpr { Children = [Leaf("a", WfConditionOp.Eq, 1), Leaf("b", WfConditionOp.Eq, 2)] }; // Logic 未设置
        Assert.True(Eval(expr, VarsMulti(("a", 1), ("b", 2))));
        Assert.False(Eval(expr, VarsMulti(("a", 1), ("b", 3))));
    }

    [Fact]
    public void Group_with_children_ignores_field_and_op()
    {
        var expr = new WfConditionExpr
        {
            Field = "should-be-ignored",
            Op = WfConditionOp.Eq,
            Logic = WfConditionLogic.And,
            Children = [Leaf("a", WfConditionOp.Eq, 1)],
        };
        Assert.True(Eval(expr, VarsMulti(("a", 1), ("should-be-ignored", 999))));
    }

    // ── 叶子缺 Field/Op → false;递归深度超限 → false(不炸栈) ──

    [Fact]
    public void Leaf_missing_field_or_op_returns_false()
    {
        Assert.False(Eval(new WfConditionExpr { Op = WfConditionOp.Eq, Value = JsonSerializer.SerializeToElement(1) }, Vars(1))); // 缺 Field
        Assert.False(Eval(new WfConditionExpr { Field = "v" }, Vars(1))); // 缺 Op
        Assert.False(Eval(new WfConditionExpr { Op = WfConditionOp.Empty }, Vars(1))); // 缺 Field,op=empty(该 guard 唯一有可观察行为的路径)
    }

    [Fact]
    public void Recursion_depth_over_limit_returns_false_without_stack_overflow()
    {
        // 200 层足够验证深度保护生效(超过 MaxDepth=64),又不会在保护被误删时炸掉整个测试宿主进程
        // (实测 5000 层会 Test Run Aborted,摘要行还先打出部分 Passed,看起来像绿的)。
        WfConditionExpr expr = Leaf("v", WfConditionOp.Eq, 1);
        for (var i = 0; i < 200; i++)
        {
            expr = Group(WfConditionLogic.And, expr); // 套 200 层组
        }

        var exception = Record.Exception(() => Eval(expr, Vars(1)));
        Assert.Null(exception);
        Assert.False(Eval(expr, Vars(1)));
    }

    // ── in/contains 旁路分支:右侧非数组当单元素列表;左侧字段是数组;contains 字段是数组/数字/布尔 ──

    [Fact]
    public void In_and_contains_cover_bypass_branches()
    {
        // in 右侧非数组 → 当单元素列表
        Assert.True(Eval(Leaf("v", WfConditionOp.In, "A"), Vars("A")));

        // in/notIn 左侧字段本身是数组 → EvaluateIn 恒 false;in 因此 false,notIn(= 字段存在且不在列表)因此 true
        Assert.False(Eval(Leaf("v", WfConditionOp.In, new[] { "A", "B" }), Vars(new[] { "A" })));
        Assert.True(Eval(Leaf("v", WfConditionOp.NotIn, new[] { "C", "D" }), Vars(new[] { "A" })));

        // contains 字段是数组 → 任一元素按 LooseEquals 命中
        Assert.True(Eval(Leaf("v", WfConditionOp.Contains, "B"), Vars(new[] { "A", "B" })));

        // contains 字段是数字/布尔 → 不是字符串也不是数组,一律 false
        Assert.False(Eval(Leaf("v", WfConditionOp.Contains, "1"), Vars(1)));
        Assert.False(Eval(Leaf("v", WfConditionOp.Contains, "true"), Vars(true)));

        // contains 字段是字符串,但 value 不是字符串(数字)→ 不做跨类型宽松转换,直接 false
        Assert.False(Eval(Leaf("v", WfConditionOp.Contains, 100), Vars("order100")));
    }

    // ── TryGetValueElement 的 Undefined 半边:Value 为 default(JsonElement)(ValueKind=Undefined)时,
    // HasValue 为 true 但要按「没有 value」处理,不能只判 HasValue(从设计器 JSON 走不到这个形状,
    // STJ 把 "value":null 与缺键都映射成 C# null;这里直接构造 default 值钉死防御逻辑本身) ──

    [Fact]
    public void Leaf_with_undefined_json_element_value_is_treated_as_missing_value()
    {
        var expr = new WfConditionExpr { Field = "v", Op = WfConditionOp.Ne, Value = default(JsonElement) };
        Assert.False(Eval(expr, Vars(1)));
    }
}
