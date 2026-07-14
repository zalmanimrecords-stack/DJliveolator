using System.Text;

namespace Liveolator.Visuals.Gl;

/// <summary>
/// Pure helpers for preparing GLSL source before it is handed to the driver. GL-free, so it
/// unit-tests off the GPU.
/// </summary>
internal static class ShaderText
{
    /// <summary>
    /// Prepares GLSL for the driver: normalizes line endings to <c>\n</c> and drops non-ASCII
    /// characters.
    /// <para>
    /// Both guard against real driver failures observed on Intel GL: a stray carriage return or a
    /// non-ASCII character (e.g. an em-dash in a comment) makes the GLSL preprocessor fail with a
    /// misleading <c>'pre-mature EOF' : syntax error</c> and the whole compositor never renders.
    /// GLSL keywords and identifiers are ASCII, so non-ASCII only appears inside comments — stripping
    /// it is safe and keeps add-on shaders robust against editor-inserted Unicode punctuation.
    /// </para>
    /// </summary>
    public static string Sanitize(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string normalized = source.Replace("\r\n", "\n").Replace('\r', '\n');

        var builder = new StringBuilder(normalized.Length);
        foreach (char ch in normalized)
        {
            if (ch > '\x7F')
                continue; // non-ASCII (only valid inside comments) trips some GL preprocessors
            builder.Append(ch);
        }
        return builder.ToString();
    }
}
