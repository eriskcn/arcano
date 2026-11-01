using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ArcanoCore.Domain.Common;
using ArcanoCore.Domain.Entities;

namespace ArcanoCore.Application.Services;

public interface IInviteKeyService
{
    // Returns the stored key and the plain key; the plain key exists only in this result.
    Task<(InviteKey Key, string Plain)> CreateAsync(InviteKeyType type, long? quotaBytes, Guid? targetUserId, int expiresInDays, Guid createdById, CancellationToken cancellationToken = default);
    // Marks a valid (right type, unused, unexpired) key as used. Fails with Auth.InviteInvalid otherwise; two concurrent calls cannot both succeed.
    Task<Result<InviteKey>> ConsumeAsync(string? plainKey, InviteKeyType type, CancellationToken cancellationToken = default);
    // Undoes ConsumeAsync when the action the key was for failed (e.g. weak password), so the key stays usable.
    Task ReleaseAsync(Guid keyId, CancellationToken cancellationToken = default);
    Task<List<InviteKey>> ListOpenAsync(CancellationToken cancellationToken = default);
    Task<bool> RevokeAsync(Guid keyId, CancellationToken cancellationToken = default);
}
