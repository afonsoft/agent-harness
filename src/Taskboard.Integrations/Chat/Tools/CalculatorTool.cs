using System.Globalization;
using System.Text.Json;
using Taskboard.Application.Contracts.Chat;

namespace Taskboard.Integrations.Chat.Tools;

/// <summary>
/// Evaluates arithmetic expressions — the open-webui calculator utility
/// (SPEC-20261001-ai-chat-openwebui). A small recursive-descent parser: + - * /
/// % ^ unary minus, parentheses, functions (sqrt, abs, min, max, round, floor,
/// ceil, pow, exp, log, ln, log10) and constants (pi, e). No code execution —
/// the evaluator never touches the process, the filesystem or the network.
/// </summary>
public sealed class CalculatorTool : IChatTool
{
    internal const int MaxExpressionChars = 2000;

    public string Name => "calculator";
    public string Description =>
        "Evaluate a math expression precisely (e.g. \"2*(3+4)^2\", \"sqrt(144)+round(3.7)\"). "
        + "Prefer this over mental arithmetic for non-trivial numbers.";
    public string ParametersJson => """
        {"type":"object","properties":{"expression":{"type":"string","description":"Arithmetic expression: + - * / % ^ parentheses, functions sqrt/abs/min/max/round/floor/ceil/pow/exp/log/ln/log10, constants pi/e"}},"required":["expression"]}
        """;

    public Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var expression = arguments.TryGetProperty("expression", out var e) && e.ValueKind == JsonValueKind.String
            ? e.GetString() ?? string.Empty
            : string.Empty;
        if (string.IsNullOrWhiteSpace(expression))
        {
            return Task.FromResult(new ChatToolResult(
                JsonSerializer.Serialize(new { error = "expression is required" }), Refused: true, "empty expression"));
        }

        if (expression.Length > MaxExpressionChars)
        {
            return Task.FromResult(new ChatToolResult(
                JsonSerializer.Serialize(new { error = $"expression too long (max {MaxExpressionChars} chars)" }),
                Refused: true, "expression too long"));
        }

