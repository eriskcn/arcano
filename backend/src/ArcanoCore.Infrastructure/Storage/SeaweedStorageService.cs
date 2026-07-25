using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using ArcanoCore.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;

namespace ArcanoCore.Infrastructure.Storage;

public class SeaweedStorageService : IStorageService
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;

    public SeaweedStorageService(IAmazonS3 s3Client, IConfiguration configuration)
    {
        _s3Client = s3Client;
        _bucketName = configuration["Storage:BucketName"] ?? "default-bucket";
    }

    private async Task EnsureBucketExistsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var bucketExists = await Amazon.S3.Util.AmazonS3Util.DoesS3BucketExistV2Async(_s3Client, _bucketName);
            if (!bucketExists)
            {
                await _s3Client.PutBucketAsync(new PutBucketRequest { BucketName = _bucketName }, cancellationToken);
            }
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            // Bucket already exists, ignore.
        }
    }

    public async Task<string> UploadAsync(Stream stream, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        await EnsureBucketExistsAsync(cancellationToken);

        string storageKey = $"{Guid.NewGuid()}_{fileName}";

        var uploadRequest = new TransferUtilityUploadRequest
        {
            InputStream = stream,
            Key = storageKey,
            BucketName = _bucketName,
            ContentType = contentType
        };

        using var transferUtility = new TransferUtility(_s3Client);
        await transferUtility.UploadAsync(uploadRequest, cancellationToken);

        return storageKey;
    }

    public async Task<Stream> DownloadAsync(string key, CancellationToken cancellationToken = default)
    {
        var response = await _s3Client.GetObjectAsync(_bucketName, key, cancellationToken);
        return response.ResponseStream;
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        await _s3Client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = _bucketName,
            Key = key
        }, cancellationToken);
    }

    public async Task<string> GetUrlAsync(string key, int expiryHours = 24, CancellationToken cancellationToken = default)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = key,
            Expires = DateTime.UtcNow.AddHours(expiryHours)
        };

        // Custom task wrap as S3 SDK lacks direct async pre-signed url generation on .NET standard 2.0 in some contexts
        return await Task.Run(() => _s3Client.GetPreSignedURL(request), cancellationToken);
    }
}
