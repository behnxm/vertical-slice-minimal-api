using Shouldly;
using TaskTracker.Api.Features.Dev;
using TaskTracker.Api.Features.Projects;
using Xunit;

namespace TaskTracker.UnitTests;

public class ValidatorTests
{
    [Fact]
    public void CreateProject_Validator_Rejects_Invalid_Payloads()
    {
        var validator = new CreateProject.Validator();

        // Empty Name
        var emptyName = new CreateProject.Request("");
        validator.Validate(emptyName).IsValid.ShouldBeFalse();

        // Name exceeding 100 characters
        var longName = new CreateProject.Request(new string('x', 101));
        validator.Validate(longName).IsValid.ShouldBeFalse();

        // Description exceeding 1000 characters
        var longDesc = new CreateProject.Request("Valid Name", new string('d', 1001));
        validator.Validate(longDesc).IsValid.ShouldBeFalse();

        // Valid payload
        var valid = new CreateProject.Request("Valid Project", "Valid Description");
        validator.Validate(valid).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void UpdateProject_Validator_Rejects_Invalid_Payloads()
    {
        var validator = new UpdateProject.Validator();

        // Empty ProjectId
        var emptyId = new UpdateProject.Request(Guid.Empty, "Name");
        validator.Validate(emptyId).IsValid.ShouldBeFalse();

        // Empty Name
        var emptyName = new UpdateProject.Request(Guid.NewGuid(), "");
        validator.Validate(emptyName).IsValid.ShouldBeFalse();

        // Valid
        var valid = new UpdateProject.Request(Guid.NewGuid(), "Updated Name", "Updated Desc");
        validator.Validate(valid).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void DevToken_Validator_Validates_User_And_Scopes()
    {
        var validator = new DevToken.Validator();

        // Empty user ID
        var emptyUser = new DevToken.Request("");
        validator.Validate(emptyUser).IsValid.ShouldBeFalse();

        // Invalid scope
        var invalidScope = new DevToken.Request("user-1", "User One", ["invalid.scope"]);
        validator.Validate(invalidScope).IsValid.ShouldBeFalse();

        // Valid with recognized scopes
        var valid = new DevToken.Request("user-1", "User One", ["tasks.read", "tasks.write"]);
        validator.Validate(valid).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void ListProjects_Validator_Enforces_Limit_Bounds()
    {
        var validator = new ListProjects.Validator();

        // Zero limit
        validator.Validate(new ListProjects.Request(Limit: 0)).IsValid.ShouldBeFalse();

        // Exceeds 100 limit
        validator.Validate(new ListProjects.Request(Limit: 101)).IsValid.ShouldBeFalse();

        // Valid limits (1 to 100)
        validator.Validate(new ListProjects.Request(Limit: 1)).IsValid.ShouldBeTrue();
        validator.Validate(new ListProjects.Request(Limit: 25)).IsValid.ShouldBeTrue();
        validator.Validate(new ListProjects.Request(Limit: 100)).IsValid.ShouldBeTrue();
    }
}
