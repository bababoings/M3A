namespace M3A.Api.Routes;

/// <summary>Single entry point that mounts every route module.</summary>
public static class RouteRegistration
{
    /// <summary>Maps all resource routes. Add one line per new resource.</summary>
    public static IEndpointRouteBuilder MapM3ARoutes(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapItemRoutes();
        endpoints.MapTicketRoutes();
        endpoints.MapVenueRoutes();
        endpoints.MapEventRoutes();
        return endpoints;
    }
}
