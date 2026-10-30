using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.HttpResults;
using NSwag.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddValidation();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<IUserService, UserService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUi(settings =>
    {
        settings.DocumentTitle = "User Management API";
        settings.SwaggerRoutes.Add(
            new SwaggerUiRoute("User Management API", "/openapi/v1.json"));
    });
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.MapUserEndpoints();

app.Run();

internal static class UserEndpoints
{
    public static WebApplication MapUserEndpoints(this WebApplication app)
    {
        var users = app.MapGroup("/api/users")
            .WithTags("Users");

        users.MapGet("/", (IUserService service, CancellationToken cancellationToken) =>
            TypedResults.Ok(service.GetAll(cancellationToken)))
            .WithName("GetUsers")
            .WithSummary("Retrieve all users")
            .WithDescription("Returns all user records ordered by ID.")
            .Produces<IReadOnlyList<UserResponse>>(StatusCodes.Status200OK);

        users.MapGet("/{id:int}", Results<Ok<UserResponse>, NotFound> (
                int id,
                IUserService service,
                CancellationToken cancellationToken) =>
            service.GetById(id, cancellationToken) is { } user
                ? TypedResults.Ok(user)
                : TypedResults.NotFound())
            .WithName("GetUserById")
            .WithSummary("Retrieve a user")
            .WithDescription("Returns a user record by its ID.")
            .Produces<UserResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        users.MapPost("/", (CreateUserRequest request, IUserService service, CancellationToken cancellationToken) =>
            {
                var user = service.Create(request, cancellationToken);
                return TypedResults.Created($"/api/users/{user.Id}", user);
            })
            .WithName("CreateUser")
            .WithSummary("Create a user")
            .WithDescription("Creates a user record and returns its location.")
            .Produces<UserResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        users.MapPut("/{id:int}", Results<Ok<UserResponse>, NotFound> (
                int id,
                UpdateUserRequest request,
                IUserService service,
                CancellationToken cancellationToken) =>
            service.Update(id, request, cancellationToken) is { } user
                ? TypedResults.Ok(user)
                : TypedResults.NotFound())
            .WithName("UpdateUser")
            .WithSummary("Update a user")
            .WithDescription("Replaces a user's name and email address.")
            .Produces<UserResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        users.MapDelete("/{id:int}", Results<NoContent, NotFound> (
                int id,
                IUserService service,
                CancellationToken cancellationToken) =>
            service.Delete(id, cancellationToken)
                ? TypedResults.NoContent()
                : TypedResults.NotFound())
            .WithName("DeleteUser")
            .WithSummary("Delete a user")
            .WithDescription("Deletes a user record by its ID.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }
}

internal interface IUserService
{
    IReadOnlyList<UserResponse> GetAll(CancellationToken cancellationToken);
    UserResponse? GetById(int id, CancellationToken cancellationToken);
    UserResponse Create(CreateUserRequest request, CancellationToken cancellationToken);
    UserResponse? Update(int id, UpdateUserRequest request, CancellationToken cancellationToken);
    bool Delete(int id, CancellationToken cancellationToken);
}

internal sealed class UserService : IUserService
{
    private readonly ConcurrentDictionary<int, UserRecord> _users = new();
    private int _nextId;

    public IReadOnlyList<UserResponse> GetAll(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return _users.Values
            .OrderBy(user => user.Id)
            .Select(ToResponse)
            .ToArray();
    }

    public UserResponse? GetById(int id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return _users.TryGetValue(id, out var user) ? ToResponse(user) : null;
    }

    public UserResponse Create(CreateUserRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var now = DateTimeOffset.UtcNow;
        var user = new UserRecord(
            Interlocked.Increment(ref _nextId),
            request.Name,
            request.Email,
            now,
            now);

        if (!_users.TryAdd(user.Id, user))
        {
            throw new InvalidOperationException("A user with the generated ID already exists.");
        }

        return ToResponse(user);
    }

    public UserResponse? Update(int id, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        while (_users.TryGetValue(id, out var current))
        {
            var updated = current with
            {
                Name = request.Name,
                Email = request.Email,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            if (_users.TryUpdate(id, updated, current))
            {
                return ToResponse(updated);
            }
        }

        return null;
    }

    public bool Delete(int id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return _users.TryRemove(id, out _);
    }

    private static UserResponse ToResponse(UserRecord user) =>
        new(user.Id, user.Name, user.Email, user.CreatedAt, user.UpdatedAt);

    private sealed record UserRecord(
        int Id,
        string Name,
        string Email,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}

/// <summary>Payload for creating a user.</summary>
public sealed record CreateUserRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public required string Name { get; init; }

    [Required, EmailAddress, StringLength(254)]
    public required string Email { get; init; }
}

/// <summary>Payload for replacing a user's name and email address.</summary>
public sealed record UpdateUserRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public required string Name { get; init; }

    [Required, EmailAddress, StringLength(254)]
    public required string Email { get; init; }
}

/// <summary>Represents a user returned by the API.</summary>
public sealed record UserResponse(
    int Id,
    string Name,
    string Email,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
