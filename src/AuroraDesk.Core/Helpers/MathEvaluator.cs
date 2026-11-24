using System;
using System.Globalization;

namespace AuroraDesk.Core.Helpers;

/// <summary>
/// Lightweight, safe recursive-descent arithmetic evaluator for quick launcher calculations.
/// Supports +, -, *, /, %, ^, and parentheses.
/// </summary>
public static class MathEvaluator
{
    public static bool TryEvaluate(string expr, out double result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(expr)) return false;

        bool hasOperator = false;
        foreach (char c in expr)
        {
            if (c is '+' or '-' or '*' or '/' or '%' or '^') { hasOperator = true; break; }
        }
        if (!hasOperator) return false;

        try
        {
            var parser = new Parser(expr);
            result = parser.ParseExpression();
            parser.SkipWhitespace();
            if (!parser.IsAtEnd || double.IsNaN(result) || double.IsInfinity(result)) return false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed class Parser(string input)
    {
        private int _pos;

        public bool IsAtEnd => _pos >= input.Length;

        public void SkipWhitespace()
        {
            while (_pos < input.Length && char.IsWhiteSpace(input[_pos])) _pos++;
        }

        public double ParseExpression()
        {
            double val = ParseTerm();
            while (true)
            {
                SkipWhitespace();
                if (_pos >= input.Length) break;
                char op = input[_pos];
                if (op == '+') { _pos++; val += ParseTerm(); }
                else if (op == '-') { _pos++; val -= ParseTerm(); }
                else break;
            }
            return val;
        }

        private double ParseTerm()
        {
            double val = ParsePower();
            while (true)
            {
                SkipWhitespace();
                if (_pos >= input.Length) break;
                char op = input[_pos];
                if (op == '*') { _pos++; val *= ParsePower(); }
                else if (op == '/')
                {
                    _pos++;
                    double d = ParsePower();
                    if (Math.Abs(d) < 1e-15) throw new DivideByZeroException();
                    val /= d;
                }
                else if (op == '%') { _pos++; val %= ParsePower(); }
                else break;
            }
            return val;
        }

        private double ParsePower()
        {
            double val = ParseUnary();
            SkipWhitespace();
            if (_pos < input.Length && input[_pos] == '^')
            {
                _pos++;
                val = Math.Pow(val, ParsePower());
            }
            return val;
        }

        private double ParseUnary()
        {
            SkipWhitespace();
            if (_pos < input.Length && input[_pos] == '+') { _pos++; return ParseUnary(); }
            if (_pos < input.Length && input[_pos] == '-') { _pos++; return -ParseUnary(); }
            return ParsePrimary();
        }

        private double ParsePrimary()
        {
            SkipWhitespace();
            if (_pos < input.Length && input[_pos] == '(')
            {
                _pos++;
                double val = ParseExpression();
                SkipWhitespace();
                if (_pos >= input.Length || input[_pos] != ')') throw new FormatException();
                _pos++;
                return val;
            }

            int start = _pos;
            bool hasDot = false;
            while (_pos < input.Length && (char.IsDigit(input[_pos]) || (input[_pos] == '.' && !hasDot)))
            {
                if (input[_pos] == '.') hasDot = true;
                _pos++;
            }
            if (_pos == start) throw new FormatException();
            return double.Parse(input[start.._pos], CultureInfo.InvariantCulture);
        }
    }
}
