using System.Globalization;
using System.Text.RegularExpressions;
using PbForMac.Models;

namespace PbForMac.Services;

/// <summary>
/// Разбор упрощённого DAX: агрегаты, [Мера], арифметика, DIVIDE, COUNTROWS,
/// CALCULATE с KEEPFILTERS / фильтром-заменой / ALL|REMOVEFILTERS.
/// </summary>
public static partial class MeasureParser
{
    public static MeasureExpr Parse(string expression)
    {
        var text = Normalize(expression);
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Пустое выражение меры.");
        var (expr, rest) = ParseAdd(text, 0);
        SkipWs(rest, out var i);
        if (i < rest.Length)
            throw new InvalidOperationException($"Лишний текст в выражении: «{rest[i..]}».");
        return expr;
    }

    private static string Normalize(string expression) =>
        expression.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();

    private static (MeasureExpr Expr, string Remaining) ParseAdd(string text, int start)
    {
        var (left, rest) = ParseMul(text, start);
        while (true)
        {
            SkipWs(rest.AsSpan(), out var ws);
            rest = rest[ws..];
            if (rest.Length == 0)
                return (left, rest);
            MeasureBinaryOp op;
            if (rest[0] == '+') op = MeasureBinaryOp.Add;
            else if (rest[0] == '-') op = MeasureBinaryOp.Subtract;
            else return (left, rest);
            var (right, after) = ParseMul(rest, 1);
            left = new BinaryExpr { Left = left, Op = op, Right = right };
            rest = after;
        }
    }

    private static (MeasureExpr Expr, string Remaining) ParseMul(string text, int start)
    {
        var (left, rest) = ParseUnary(text, start);
        while (true)
        {
            SkipWs(rest.AsSpan(), out var ws);
            rest = rest[ws..];
            if (rest.Length == 0)
                return (left, rest);
            MeasureBinaryOp op;
            if (rest[0] == '*') op = MeasureBinaryOp.Multiply;
            else if (rest[0] == '/') op = MeasureBinaryOp.Divide;
            else return (left, rest);
            var (right, after) = ParseUnary(rest, 1);
            left = new BinaryExpr { Left = left, Op = op, Right = right };
            rest = after;
        }
    }

    private static (MeasureExpr Expr, string Remaining) ParseUnary(string text, int start)
    {
        SkipWs(text.AsSpan(start), out var offset);
        var i = start + offset;
        if (i < text.Length && text[i] == '-')
        {
            var (inner, rest) = ParseUnary(text, i + 1);
            return (new UnaryNegateExpr { Inner = inner }, rest);
        }
        return ParsePrimary(text, i);
    }

    private static (MeasureExpr Expr, string Remaining) ParsePrimary(string text, int start)
    {
        SkipWs(text.AsSpan(start), out var offset);
        var i = start + offset;
        if (i >= text.Length)
            throw new InvalidOperationException("Ожидалось выражение.");

        if (text[i] == '(')
        {
            var (inner, rest) = ParseAdd(text, i + 1);
            SkipWs(rest.AsSpan(), out var ws);
            rest = rest[ws..];
            if (rest.Length == 0 || rest[0] != ')')
                throw new InvalidOperationException("Ожидалась «)».");
            return (inner, rest[1..]);
        }

        if (text[i] == '[')
            return ParseMeasureRef(text, i);

        if (char.IsDigit(text[i]) || text[i] == '.')
            return ParseNumber(text, i);

        if (StartsWithIgnoreCase(text, i, "CALCULATE"))
            return ParseCalculate(text, i);

        if (StartsWithIgnoreCase(text, i, "DIVIDE"))
            return ParseDivide(text, i);

        if (StartsWithIgnoreCase(text, i, "COUNTROWS"))
            return ParseCountRows(text, i);

        foreach (var (name, agg) in Aggregates)
        {
            if (StartsWithIgnoreCase(text, i, name))
                return ParseAggregate(text, i, name.Length, agg);
        }

        throw new InvalidOperationException(
            "Ожидалось число, SUM(...), [Мера], DIVIDE(...), COUNTROWS(...), CALCULATE(...) или «(...)».");
    }

