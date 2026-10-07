using Microsoft.EntityFrameworkCore;
using Relio.Application.Notes;
using Relio.Application.Ownership;
using Relio.Application.Security;
using Relio.Domain;

namespace Relio.Data.Notes;

/// <summary>
/// EF Core implementation of <see cref="INoteService"/>. Every query explicitly filters by owner;
/// reads are untracked, and each mutation clears the circuit-scoped context's tracker in a finally
/// block. Note text is never logged.
/// </summary>
public sealed class NoteService(RelioDbContext dbContext, ICurrentUser currentUser) : INoteService
{
    private readonly RelioDbContext _dbContext =
        dbContext ?? throw new ArgumentNullException(nameof(dbContext));

    private readonly ICurrentUser _currentUser =
        currentUser ?? throw new ArgumentNullException(nameof(currentUser));

    /// <inheritdoc />
    public async Task<Note?> GetAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        var ownerId = _currentUser.RequireUserId();

        return await _dbContext.Set<Note>()
            .AsNoTracking()
            .FirstOrDefaultAsync(note => note.Id == noteId && note.OwnerId == ownerId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Note>> ListPinnedAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        var ownerId = _currentUser.RequireUserId();

        return await _dbContext.Set<Note>()
            .AsNoTracking()
            .Where(note => note.OwnerId == ownerId && note.PersonId == personId && note.IsPinned)
            .OrderByDescending(note => note.CreatedAtUtc)
            .ThenByDescending(note => note.Id)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Note> CreateAsync(CreateNoteRequest request, CancellationToken cancellationToken = default)
    {
        var ownerId = _currentUser.RequireUserId();

        try
        {
            ArgumentNullException.ThrowIfNull(request);
            var text = NormalizeAndValidate(request.Text);

            var personExists = await _dbContext.People
                .AsNoTracking()
                .AnyAsync(person => person.Id == request.PersonId && person.OwnerId == ownerId, cancellationToken);
            if (!personExists)
            {
                throw new ForeignEntityNotOwnedException("people");
            }

            var note = new Note
            {
                OwnerId = ownerId,
                PersonId = request.PersonId,
                Text = text,
                IsPinned = request.IsPinned,
            };

            _dbContext.Set<Note>().Add(note);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return note;
        }
        finally
        {
            _dbContext.ChangeTracker.Clear();
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(
        Guid noteId, UpdateNoteRequest request, CancellationToken cancellationToken = default)
    {
        var ownerId = _currentUser.RequireUserId();

        try
        {
            ArgumentNullException.ThrowIfNull(request);

            var note = await _dbContext.Set<Note>()
                .FirstOrDefaultAsync(candidate => candidate.Id == noteId && candidate.OwnerId == ownerId, cancellationToken);
            if (note is null)
            {
                return false;
            }

            note.Text = NormalizeAndValidate(request.Text);
            note.IsPinned = request.IsPinned;

            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally
        {
            _dbContext.ChangeTracker.Clear();
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        var ownerId = _currentUser.RequireUserId();

        try
        {
            var note = await _dbContext.Set<Note>()
                .FirstOrDefaultAsync(candidate => candidate.Id == noteId && candidate.OwnerId == ownerId, cancellationToken);
            if (note is null)
            {
                return false;
            }

            _dbContext.Set<Note>().Remove(note);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally
        {
            _dbContext.ChangeTracker.Clear();
        }
    }

    /// <inheritdoc />
    public async Task<bool> SetPinnedAsync(
        Guid noteId, bool isPinned, CancellationToken cancellationToken = default)
    {
        var ownerId = _currentUser.RequireUserId();

        try
        {
            var note = await _dbContext.Set<Note>()
                .FirstOrDefaultAsync(candidate => candidate.Id == noteId && candidate.OwnerId == ownerId, cancellationToken);
            if (note is null)
            {
                return false;
            }

            if (note.IsPinned == isPinned)
            {
                return true;
            }

            note.IsPinned = isPinned;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally
        {
            _dbContext.ChangeTracker.Clear();
        }
    }

    private static string NormalizeAndValidate(string? text)
    {
        var normalized = NoteRules.NormalizeText(text);
        var errors = NoteRules.Validate(normalized);
        if (errors.Count > 0)
        {
            throw new NoteValidationException(errors);
        }

        return normalized;
    }
}
