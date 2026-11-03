namespace ReportBuilder.Infrastructure.Services;

internal static class EmailLogoHtml
{
    public const int MaxWidth = 160;
    public const int MaxHeight = 72;

    public static string Render(string altText, byte[]? bytes)
    {
        var alt = System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(altText) ? "Logo" : altText.Trim());
        var (width, height) = Fit(bytes);
        var heightAttr = height > 0 ? $" height=\"{height}\"" : string.Empty;
        return $"""
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin:0 0 16px;">
            <tr>
            <td align="center" style="text-align:center;padding:0;">
            <img src="cid:report-logo" alt="{alt}" width="{width}"{heightAttr} style="display:block;margin:0 auto;border:0;outline:none;width:{width}px;max-width:{MaxWidth}px;height:auto;">
            </td>
            </tr>
            </table>
            """;
    }

    public static (int Width, int Height) Fit(byte[]? bytes)
    {
        if (!TryReadSize(bytes, out var naturalWidth, out var naturalHeight)
            || naturalWidth <= 0
            || naturalHeight <= 0)
        {
            return (MaxWidth, 0);
        }

        var scale = Math.Min(1d, Math.Min(MaxWidth / (double)naturalWidth, MaxHeight / (double)naturalHeight));
        var width = Math.Max(1, (int)Math.Round(naturalWidth * scale));
        var height = Math.Max(1, (int)Math.Round(naturalHeight * scale));
        return (width, height);
    }

    private static bool TryReadSize(byte[]? bytes, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (bytes is not { Length: >= 10 })
        {
            return false;
        }

        if (bytes.Length >= 24
            && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            width = ReadBigEndian(bytes, 16);
            height = ReadBigEndian(bytes, 20);
            return width > 0 && height > 0;
        }

        if (bytes[0] == 0xFF && bytes[1] == 0xD8)
        {
            return TryReadJpeg(bytes, out width, out height);
        }

        if (bytes.Length >= 10
            && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38)
        {
            width = bytes[6] | (bytes[7] << 8);
            height = bytes[8] | (bytes[9] << 8);
            return width > 0 && height > 0;
        }

        if (bytes.Length >= 30
            && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
            && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
        {
            return TryReadWebp(bytes, out width, out height);
        }

        return false;
    }

    private static bool TryReadJpeg(byte[] bytes, out int width, out int height)
    {
        width = 0;
        height = 0;
        var index = 2;
        while (index + 8 < bytes.Length)
        {
            if (bytes[index] != 0xFF)
            {
                index++;
                continue;
            }

            var marker = bytes[index + 1];
            if (marker == 0xDA)
            {
                return false;
            }

            if (marker == 0xD8 || marker == 0xD9 || marker is >= 0xD0 and <= 0xD7)
            {
                index += 2;
                continue;
            }

            if (index + 3 >= bytes.Length)
            {
                return false;
            }

            var segmentLength = (bytes[index + 2] << 8) | bytes[index + 3];
            if (segmentLength < 2 || index + 2 + segmentLength > bytes.Length)
            {
                return false;
            }

            if (marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF)
            {
                if (segmentLength < 8 || index + 9 > bytes.Length)
                {
                    return false;
                }

                height = (bytes[index + 5] << 8) | bytes[index + 6];
                width = (bytes[index + 7] << 8) | bytes[index + 8];
                return width > 0 && height > 0;
            }

            index += 2 + segmentLength;
        }

        return false;
    }

    private static bool TryReadWebp(byte[] bytes, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (bytes.Length >= 30
            && bytes[12] == 0x56 && bytes[13] == 0x50 && bytes[14] == 0x38 && bytes[15] == 0x20)
        {
            width = (bytes[26] | (bytes[27] << 8)) & 0x3FFF;
            height = (bytes[28] | (bytes[29] << 8)) & 0x3FFF;
            return width > 0 && height > 0;
        }

        if (bytes.Length >= 25
            && bytes[12] == 0x56 && bytes[13] == 0x50 && bytes[14] == 0x38 && bytes[15] == 0x4C)
        {
            var packed = bytes[21] | (bytes[22] << 8) | (bytes[23] << 16) | (bytes[24] << 24);
            width = (packed & 0x3FFF) + 1;
            height = ((packed >> 14) & 0x3FFF) + 1;
            return true;
        }

        if (bytes.Length >= 30
            && bytes[12] == 0x56 && bytes[13] == 0x50 && bytes[14] == 0x38 && bytes[15] == 0x58)
        {
            width = 1 + (bytes[24] | (bytes[25] << 8) | (bytes[26] << 16));
            height = 1 + (bytes[27] | (bytes[28] << 8) | (bytes[29] << 16));
            return width > 0 && height > 0;
        }

        return false;
    }

    private static int ReadBigEndian(byte[] bytes, int offset)
        => (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
}
