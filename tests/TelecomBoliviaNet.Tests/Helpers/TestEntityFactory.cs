using System.Reflection;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Tests.Helpers;

/// <summary>
/// Creates domain entities for tests, bypassing private constructors and
/// private setters that exist to enforce business invariants in production code.
///
/// Using reflection is intentional here: tests need to set up specific states
/// (e.g. Suspendido, DadoDeBaja) without going through the full business flow,
/// and the domain pattern is well-tested via integration/handler tests.
/// </summary>
public static class TestEntityFactory
{
    /// <summary>Creates a Client in any state for unit testing.</summary>
    public static Client MakeClient(
        Guid?             id               = null,
        string            tbnCode          = "TBN-001",
        string            fullName         = "Test Cliente",
        string            phoneMain        = "59170000001",
        Guid?             planId           = null,
        ClientStatus      status           = ClientStatus.Activo,
        DateTime?         installationDate = null,
        DateTime?         suspendedAt      = null,
        IEnumerable<Invoice>? invoices     = null)
    {
        // Invoke the private parameterless constructor EF Core relies on
        var client = (Client)Activator.CreateInstance(
            typeof(Client), nonPublic: true)!;

        // Set init property (Entity.Id) — init is byte-equivalent to a private setter
        // for reflection purposes
        SetProperty(client, typeof(Entity), "Id", id ?? Guid.NewGuid());

        // Public setters — set directly
        client.TbnCode          = tbnCode;
        client.FullName         = fullName;
        client.PhoneMain        = phoneMain;
        client.PlanId           = planId ?? Guid.NewGuid();
        client.Zone             = "Sur";
        client.IdentityCard     = "12345678";
        client.WinboxNumber     = string.Empty;
        client.InstallationDate = installationDate ?? DateTime.UtcNow.AddMonths(-3);

        // Private setters — use reflection
        SetProperty(client, typeof(Client), "Status", status);
        if (suspendedAt.HasValue)
            SetProperty(client, typeof(Client), "SuspendedAt", suspendedAt.Value);

        if (invoices != null)
            client.Invoices = invoices.ToList();

        return client;
    }

    // ── Internal helper ─────────────────────────────────────────────────────

    private static void SetProperty(object target, Type declaringType, string name, object value)
    {
        var prop = declaringType.GetProperty(name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (prop is null)
            throw new InvalidOperationException(
                $"Property '{name}' not found on {declaringType.Name}.");

        // For 'init' properties we need to use the backing set accessor directly
        var setter = prop.GetSetMethod(nonPublic: true)
                  ?? prop.DeclaringType?.GetMethod($"set_{name}",
                       BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        if (setter is null)
            throw new InvalidOperationException(
                $"No setter found for '{name}' on {declaringType.Name}.");

        setter.Invoke(target, [value]);
    }
}
