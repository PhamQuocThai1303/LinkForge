namespace UrlShortener.Api;

public static class RootEndpoints
{
    public static void MapRootEndpoints(this WebApplication app)
    {
        app.MapGet("/", () => Results.Ok(new ServiceInfoResponse(
                "LinkForge",
                "ok",
                "/api/v1/urls",
                "/health/live",
                "/health/ready")))
            .WithName("ServiceInfo")
            .WithTags("Service")
            .Produces<ServiceInfoResponse>(StatusCodes.Status200OK);
    }
}

public sealed record ServiceInfoResponse(
    string Service,
    string Status,
    string ApiBasePath,
    string LivenessPath,
    string ReadinessPath);
