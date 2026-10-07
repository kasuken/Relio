namespace Relio.Application.People.Import;

/// <summary>
/// Thrown when a file cannot be imported at all. The message is the problem code and nothing else -
/// never the file name or any of its content - so it is safe to log (see the gdpr-compliant skill).
/// </summary>
public sealed class ImportFileException(ImportFileProblem problem) : Exception(problem.ToString())
{
    /// <summary>What was wrong with the file.</summary>
    public ImportFileProblem Problem { get; } = problem;
}
