using System.Text;

namespace ReportBuilder.Infrastructure.Export;

internal static class PdfArabicText
{
    private readonly record struct Glyph(char Isolated, char Initial, char Medial, char Final)
    {
        public bool Dual => Initial != '\0' && Medial != '\0';
    }

    private static readonly HashSet<char> Marks =
    [
        '\u0610', '\u0612', '\u0613', '\u0614', '\u0615',
        '\u064B', '\u064C', '\u064D', '\u064E', '\u064F', '\u0650', '\u0651', '\u0652',
        '\u0653', '\u0654', '\u0655', '\u0656', '\u0657', '\u0658', '\u0670',
        '\u06D6', '\u06D7', '\u06D8', '\u06D9', '\u06DA', '\u06DB', '\u06DC',
        '\u06DF', '\u06E0', '\u06E1', '\u06E2', '\u06E3', '\u06E4', '\u06E7', '\u06E8',
        '\u06EA', '\u06EB', '\u06EC', '\u06ED'
    ];

    private static readonly Dictionary<char, Glyph> Letters = new()
    {
        ['\u0621'] = new('\uFE80', '\0', '\0', '\0'),
        ['\u0622'] = new('\uFE81', '\0', '\0', '\uFE82'),
        ['\u0623'] = new('\uFE83', '\0', '\0', '\uFE84'),
        ['\u0624'] = new('\uFE85', '\0', '\0', '\uFE86'),
        ['\u0625'] = new('\uFE87', '\0', '\0', '\uFE88'),
        ['\u0626'] = new('\uFE89', '\uFE8B', '\uFE8C', '\uFE8A'),
        ['\u0627'] = new('\uFE8D', '\0', '\0', '\uFE8E'),
        ['\u0628'] = new('\uFE8F', '\uFE91', '\uFE92', '\uFE90'),
        ['\u0629'] = new('\uFE93', '\0', '\0', '\uFE94'),
        ['\u062A'] = new('\uFE95', '\uFE97', '\uFE98', '\uFE96'),
        ['\u062B'] = new('\uFE99', '\uFE9B', '\uFE9C', '\uFE9A'),
        ['\u062C'] = new('\uFE9D', '\uFE9F', '\uFEA0', '\uFE9E'),
        ['\u062D'] = new('\uFEA1', '\uFEA3', '\uFEA4', '\uFEA2'),
        ['\u062E'] = new('\uFEA5', '\uFEA7', '\uFEA8', '\uFEA6'),
        ['\u062F'] = new('\uFEA9', '\0', '\0', '\uFEAA'),
        ['\u0630'] = new('\uFEAB', '\0', '\0', '\uFEAC'),
        ['\u0631'] = new('\uFEAD', '\0', '\0', '\uFEAE'),
        ['\u0632'] = new('\uFEAF', '\0', '\0', '\uFEB0'),
        ['\u0633'] = new('\uFEB1', '\uFEB3', '\uFEB4', '\uFEB2'),
        ['\u0634'] = new('\uFEB5', '\uFEB7', '\uFEB8', '\uFEB6'),
        ['\u0635'] = new('\uFEB9', '\uFEBB', '\uFEBC', '\uFEBA'),
        ['\u0636'] = new('\uFEBD', '\uFEBF', '\uFEC0', '\uFEBE'),
        ['\u0637'] = new('\uFEC1', '\uFEC3', '\uFEC4', '\uFEC2'),
        ['\u0638'] = new('\uFEC5', '\uFEC7', '\uFEC8', '\uFEC6'),
        ['\u0639'] = new('\uFEC9', '\uFECB', '\uFECC', '\uFECA'),
        ['\u063A'] = new('\uFECD', '\uFECF', '\uFED0', '\uFECE'),
        ['\u0640'] = new('\u0640', '\u0640', '\u0640', '\u0640'),
        ['\u0641'] = new('\uFED1', '\uFED3', '\uFED4', '\uFED2'),
        ['\u0642'] = new('\uFED5', '\uFED7', '\uFED8', '\uFED6'),
        ['\u0643'] = new('\uFED9', '\uFEDB', '\uFEDC', '\uFEDA'),
        ['\u0644'] = new('\uFEDD', '\uFEDF', '\uFEE0', '\uFEDE'),
        ['\u0645'] = new('\uFEE1', '\uFEE3', '\uFEE4', '\uFEE2'),
        ['\u0646'] = new('\uFEE5', '\uFEE7', '\uFEE8', '\uFEE6'),
        ['\u0647'] = new('\uFEE9', '\uFEEB', '\uFEEC', '\uFEEA'),
        ['\u0648'] = new('\uFEED', '\0', '\0', '\uFEEE'),
        ['\u0649'] = new('\uFEEF', '\0', '\0', '\uFEF0'),
        ['\u064A'] = new('\uFEF1', '\uFEF3', '\uFEF4', '\uFEF2'),
        ['\u06A9'] = new('\uFB8E', '\uFB90', '\uFB91', '\uFB8F'),
        ['\u06CC'] = new('\uFBFC', '\uFBFE', '\uFBFF', '\uFBFD'),
    };

