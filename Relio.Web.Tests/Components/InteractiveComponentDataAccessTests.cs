using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Relio.Data;
using Relio.Data.Identity;

namespace Relio.Web.Tests.Components;

/// <summary>
/// Guards the database lane (see "One database operation at a time" in AGENTS.md): every
/// interactive component of a Blazor circuit shares one scoped <see cref="RelioDbContext"/>, and
/// only the data services' calls are serialized on it. A component that injected the context (or
/// Identity's managers, which use it) would run its own queries outside the lane and collide with its
/// siblings on SQL Server, where InMemory-backed tests cannot see it.
/// </summary>
public class InteractiveComponentDataAccessTests
{
    [Fact]
    public void Only_static_SSR_pages_inject_the_DbContext_or_Identity_managers()
    {
        Type[] forbidden =
        [
            typeof(RelioDbContext),
            typeof(DbContext),
            typeof(IDbContextFactory<RelioDbContext>),
            typeof(UserManager<RelioUser>),
            typeof(SignInManager<RelioUser>),
        ];

        var offenders = typeof(Program).Assembly.GetTypes()
            .Where(t => typeof(IComponent).IsAssignableFrom(t) && !t.IsAbstract)
            .Where(t => t.GetCustomAttribute<ExcludeFromInteractiveRoutingAttribute>() is null)
            .SelectMany(t => t
                .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(p => p.GetCustomAttribute<InjectAttribute>() is not null
                    && forbidden.Any(f => f.IsAssignableFrom(p.PropertyType)))
                .Select(p => $"{t.FullName}.{p.Name}"))
            .ToList();

        offenders.Should().BeEmpty(
            "interactive components share the circuit's DbContext; go through a data service instead");
    }

    [Fact]
    public void The_guard_sees_the_static_SSR_pages_that_do_inject_Identity_managers()
    {
        // Proves the scan above is looking at the right things: these pages do inject them, and are
        // allowed to only because they are excluded from interactive routing.
        var injecting = typeof(Program).Assembly.GetTypes()
            .Where(t => typeof(IComponent).IsAssignableFrom(t) && !t.IsAbstract)
            .Where(t => t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(p => p.GetCustomAttribute<InjectAttribute>() is not null
                    && (p.PropertyType == typeof(UserManager<RelioUser>)
                        || p.PropertyType == typeof(SignInManager<RelioUser>))))
            .ToList();

        injecting.Should().NotBeEmpty();
        injecting.Should().OnlyContain(t => t.GetCustomAttribute<ExcludeFromInteractiveRoutingAttribute>() != null);
    }
}
