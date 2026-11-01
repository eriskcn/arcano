using ArcanoCore.Application.Services;
using ArcanoCore.Domain.Entities;
using ArcanoCore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ArcanoCore.Domain.UnitTests.Services;

public class InviteKeyServiceTests
{
    private readonly ApplicationDbContext _db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Guid _admin = Guid.NewGuid();

    private InviteKeyService Service => new(_db);

    [Fact]
    public async Task Create_StoresOnlyHash_AndExpiresInRequestedDays()
    {
        var (key, plain) = await Service.CreateAsync(InviteKeyType.Join, 10, null, 7, _admin);

        Assert.NotEqual(plain, key.KeyHash);
        Assert.Equal(InviteKeyService.Hash(plain), key.KeyHash);
        Assert.DoesNotContain(plain, (await _db.InviteKeys.SingleAsync()).KeyHash);
        Assert.InRange(key.ExpiresAtUtc, DateTime.UtcNow.AddDays(7).AddMinutes(-1), DateTime.UtcNow.AddDays(7).AddMinutes(1));
        Assert.Equal(43, plain.Length); // 32 bytes, base64url without padding
    }

    [Fact]
    public async Task Consume_IsSingleUse()
    {
        var (_, plain) = await Service.CreateAsync(InviteKeyType.Join, 10, null, 7, _admin);

        var first = await Service.ConsumeAsync(plain, InviteKeyType.Join);
        var second = await Service.ConsumeAsync(plain, InviteKeyType.Join);

        Assert.True(first.IsSuccess);
        Assert.Equal(10, first.Value.QuotaBytes);
        Assert.Equal("Auth.InviteInvalid", second.Error.Code);
    }

    [Fact]
    public async Task Consume_RejectsExpiredWrongTypeUnknownAndEmptyKeys()
    {
        var (expired, expiredPlain) = await Service.CreateAsync(InviteKeyType.Join, null, null, 7, _admin);
        expired.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1);
        await _db.SaveChangesAsync();
        var (_, joinPlain) = await Service.CreateAsync(InviteKeyType.Join, null, null, 7, _admin);

        Assert.Equal("Auth.InviteInvalid", (await Service.ConsumeAsync(expiredPlain, InviteKeyType.Join)).Error.Code);
        Assert.Equal("Auth.InviteInvalid", (await Service.ConsumeAsync(joinPlain, InviteKeyType.Reset)).Error.Code);
        Assert.Equal("Auth.InviteInvalid", (await Service.ConsumeAsync("nope", InviteKeyType.Join)).Error.Code);
        Assert.Equal("Auth.InviteInvalid", (await Service.ConsumeAsync(null, InviteKeyType.Join)).Error.Code);
        Assert.True((await Service.ConsumeAsync(joinPlain, InviteKeyType.Join)).IsSuccess); // wrong-type attempt did not burn it
    }

    [Fact]
    public async Task Release_MakesKeyUsableAgain_AndListShowsOnlyOpenKeys()
    {
        var (key, plain) = await Service.CreateAsync(InviteKeyType.Reset, null, Guid.NewGuid(), 7, _admin);
        await Service.CreateAsync(InviteKeyType.Join, null, null, 7, _admin);
        var used = (await Service.CreateAsync(InviteKeyType.Join, null, null, 7, _admin)).Plain;
        await Service.ConsumeAsync(used, InviteKeyType.Join);

        Assert.Equal(2, (await Service.ListOpenAsync()).Count);

        await Service.ConsumeAsync(plain, InviteKeyType.Reset);
        Assert.Single(await Service.ListOpenAsync());
        await Service.ReleaseAsync(key.Id);
        Assert.Equal(2, (await Service.ListOpenAsync()).Count);
        Assert.True((await Service.ConsumeAsync(plain, InviteKeyType.Reset)).IsSuccess);
    }

    [Fact]
    public async Task Revoke_DeletesKey()
    {
        var (key, plain) = await Service.CreateAsync(InviteKeyType.Join, null, null, 7, _admin);

        Assert.True(await Service.RevokeAsync(key.Id));
        Assert.False(await Service.RevokeAsync(key.Id));
        Assert.True((await Service.ConsumeAsync(plain, InviteKeyType.Join)).IsFailure);
    }
}
