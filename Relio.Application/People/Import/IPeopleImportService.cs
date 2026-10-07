namespace Relio.Application.People.Import;

/// <summary>
/// Previews and performs the import of people from a vCard or CSV file (issue #29). The file itself never
/// reaches this service: the page reads and parses it (pure code in <c>Relio.Application.People.Import</c>)
/// and hands over only what was parsed, so nothing about the file is ever stored or logged.
/// </summary>
/// <remarks>
/// Every method runs for the signed-in user (<see cref="Security.UnauthenticatedUserException"/> when
/// there is none) and touches only that user's data. There are no foreign ids: an import sets no
/// relationship type and no tags.
/// </remarks>
public interface IPeopleImportService
{
    /// <summary>
    /// Builds the preview for <paramref name="read"/>: every person cleaned with the profile rules ("today"
    /// is today in the user's time zone) and compared with the user's own people, archived ones included.
    /// Read-only: it saves nothing and logs nothing.
    /// </summary>
    Task<ImportPreview> PreviewAsync(ImportReadResult read, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates every person in <paramref name="people"/> with one save: all of them, or (when anything fails)
    /// none. Returns how many were created. Each request is validated again; the service, not the page, is
    /// the authority.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="people"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The list is empty, holds a <see langword="null"/>, or a contact method carries an id.</exception>
    /// <exception cref="ArgumentOutOfRangeException">There are more than <see cref="ImportLimits.MaxPeople"/> people.</exception>
    /// <exception cref="PeopleImportValidationException">A request breaks a profile rule; nothing was saved.</exception>
    Task<int> ImportAsync(IReadOnlyList<ImportPersonRequest> people, CancellationToken cancellationToken = default);
}
