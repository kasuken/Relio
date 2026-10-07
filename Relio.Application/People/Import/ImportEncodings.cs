using System.Text;

namespace Relio.Application.People.Import;

/// <summary>Resolves the <c>CHARSET</c> parameter of a vCard property to an <see cref="Encoding"/>.</summary>
public static class ImportEncodings
{
    /// <summary>
    /// The encoding named by <paramref name="charsetName"/>, or <see langword="null"/> when it is blank or unknown
    /// (the caller then decodes as UTF-8, falling back to Windows-1252). Code pages come from
    /// <see cref="CodePagesEncodingProvider"/> directly; nothing is registered globally.
    /// </summary>
    public static Encoding? Get(string? charsetName)
    {
        var name = charsetName?.Trim().Trim('"');
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        // US-ASCII is read as UTF-8, its superset, because exporters often mislabel UTF-8 text.
        if (name.Equals("UTF-8", StringComparison.OrdinalIgnoreCase)
            || name.Equals("UTF8", StringComparison.OrdinalIgnoreCase)
            || name.Equals("US-ASCII", StringComparison.OrdinalIgnoreCase)
            || name.Equals("ASCII", StringComparison.OrdinalIgnoreCase))
        {
            return Encoding.UTF8;
        }

        if (name.Equals("ISO-8859-1", StringComparison.OrdinalIgnoreCase) || name.Equals("LATIN1", StringComparison.OrdinalIgnoreCase))
        {
            return Encoding.Latin1;
        }

        if (name.Equals("WINDOWS-1252", StringComparison.OrdinalIgnoreCase) || name.Equals("CP1252", StringComparison.OrdinalIgnoreCase))
        {
            return TextDecoder.Windows1252();
        }

        try
        {
            return CodePagesEncodingProvider.Instance.GetEncoding(name);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
