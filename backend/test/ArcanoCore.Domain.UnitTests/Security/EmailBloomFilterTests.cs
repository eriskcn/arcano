using System;
using System.Threading.Tasks;
using ArcanoCore.Infrastructure.Data;
using ArcanoCore.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ArcanoCore.Domain.UnitTests.Security;

public class EmailBloomFilterTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly ApplicationDbContext _dbContext;
    private readonly string _databaseName = $"TestDb_{Guid.NewGuid()}";

    public EmailBloomFilterTests()
    {
        var services = new ServiceCollection();

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(_databaseName));

        _serviceProvider = services.BuildServiceProvider();
        _dbContext = _serviceProvider.GetRequiredService<ApplicationDbContext>();
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
        _serviceProvider.Dispose();
    }

    [Fact]
    // TODO: test failed
    public async Task MightContainAsync_ShouldReturnTrue_ForEmailsLoadedFromDatabase()
    {
        // Arrange
        var user1 = new IdentityUser<Guid> { Id = Guid.NewGuid(), Email = "user1@example.com", NormalizedEmail = "USER1@EXAMPLE.COM" };
        var user2 = new IdentityUser<Guid> { Id = Guid.NewGuid(), Email = "user2@example.com", NormalizedEmail = "USER2@EXAMPLE.COM" };
        _dbContext.Users.AddRange(user1, user2);
        await _dbContext.SaveChangesAsync();

        var filter = new EmailBloomFilter(_serviceProvider);

        // Act & Assert
        Assert.True(await filter.MightContainAsync("user1@example.com"));
        Assert.True(await filter.MightContainAsync("user2@example.com"));
        Assert.False(await filter.MightContainAsync("user3@example.com"));
    }

    [Fact]
    // TODO: test failed
    public async Task MightContainAsync_ShouldReturnTrue_ForAddedEmails()
    {
        // Arrange
        var filter = new EmailBloomFilter(_serviceProvider);

        // Act
        await filter.AddAsync("added@example.com");

        // Assert
        Assert.True(await filter.MightContainAsync("added@example.com"));
        Assert.False(await filter.MightContainAsync("not-added@example.com"));
    }

    [Fact]
    // TODO: test failed
    public async Task MightContainAsync_ShouldBeCaseInsensitive()
    {
        // Arrange
        var filter = new EmailBloomFilter(_serviceProvider);

        // Act
        await filter.AddAsync("CaseTest@Example.com");

        // Assert
        Assert.True(await filter.MightContainAsync("casetest@example.com"));
        Assert.True(await filter.MightContainAsync("CASETEST@EXAMPLE.COM"));
    }
}
