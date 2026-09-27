using System.Reflection;

namespace TenonAdmin.Workflow;

/// <summary>只识别四种数据库驱动的唯一键冲突；未知异常必须原样上抛。</summary>
internal static class WfDatabaseExceptionClassifier
{
    public static bool IsUniqueConstraintViolation(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var typeName = current.GetType().FullName ?? current.GetType().Name;
            if (typeName.EndsWith("PostgresException", StringComparison.Ordinal)
                && string.Equals(StringProperty(current, "SqlState"), "23505", StringComparison.Ordinal))
                return true;
            if (typeName.EndsWith("MySqlException", StringComparison.Ordinal)
                && IntProperty(current, "Number") == 1062)
                return true;
            if (typeName.EndsWith("SqlException", StringComparison.Ordinal)
                && (IntProperty(current, "Number") is 2601 or 2627))
                return true;
            if (typeName.EndsWith("SqliteException", StringComparison.Ordinal)
                && IntProperty(current, "SqliteErrorCode") == 19)
                return true;
        }

        return false;
    }

    private static string? StringProperty(Exception exception, string name) =>
        exception.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(exception) as string;

    private static int? IntProperty(Exception exception, string name)
    {
        var value = exception.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(exception);
        return value is int number ? number : null;
    }
}
