using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Api.Common;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class InternalSecretAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var expected = config["Internal:Secret"];
        var provided = context.HttpContext.Request.Headers["X-Internal-Secret"].ToString();
        if (string.IsNullOrWhiteSpace(expected) || provided != expected)
        {
            context.Result = new UnauthorizedResult();
        }
    }
}