        try
        {
            var parser = new Parser(expression);
            var result = parser.ParseExpression();
            parser.SkipWhitespace();
            if (!parser.AtEnd)
            {
                return Task.FromResult(new ChatToolResult(JsonSerializer.Serialize(new
                {
                    error = $"unexpected input at position {parser.Position}: '{expression[parser.Position..]}'",
                })));
            }

            if (double.IsNaN(result) || double.IsInfinity(result))
            {
                return Task.FromResult(new ChatToolResult(JsonSerializer.Serialize(new
                {
                    expression,
                    error = "result is not a finite number",
                })));
            }

            return Task.FromResult(new ChatToolResult(JsonSerializer.Serialize(new
            {
                expression,
                result,
                formatted = result.ToString("G17", CultureInfo.InvariantCulture),
            })));
        }
        catch (FormatException ex)
        {
            return Task.FromResult(new ChatToolResult(JsonSerializer.Serialize(new
            {
                expression,
                error = ex.Message,
            })));
        }
    }

    /// <summary>Tiny recursive-descent evaluator — no eval, no process.</summary>
    private sealed class Parser(string input)
    {
        private int _position;

        public int Position => _position;
        public bool AtEnd => _position >= input.Length;

        public void SkipWhitespace()
        {
            while (!AtEnd && char.IsWhiteSpace(input[_position]))
            {
                _position++;
            }
        }

        public double ParseExpression() => ParseAddSub();

        private double ParseAddSub()
        {
            var left = ParseMulDiv();
            while (true)
            {
                SkipWhitespace();
                if (Match('+'))
                {
                    left += ParseMulDiv();
                }
                else if (Match('-'))
                {
                    left -= ParseMulDiv();
                }
                else
                {
                    return left;
                }
            }
        }

        private double ParseMulDiv()
        {
            var left = ParseUnary();
            while (true)
            {
                SkipWhitespace();
                if (Match('*'))
                {
                    left *= ParseUnary();
                }
                else if (Match('/'))
                {
                    left /= ParseUnary();
                }
                else if (Match('%'))
                {
                    left %= ParseUnary();
                }
                else
                {
                    return left;
                }
            }
        }

        private double ParseUnary()
        {
            SkipWhitespace();
            if (Match('-'))
            {
                return -ParseUnary();
            }

            if (Match('+'))
            {
                return ParseUnary();
            }

            return ParsePower();
        }

        private double ParsePower()
        {
            var baseValue = ParsePrimary();
            SkipWhitespace();
            if (Match('^'))
            {
                // Right-associative.
                return Math.Pow(baseValue, ParseUnary());
            }

            return baseValue;
        }

        private double ParsePrimary()
        {
            SkipWhitespace();
            if (AtEnd)
            {
                throw new FormatException("unexpected end of expression");
            }

            if (Match('('))
            {
                var inner = ParseExpression();
                SkipWhitespace();
                if (!Match(')'))
                {
                    throw new FormatException("missing closing parenthesis");
                }

                return inner;
            }

            if (char.IsDigit(input[_position]) || input[_position] == '.')
            {
                return ParseNumber();
            }

            if (char.IsLetter(input[_position]) || input[_position] == '_')
            {
                return ParseIdentifier();
            }

            throw new FormatException($"unexpected character '{input[_position]}' at position {_position}");
        }

        private double ParseNumber()
        {
            var start = _position;
            while (!AtEnd && (char.IsDigit(input[_position]) || input[_position] == '.'
                || ((input[_position] is 'e' or 'E')
                    && _position > start
                    && (char.IsDigit(input[_position - 1]) || input[_position - 1] == '.'))))
            {
                _position++;
                if (!AtEnd && (input[_position] is '+' or '-')
                    && input[_position - 1] is 'e' or 'E')
                {
                    _position++;
                }
            }

            var token = input[start.._position];
            return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : throw new FormatException($"invalid number '{token}'");
        }

        private double ParseIdentifier()
        {
            var start = _position;
            while (!AtEnd && (char.IsLetterOrDigit(input[_position]) || input[_position] == '_'))
            {
                _position++;
            }

            var name = input[start.._position].ToLowerInvariant();
            switch (name)
            {
                case "pi":
                    return Math.PI;
                case "e":
                    return Math.E;
            }

            SkipWhitespace();
            var args = ParseArguments();

            return name switch
            {
                "sqrt" => Require(args, 1, name, Math.Sqrt),
                "abs" => Require(args, 1, name, Math.Abs),
                "round" => Require(args, 1, name, Math.Round),
                "floor" => Require(args, 1, name, Math.Floor),
                "ceil" => Require(args, 1, name, Math.Ceiling),
                "exp" => Require(args, 1, name, Math.Exp),
                "ln" => Require(args, 1, name, Math.Log),
                "log" => Require(args, 1, name, Math.Log10),
                "log10" => Require(args, 1, name, Math.Log10),
                "pow" => Require2(args, name, Math.Pow),
                "min" => args.Count >= 1 ? args.Min() : throw new FormatException("min needs arguments"),
                "max" => args.Count >= 1 ? args.Max() : throw new FormatException("max needs arguments"),
                _ => throw new FormatException($"unknown function or constant '{name}'"),
            };
        }

        private List<double> ParseArguments()
        {
            var args = new List<double>();
            if (!Match('('))
            {
                return args;
            }

            while (true)
            {
                args.Add(ParseExpression());
                SkipWhitespace();
                if (Match(','))
                {
                    continue;
                }

                if (!Match(')'))
                {
                    throw new FormatException("missing closing parenthesis");
                }

                break;
            }

            return args;
        }

        private static double Require(List<double> args, int count, string name, Func<double, double> fn) =>
            args.Count == count ? fn(args[0]) : throw new FormatException($"{name} expects {count} argument(s)");

        private static double Require2(List<double> args, string name, Func<double, double, double> fn) =>
            args.Count == 2 ? fn(args[0], args[1]) : throw new FormatException($"{name} expects 2 arguments");

        private bool Match(char expected)
        {
            if (!AtEnd && input[_position] == expected)
            {
                _position++;
                return true;
            }

            return false;
        }
    }
}
