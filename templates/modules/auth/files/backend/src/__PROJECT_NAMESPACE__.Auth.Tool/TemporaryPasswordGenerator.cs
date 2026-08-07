using System.Security.Cryptography;

namespace __PROJECT_NAMESPACE__.Auth.Tool;

public static class TemporaryPasswordGenerator
{
    private const int PasswordLength = 32;

    private const string Uppercase =
        "ABCDEFGHJKLMNPQRSTUVWXYZ";

    private const string Lowercase =
        "abcdefghijkmnopqrstuvwxyz";

    private const string Digits =
        "23456789";

    private const string Symbols =
        "!@#$%*-_=+?";

    private const string AllCharacters =
        Uppercase + Lowercase + Digits + Symbols;

    public static string Generate()
    {
        Span<char> characters =
            stackalloc char[PasswordLength];

        characters[0] =
            GetRandomCharacter(Uppercase);

        characters[1] =
            GetRandomCharacter(Lowercase);

        characters[2] =
            GetRandomCharacter(Digits);

        characters[3] =
            GetRandomCharacter(Symbols);

        for (var index = 4;
             index < characters.Length;
             index++)
        {
            characters[index] =
                GetRandomCharacter(AllCharacters);
        }

        Shuffle(characters);

        return new string(characters);
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
        for (var index = characters.Length - 1;
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