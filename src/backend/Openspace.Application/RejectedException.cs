namespace Openspace.Application;

/// <summary>
/// A request Openspace cannot carry out as asked, with a stable code the interface
/// can react to (for example by offering a fix).
/// </summary>
public sealed class RejectedException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
