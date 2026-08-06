namespace __PROJECT_NAMESPACE__.Auth.Tool;

public sealed record JwtKeyOperationResult(
    string ActiveKeyId,
    int ValidationKeyCount);