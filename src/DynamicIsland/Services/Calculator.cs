using System.Globalization;
using System.Text.RegularExpressions;

namespace DynamicIsland.Services;

/// <summary>
/// Petite calculatrice de la palette : + − × ÷ ^, parenthèses, virgule décimale, racine(…), pi et les
/// pourcentages (« 15% de 80 » = 12, « 80 + 15% » = 92, « 200 × 5% » = 10).
/// </summary>
public static partial class Calculator
{
    

    [GeneratedRegex(@"^(?<a>.+?)\s*%\s*(?:de|d'|du|des|of|sur)\s*(?<b>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex PercentOf();

    [GeneratedRegex(@"(?<=[\d)])\s*[xX×]\s*(?=[\d(])")]
    private static partial Regex TimesSign();

    [GeneratedRegex(@"(?<=\d),(?=\d)")]
    private static partial Regex DecimalComma();

    [GeneratedRegex(@"[+\-*/^%×÷]|racine|sqrt|(?<=\d)\s*[xX]\s*\d")]
    private static partial Regex HasOperator();

    /// <summary>Vrai si le texte est un calcul (un nombre seul n'en est pas un).</summary>
    public static bool TryEvaluate(string text, out double result)
    {
        result = 0;
        var input = text.Trim().TrimEnd('=').Trim();
        if (input.Length == 0 || !input.Any(char.IsDigit) || !HasOperator().IsMatch(input)) return false;

        var percentOf = PercentOf().Match(input);
        if (percentOf.Success)
        {
            if (!TryCompute(percentOf.Groups["a"].Value, out var part) || !TryCompute(percentOf.Groups["b"].Value, out var whole)) return false;
            result = part / 100 * whole;
        }
        else if (!TryCompute(input, out result))
        {
            return false;
        }
        return double.IsFinite(result);
    }

    /// <summary>« 1 234,5 » (espaces fines entre les milliers).</summary>
    public static string Format(double value) =>
        Math.Abs(value) >= 1e15 || (value != 0 && Math.Abs(value) < 1e-9)
            ? value.ToString("G10", Loc.Culture)
            : Math.Round(value, 10).ToString("#,##0.##########", Loc.Culture);

    /// <summary>« 1234,5 » : pour le presse-papiers.</summary>
    public static string FormatPlain(double value) => Math.Round(value, 10).ToString("0.##########", Loc.Culture);

    private static bool TryCompute(string text, out double result)
    {
        var normalized = DecimalComma().Replace(TimesSign().Replace(text.ToLowerInvariant(), "*"), ".")
            .Replace('×', '*').Replace('÷', '/').Replace('−', '-').Replace(" ", "").Replace(" ", "").Replace(" ", "");
        try
        {
            result = new Parser(normalized).Parse();
            return true;
        }
        catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException)
        {
            result = 0;
            return false;
        }
    }

    /// <summary>Analyse récursive classique ; un nombre suivi de « % » vaut n/100 et, après + ou −, s'applique au total.</summary>
    private sealed class Parser(string text)
    {
        private int _position;

        public double Parse()
        {
            double value = Expression();
            if (_position != text.Length) throw new FormatException();
            return value;
        }

        private char Peek => _position < text.Length ? text[_position] : '\0';

        private double Expression()
        {
            double value = Term().Value;
            while (Peek is '+' or '-')
            {
                char op = text[_position++];
                var (right, percent) = Term();
                if (percent) right *= value; // 80 + 15 % = 80 + 80 × 0,15
                value = op == '+' ? value + right : value - right;
            }
            return value;
        }

        private (double Value, bool Percent) Term()
        {
            var (value, percent) = Power();
            while (Peek is '*' or '/')
            {
                char op = text[_position++];
                double right = Power().Value;
                value = op == '*' ? value * right : value / right;
                percent = false;
            }
            return (value, percent);
        }

        private (double Value, bool Percent) Power()
        {
            var (value, percent) = Unary();
            if (Peek != '^') return (value, percent);
            _position++;
            return (Math.Pow(value, Power().Value), false);
        }

        private (double Value, bool Percent) Unary()
        {
            if (Peek == '-')
            {
                _position++;
                var (value, percent) = Unary();
                return (-value, percent);
            }
            if (Peek == '+') _position++;
            return Primary();
        }

        private (double Value, bool Percent) Primary()
        {
            double value;
            if (Peek == '(')
            {
                _position++;
                value = Expression();
                Expect(')');
            }
            else if (char.IsLetter(Peek))
            {
                int start = _position;
                while (char.IsLetter(Peek)) _position++;
                switch (text[start.._position])
                {
                    case "pi":
                        value = Math.PI;
                        break;
                    case "racine" or "sqrt" or "rac":
                        Expect('(');
                        value = Math.Sqrt(Expression());
                        Expect(')');
                        break;
                    default:
                        throw new FormatException();
                }
            }
            else
            {
                int start = _position;
                while (char.IsDigit(Peek) || Peek == '.') _position++;
                if (!double.TryParse(text[start.._position], NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                    throw new FormatException();
            }

            if (Peek != '%') return (value, false);
            _position++;
            return (value / 100, true);
        }

        private void Expect(char expected)
        {
            if (Peek != expected) throw new FormatException();
            _position++;
        }
    }
}
