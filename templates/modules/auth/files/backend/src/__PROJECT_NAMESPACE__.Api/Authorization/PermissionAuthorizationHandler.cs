using __PROJECT_NAMESPACE__.Application.Auth.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace __PROJECT_NAMESPACE__.Api.Authorization;

public sealed class PermissionAuthorizationHandler
    : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (context.User.HasClaim(
                AuthClaimNames.Permission,
                requirement.Permission)
            && !context.User.HasClaim(
                AuthClaimNames.MustChangePassword,
                bool.TrueString.ToLowerInvariant()))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
