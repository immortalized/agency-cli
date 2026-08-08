using __PROJECT_NAMESPACE__.Application.Auth.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace __PROJECT_NAMESPACE__.Api.Authorization;

[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Method,
    AllowMultiple = true,
    Inherited = true)]
public sealed class HasPermissionAttribute
    : AuthorizeAttribute
{
    public HasPermissionAttribute(string permission)
    {
        Policy = AuthPolicies.ForPermission(permission);
    }
}
