using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace viora_BE.OpenApi;

public sealed class AuthorizeOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var method = context.MethodInfo;
        var controller = method.DeclaringType;
        var isAnonymous = method.IsDefined(typeof(AllowAnonymousAttribute), inherit: true) ||
                          controller?.IsDefined(typeof(AllowAnonymousAttribute), inherit: true) == true;
        var requiresAuthorization = method.IsDefined(typeof(AuthorizeAttribute), inherit: true) ||
                                    controller?.IsDefined(typeof(AuthorizeAttribute), inherit: true) == true;

        if (isAnonymous || !requiresAuthorization)
        {
            return;
        }

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            }] = []
        });
    }
}
