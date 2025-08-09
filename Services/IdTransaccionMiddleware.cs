using Microsoft.AspNetCore.Http;

namespace Core
{
    public class IdTransaccionMiddleware
    {
        private readonly RequestDelegate _next;

        public IdTransaccionMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var IdTransaccion = Guid.NewGuid().ToString();
            context.Items["IdTransaccion"] = IdTransaccion;
            await _next(context);
        }
    }
}
