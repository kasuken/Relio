using System.Reflection;
using Relio.Application.Administration;
using Relio.Domain;

namespace Relio.Application.Tests.Administration;

/// <summary>
/// Proves the architectural promise of issue #19: an Administrator manages accounts, not content.
/// The administration and registration interfaces can only exchange primitives and the records in
/// <c>Relio.Application.Administration</c>, so nothing in them can carry a person, tag, profile or
/// any other user-owned entity - there is no code path by which "administrator" becomes "can read
/// someone's notebook". Adding such a type to either interface fails this test.
/// </summary>
public class AdministrationSurfaceTests
{
    [Fact]
    public void Administration_services_expose_no_user_owned_types()
    {
        var domainAssembly = typeof(IOwnedEntity).Assembly;
        var seen = new HashSet<Type>();

        foreach (var serviceType in new[] { typeof(IUserAdministrationService), typeof(IAccountRegistrationService) })
        {
            foreach (var method in serviceType.GetMethods())
            {
                Collect(method.ReturnType, seen);
                foreach (var parameter in method.GetParameters())
                {
                    Collect(parameter.ParameterType, seen);
                }
            }
        }

        seen.Should().NotBeEmpty();
        seen.Where(t => t.Assembly == domainAssembly).Should().BeEmpty("no Domain entity may cross this boundary");
        seen.Where(t => typeof(IOwnedEntity).IsAssignableFrom(t)).Should().BeEmpty();
        seen.Select(t => t.Name).Should().NotContain("RelioUser", "Identity's user entity stays inside Relio.Data");
        seen.Where(t => t.Assembly != typeof(object).Assembly && t.Assembly != typeof(AccountSummary).Assembly)
            .Should().BeEmpty("only BCL types and Relio.Application.Administration types may appear");
    }

    private static void Collect(Type type, HashSet<Type> seen)
    {
        if (type == typeof(void) || !seen.Add(type))
        {
            return;
        }

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                Collect(argument, seen);
            }
        }

        if (type.HasElementType)
        {
            Collect(type.GetElementType()!, seen);
        }

        // Records: follow their public properties (e.g. CreateInvitationResult -> CreatedInvitation).
        if (type.Assembly == typeof(AccountSummary).Assembly && !type.IsEnum)
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Collect(property.PropertyType, seen);
            }
        }
    }
}
