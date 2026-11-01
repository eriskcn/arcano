using System.Security.Cryptography;
using Amazon.S3;
using ArcanoCore.Application.Common.Interfaces;
using ArcanoCore.Application.Common.Models;
using ArcanoCore.Application.Services;
using ArcanoCore.Domain.Common;
using ArcanoCore.Domain.Entities;
using ArcanoCore.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ArcanoCore.IntegrationTests;

// Real UploadService + SeaweedStorageService against the Compose SeaweedFS (S3 on localhost:8333).
// Run: docker compose up -d seaweedfs
//      ARCANO_IT_S3=1 dotnet test backend/test/ArcanoCore.IntegrationTests
// Without ARCANO_IT_S3=1 the tests are skipped.
public class RequiresS3FactAttribute : FactAttribute
{
    public RequiresS3FactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ARCANO_IT_S3") != "1")
        {
            Skip = "Set ARCANO_IT_S3=1 and start SeaweedFS (docker compose up -d seaweedfs).";
        }
    }
}

public class UploadS3IntegrationTests
{
    private const long Chunk = UploadService.ChunkSize;
    private readonly TestContext _context = new();
    private readonly SeaweedStorageService _storage;
    private readonly UploadService _service;

    public UploadS3IntegrationTests()
    {
        var s3 = new AmazonS3Client("any_key", "any_secret", new AmazonS3Config
        {
            ServiceURL = "http://localhost:8333",
            ForcePathStyle = true,
            SignatureVersion = "4"
        });
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:BucketName"] = "arcano-integration-tests" })
            .Build();
        _storage = new SeaweedStorageService(s3, config);
        _service = new UploadService(_context, _storage, _storage, new FakeUser(Guid.NewGuid().ToString()), new FakeQuota());
    }

    [RequiresS3Fact]
    public async Task ThreePartUpload_DownloadsIdentical()
    {
        var data = RandomBytes(Chunk * 2 + 123);
        var id = (await _service.StartAsync(new UploadStartDto("big.bin", "application/octet-stream", data.Length, null))).Value.Id;

        for (var n = 1; n <= 3; n++)
        {
            Assert.True((await UploadPart(id, n, data)).IsSuccess);
        }
        var done = await _service.CompleteAsync(id);

        Assert.True(done.IsSuccess, done.IsSuccess ? null : done.Error.Code);
        Assert.Equal(data.Length, done.Value.FileSize);
        Assert.Empty(_context.Sessions);
        await using var downloaded = await _storage.DownloadAsync(_context.Files.Single().StorageKey);
        using var copy = new MemoryStream();
        await downloaded.CopyToAsync(copy);
        Assert.Equal(data.Length, copy.Length);
        Assert.Equal(SHA256.HashData(data), SHA256.HashData(copy.ToArray()));
    }

    [RequiresS3Fact]
    public async Task Resume_OutOfOrderParts_CompleteOnlyWhenAllPresent()
    {
        var data = RandomBytes(Chunk * 2 + 123);
        var id = (await _service.StartAsync(new UploadStartDto("big.bin", "application/octet-stream", data.Length, null))).Value.Id;

        await UploadPart(id, 2, data);
        var status = await _service.GetStatusAsync(id);
        var early = await _service.CompleteAsync(id);
        await UploadPart(id, 1, data);
        await UploadPart(id, 3, data);
        var done = await _service.CompleteAsync(id);

        Assert.Equal(new[] { 2 }, status.Value.UploadedParts);
        Assert.Equal("Upload.Incomplete", early.Error.Code);
        Assert.True(done.IsSuccess, done.IsSuccess ? null : done.Error.Code);
    }

    [RequiresS3Fact]
    public async Task Abort_RemovesSession()
    {
        var data = RandomBytes(Chunk + 10);
        var id = (await _service.StartAsync(new UploadStartDto("big.bin", "application/octet-stream", data.Length, null))).Value.Id;
        await UploadPart(id, 1, data);

        var result = await _service.AbortAsync(id);

        Assert.True(result.IsSuccess);
        Assert.Empty(_context.Sessions);
    }

    private static byte[] RandomBytes(long length)
    {
        var bytes = new byte[length];
        new Random(42).NextBytes(bytes);
        return bytes;
    }

    private Task<Result> UploadPart(Guid id, int part, byte[] data)
    {
        var offset = (part - 1) * Chunk;
        var length = Math.Min(Chunk, data.Length - offset);
        return _service.UploadPartAsync(id, part, new MemoryStream(data, (int)offset, (int)length), length);
    }

    private sealed class FakeUser(string userId) : ICurrentUser
    {
        public string? UserId { get; } = userId;
        public bool IsAuthenticated => true;
    }

    private sealed class FakeQuota : IStorageQuotaService
    {
        public Task<Result> EnsureWithinQuotaAsync(Guid ownerId, long additionalBytes, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());

        public Task<StorageUsageDto> GetUsageAsync(Guid ownerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StorageUsageDto(0, long.MaxValue));
    }

    private sealed class TestContext : DbContext, IApplicationDbContext
    {
        public DbSet<FileMetadata> Files => Set<FileMetadata>();
        public DbSet<UploadSession> Sessions => Set<UploadSession>();
        public DbSet<FileMetadata> FileMetadatas => Files;
        public DbSet<Folder> Folders => Set<Folder>();
        public DbSet<SharedLink> SharedLinks => throw new NotSupportedException();
        public DbSet<UploadSession> UploadSessions => Sessions;
        public DbSet<UserProfile> UserProfiles => throw new NotSupportedException();
        public DbSet<InviteKey> InviteKeys => throw new NotSupportedException();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseInMemoryDatabase(Guid.NewGuid().ToString());
    }
}
