using System;
using Xunit;
using ArcanoCore.Domain.Common;

namespace ArcanoCore.Domain.UnitTests.Common;

public class PasswordHasherTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Hash_ShouldThrowArgumentException_WhenPasswordIsEmpty(string? password)
    {
        Assert.Throws<ArgumentException>(() => PasswordHasher.Hash(password!));
    }

    [Fact]
    public void Hash_ShouldReturnHashedPassword_WhenPasswordIsValid()
    {
        var password = "SuperSecurePassword123!";
        var hash = PasswordHasher.Hash(password);

        Assert.NotNull(hash);
        Assert.NotEmpty(hash);
        Assert.StartsWith("$2", hash);
    }

    [Fact]
    public void Verify_ShouldReturnTrue_WhenPasswordMatchesHash()
    {
        var password = "MySecretPassword";
        var hash = PasswordHasher.Hash(password);

        var result = PasswordHasher.Verify(password, hash);

        Assert.True(result);
    }

    [Fact]
    public void Verify_ShouldReturnFalse_WhenPasswordDoesNotMatchHash()
    {
        var password = "MySecretPassword";
        var hash = PasswordHasher.Hash(password);

        var result = PasswordHasher.Verify("WrongPassword", hash);

        Assert.False(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Verify_ShouldThrowArgumentException_WhenParametersAreEmpty(string? emptyInput)
    {
        var validHash = PasswordHasher.Hash("valid");

        Assert.Throws<ArgumentException>(() => PasswordHasher.Verify(emptyInput!, validHash));
        Assert.Throws<ArgumentException>(() => PasswordHasher.Verify("valid", emptyInput!));
    }
}
