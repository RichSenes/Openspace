using Openspace.Domain.Common;

namespace Openspace.Domain.Offices;

/// <summary>
/// How the agent at a desk looks, chosen by hand: names the frontend knows for the skin tone,
/// hair colour and style, clothes colour, and optionally an accessory, an outfit or a costume.
/// Names it does not know are drawn with its defaults.
/// </summary>
public sealed record DeskLook
{
    private DeskLook(string skin, string hair, string hairStyle, string cloth, string? accessory, string? outfit, string? costume)
    {
        Skin = skin;
        Hair = hair;
        HairStyle = hairStyle;
        Cloth = cloth;
        Accessory = accessory;
        Outfit = outfit;
        Costume = costume;
    }

    public string Skin { get; }

    public string Hair { get; }

    public string HairStyle { get; }

    public string Cloth { get; }

    public string? Accessory { get; }

    public string? Outfit { get; }

    public string? Costume { get; }

    public static DeskLook Create(string skin, string hair, string hairStyle, string cloth, string? accessory = null, string? outfit = null, string? costume = null) =>
        new(Word(skin, "Skin"), Word(hair, "Hair"), Word(hairStyle, "Hair style"), Word(cloth, "Clothes"),
            Optional(accessory, "Accessory"), Optional(outfit, "Outfit"), Optional(costume, "Costume"));

    private static string? Optional(string? value, string what) => string.IsNullOrWhiteSpace(value) ? null : Word(value, what);

    private static string Word(string value, string what)
    {
        var word = Guard.NotBlank(value, what, 30);
        return word.All(char.IsAsciiLetterOrDigit) ? word : throw new DomainException($"{what} must be a single word of letters and digits.");
    }
}
