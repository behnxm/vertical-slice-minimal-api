using Shouldly;
using TaskTracker.Api.Common.Pagination;
using TaskTracker.Api.Common.Results;
using Xunit;

namespace TaskTracker.UnitTests;

public class CursorTests
{
    [Fact]
    public void Encode_And_Decode_Returns_Original_Values()
    {
        // Arrange
        var sortKey = new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
        var id = Guid.Parse("0192df45-4200-7531-97b1-e23a4a71b285");

        // Act
        var encoded = Cursor.Encode(sortKey, id);
        var result = Cursor.TryDecode(encoded);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.SortKey.ShouldBe(sortKey);
        result.Value.Id.ShouldBe(id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Decode_Empty_Or_Whitespace_Returns_Validation_Error(string? emptyCursor)
    {
        // Act
        var result = Cursor.TryDecode(emptyCursor);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("pagination.cursor_empty");
    }

    [Theory]
    [InlineData("not-valid-base64-!@#$%")]
    [InlineData("dGhpcy1pcy1ub3QtYS1jdXJzb3I=")] // "this-is-not-a-cursor" without separator
    [InlineData("MjAyNi0xMC0zMQ==")] // only date without id
    [InlineData("MjAyNi0xMC0zMXxub3QtYS1ndWlk")] // valid date | not-a-guid
    public void Decode_Tampered_Or_Malformed_Cursor_Returns_Validation_Error(string tamperedCursor)
    {
        // Act
        var result = Cursor.TryDecode(tamperedCursor);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("pagination.cursor_invalid");
    }
}
