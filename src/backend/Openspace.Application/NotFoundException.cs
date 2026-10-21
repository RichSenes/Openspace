namespace Openspace.Application;

/// <summary>Thrown when a use case refers to something that does not exist.</summary>
public sealed class NotFoundException(string what, Guid id)
    : Exception($"{what} {id} was not found.");
