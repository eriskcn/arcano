using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using ArcanoCore.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;

namespace ArcanoCore.Infrastructure.Storage;

public class SeaweedStorageService : IStorageService, IMultipartStorage
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;

    public SeaweedStorageService(IAmazonS3 s3Client, IConfiguration configuration)
    {
        _s3Client = s3Client;
        _bucketName = configuration["Storage:BucketName"] ?? "default-bucket";
    }

    private static volatile bool _bucketReady;

    private async Task EnsureBucketExistsAsync(CancellationToken cancellationToken)
    {
        if (_bucketReady)
        {
            return;
        }

        try
        {
            var bucketExists = await Amazon.S3.Util.AmazonS3Util.DoesS3BucketExistV2Async(_s3Client, _bucketName);
            if (!bucketExists)
            {
                await _s3Client.PutBucketAsync(new PutBucketRequest { BucketName = _bucketName }, cancellationToken);
            }
            _bucketReady = true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            // Bucket already exists, ignore.
        }
    }

    public async Task<string> UploadAsync(Stream stream, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        await EnsureBucketExistsAsync(cancellationToken);

        string storageKey = Guid.NewGuid().ToString();

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

    public async Task<(string Key, string UploadId)> InitiateAsync(string contentType, CancellationToken cancellationToken = default)
    {
        await EnsureBucketExistsAsync(cancellationToken);
        var key = Guid.NewGuid().ToString();
        var response = await _s3Client.InitiateMultipartUploadAsync(new InitiateMultipartUploadRequest
        {
            BucketName = _bucketName,
            Key = key,
            ContentType = contentType
        }, cancellationToken);
        return (key, response.UploadId);
    }

    public async Task UploadPartAsync(string key, string uploadId, int partNumber, Stream content, CancellationToken cancellationToken = default)
    {
        await _s3Client.UploadPartAsync(new UploadPartRequest
        {
            BucketName = _bucketName,
            Key = key,
            UploadId = uploadId,
            PartNumber = partNumber,
            InputStream = content
        }, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<int, long>> ListPartsAsync(string key, string uploadId, CancellationToken cancellationToken = default)
    {
        var parts = new Dictionary<int, long>();
        var request = new ListPartsRequest { BucketName = _bucketName, Key = key, UploadId = uploadId };
        ListPartsResponse response;
        do
        {
            response = await _s3Client.ListPartsAsync(request, cancellationToken);
            foreach (var part in response.Parts)
            {
                parts[part.PartNumber] = part.Size;
            }
            request.PartNumberMarker = response.NextPartNumberMarker.ToString();
        }
        while (response.IsTruncated);
        return parts;
    }

    public async Task CompleteAsync(string key, string uploadId, CancellationToken cancellationToken = default)
    {
        // The ETags come from the listing, so the caller only has to track part numbers.
        var request = new ListPartsRequest { BucketName = _bucketName, Key = key, UploadId = uploadId };
        var etags = new List<PartETag>();
        ListPartsResponse response;
        do
        {
            response = await _s3Client.ListPartsAsync(request, cancellationToken);
            etags.AddRange(response.Parts.Select(p => new PartETag(p.PartNumber, p.ETag)));
            request.PartNumberMarker = response.NextPartNumberMarker.ToString();
        }
        while (response.IsTruncated);

        await _s3Client.CompleteMultipartUploadAsync(new CompleteMultipartUploadRequest
        {
            BucketName = _bucketName,
            Key = key,
            UploadId = uploadId,
            PartETags = etags
        }, cancellationToken);
    }

    public async Task AbortAsync(string key, string uploadId, CancellationToken cancellationToken = default)
    {
        await _s3Client.AbortMultipartUploadAsync(new AbortMultipartUploadRequest
        {
            BucketName = _bucketName,
            Key = key,
            UploadId = uploadId
        }, cancellationToken);
    }
}
