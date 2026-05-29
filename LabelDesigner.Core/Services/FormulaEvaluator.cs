using System.Globalization;
using System.Text;

namespace LabelDesigner.Core.Services;

/// <summary>
/// Small, dependency-free expression evaluator for Formula data sources. Supports:
///   {Field}                references another field/source
///   "literal"              string literal (\" escapes)
///   123.45                 number
///   &amp;                  string concatenation
///   + - * /                numeric arithmetic (operands parsed as numbers; non-numeric → 0)
///   ( )                    grouping
///   UPPER LOWER TRIM LEN CONCAT LEFT(s,n) RIGHT(s,n) MID(s,start,len) ROUND(n,digits) ABS(n)
/// Precedence: &amp; (lowest) → + - → * / → unary. Returns a string and NEVER throws — a malformed
/// formula yields "" (a visible blank in preview) rather than crashing a print run.
/// </summary>
public static class FormulaEvaluator
{
    public static string Evaluate(string? expression, IReadOnlyDictionary<string, string> fields)
    {
        if (string.IsNullOrWhiteSpace(expression)) return "";
        try
        {
            var p = new Parser(expression, fields);
            var result = p.ParseExpression();
            p.ExpectEnd();
            return result;
        }
        catch { return ""; }
    }

    private sealed class Parser
    {
        private readonly string _s;
        private readonly IReadOnlyDictionary<string, string> _fields;
        private int _i;

        public Parser(string s, IReadOnlyDictionary<string, string> fields) { _s = s; _fields = fields; }

        public void ExpectEnd() { SkipWs(); if (_i < _s.Length) throw new FormatException(); }

        public string ParseExpression() => ParseConcat();

        private string ParseConcat()
        {
            var left = ParseAdditive();
            while (Peek() == '&') { _i++; left += ParseAdditive(); }
            return left;
        }

        private string ParseAdditive()
        {
            var left = ParseMul();
            while (Peek() is '+' or '-')
            {
                char op = _s[_i++];
                double a = Num(left), b = Num(ParseMul());
                left = NumStr(op == '+' ? a + b : a - b);
            }
            return left;
        }

        private string ParseMul()
        {
            var left = ParseUnary();
            while (Peek() is '*' or '/')
            {
                char op = _s[_i++];
                double a = Num(left), b = Num(ParseUnary());
                left = NumStr(op == '*' ? a * b : (b == 0 ? 0 : a / b));
            }
            return left;
        }

        private string ParseUnary()
        {
            if (Peek() == '-') { _i++; return NumStr(-Num(ParseUnary())); }
            return ParsePrimary();
        }

        private string ParsePrimary()
        {
            char c = Peek();
            if (c == '(') { _i++; var v = ParseExpression(); Expect(')'); return v; }
            if (c == '"') return ParseString();
            if (c == '{') return ParseField();
            if (char.IsDigit(c) || c == '.') return ParseNumber();
            if (char.IsLetter(c)) return ParseFunction();
            throw new FormatException();
        }

        private string ParseString()
        {
            _i++; // opening quote
            var sb = new StringBuilder();
            while (_i < _s.Length && _s[_i] != '"')
            {
                if (_s[_i] == '\\' && _i + 1 < _s.Length) { _i++; sb.Append(_s[_i]); }
                else sb.Append(_s[_i]);
                _i++;
            }
            Expect('"');
            return sb.ToString();
        }

        private string ParseField()
        {
            _i++; // opening brace
            var sb = new StringBuilder();
            while (_i < _s.Length && _s[_i] != '}') sb.Append(_s[_i++]);
            Expect('}');
            return _fields.TryGetValue(sb.ToString().Trim(), out var v) ? v ?? "" : "";
        }

        private string ParseNumber()
        {
            int start = _i;
            while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.')) _i++;
            return _s.Substring(start, _i - start);
        }

        private string ParseFunction()
        {
            int start = _i;
            while (_i < _s.Length && char.IsLetterOrDigit(_s[_i])) _i++;
            string name = _s.Substring(start, _i - start).ToUpperInvariant();

            var args = new List<string>();
            if (Peek() == '(')
            {
                _i++;
                if (Peek() != ')')
                {
                    args.Add(ParseExpression());
                    while (Peek() == ',') { _i++; args.Add(ParseExpression()); }
                }
                Expect(')');
            }
            return Apply(name, args);
        }

        private static string Apply(string name, List<string> a)
        {
            string A(int i) => i < a.Count ? a[i] : "";
            switch (name)
            {
                case "UPPER":  return A(0).ToUpperInvariant();
                case "LOWER":  return A(0).ToLowerInvariant();
                case "TRIM":   return A(0).Trim();
                case "LEN":    return A(0).Length.ToString(CultureInfo.InvariantCulture);
                case "CONCAT": return string.Concat(a);
                case "LEFT":   { var s = A(0); int n = Math.Clamp((int)Num(A(1)), 0, s.Length); return s[..n]; }
                case "RIGHT":  { var s = A(0); int n = Math.Clamp((int)Num(A(1)), 0, s.Length); return s[(s.Length - n)..]; }
                case "MID":
                {
                    var s = A(0);
                    int st = Math.Clamp((int)Num(A(1)), 1, s.Length + 1);
                    int len = Math.Clamp((int)Num(A(2)), 0, s.Length - (st - 1));
                    return s.Substring(st - 1, len);
                }
                case "ROUND":  return NumStr(Math.Round(Num(A(0)), Math.Clamp((int)Num(A(1)), 0, 15)));
                case "ABS":    return NumStr(Math.Abs(Num(A(0))));
                default:       return "";
            }
        }

        private static double Num(string s) =>
            double.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out var d) ||
            double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out d) ? d : 0;

        private static string NumStr(double d) => d.ToString("0.############", CultureInfo.CurrentCulture);

        private char Peek() { SkipWs(); return _i < _s.Length ? _s[_i] : '\0'; }
        private void SkipWs() { while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++; }
        private void Expect(char c) { SkipWs(); if (_i >= _s.Length || _s[_i] != c) throw new FormatException(); _i++; }
    }
}
