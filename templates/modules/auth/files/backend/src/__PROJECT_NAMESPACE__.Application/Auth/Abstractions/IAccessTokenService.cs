using __PROJECT_NAMESPACE__.Application.Auth.Models;

namespace __PROJECT_NAMESPACE__.Application.Auth.Abstractions;

public interface IAccessTokenService
{
    AccessTokenResult Create(
        AccessTokenSubject subject);
}