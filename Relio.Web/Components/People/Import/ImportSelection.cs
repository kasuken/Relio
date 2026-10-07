using Relio.Application.People.Import;

namespace Relio.Web.Components.People.Import;

/// <summary>
/// Which rows of an import preview are ticked. Lives in the import page for the length of the preview
/// and dies with it. A row that cannot be imported can never be selected; rows that may be duplicates
/// start unselected but stay selectable.
/// </summary>
public sealed class ImportSelection
{
    private readonly IReadOnlyList<ImportCandidate> _candidates;
    private readonly HashSet<Guid> _selected;

    /// <summary>Starts with every clean row selected (<see cref="ImportCandidate.SelectedByDefault"/>).</summary>
    public ImportSelection(ImportPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        _candidates = preview.Candidates;
        _selected = [.. _candidates.Where(candidate => candidate.SelectedByDefault).Select(candidate => candidate.RowId)];
    }

    /// <summary>How many rows are selected.</summary>
    public int Count => _selected.Count;

    /// <summary>Whether the row is selected.</summary>
    public bool IsSelected(Guid rowId) => _selected.Contains(rowId);

    /// <summary>Selects or deselects a row. A row that cannot be imported (or is not in the preview) is ignored.</summary>
    public void Set(Guid rowId, bool selected)
    {
        if (!_candidates.Any(candidate => candidate.RowId == rowId && candidate.CanImport))
        {
            return;
        }

        if (selected)
        {
            _selected.Add(rowId);
        }
        else
        {
            _selected.Remove(rowId);
        }
    }

    /// <summary>Selects every row that can be imported, possible duplicates included.</summary>
    public void SelectAll()
    {
        foreach (var candidate in _candidates.Where(candidate => candidate.CanImport))
        {
            _selected.Add(candidate.RowId);
        }
    }

    /// <summary>Deselects everything.</summary>
    public void SelectNone() => _selected.Clear();

    /// <summary>What to create: the selected rows' requests, in file order.</summary>
    public IReadOnlyList<ImportPersonRequest> SelectedRequests() =>
        [.. _candidates.Where(candidate => _selected.Contains(candidate.RowId) && candidate.Request is not null).Select(candidate => candidate.Request!)];
}
