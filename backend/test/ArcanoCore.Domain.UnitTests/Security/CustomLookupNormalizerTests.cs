using Xunit;
using ArcanoCore.Infrastructure.Security;

namespace ArcanoCore.Domain.UnitTests.Security;

public class CustomLookupNormalizerTests
{
    private readonly CustomLookupNormalizer _normalizer = new();

    [Theory]
    [InlineData("testuser", "TESTUSER")]
    [InlineData("TestUser", "TESTUSER")]
    [InlineData("TESTUSER", "TESTUSER")]
    public void NormalizeName_ShouldUppercaseName(string name, string expected)
    {
        var result = _normalizer.NormalizeName(name);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("john.doe+tag@gmail.com", "JOHNDOE@GMAIL.COM")]
    [InlineData("john.doe+tag@outlook.com", "JOHN.DOE@OUTLOOK.COM")]
    [InlineData("J.O.H.N.doe+abc@GMAIL.com", "JOHNDOE@GMAIL.COM")]
    [InlineData("plainaddress", "PLAINADDRESS")] // Fallback if invalid format
    public void NormalizeEmail_ShouldNormalizeAndUppercase(string email, string expected)
    {
        var result = _normalizer.NormalizeEmail(email);
        Assert.Equal(expected, result);
    }
}
