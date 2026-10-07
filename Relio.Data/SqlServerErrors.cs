using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Relio.Data;

/// <summary>
/// Recognises the SQL Server errors a service translates into a calm, typed answer.
/// </summary>
internal static class SqlServerErrors
{
    /// <summary>
    /// True when <paramref name="exception"/> is SQL Server refusing a duplicate key (error 2601 or
    /// 2627) on the unique index called <paramref name="indexName"/>.
    /// </summary>
    /// <remarks>
    /// The error message quotes the duplicate key - the owner id and the name - so it must never be
    /// logged, attached to another exception or shown. Callers throw a fresh exception that carries
    /// only a code.
    /// </remarks>
    public static bool IsUniqueIndexViolation(DbUpdateException exception, string indexName) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 } sqlException
        && sqlException.Message.Contains(indexName, StringComparison.Ordinal);
}
