using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;

using PipedWebsocket.Infrastructure;

namespace PipedWebsocket
{
    public class WebSocketCommunication
    {
        private readonly MessageQueue _messages = new MessageQueue();
        private readonly MemoryPool<byte> _memoryPool = KestrelMemoryPool.Create();
        private readonly Pipe _pipe;

        public WebSocketCommunication()
        {
            _pipe = new Pipe(new PipeOptions
            (
                _memoryPool,
                PipeScheduler.Inline,
                PipeScheduler.ThreadPool,
                1,
                1,
                KestrelMemoryPool.MinimumSegmentSize,
                false
            ));
        }

        public async Task ReadPipeAsync(CancellationToken cancellationToken)
        {
            var reader = _pipe.Reader;

            var readResult = await reader.ReadAsync(cancellationToken);
            while (!readResult.IsCompleted)
            {
                var buffer = readResult.Buffer;

                var eolPosition = buffer.PositionOf((byte)0);
                if (eolPosition != null)
                {
                    var message = Encoding.UTF8.GetString(buffer.Slice(0, eolPosition.Value).First.Span);
                    var messageCopy = Encoding.UTF8.GetBytes($"Du skrev {message}!!");
                    _messages.Enqueue(messageCopy);

                    buffer = buffer.Slice(buffer.GetPosition(1, eolPosition.Value));
                }

                var copy = buffer.ToArray();
                copy.AsSpan().Reverse();
                _messages.Enqueue(copy);

                reader.AdvanceTo(buffer.End);

                readResult = await reader.ReadAsync(cancellationToken);
            }

            reader.Complete();
        }

        public async Task FillPipeAsync(WebSocket webSocket, CancellationToken cancellationToken)
        {
            var writer = _pipe.Writer;

            while (true)
            {
                var memory = writer.GetMemory(KestrelMemoryPool.MinimumSegmentSize);
                try
                {
                    var wsResult = await webSocket.ReceiveAsync(memory, cancellationToken);
                    if (wsResult.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

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

            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, cancellationToken);
            writer.Complete();
        }

        public async Task WriteWebSockets(WebSocket webSocket, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var message = await _messages.DequeueAsync(cancellationToken);
                    await webSocket.SendAsync(message, WebSocketMessageType.Binary, true, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }
}