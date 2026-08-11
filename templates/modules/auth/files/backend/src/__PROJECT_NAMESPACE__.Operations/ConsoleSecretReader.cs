using System.Security.Cryptography;
using System.Text;

namespace __PROJECT_NAMESPACE__.Operations;

public static class ConsoleSecretReader
{
    public static byte[] ReadRequired(string prompt)
    {
        if (Console.IsInputRedirected)
        {
            throw new InvalidOperationException(
                "Secret input requires an interactive terminal and is never accepted through stdin redirection, arguments, or environment variables.");
        }

        Console.Write(prompt);
        var buffer = new List<char>();

        try
        {
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    break;
                }

                if (key.Key == ConsoleKey.Backspace)
                {
                    if (buffer.Count > 0)
                    {
                        buffer.RemoveAt(buffer.Count - 1);
                    }
                    continue;
                }

                if (!char.IsControl(key.KeyChar))
                {
                    buffer.Add(key.KeyChar);
                }
            }

            if (buffer.Count == 0)
            {
                throw new InvalidOperationException("A non-empty secret is required.");
            }

            return Encoding.UTF8.GetBytes(buffer.ToArray());
        }
        finally
        {
            CollectionsMarshalZero(buffer);
        }
    }

    private static void CollectionsMarshalZero(List<char> value)
    {
        for (var index = 0; index < value.Count; index++)
        {
            value[index] = '\0';
        }
        value.Clear();
    }
}
