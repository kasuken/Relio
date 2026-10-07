using Relio.Domain;

namespace Relio.Data;

internal readonly record struct ImportedAuditMapping(
    IOwnedEntity Entity,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed partial class RelioDbContext
{
    private Dictionary<IOwnedEntity, ImportedAudit>? _importedAudits;

    internal IDisposable BeginImportedAuditScope(IEnumerable<ImportedAuditMapping> mappings)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        if (_importedAudits is not null)
        {
            throw new InvalidOperationException("An imported-audit scope is already active for this context.");
        }

        var values = new Dictionary<IOwnedEntity, ImportedAudit>(ReferenceEqualityComparer.Instance);
        foreach (var mapping in mappings)
        {
            ArgumentNullException.ThrowIfNull(mapping.Entity);
            if (mapping.CreatedAtUtc.Kind != DateTimeKind.Utc
                || mapping.UpdatedAtUtc.Kind != DateTimeKind.Utc
                || mapping.CreatedAtUtc > mapping.UpdatedAtUtc
                || !values.TryAdd(mapping.Entity, new ImportedAudit(mapping.CreatedAtUtc, mapping.UpdatedAtUtc)))
            {
                throw new ArgumentException("Imported audit mappings must be unique and chronologically valid.", nameof(mappings));
            }
        }

        _importedAudits = values;
        return new ImportedAuditScope(this, values);
    }

    private bool TryApplyImportedAudit(IOwnedEntity entity)
    {
        if (_importedAudits is null
            || !_importedAudits.TryGetValue(entity, out var audit)
            || Entry(entity).State is not (Microsoft.EntityFrameworkCore.EntityState.Added or Microsoft.EntityFrameworkCore.EntityState.Modified))
        {
            return false;
        }

        entity.CreatedAtUtc = audit.CreatedAtUtc;
        entity.UpdatedAtUtc = audit.UpdatedAtUtc;
        return true;
    }

    private void EndImportedAuditScope(Dictionary<IOwnedEntity, ImportedAudit> mappings)
    {
        if (ReferenceEquals(_importedAudits, mappings))
        {
            _importedAudits = null;
        }
    }

    private sealed record ImportedAudit(DateTime CreatedAtUtc, DateTime UpdatedAtUtc);

    private sealed class ImportedAuditScope(
        RelioDbContext owner,
        Dictionary<IOwnedEntity, ImportedAudit> mappings) : IDisposable
    {
        private RelioDbContext? _owner = owner;

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.EndImportedAuditScope(mappings);
        }
    }
}
