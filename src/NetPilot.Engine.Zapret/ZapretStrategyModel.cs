namespace NetPilot.Engine.Zapret;

public enum ZapretTemplatePartKind
{
    Literal,
    EngineBin,
    Lists,
    GameFilterTcp,
    GameFilterUdp
}

public sealed record ZapretTemplatePart(
    ZapretTemplatePartKind Kind,
    string Value);

public sealed record ZapretArgumentTemplate(
    IReadOnlyList<ZapretTemplatePart> Parts)
{
    public string ToDisplayString() =>
        string.Concat(Parts.Select(static part => part.Kind switch
        {
            ZapretTemplatePartKind.Literal => part.Value,
            ZapretTemplatePartKind.EngineBin => "${EngineBin}",
            ZapretTemplatePartKind.Lists => "${Lists}",
            ZapretTemplatePartKind.GameFilterTcp => "${GameFilterTcp}",
            ZapretTemplatePartKind.GameFilterUdp => "${GameFilterUdp}",
            _ => throw new InvalidOperationException("Неизвестный тип части шаблона Zapret.")
        }));
}

public sealed record ZapretStrategyTemplate(
    string Id,
    string DisplayName,
    string SourceFileName,
    IReadOnlyList<ZapretArgumentTemplate> Arguments);

public sealed class ZapretStrategyParseException(
    string code,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
}
