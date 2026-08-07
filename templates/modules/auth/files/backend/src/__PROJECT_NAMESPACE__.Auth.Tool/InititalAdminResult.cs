namespace __PROJECT_NAMESPACE__.Auth.Tool;

public sealed record InitialAdminResult(
    Guid UserId,
    string Username,
    string? Email,
    string TemporaryPassword);