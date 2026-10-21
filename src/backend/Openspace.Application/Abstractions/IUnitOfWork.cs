namespace Openspace.Application.Abstractions;

/// <summary>Commits every change made through the repositories in one go.</summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
