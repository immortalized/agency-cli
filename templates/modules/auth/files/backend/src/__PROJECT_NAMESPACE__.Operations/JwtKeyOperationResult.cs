namespace __PROJECT_NAMESPACE__.Operations;

public sealed record JwtKeyOperationResult(
    string ActiveKeyId,
    int ValidationKeyCount);