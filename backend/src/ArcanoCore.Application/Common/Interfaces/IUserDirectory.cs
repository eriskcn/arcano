using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ArcanoCore.Application.Common.Interfaces;

public record DirectoryUser(Guid Id, string Email, bool IsDisabled);

// Read-only view of Identity users for the Application layer.
public interface IUserDirectory
{
    Task<DirectoryUser?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<Dictionary<Guid, DirectoryUser>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
}
