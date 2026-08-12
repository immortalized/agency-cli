using __PROJECT_NAMESPACE__.Api.Authorization;
using __PROJECT_NAMESPACE__.Api.Contracts.Roles;
using __PROJECT_NAMESPACE__.Application.Auth.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace __PROJECT_NAMESPACE__.Api.Controllers;

/// <summary>
/// Exposes the permission keys every installed module registers, so an admin
/// UI can build a role-editing form without hardcoding permission names.
/// </summary>
[ApiController]
[Route("api/admin/permissions")]
public sealed class AdminPermissionsController(
    PermissionCatalog permissionCatalog)
    : ControllerBase
{
    [HttpGet]
    [HasPermission(AuthPermissions.RolesRead)]
    public ActionResult<IReadOnlyList<PermissionResponse>> GetAll() =>
        Ok(permissionCatalog.Definitions
            .Select(definition => new PermissionResponse(
                definition.Name,
                definition.Module,
                definition.Description))
            .ToArray());
}
