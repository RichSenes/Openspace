namespace Openspace.Domain.Common;

internal static class Guard
{
    public static string NotBlank(string? value, string what, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new DomainException($"{what} cannot be empty.");
        }

        if (trimmed.Length > maxLength)
        {
            throw new DomainException($"{what} cannot be longer than {maxLength} characters.");
        }

        return trimmed;
    }
}
