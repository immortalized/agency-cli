namespace __PROJECT_NAMESPACE__.Operations;

public sealed record InitialAdminResult(
    Guid UserId,
    string Username,
    string? Email,
    string TemporaryPassword);