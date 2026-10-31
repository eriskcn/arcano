using System;
using System.Threading;
using System.Threading.Tasks;

namespace ArcanoCore.Application.Services;

public interface IAccountService
{
    // Permanently deletes everything the user owns (files incl. trashed, folders, links, upload sessions) and its stored objects.
    Task DeleteUserDataAsync(Guid ownerId, CancellationToken cancellationToken = default);
}