    public static bool ContainsArabic(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        foreach (var ch in text)
        {
            if (IsArabicLetter(ch))
            {
                return true;
            }
        }

        return false;
    }

    public static string Prepare(string? value)
    {
        var text = ReportExportWriter.Sanitize(value);
        if (text.Length == 0 || !ContainsArabic(text))
        {
            return text;
        }

        return ReverseRtlRuns(Shape(text));
    }

    private static string Shape(string text)
    {
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var current = text[i];
            if (!Letters.TryGetValue(current, out var glyph))
            {
                builder.Append(current);
                continue;
            }

            var prev = PreviousLetter(text, i);
            var nextIndex = NextLetterIndex(text, i);
            var next = nextIndex >= 0 ? text[nextIndex] : '\0';

            if (current == '\u0644' && next is '\u0622' or '\u0623' or '\u0625' or '\u0627')
            {
                builder.Append(LamAlef(next, prev != '\0'));
                i = nextIndex;
                continue;
            }

            var joinPrev = prev != '\0' && Letters[prev].Dual;
            var joinNext = next != '\0'
                && glyph.Dual
                && Letters.TryGetValue(next, out var nextGlyph)
                && (nextGlyph.Final != '\0' || next == '\u0640');

            char shaped;
            if (joinPrev && joinNext && glyph.Medial != '\0')
            {
                shaped = glyph.Medial;
            }
            else if (joinPrev && glyph.Final != '\0')
            {
                shaped = glyph.Final;
            }
            else if (joinNext && glyph.Initial != '\0')
            {
                shaped = glyph.Initial;
            }
            else
            {
                shaped = glyph.Isolated;
            }

            builder.Append(shaped);
        }

        return builder.ToString();
    }

    private static char LamAlef(char alef, bool joinPrev) => (alef, joinPrev) switch
    {
        ('\u0622', false) => '\uFEF5',
        ('\u0622', true) => '\uFEF6',
        ('\u0623', false) => '\uFEF7',
        ('\u0623', true) => '\uFEF8',
        ('\u0625', false) => '\uFEF9',
        ('\u0625', true) => '\uFEFA',
        ('\u0627', true) => '\uFEFC',
        _ => '\uFEFB'
    };

    private static char PreviousLetter(string text, int index)
    {
        for (var i = index - 1; i >= 0; i--)
        {
            if (Marks.Contains(text[i]))
            {
                continue;
            }

            return Letters.ContainsKey(text[i]) ? text[i] : '\0';
        }

        return '\0';
    }

    private static int NextLetterIndex(string text, int index)
    {
        for (var i = index + 1; i < text.Length; i++)
        {
            if (Marks.Contains(text[i]))
            {
                continue;
            }

            return Letters.ContainsKey(text[i]) ? i : -1;
        }

        return -1;
    }

    private static string ReverseRtlRuns(string text)
    {
        var chars = text.ToCharArray();
        var i = 0;
        while (i < chars.Length)
        {
            if (!IsRtl(chars[i]))
            {
                i++;
                continue;
            }

            var start = i;
            while (i < chars.Length && (IsRtl(chars[i]) || chars[i] is ' ' or '\u00A0'))
            {
                i++;
            }

            while (i > start && chars[i - 1] is ' ' or '\u00A0')
            {
                i--;
            }

            Array.Reverse(chars, start, i - start);
        }

        return new string(chars);
    }

    private static bool IsArabicLetter(char ch)
        => ch is >= '\u0600' and <= '\u06FF' or >= '\u0750' and <= '\u077F'
            or >= '\u08A0' and <= '\u08FF' or >= '\uFB50' and <= '\uFDFF'
            or >= '\uFE70' and <= '\uFEFF';

    private static bool IsRtl(char ch)
        => IsArabicLetter(ch) && ch is not (>= '\u0660' and <= '\u0669')
            && ch is not (>= '\u06F0' and <= '\u06F9');
}