    private static readonly (string Name, Aggregation Agg)[] Aggregates =
    [
        ("SUM", Aggregation.Sum),
        ("AVERAGE", Aggregation.Average),
        ("AVG", Aggregation.Average),
        ("COUNT", Aggregation.Count),
        ("DISTINCTCOUNT", Aggregation.DistinctCount),
        ("MIN", Aggregation.Min),
        ("MAX", Aggregation.Max),
    ];

    private static (MeasureExpr Expr, string Remaining) ParseNumber(string text, int i)
    {
        var m = NumberRegex().Match(text[i..]);
        if (!m.Success)
            throw new InvalidOperationException("Ожидалось число.");
        if (!double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            throw new InvalidOperationException($"Не удалось разобрать число «{m.Value}».");
        return (new NumberExpr { Value = value }, text[(i + m.Length)..]);
    }

    private static (MeasureExpr Expr, string Remaining) ParseMeasureRef(string text, int i)
    {
        var close = text.IndexOf(']', i + 1);
        if (close < 0)
            throw new InvalidOperationException("Не закрыта ссылка на меру [...]");
        var name = text[(i + 1)..close].Trim();
        if (name.Length == 0)
            throw new InvalidOperationException("Пустое имя меры.");
        return (new MeasureRefExpr { MeasureName = name }, text[(close + 1)..]);
    }

    private static (MeasureExpr Expr, string Remaining) ParseAggregate(string text, int i, int nameLen, Aggregation agg)
    {
        var afterName = i + nameLen;
        SkipWs(text.AsSpan(afterName), out var ws);
        var open = afterName + ws;
        if (open >= text.Length || text[open] != '(')
            throw new InvalidOperationException($"После {agg} ожидалась «(».");
        var (table, column, rest) = ParseColumnRef(text, open + 1);
        SkipWs(rest.AsSpan(), out var ws2);
        if (ws2 >= rest.Length || rest[ws2] != ')')
            throw new InvalidOperationException("Ожидалась «)» после столбца.");
        MeasureExpr expr = agg == Aggregation.Sum
            ? new SumColumnExpr { Table = table, Column = column }
            : new AggregateColumnExpr { Aggregation = agg, Table = table, Column = column };
        return (expr, rest[(ws2 + 1)..]);
    }

    private static (MeasureExpr Expr, string Remaining) ParseDivide(string text, int i)
    {
        var after = i + "DIVIDE".Length;
        SkipWs(text.AsSpan(after), out var ws);
        var open = after + ws;
        if (open >= text.Length || text[open] != '(')
            throw new InvalidOperationException("После DIVIDE ожидалась «(».");
        var (num, rest) = ParseAdd(text, open + 1);
        SkipWs(rest.AsSpan(), out var ws2);
        rest = rest[ws2..];
        if (rest.Length == 0 || rest[0] != ',')
            throw new InvalidOperationException("В DIVIDE ожидалась запятая между числителем и знаменателем.");
        var (den, rest2) = ParseAdd(rest, 1);
        SkipWs(rest2.AsSpan(), out var ws3);
        rest2 = rest2[ws3..];
        MeasureExpr? alt = null;
        if (rest2.Length > 0 && rest2[0] == ',')
        {
            var (a, afterAlt) = ParseAdd(rest2, 1);
            alt = a;
            rest2 = afterAlt;
            SkipWs(rest2.AsSpan(), out var ws4);
            rest2 = rest2[ws4..];
        }
        if (rest2.Length == 0 || rest2[0] != ')')
            throw new InvalidOperationException("Ожидалась «)» после DIVIDE.");
        return (new DivideFnExpr { Numerator = num, Denominator = den, Alternate = alt }, rest2[1..]);
    }

    private static (MeasureExpr Expr, string Remaining) ParseCountRows(string text, int i)
    {
        var after = i + "COUNTROWS".Length;
        SkipWs(text.AsSpan(after), out var ws);
        var open = after + ws;
        if (open >= text.Length || text[open] != '(')
            throw new InvalidOperationException("После COUNTROWS ожидалась «(».");
        SkipWs(text.AsSpan(open + 1), out var ws2);
        var body = open + 1 + ws2;
        string? table = null;
        string rest;
        if (body < text.Length && text[body] == ')')
        {
            rest = text[(body + 1)..];
        }
        else
        {
            var m = IdentRegex().Match(text[body..]);
            if (!m.Success)
                throw new InvalidOperationException("В COUNTROWS укажите имя таблицы или пустые скобки.");
            table = m.Value;
            var afterTable = text[(body + m.Length)..];
            SkipWs(afterTable.AsSpan(), out var ws3);
            afterTable = afterTable[ws3..];
            if (afterTable.Length == 0 || afterTable[0] != ')')
                throw new InvalidOperationException("Ожидалась «)» после COUNTROWS.");
            rest = afterTable[1..];
        }
        return (new CountRowsExpr { Table = table }, rest);
    }

    private static (MeasureExpr Expr, string Remaining) ParseCalculate(string text, int i)
    {
        var after = i + "CALCULATE".Length;
        SkipWs(text.AsSpan(after), out var ws);
        var open = after + ws;
        if (open >= text.Length || text[open] != '(')
            throw new InvalidOperationException("После CALCULATE ожидалась «(».");

        var (inner, rest) = ParseAdd(text, open + 1);
        SkipWs(rest.AsSpan(), out var ws2);
        rest = rest[ws2..];
        var modifiers = new List<CalculateModifier>();
        while (rest.StartsWith(','))
        {
            rest = rest[1..];
            SkipWs(rest.AsSpan(), out var ws3);
            rest = rest[ws3..];
            var (mod, afterMod) = ParseCalculateModifier(rest);
            modifiers.Add(mod);
            SkipWs(afterMod.AsSpan(), out var ws4);
            rest = afterMod[ws4..];
        }

        if (rest.Length == 0 || rest[0] != ')')
            throw new InvalidOperationException("Ожидалась «)» после CALCULATE.");
        return (new CalculateExpr { Inner = inner, Modifiers = modifiers }, rest[1..]);
    }

    private static (CalculateModifier Modifier, string Remaining) ParseCalculateModifier(string text)
    {
        if (StartsWithIgnoreCase(text, 0, "KEEPFILTERS"))
        {
            var afterKeep = "KEEPFILTERS".Length;
            SkipWs(text.AsSpan(afterKeep), out var ws);
            var kOpen = afterKeep + ws;
            if (kOpen >= text.Length || text[kOpen] != '(')
                throw new InvalidOperationException("После KEEPFILTERS ожидалась «(».");
            var (clause, afterClause) = ParseFilterEqualsBody(text, kOpen + 1);
            SkipWs(afterClause.AsSpan(), out var ws2);
            afterClause = afterClause[ws2..];
            if (afterClause.Length == 0 || afterClause[0] != ')')
                throw new InvalidOperationException("Ожидалась «)» после KEEPFILTERS.");
            return (new FilterEqualsModifier
            {
                KeepExisting = true,
                Table = clause.Table,
                Column = clause.Column,
                Value = clause.Value,
            }, afterClause[1..]);
        }

        if (StartsWithIgnoreCase(text, 0, "REMOVEFILTERS") || StartsWithIgnoreCase(text, 0, "ALL"))
        {
            var keyword = StartsWithIgnoreCase(text, 0, "REMOVEFILTERS") ? "REMOVEFILTERS" : "ALL";
            var afterKw = keyword.Length;
            SkipWs(text.AsSpan(afterKw), out var ws);
            var open = afterKw + ws;
            if (open >= text.Length || text[open] != '(')
                throw new InvalidOperationException($"После {keyword} ожидалась «(».");
            var (table, column, rest) = ParseAllTarget(text, open + 1);
            SkipWs(rest.AsSpan(), out var ws2);
            rest = rest[ws2..];
            if (rest.Length == 0 || rest[0] != ')')
                throw new InvalidOperationException($"Ожидалась «)» после {keyword}.");
            return (new RemoveFiltersModifier { Table = table, Column = column }, rest[1..]);
        }

        // Table[Column] = value — замена фильтра
        var (eq, afterEq) = ParseFilterEqualsBody(text, 0);
        return (new FilterEqualsModifier
        {
            KeepExisting = false,
            Table = eq.Table,
            Column = eq.Column,
            Value = eq.Value,
        }, afterEq);
    }

    private static (string Table, string? Column, string Remaining) ParseAllTarget(string text, int start)
    {
        SkipWs(text.AsSpan(start), out var ws);
        var i = start + ws;
        if (i >= text.Length)
            throw new InvalidOperationException("В ALL/REMOVEFILTERS укажите таблицу или Table[Column].");

        var fieldMatch = FieldRefRegex().Match(text[i..]);
        if (fieldMatch.Success && fieldMatch.Groups["table"].Success)
        {
            return (
                fieldMatch.Groups["table"].Value,
                fieldMatch.Groups["column"].Value,
                text[(i + fieldMatch.Length)..]);
        }

        var ident = IdentRegex().Match(text[i..]);
        if (!ident.Success)
            throw new InvalidOperationException("В ALL/REMOVEFILTERS укажите таблицу или Table[Column].");
        return (ident.Value, null, text[(i + ident.Length)..]);
    }

    private static (FilterEqualsModifier Clause, string Remaining) ParseFilterEqualsBody(string text, int start)
    {
        var (table, column, rest) = ParseColumnRef(text, start);
        SkipWs(rest.AsSpan(), out var ws);
        rest = rest[ws..];
        if (rest.Length == 0 || rest[0] != '=')
            throw new InvalidOperationException("В фильтре CALCULATE ожидалось «=».");
        rest = rest[1..];
        SkipWs(rest.AsSpan(), out var ws2);
        rest = rest[ws2..];
        var (value, afterValue) = ParseLiteral(rest);
        if (string.IsNullOrEmpty(table))
            throw new InvalidOperationException("В фильтре укажите таблицу: Таблица[Столбец] = \"значение\".");
        return (new FilterEqualsModifier { Table = table, Column = column, Value = value }, afterValue);
    }

    private static (string? Table, string Column, string Remaining) ParseColumnRef(string text, int start)
    {
        SkipWs(text.AsSpan(start), out var ws);
        var i = start + ws;
        if (i >= text.Length)
            throw new InvalidOperationException("Ожидалась ссылка на столбец.");

        var fieldMatch = FieldRefRegex().Match(text[i..]);
        if (fieldMatch.Success)
        {
            var table = fieldMatch.Groups["table"].Success ? fieldMatch.Groups["table"].Value : null;
            var column = fieldMatch.Groups["column"].Value;
            return (table, column, text[(i + fieldMatch.Length)..]);
        }

        var bare = BareColumnRegex().Match(text[i..]);
        if (bare.Success)
            return (null, bare.Groups["column"].Value, text[(i + bare.Length)..]);

        throw new InvalidOperationException("Ожидалось Table[Column], [Column] или имя столбца.");
    }

    private static (string Value, string Remaining) ParseLiteral(string text)
    {
        if (text.Length == 0)
            throw new InvalidOperationException("Ожидалось значение.");
        if (text[0] is '"' or '\'')
        {
            var quote = text[0];
            var end = 1;
            while (end < text.Length && text[end] != quote)
                end++;
            if (end >= text.Length)
                throw new InvalidOperationException("Не закрыта строка в кавычках.");
            return (text[1..end], text[(end + 1)..]);
        }

        var m = NumberOrIdentRegex().Match(text);
        if (!m.Success)
            throw new InvalidOperationException("Ожидалось значение в кавычках или число.");
        return (m.Value, text[m.Length..]);
    }

    private static bool StartsWithIgnoreCase(string text, int index, string value) =>
        index + value.Length <= text.Length
        && text.AsSpan(index, value.Length).Equals(value, StringComparison.OrdinalIgnoreCase);

    private static void SkipWs(ReadOnlySpan<char> span, out int count)
    {
        count = 0;
        while (count < span.Length && char.IsWhiteSpace(span[count]))
            count++;
    }

    [GeneratedRegex(@"^(?<table>[^\s\[\],()]+)?\[(?<column>[^\]]+)\]", RegexOptions.CultureInvariant)]
    private static partial Regex FieldRefRegex();

    [GeneratedRegex(@"^(?<column>[A-Za-z_][\w]*)", RegexOptions.CultureInvariant)]
    private static partial Regex BareColumnRegex();

    [GeneratedRegex(@"^[A-Za-z_][\w]*", RegexOptions.CultureInvariant)]
    private static partial Regex IdentRegex();

    [GeneratedRegex(@"^\d+(\.\d+)?([eE][+-]?\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"^[\w.+-]+", RegexOptions.CultureInvariant)]
    private static partial Regex NumberOrIdentRegex();
}
