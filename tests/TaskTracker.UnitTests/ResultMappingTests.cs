using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Shouldly;
using TaskTracker.Api.Common.Results;
using Xunit;

namespace TaskTracker.UnitTests;

public class ResultMappingTests
{
    [Fact]
    public void Result_Success_Maps_To_NoContent()
    {
        var result = Result.Success();
        var http = result.ToHttpResult();
        http.ShouldBeOfType<NoContent>();
    }

    [Fact]
    public void ResultT_Success_Maps_To_Ok()
    {
        var result = Result.Success("hello world");
        var http = result.ToHttpResult();
        var ok = http.ShouldBeOfType<Ok<string>>();
        ok.Value.ShouldBe("hello world");
    }

    [Theory]
    [InlineData(ErrorType.Validation, 400)]
    [InlineData(ErrorType.Unauthorized, 401)]
    [InlineData(ErrorType.Forbidden, 403)]
    [InlineData(ErrorType.NotFound, 404)]
    [InlineData(ErrorType.Conflict, 409)]
    [InlineData(ErrorType.PreconditionFailed, 412)]
    [InlineData(ErrorType.PreconditionRequired, 428)]
    [InlineData(ErrorType.TooManyRequests, 429)]
    [InlineData(ErrorType.Failure, 500)]
    public void Result_Failure_Maps_To_Expected_Status_Code(ErrorType errorType, int expectedStatusCode)
    {
        var error = new Error("test.code", "Test error description", errorType);
        var result = Result.Failure(error);
        var http = result.ToHttpResult();

        var problem = http.ShouldBeOfType<ProblemHttpResult>();
        problem.StatusCode.ShouldBe(expectedStatusCode);
        problem.ProblemDetails.Detail.ShouldBe("Test error description");
        problem.ProblemDetails.Extensions["code"].ShouldBe("test.code");
    }
}
