namespace __PROJECT_NAMESPACE__.Api.Contracts.Auth;

public sealed record AdminCreateUserResponse(
    AdminUserResponse User,
    string TemporaryPassword);
