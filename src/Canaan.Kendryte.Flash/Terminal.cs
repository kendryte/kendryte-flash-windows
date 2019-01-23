using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Canaan.Kendryte.Flash
{
    public class Terminal : IDisposable
    {
        private readonly Stream _stream;
        private readonly CancellationTokenSource _cts;

        public Terminal(Stream stream)
        {
            _stream = stream;
            _cts = new CancellationTokenSource();

            Start(_cts.Token);
        }

        private async void Start(CancellationToken cancellationToken)
        {
            try
            {
                var readTask = StartReading(cancellationToken);
                await Task.WhenAll(readTask);
            }
            catch (Exception)
            {
            }
        }

        private Task StartReading(CancellationToken cancellationToken)
        {
            var pipe = new Pipe();
            var filler = ReadRemoteAsync(_stream, pipe.Writer, cancellationToken);
            var decode = DecodePipeAsync(pipe.Reader, cancellationToken);
            return Task.WhenAll(filler, decode);
        }

        private async Task ReadRemoteAsync(Stream stream, PipeWriter writer, CancellationToken cancellationToken)
        {
            const int minimumBufferSize = 512;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var memory = writer.GetMemory(minimumBufferSize);
                try
                {
                    var isArray = MemoryMarshal.TryGetArray<byte>(memory, out var segment);
                    System.Diagnostics.Debug.Assert(isArray);
                    var bytesRead = await stream.ReadAsync(segment.Array, segment.Offset, segment.Count, cancellationToken);
                    if (bytesRead == 0) break;
                    writer.Advance(bytesRead);
                }
                catch (Exception ex)
                {
                    writer.Complete(ex);
                    return;
                }

                var result = await writer.FlushAsync(cancellationToken);
                if (result.IsCompleted) break;
            }

            writer.Complete();
        }

        private async Task DecodePipeAsync(PipeReader reader, CancellationToken cancellationToken)
        {
            var decoder = Encoding.UTF8.GetDecoder();

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await reader.ReadAsync(cancellationToken);
                var buffer = result.Buffer;

                foreach (var segment in buffer)
                {
                    DecodeSegment(decoder, segment.Span);
                }

                reader.AdvanceTo(buffer.End);
                if (result.IsCompleted) break;
            }
        }

        private unsafe void DecodeSegment(Decoder decoder, ReadOnlySpan<byte> segment)
        {
            const int bufferSize = 512;
            var restSize = segment.Length;

            while (restSize > 0)
            {
                var toRead = Math.Min(restSize, bufferSize);
                Span<char> destBuffer = stackalloc char[Encoding.UTF8.GetMaxCharCount(toRead)];
                var srcBuffer = segment.Slice(0, toRead);
                segment = segment.Slice(toRead);
                restSize -= toRead;

                var src = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(srcBuffer));
                var dest = (char*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(destBuffer));
                var decoded = decoder.GetChars(src, srcBuffer.Length, dest, destBuffer.Length, false);
                OnDecoded(new ReadOnlySpan<char>(dest, decoded));
            }
        }

        protected virtual void OnDecoded(ReadOnlySpan<char> decoded)
        {
            Console.Write(decoded.ToString());
        }

        #region IDisposable Support
        private bool _disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _cts.Cancel();
                    _stream.Dispose();
                }

                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
        #endregion
    }
}
