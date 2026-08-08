namespace __PROJECT_NAMESPACE__.Api.Contracts.Auth;

public sealed record AdminResetPasswordResponse(
    string TemporaryPassword,
    bool MustChangePassword);
