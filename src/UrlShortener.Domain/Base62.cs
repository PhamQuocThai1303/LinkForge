namespace UrlShortener.Domain;

public static class Base62
{
    private const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
    public const long SevenCharacterCapacity = 3_521_614_606_208;

    public static string EncodeSeven(long number)
    {
        if (number < 0 || number >= SevenCharacterCapacity)
        {
            throw new ArgumentOutOfRangeException(nameof(number));
        }

        Span<char> result = stackalloc char[7];
        for (var index = result.Length - 1; index >= 0; index--)
        {
            result[index] = Alphabet[(int)(number % 62)];
            number /= 62;
        }

        return new string(result);
    }

    public static bool TryDecode(string? code, out long number)
    {
        number = 0;
        if (string.IsNullOrEmpty(code) || code.Length > 7)
        {
            return false;
        }

        foreach (var character in code)
        {
            var digit = Alphabet.IndexOf(character);
            if (digit < 0)
            {
                number = 0;
                return false;
            }

            number = checked(number * 62 + digit);
        }

        return true;
    }
}
