using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace PipedWebsocket
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            var webSocketOptions = new WebSocketOptions
            {
                KeepAliveInterval = TimeSpan.FromSeconds(120),
                ReceiveBufferSize = KestrelMemoryPool.MinimumSegmentSize
            };

            app.UseWebSockets(webSocketOptions);
            app.Use((context, next) =>
            {
                if (context.WebSockets.IsWebSocketRequest)
                {
                    if (context.Request.Path == "/ws")
                    {
                        return ProcessWebsocket(context);
                    }

                    context.Response.StatusCode = 400;
                }

                return next();
            });
            app.UseFileServer();
        }

        private static async Task ProcessWebsocket(HttpContext context)
        {
            using (var webSocket = await context.WebSockets.AcceptWebSocketAsync())
            {
                var communication = new WebSocketCommunication();

                var internalSource = new CancellationTokenSource();
                var internalToken = internalSource.Token;
                var externalToken = context.RequestAborted;

                using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(internalToken, externalToken))
                {
                    var cancellationToken = linkedCts.Token;

                    var writeTask = communication.FillPipeAsync(webSocket, cancellationToken);
                    var readTask = communication.ReadPipeAsync(cancellationToken);
                    var messageTask = communication.WriteWebSockets(webSocket, cancellationToken);

                    await writeTask;
                    await readTask;

                    internalSource.Cancel();
                    await messageTask;
                }
            }
        }
    }
}