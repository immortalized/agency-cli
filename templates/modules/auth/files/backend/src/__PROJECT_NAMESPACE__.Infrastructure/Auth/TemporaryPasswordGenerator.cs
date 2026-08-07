using System.Security.Cryptography;
using __PROJECT_NAMESPACE__.Application.Auth.Abstractions;

namespace __PROJECT_NAMESPACE__.Infrastructure.Auth;

public sealed class TemporaryPasswordGenerator
    : ITemporaryPasswordGenerator
{
    private const int PasswordLength = 32;

    private const string UppercaseCharacters =
        "ABCDEFGHJKLMNPQRSTUVWXYZ";

    private const string LowercaseCharacters =
        "abcdefghijkmnopqrstuvwxyz";

    private const string DigitCharacters =
        "23456789";

    private const string SymbolCharacters =
        "!@#$%*-_=+?";

    private const string AllCharacters =
        UppercaseCharacters
        + LowercaseCharacters
        + DigitCharacters
        + SymbolCharacters;

    public string Generate()
    {
        Span<char> password =
            stackalloc char[PasswordLength];

        password[0] =
            GetRandomCharacter(
                UppercaseCharacters);

        password[1] =
            GetRandomCharacter(
                LowercaseCharacters);

        password[2] =
            GetRandomCharacter(
                DigitCharacters);

        password[3] =
            GetRandomCharacter(
                SymbolCharacters);

        for (
            var index = 4;
            index < password.Length;
            index++)
        {
            password[index] =
                GetRandomCharacter(
                    AllCharacters);
        }

        Shuffle(password);

        return new string(password);
    }

    private static char GetRandomCharacter(
        string characters)
    {
        var index =
            RandomNumberGenerator.GetInt32(
                characters.Length);

        return characters[index];
    }

    private static void Shuffle(
        Span<char> characters)
    {
        for (
            var index =
                characters.Length - 1;
            index > 0;
            index--)
        {
            var swapIndex =
                RandomNumberGenerator.GetInt32(
                    index + 1);

            (
                characters[index],
                characters[swapIndex]
            ) = (
                characters[swapIndex],
                characters[index]
            );
        }
    }
}