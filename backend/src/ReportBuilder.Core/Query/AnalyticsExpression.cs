using System.Globalization;
using System.Text;
using ReportBuilder.Core.Entities;
using ReportBuilder.Core.Services;

namespace ReportBuilder.Core.Query;

/// <summary>
/// Compiles a restricted analytics expression into SQL Server fragment.
/// Allows column refs, arithmetic, and a small whitelist of scalar/aggregate functions.
/// </summary>
public static class AnalyticsExpression
{
    private static readonly HashSet<string> ScalarFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "YEAR", "MONTH", "DAY", "HOUR", "MINUTE", "SECOND",
        "UPPER", "LOWER", "LTRIM", "RTRIM", "TRIM", "LEFT", "RIGHT", "LEN", "LENGTH",
        "ISNULL", "COALESCE", "CONCAT", "CONCAT_WS", "ABS", "ROUND", "FLOOR", "CEILING",
        "CAST", "TRY_CONVERT", "CONVERT", "NULLIF", "DATEPART", "DATENAME"
    };

    private static readonly HashSet<string> AggregateFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "COUNT", "COUNT_BIG", "SUM", "AVG", "MIN", "MAX"
    };

    private static readonly HashSet<string> DateParts = new(StringComparer.OrdinalIgnoreCase)
    {
        "year", "yy", "yyyy", "quarter", "qq", "q", "month", "mm", "m",
        "dayofyear", "dy", "y", "day", "dd", "d", "week", "wk", "ww",
        "weekday", "dw", "hour", "hh", "minute", "mi", "n", "second", "ss", "s"
    };

    private static readonly HashSet<string> CastTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "int", "bigint", "smallint", "tinyint", "bit",
        "float", "real", "decimal", "numeric", "money",
        "date", "datetime", "datetime2", "time",
        "nvarchar", "varchar", "nchar", "char"
    };

    public static string Compile(
        string expression,
        string tableAlias,
        Dataset dataset,
        bool allowAggregates)
    {
        var tokens = Tokenize(expression);
        if (tokens.Count == 0)
        {
            throw new ReportQueryException("Expression is empty.");
        }

        var columns = dataset.Columns
            .Where(column => !column.IsHidden)
            .Select(column => column.ColumnName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var output = new StringBuilder();
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            switch (token.Kind)
            {
                case TokenKind.Number:
                case TokenKind.String:
                case TokenKind.Operator:
                    output.Append(token.Text);
                    break;
                case TokenKind.Identifier:
                    if (token.Text.Equals("AS", StringComparison.OrdinalIgnoreCase)
                        || token.Text.Equals("DISTINCT", StringComparison.OrdinalIgnoreCase))
                    {
                        output.Append(token.Text.Equals("AS", StringComparison.OrdinalIgnoreCase) ? " AS " : " DISTINCT ");
                        break;
                    }

                    if (i + 1 < tokens.Count && tokens[i + 1].Kind == TokenKind.Operator && tokens[i + 1].Text == "(")
                    {
                        var name = token.Text.ToUpperInvariant();
                        if (name == "LENGTH")
                        {
                            name = "LEN";
                        }

                        if (AggregateFunctions.Contains(name))
                        {
                            if (!allowAggregates)
                            {
                                throw new ReportQueryException(
                                    $"Aggregate '{token.Text}' is not allowed in this expression.");
                            }

                            output.Append(name);
                            break;
                        }

                        if (!ScalarFunctions.Contains(name))
                        {
                            throw new ReportQueryException($"Function '{token.Text}' is not allowed.");
                        }

                        output.Append(name);
                        break;
                    }

                    // DATEPART / DATENAME first arg is a keyword date part, not a column.
                    if (i > 0
                        && tokens[i - 1].Kind == TokenKind.Operator
                        && tokens[i - 1].Text == "("
                        && i >= 2
                        && tokens[i - 2].Kind == TokenKind.Identifier
                        && (tokens[i - 2].Text.Equals("DATEPART", StringComparison.OrdinalIgnoreCase)
                            || tokens[i - 2].Text.Equals("DATENAME", StringComparison.OrdinalIgnoreCase))
                        && DateParts.Contains(token.Text))
                    {
                        output.Append(token.Text);
                        break;
                    }

                    // CAST(... AS type) / CONVERT(type, ...) type names.
                    if (CastTypes.Contains(token.Text)
                        && LooksLikeTypeContext(tokens, i))
                    {
                        output.Append(NormalizeCastType(token.Text, tokens, i));
                        break;
                    }

                    if (!columns.TryGetValue(token.Text, out var columnName)
                        && !TryMatchBracketColumn(token.Text, columns, out columnName))
                    {
                        throw new ReportQueryException(
                            $"Unknown column '{token.Text}' on {dataset.SchemaName}.{dataset.ObjectName}.");
                    }

                    output.Append(tableAlias).Append('.').Append(SqlIdentifiers.Quote(columnName));
                    break;
                default:
                    throw new ReportQueryException("Invalid expression token.");
            }
        }

        return output.ToString();
    }

    public static void Validate(
        string expression,
        Dataset dataset,
        bool allowAggregates)
    {
        _ = Compile(expression, "t", dataset, allowAggregates);
    }

    private static bool LooksLikeTypeContext(IReadOnlyList<Token> tokens, int index)
    {
        // CONVERT(type, ...) — type immediately after '(' of CONVERT/TRY_CONVERT/CAST
        if (index > 0
            && tokens[index - 1].Kind == TokenKind.Operator
            && tokens[index - 1].Text == "("
            && index >= 2
            && tokens[index - 2].Kind == TokenKind.Identifier
            && tokens[index - 2].Text is var fn
            && (fn.Equals("CONVERT", StringComparison.OrdinalIgnoreCase)
                || fn.Equals("TRY_CONVERT", StringComparison.OrdinalIgnoreCase)
                || fn.Equals("CAST", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // CAST(x AS type)
        if (index > 0
            && tokens[index - 1].Kind == TokenKind.Identifier
            && tokens[index - 1].Text.Equals("AS", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static string NormalizeCastType(string typeName, IReadOnlyList<Token> tokens, int index)
    {
        // Allow nvarchar(4000) style — size already emitted as separate tokens.
        _ = tokens;
        _ = index;
        return typeName.ToLowerInvariant() switch
        {
            "length" => "int",
            _ => typeName
        };
    }

    private static bool TryMatchBracketColumn(
        string text,
        HashSet<string> columns,
        out string columnName)
    {
        columnName = text;
        if (text.Length >= 2 && text[0] == '[' && text[^1] == ']')
        {
            var inner = text[1..^1].Replace("]]", "]");
            if (columns.TryGetValue(inner, out columnName!))
            {
                return true;
            }
        }

        return false;
    }

    private static List<Token> Tokenize(string expression)
    {
        var raw = expression?.Trim() ?? string.Empty;
        if (raw.Length == 0)
        {
            return [];
        }

        if (raw.Contains(';') || raw.Contains("--") || raw.Contains("/*") || raw.Contains("*/"))
        {
            throw new ReportQueryException("Expression contains forbidden characters.");
        }

        var tokens = new List<Token>();
        var i = 0;
        while (i < raw.Length)
        {
            var ch = raw[i];
            if (char.IsWhiteSpace(ch))
            {
                i++;
                continue;
            }

            if (ch is '+' or '-' or '*' or '/' or '%' or '(' or ')' or ',' or '.')
            {
                tokens.Add(new Token(TokenKind.Operator, ch.ToString()));
                i++;
                continue;
            }

            if (ch == '\'')
            {
                var end = i + 1;
                var sb = new StringBuilder().Append('\'');
                while (end < raw.Length)
                {
                    if (raw[end] == '\'')
                    {
                        sb.Append('\'');
                        if (end + 1 < raw.Length && raw[end + 1] == '\'')
                        {
                            sb.Append('\'');
                            end += 2;
                            continue;
                        }

                        end++;
                        break;
                    }

                    sb.Append(raw[end]);
                    end++;
                }

                if (sb[^1] != '\'')
                {
                    throw new ReportQueryException("Unterminated string literal in expression.");
                }

                tokens.Add(new Token(TokenKind.String, sb.ToString()));
                i = end;
                continue;
            }

            if (ch == '[')
            {
                var end = i + 1;
                var sb = new StringBuilder().Append('[');
                while (end < raw.Length)
                {
                    if (raw[end] == ']')
                    {
                        sb.Append(']');
                        if (end + 1 < raw.Length && raw[end + 1] == ']')
                        {
                            sb.Append(']');
                            end += 2;
                            continue;
                        }

                        end++;
                        break;
                    }

                    sb.Append(raw[end]);
                    end++;
                }

                if (sb[^1] != ']')
                {
                    throw new ReportQueryException("Unterminated [column] in expression.");
                }

                tokens.Add(new Token(TokenKind.Identifier, sb.ToString()));
                i = end;
                continue;
            }

            if (char.IsDigit(ch))
            {
                var end = i + 1;
                while (end < raw.Length && (char.IsDigit(raw[end]) || raw[end] == '.'))
                {
                    end++;
                }

                tokens.Add(new Token(TokenKind.Number, raw[i..end]));
                i = end;
                continue;
            }

            if (char.IsLetter(ch) || ch == '_')
            {
                var end = i + 1;
                while (end < raw.Length && (char.IsLetterOrDigit(raw[end]) || raw[end] == '_'))
                {
                    end++;
                }

                var ident = raw[i..end];
                // Treat AS as operator-like keyword for CAST.
                if (ident.Equals("AS", StringComparison.OrdinalIgnoreCase)
                    || ident.Equals("DISTINCT", StringComparison.OrdinalIgnoreCase))
                {
                    tokens.Add(new Token(TokenKind.Identifier, ident));
                }
                else
                {
                    tokens.Add(new Token(TokenKind.Identifier, ident));
                }

                i = end;
                continue;
            }

            throw new ReportQueryException(
                $"Unexpected character '{ch}' in expression at position {i.ToString(CultureInfo.InvariantCulture)}.");
        }

        return tokens;
    }

    private enum TokenKind
    {
        Identifier,
        Number,
        String,
        Operator
    }

    private readonly record struct Token(TokenKind Kind, string Text);
}
