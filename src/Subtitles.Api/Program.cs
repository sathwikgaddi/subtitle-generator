using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Subtitles.Api.Contracts;
using Subtitles.Infrastructure.Auth;
using Subtitles.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    // Keep validation failures in the same { error: { code, message } } envelope as every
    // other error response — see docs/API.md "Conventions" — instead of ASP.NET Core's
    // default ProblemDetails shape.
    options.InvalidModelStateResponseFactory = context =>
    {
        var message = context.ModelState.Values
            .SelectMany(v => v.Errors)
            .Select(e => e.ErrorMessage)
            .FirstOrDefault() ?? "The request was invalid.";
        return new BadRequestObjectResult(ApiError.Of("invalid_request", message));
    };
});
builder.Services.AddOpenApi();

builder.Services.AddSubtitlesData(builder.Configuration);
builder.Services.AddSubtitlesAuth(builder.Configuration);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<JwtTokenService>((options, tokenService) =>
    {
        // See JwtTokenService.Validate for why this must be false.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = tokenService.AccessTokenValidationParameters;
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var tokenUse = context.Principal?.FindFirstValue("token_use");
                if (tokenUse != TokenUse.Access)
                {
                    context.Fail("Only access tokens may be used to authenticate requests.");
                }

                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
