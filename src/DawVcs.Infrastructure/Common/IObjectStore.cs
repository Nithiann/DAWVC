namespace DawVcs.Infrastructure.Common;

/// <summary>
/// Legacy interface. Use <see cref="DawVcs.Domain.Storage.IObjectStore"/> instead.
/// </summary>
[Obsolete("Use DawVcs.Domain.Storage.IObjectStore instead.")]
public interface IObjectStore : DawVcs.Domain.Storage.IObjectStore
{
}
