using Microsoft.AspNetCore.Http;
using Shouldly;
using TaskTracker.Api.Common.Security;
using TaskTracker.Api.Domain.Errors;
using Xunit;

namespace TaskTracker.UnitTests;

public class ETagHelperTests
{
    [Fact]
    public void Format_Produces_Weak_ETag_Format()
    {
        ETagHelper.Format(1u).ShouldBe("W/\"1\"");
        ETagHelper.Format(42u).ShouldBe("W/\"42\"");
    }

    [Fact]
    public void IsNotModified_Evaluates_IfNoneMatch_Accurately()
    {
        var context = new DefaultHttpContext();

        // No header -> modified (false)
        ETagHelper.IsNotModified(context, 1u).ShouldBeFalse();

        // Wildcard header -> not modified (true)
        context.Request.Headers.IfNoneMatch = "*";
        ETagHelper.IsNotModified(context, 1u).ShouldBeTrue();

        // Exact weak ETag match -> not modified (true)
        context.Request.Headers.IfNoneMatch = "W/\"1\"";
        ETagHelper.IsNotModified(context, 1u).ShouldBeTrue();

        // Mismatched weak ETag -> modified (false)
        context.Request.Headers.IfNoneMatch = "W/\"2\"";
        ETagHelper.IsNotModified(context, 1u).ShouldBeFalse();
    }

    [Fact]
    public void ValidateIfMatch_Requires_And_Validates_Header()
    {
        var context = new DefaultHttpContext();

        // Missing header -> 428 Precondition Required
        var missingResult = ETagHelper.ValidateIfMatch(context, 1u);
        missingResult.IsFailure.ShouldBeTrue();
        missingResult.Error.Code.ShouldBe(DomainErrors.Concurrency.PreconditionRequired.Code);

        // Mismatched version -> 412 Precondition Failed
        context.Request.Headers.IfMatch = "W/\"2\"";
        var mismatchResult = ETagHelper.ValidateIfMatch(context, 1u);
        mismatchResult.IsFailure.ShouldBeTrue();
        mismatchResult.Error.Code.ShouldBe(DomainErrors.Concurrency.PreconditionFailed.Code);

        // Matching version -> Success
        context.Request.Headers.IfMatch = "W/\"1\"";
        var matchResult = ETagHelper.ValidateIfMatch(context, 1u);
        matchResult.IsSuccess.ShouldBeTrue();
    }
}
