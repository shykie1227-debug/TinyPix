using System;
using System.IO;
using PdfSharp.Fonts;

namespace TinyPix.Media.Pdf;

/// <summary>
/// Minimal PDFsharp font resolver: loads TrueType font files from known OS
/// paths so watermark text rendering works on both Windows (Segoe UI) and
/// macOS/Linux (Helvetica/Arial). Must be assigned to
/// GlobalFontSettings.FontResolver before any text drawing.
/// </summary>
internal sealed class SystemFontResolver : IFontResolver
{
    private static readonly (string Family, string Path, bool Bold, bool Italic)[] KnownFonts =
    {
        // Windows
        ("Segoe UI", @"C:\Windows\Fonts\segoeui.ttf", false, false),
        ("Segoe UI", @"C:\Windows\Fonts\segoeuib.ttf", true, false),
        ("Arial", @"C:\Windows\Fonts\arial.ttf", false, false),
        ("Arial", @"C:\Windows\Fonts\arialbd.ttf", true, false),
        ("Helvetica", @"C:\Windows\Fonts\arial.ttf", false, false),
        // macOS
        ("Helvetica", "/System/Library/Fonts/Helvetica.ttc", false, false),
        ("Arial", "/Library/Fonts/Arial.ttf", false, false),
        ("Arial", "/System/Library/Fonts/Supplemental/Arial.ttf", false, false),
        // Linux
        ("DejaVu Sans", "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", false, false),
        ("Liberation Sans", "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf", false, false),
        ("Helvetica", "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf", false, false),
    };

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        foreach ((string family, string path, bool bold, bool italic) in KnownFonts)
        {
            if (family.Equals(familyName, StringComparison.OrdinalIgnoreCase) &&
                bold == isBold && italic == isItalic &&
                File.Exists(path))
            {
                return new FontResolverInfo($"{path}|{(isBold ? "b" : "")}{(isItalic ? "i" : "")}");
            }
        }

        // Fallback: first available font.
        foreach ((string family, string path, bool bold, bool italic) in KnownFonts)
        {
            if (File.Exists(path))
            {
                return new FontResolverInfo($"{path}|");
            }
        }

        return null;
    }

    public byte[]? GetFont(string faceName)
    {
        int separator = faceName.IndexOf('|');
        string path = separator >= 0 ? faceName[..separator] : faceName;
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    /// <summary>Install the resolver exactly once before any PDFsharp text drawing.</summary>
    public static void EnsureInstalled()
    {
        if (GlobalFontSettings.FontResolver is null)
        {
            GlobalFontSettings.FontResolver = new SystemFontResolver();
        }
    }
}
