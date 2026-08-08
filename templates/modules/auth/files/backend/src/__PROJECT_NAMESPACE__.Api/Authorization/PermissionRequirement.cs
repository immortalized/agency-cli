using Microsoft.AspNetCore.Authorization;

namespace __PROJECT_NAMESPACE__.Api.Authorization;

public sealed record PermissionRequirement(string Permission)
    : IAuthorizationRequirement;
