using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ArcanoCore.API.Controllers;
using ArcanoCore.Domain.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ArcanoCore.API.UnitTests;

public class FilesControllerTests
{
    [Fact]
    public async Task Upload_EmptyFile_ReturnsFileEmptyError()
    {
        var controller = new FilesController(null!); // an empty file is refused before the service is called
        var empty = new FormFile(Stream.Null, 0, 0, "file", "empty.txt");

        var bad = Assert.IsType<BadRequestObjectResult>(await controller.Upload(empty, null, CancellationToken.None));

        Assert.Equal("File.Empty", Assert.IsType<Error>(bad.Value).Code);
    }
}
