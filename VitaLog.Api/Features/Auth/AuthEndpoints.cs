namespace VitaLog.Api.Features.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthFeature(this IEndpointRouteBuilder app)
    {
        var authGroup = app.MapGroup("/api/auth")
            .WithTags("Auth");

        authGroup.MapRegister();
        authGroup.MapLogin();
        authGroup.MapRefresh();

        return app;
    }
}
