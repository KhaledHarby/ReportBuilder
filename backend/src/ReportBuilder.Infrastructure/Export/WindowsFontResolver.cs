using PdfSharp.Fonts;

namespace ReportBuilder.Infrastructure.Export;

internal sealed class WindowsFontResolver : IFontResolver
{
    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        var file = FaceFile(isBold, isItalic);
        return file is null ? null : new FontResolverInfo(file);
    }

    public byte[] GetFont(string faceName)
    {
        var path = Path.Combine(FontFolder, faceName);
        if (!File.Exists(path))
        {
            path = Path.Combine(FontFolder, "arial.ttf");
        }

        return File.ReadAllBytes(path);
    }

    private static string FontFolder =>
        Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

    private static string? FaceFile(bool bold, bool italic)
    {
        string[] names = (bold, italic) switch
        {
            (true, true) => ["arialbi.ttf", "calibribi.ttf", "seguisbi.ttf"],
            (true, false) => ["tahomabd.ttf", "arialbd.ttf", "calibrib.ttf", "segoeuib.ttf"],
            (false, true) => ["ariali.ttf", "calibrii.ttf", "segoeuii.ttf"],
            _ => ["tahoma.ttf", "arial.ttf", "segoeui.ttf", "calibri.ttf"]
        };

        foreach (var name in names)
        {
            if (File.Exists(Path.Combine(FontFolder, name)))
            {
                return name;
            }
        }

        return Directory.EnumerateFiles(FontFolder, "*.ttf").Select(Path.GetFileName).FirstOrDefault();
    }
}
