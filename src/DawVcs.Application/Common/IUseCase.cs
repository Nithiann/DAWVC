namespace DawVcs.Application.Common;

/// <summary>
/// Basiscontract voor application use cases.
/// </summary>
public interface IUseCase<in TRequest, TResponse>
{
    Task<TResponse> ExecuteAsync(TRequest request, CancellationToken cancellationToken = default);
}
