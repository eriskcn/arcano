using Xunit;
using ArcanoCore.Domain.Common;

namespace ArcanoCore.Domain.UnitTests.Common;

public class EmailNormalizerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    public void Normalize_ShouldReturnEmptyError_WhenEmailIsNullOrWhitespace(string? email)
    {
        // Act
        var result = EmailNormalizer.Normalize(email);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Email.Empty", result.Error.Code);
        Assert.Equal("Email cannot be empty.", result.Error.Message);
    }

    [Theory]
    [InlineData("plainaddress")]
    [InlineData("@no-local-part.com")]
    [InlineData("no-domain@")]
    [InlineData("user@domain@domain.com")]
    [InlineData("user @domain.com")]
    public void Normalize_ShouldReturnInvalidError_WhenEmailIsInvalidFormat(string email)
    {
        // Act
        var result = EmailNormalizer.Normalize(email);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Email.Invalid", result.Error.Code);
        Assert.Equal("Email format is invalid.", result.Error.Message);
    }

    [Theory]
    [InlineData(" USER@domain.com ", "user@domain.com")]
    [InlineData("john.doe@Example.com", "john.doe@example.com")]
    [InlineData("TEST@TEST.COM", "test@test.com")]
    public void Normalize_ShouldTrimAndLowercase_WhenEmailIsValidStandard(string input, string expected)
    {
        // Act
        var result = EmailNormalizer.Normalize(input);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value);
    }

    [Theory]
    [InlineData("john.doe+tag@gmail.com", "johndoe@gmail.com")]
    [InlineData("john.doe+tag@googlemail.com", "johndoe@gmail.com")]
    [InlineData("J.O.H.N.doe+abc@GMAIL.com", "johndoe@gmail.com")]
    [InlineData("johndoe@gmail.com", "johndoe@gmail.com")]
    public void Normalize_ShouldNormalizeGmailAndGooglemail_WhenGmailOrGooglemail(string input, string expected)
    {
        // Act
        var result = EmailNormalizer.Normalize(input);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value);
    }

    [Theory]
    [InlineData("john.doe+tag@outlook.com", "john.doe@outlook.com")]
    [InlineData("john.doe+tag@hotmail.com", "john.doe@hotmail.com")]
    [InlineData("john.doe+tag@live.com", "john.doe@live.com")]
    [InlineData("john.doe@outlook.com", "john.doe@outlook.com")]
    public void Normalize_ShouldRemoveTagsButKeepDots_WhenOutlookHotmailOrLive(string input, string expected)
    {
        // Act
        var result = EmailNormalizer.Normalize(input);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value);
    }

    [Theory]
    [InlineData("john.doe+tag@example.com", "john.doe+tag@example.com")]
    [InlineData("john.doe@example.com", "john.doe@example.com")]
    public void Normalize_ShouldNotModifyTagsOrDots_WhenOtherDomains(string input, string expected)
    {
        // Act
        var result = EmailNormalizer.Normalize(input);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value);
    }
}
