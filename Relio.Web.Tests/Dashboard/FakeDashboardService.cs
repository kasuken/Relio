using Relio.Application.Dashboard;

namespace Relio.Web.Tests.Dashboard;

internal sealed class FakeDashboardService(DashboardSnapshot snapshot) : IDashboardService
{
    public DashboardSnapshot Snapshot { get; set; } = snapshot;

    public Func<DashboardSnapshot>? OnGet { get; set; }

    public Task<DashboardSnapshot>? PendingResult { get; set; }

    public int Calls { get; private set; }

    public Task<DashboardSnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        Calls++;
        return PendingResult ?? Task.FromResult(OnGet?.Invoke() ?? Snapshot);
    }
}
