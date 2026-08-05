namespace __PROJECT_NAMESPACE__.Application.Auth.Models;

public enum PasswordVerificationResult
{
    Failed = 0,
    Success = 1,
    SuccessRehashNeeded = 2
}