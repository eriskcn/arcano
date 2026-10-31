using Amazon.S3;
using Amazon.S3.Model;
using ArcanoCore.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;

namespace ArcanoCore.API.Controllers;

public class HealthController(ApplicationDbContext db, IAmazonS3 s3, IConfiguration configuration) : ApiControllerBase
{
    [HttpGet]
    [Route("/health")]
    public async Task<IActionResult> Health(CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(3));

        var database = await Check(() => db.Database.CanConnectAsync(cts.Token));
        var storage = await Check(async () =>
        {
            try
            {
                await s3.ListObjectsV2Async(new ListObjectsV2Request
                {
                    BucketName = configuration["Storage:BucketName"] ?? "default-bucket",
                    MaxKeys = 1
                }, cts.Token);
            }
            catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchBucket")
            {
                // Bucket is created lazily on first upload; the endpoint answered, so storage is reachable.
            }
            return true;
        });

        var checks = new { database = database ? "ok" : "failed", storage = storage ? "ok" : "failed" };
        return database && storage
            ? Ok(new { status = "Healthy", checks })
            : StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "Unhealthy", checks });
    }

    private static async Task<bool> Check(Func<Task<bool>> probe)
    {
        try { return await probe(); }
        catch { return false; }
    }
}
