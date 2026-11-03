namespace PowerExec.Core.Config;

public enum IconSource { None, Fluent, Hero, Glyph, Emoji, File }

/// <summary>
/// Verweis auf ein Symbol: <c>fluent:folder</c> (Fluent UI System Icons), <c>hero:server</c> (Heroicons),
/// <c>glyph:E8B7</c> (Segoe Fluent Icons), <c>emoji:🚀</c> oder <c>file:C:\Bilder\logo.png</c>.
/// </summary>
public readonly record struct IconRef(IconSource Source, string Value)
{
    public static IconRef None => new(IconSource.None, string.Empty);

    public bool IsEmpty => Source == IconSource.None || string.IsNullOrEmpty(Value);

    public static IconRef Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return None;
        var i = text.IndexOf(':');
        if (i <= 0) return None;
        var value = text[(i + 1)..];
        return text[..i].ToLowerInvariant() switch
        {
            "fluent" => new IconRef(IconSource.Fluent, value),
            "hero" => new IconRef(IconSource.Hero, value),
            "glyph" => new IconRef(IconSource.Glyph, value),
            "emoji" => new IconRef(IconSource.Emoji, value),
            "file" => new IconRef(IconSource.File, value),
            _ => None
        };
    }

    public override string ToString() => IsEmpty ? string.Empty : Source switch
    {
        IconSource.Fluent => "fluent:" + Value,
        IconSource.Hero => "hero:" + Value,
        IconSource.Glyph => "glyph:" + Value,
        IconSource.Emoji => "emoji:" + Value,
        IconSource.File => "file:" + Value,
        _ => string.Empty
    };
}
