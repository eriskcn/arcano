using System;
using System.Threading;
using System.Threading.Tasks;
using ArcanoCore.Application.Common.Interfaces;
using ArcanoCore.Application.Services;
using ArcanoCore.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ArcanoCore.API.Controllers;

[Authorize]
public class AccountController : ApiControllerBase
{
    private readonly IStorageQuotaService _quotaService;
    private readonly ICurrentUser _currentUser;

    public AccountController(IStorageQuotaService quotaService, ICurrentUser currentUser)
    {
        _quotaService = quotaService;
        _currentUser = currentUser;
    }

    [HttpGet("storage")]
    public async Task<IActionResult> Storage(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUser.UserId, out var ownerId) || ownerId == Guid.Empty)
        {
            return Unauthorized(new Error("Account.Unauthorized", "A valid user ID is required."));
        }

        return Ok(await _quotaService.GetUsageAsync(ownerId, cancellationToken));
    }
}
