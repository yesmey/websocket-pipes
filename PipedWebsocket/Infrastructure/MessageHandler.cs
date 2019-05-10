using System;
using System.Buffers;
using System.Text;
using System.Threading.Tasks;

namespace PipedWebsocket.Infrastructure
{
    public class MessageHandler
    {
        private byte[] MessageCopy { get; set; }
        private byte[] Copy { get; set; }

        public static MessageHandler ParseFromBuffer(ref ReadOnlySequence<byte> buffer)
        {
            byte[] messageCopy = null;

            var eolPosition = buffer.PositionOf((byte)0);
            if (eolPosition != null)
            {
                var message = Encoding.UTF8.GetString(buffer.Slice(0, eolPosition.Value).First.Span);
                messageCopy = Encoding.UTF8.GetBytes($"Du skrev {message}!!");
                buffer = buffer.Slice(buffer.GetPosition(1, eolPosition.Value));
            }

            var copy = buffer.ToArray();
            copy.AsSpan().Reverse();
            return new MessageHandler
            {
                MessageCopy = messageCopy,
                Copy = copy
            };
        }

        public void HandleMessage(MessageQueue messages)
        {
            messages.Enqueue(MessageCopy);
            messages.Enqueue(Copy);
        }
    }
}
