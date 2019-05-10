using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace PipedWebsocket.Infrastructure
{
    public sealed class MessageQueue
    {
        private readonly ConcurrentQueue<ReadOnlyMemory<byte>> _workItems = new ConcurrentQueue<ReadOnlyMemory<byte>>();
        private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);

        public void Enqueue(ReadOnlyMemory<byte> item)
        {
            _workItems.Enqueue(item);
            _signal.Release();
        }

        public async ValueTask<ReadOnlyMemory<byte>> DequeueAsync(CancellationToken cancellationToken)
        {
            await _signal.WaitAsync(cancellationToken);
            _workItems.TryDequeue(out var workItem);
            return workItem;
        }
    }
}