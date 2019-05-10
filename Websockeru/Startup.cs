using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Websockeru
{
    public class Startup
    {
        private readonly MemoryPool<byte> _memoryPool = KestrelMemoryPool.Create();

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment()) app.UseDeveloperExceptionPage();


            var webSocketOptions = new WebSocketOptions
            {
                KeepAliveInterval = TimeSpan.FromSeconds(120),
                ReceiveBufferSize = KestrelMemoryPool.MinimumSegmentSize
            };

            app.UseWebSockets(webSocketOptions);
            app.Use(async (context, next) =>
            {
                if (context.Request.Path == "/ws")
                {
                    if (context.WebSockets.IsWebSocketRequest)
                    {
                        using (var webSocket = await context.WebSockets.AcceptWebSocketAsync())
                        {
                            var messages = new MessageQueue();
                            
                            var pipe = CreateDataPipe(_memoryPool);
                            var writeTask = FillPipeAsync(context, webSocket, pipe.Writer);
                            var readTask = ReadPipeAsync(context, pipe.Reader, messages);
                            var websocketTask = WriteWebSockets(context, webSocket, messages);
                            await writeTask;
                            await readTask;
                            await websocketTask;
                        }
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                    }
                }
                else
                {
                    await next();
                }
            });
            app.UseFileServer();
        }

        private async Task WriteWebSockets(HttpContext context, WebSocket webSocket, MessageQueue queue)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1000));
            while (webSocket.State != WebSocketState.Closed)
            {
                var message = await queue.DequeueAsync(context.RequestAborted);
                if (webSocket.State != WebSocketState.Open)
                {
                    return;
                }

                await webSocket.SendAsync(message.AsMemory(), WebSocketMessageType.Binary, true, default);
            }
        }

        private static Pipe CreateDataPipe(MemoryPool<byte> pool)
        {
            return new Pipe(new PipeOptions
            (
                pool,
                PipeScheduler.Inline,
                PipeScheduler.ThreadPool,
                1,
                1,
                KestrelMemoryPool.MinimumSegmentSize,
                false
            ));
        }

        private static async Task ReadPipeAsync(HttpContext context, PipeReader reader, MessageQueue queue)
        {
            var cancellationToken = context.RequestAborted;
            var readResult = await reader.ReadAsync(cancellationToken);
            while (!readResult.IsCompleted)
            {
                var buffer = readResult.Buffer;

                var copy = buffer.ToArray();
                Array.Reverse(copy);
                queue.Enqueue(copy);

                reader.AdvanceTo(buffer.End);

                readResult = await reader.ReadAsync(cancellationToken);
            }

            reader.Complete();
        }

        private static async Task FillPipeAsync(HttpContext context, WebSocket webSocket, PipeWriter writer)
        {
            var cancellationToken = context.RequestAborted;

            while (true)
            {
                var memory = writer.GetMemory(KestrelMemoryPool.MinimumSegmentSize);
                try
                {
                    var wsResult = await webSocket.ReceiveAsync(memory, cancellationToken);
                    if (wsResult.MessageType == WebSocketMessageType.Close)
                        break;
                    writer.Advance(wsResult.Count);
                }
                catch
                {
                    break;
                }

                var flushResult = await writer.FlushAsync(cancellationToken);
                if (flushResult.IsCompleted)
                {
                    break;
                }
            }

            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Hejdå", cancellationToken);
            writer.Complete();
        }


        public class MessageQueue
        {
            private readonly ConcurrentQueue<byte[]> _workItems = new ConcurrentQueue<byte[]>();
            private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);

            public void Enqueue(byte[] item)
            {
                _workItems.Enqueue(item);
                _signal.Release();
            }

            public async Task<byte[]> DequeueAsync(CancellationToken cancellationToken)
            {
                await _signal.WaitAsync(cancellationToken);
                _workItems.TryDequeue(out var workItem);
                return workItem;
            }
        }
    }
}