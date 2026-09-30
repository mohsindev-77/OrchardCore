using OrchardCore.Security.Permissions;

namespace WorkMate.Hcm.Attendance;

/// <summary>Permissions are declared here and checked in services, never only in controllers.</summary>
public sealed class Permissions : IPermissionProvider
{
    public Task<IEnumerable<Permission>> GetPermissionsAsync() => Task.FromResult(Enumerable.Empty<Permission>());
    public IEnumerable<PermissionStereotype> GetDefaultStereotypes() => [];
}
