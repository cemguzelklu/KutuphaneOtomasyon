using System.Linq;

public static class IsbnHelper
{
    public static string OnlyDigits(string s) => new string((s ?? "").Where(char.IsDigit).ToArray());

    public static string NormalizeIsbn13(string s)
    {
        var d = OnlyDigits(s);
        if (d.Length != 13 || !d.StartsWith("97")) return null;
        int sum = 0;
        for (int i = 0; i < 12; i++) sum += (i % 2 == 0 ? 1 : 3) * (d[i] - '0');
        int check = (10 - (sum % 10)) % 10;
        return check == (d[12] - '0') ? d : null;
    }

    public static string ToIsbn10(string isbn13)
    {
        var d = OnlyDigits(isbn13);
        if (d.Length != 13 || !d.StartsWith("978")) return null;
        var core = d.Substring(3, 9);
        int sum = 0;
        for (int i = 0; i < 9; i++) sum += (10 - i) * (core[i] - '0');
        int rem = 11 - (sum % 11);
        char check = rem == 10 ? 'X' : (rem == 11 ? '0' : (char)('0' + rem));
        return core + check;
    }
    public static string? ToIsbn13From10(string isbn10)
    {
        var d = OnlyDigits(isbn10);
        if (d.Length != 10) return null;
        var core9 = d.Substring(0, 9);
        var withPrefix = "978" + core9;
        int sum = 0;
        for (int i = 0; i < 12; i++)
            sum += (i % 2 == 0 ? 1 : 3) * (withPrefix[i] - '0');
        int check = (10 - (sum % 10)) % 10;
        return withPrefix + check;
    }
}
