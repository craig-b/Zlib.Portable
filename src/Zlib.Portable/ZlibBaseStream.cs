// ZlibBaseStream.cs
// ------------------------------------------------------------------
//
// Copyright (c) 2009 Dino Chiesa and Microsoft Corporation.
// All rights reserved.
//
// This code module is part of DotNetZip, a zipfile class library.
//
// ------------------------------------------------------------------
//
// This code is licensed under the Microsoft Public License.
// See the file License.txt for the license details.
// More info on: http://dotnetzip.codeplex.com
//
// ------------------------------------------------------------------
//
// last saved (in emacs):
// Time-stamp: <2011-August-06 21:22:38>
//
// ------------------------------------------------------------------
//
// This module defines the ZlibBaseStream class, which is an intnernal
// base class for DeflateStream, ZlibStream and GZipStream.
//
// ------------------------------------------------------------------

using System;
using System.IO;
using static Ionic.Zlib.MemoryManagement;

namespace Ionic.Zlib
{
    internal enum ZlibStreamFlavor { ZLIB = 1950, DEFLATE = 1951, GZIP = 1952 }

    internal class ZlibBaseStream : Stream
    {
        protected internal ZlibCodec? _zlibCodec; // deferred init... new ZlibCodec();

        protected internal StreamMode _streamMode = StreamMode.Undefined;
        protected internal FlushType _flushMode;
        protected internal ZlibStreamFlavor _flavor;
        protected internal CompressionMode _compressionMode;
        protected internal CompressionLevel _level;
        protected internal bool _leaveOpen;
        protected internal byte[]? _workingBuffer;
        protected internal int _bufferSize = ZlibConstants.WorkingBufferSizeDefault;
        protected internal byte[] _buf1 = new byte[1];

        protected internal Stream? _stream;
        protected internal CompressionStrategy Strategy = CompressionStrategy.Default;

        // workitem 7159
        private readonly Crc.CRC32? crc;
        private bool _nomoreinput;

        protected internal string? _GzipFileName;
        protected internal string? _GzipComment;
        protected internal DateTime _GzipMtime;
        protected internal int _gzipHeaderByteCount;

        internal int Crc32 { get { if (crc == null) return 0; return crc.Crc32Result; } }

        public ZlibBaseStream(Stream stream, CompressionMode compressionMode, CompressionLevel level, ZlibStreamFlavor flavor, bool leaveOpen)
        {
            _flushMode = FlushType.None;
            //this._workingBuffer = new byte[WORKING_BUFFER_SIZE_DEFAULT];
            _stream = stream;
            _leaveOpen = leaveOpen;
            _compressionMode = compressionMode;
            _flavor = flavor;
            _level = level;
            // workitem 7159
            if (flavor == ZlibStreamFlavor.GZIP)
            {
                crc = new Crc.CRC32();
            }
        }

        protected internal bool _wantCompress => _compressionMode == CompressionMode.Compress;

        private ZlibCodec ZlibCodec
        {
            get
            {
                if (_zlibCodec == null)
                {
                    bool wantRfc1950Header = (_flavor == ZlibStreamFlavor.ZLIB);
                    _zlibCodec = new ZlibCodec();
                    if (_compressionMode == CompressionMode.Decompress)
                    {
                        _zlibCodec.InitializeInflate(wantRfc1950Header);
                    }
                    else
                    {
                        _zlibCodec.Strategy = Strategy;
                        _zlibCodec.InitializeDeflate(_level, wantRfc1950Header);
                    }
                }
                return _zlibCodec;
            }
        }

        private byte[] WorkingBuffer
        {
            get
            {
                if (_workingBuffer == null)
                    RentAndReturn(ref _workingBuffer, _bufferSize);
                return _workingBuffer!;
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (_stream == null) throw new InvalidOperationException("Stream disposed");

            // workitem 7159
            // calculate the CRC on the unccompressed data  (before writing)
            crc?.SlurpBlock(buffer, offset, count);

            if (_streamMode == StreamMode.Undefined)
                _streamMode = StreamMode.Writer;
            else if (_streamMode != StreamMode.Writer)
                throw new ZlibException("Cannot Write after Reading.");

            if (count == 0)
                return;

            // first reference of z property will initialize the private var _z
            var zlibCodec = ZlibCodec;
            zlibCodec.InputBuffer = buffer;
            zlibCodec.NextIn = offset;
            zlibCodec.AvailableBytesIn = count;
            bool done;
            do
            {
                var workingBuffer = WorkingBuffer;
                zlibCodec.OutputBuffer = workingBuffer;
                zlibCodec.NextOut = 0;
                zlibCodec.AvailableBytesOut = workingBuffer.Length;
                int rc = (_wantCompress)
                    ? zlibCodec.Deflate(_flushMode)
                    : zlibCodec.Inflate(_flushMode);
                if (rc != ZlibConstants.Z_OK && rc != ZlibConstants.Z_STREAM_END)
                    throw new ZlibException($"{(_wantCompress ? "de" : "in")}flating: {zlibCodec.Message}");

                //if (workingBuffer.Length - zlibCodec.AvailableBytesOut > 0)
                _stream.Write(workingBuffer, 0, workingBuffer.Length - zlibCodec.AvailableBytesOut);

                done = zlibCodec.AvailableBytesIn == 0 && zlibCodec.AvailableBytesOut != 0;

                // If GZIP and de-compress, we're done when 8 bytes remain.
                if (_flavor == ZlibStreamFlavor.GZIP && !_wantCompress)
                    done = (zlibCodec.AvailableBytesIn == 8 && zlibCodec.AvailableBytesOut != 0);
            }
            while (!done);
        }

        private void Finish()
        {
            if (_zlibCodec == null) return;
            if (_stream == null) throw new InvalidOperationException("Stream disposed");

            if (_streamMode == StreamMode.Writer)
            {
                bool done;
                do
                {
                    var workingBuffer = WorkingBuffer;
                    _zlibCodec.OutputBuffer = workingBuffer;
                    _zlibCodec.NextOut = 0;
                    _zlibCodec.AvailableBytesOut = workingBuffer.Length;
                    int rc = (_wantCompress)
                        ? _zlibCodec.Deflate(FlushType.Finish)
                        : _zlibCodec.Inflate(FlushType.Finish);

                    if (rc != ZlibConstants.Z_STREAM_END && rc != ZlibConstants.Z_OK)
                    {
                        string verb = (_wantCompress ? "de" : "in") + "flating";
                        if (_zlibCodec.Message == null)
                            throw new ZlibException($"{verb}: (rc = {rc})");
                        else
                            throw new ZlibException(verb + ": " + _zlibCodec.Message);
                    }

                    if (workingBuffer.Length - _zlibCodec.AvailableBytesOut > 0)
                    {
                        _stream.Write(workingBuffer, 0, workingBuffer.Length - _zlibCodec.AvailableBytesOut);
                    }

                    done = _zlibCodec.AvailableBytesIn == 0 && _zlibCodec.AvailableBytesOut != 0;
                    // If GZIP and de-compress, we're done when 8 bytes remain.
                    if (_flavor == ZlibStreamFlavor.GZIP && !_wantCompress)
                        done = (_zlibCodec.AvailableBytesIn == 8 && _zlibCodec.AvailableBytesOut != 0);
                }
                while (!done);

                Flush();

                // workitem 7159
                if (_flavor == ZlibStreamFlavor.GZIP)
                {
                    if (_wantCompress)
                    {
                        // Emit the GZIP trailer: CRC32 and  size mod 2^32
                        var c1 = crc!.Crc32Result;
                        _stream.Write(BitConverter.GetBytes(c1), 0, 4);
                        var c2 = (int)(crc.TotalBytesRead & 0x00000000FFFFFFFF);
                        _stream.Write(BitConverter.GetBytes(c2), 0, 4);
                    }
                    else
                    {
                        throw new ZlibException("Writing with decompression is not supported.");
                    }
                }
            }
            // workitem 7159
            else if (_streamMode == StreamMode.Reader)
            {
                if (_flavor == ZlibStreamFlavor.GZIP)
                {
                    if (!_wantCompress)
                    {
                        // workitem 8501: handle edge case (decompress empty stream)
                        if (_zlibCodec.TotalBytesOut == 0L)
                            return;

                        // Do not validate the GZIP trailer, the System.Io.Compression library doesn't do it as well
                        // CRC32 and size mod 2^32

                    }
                    else
                    {
                        throw new ZlibException("Reading with compression is not supported.");
                    }
                }
            }
        }

        private void End()
        {
            if (ZlibCodec == null)
                return;
            if (_wantCompress)
            {
                _zlibCodec?.EndDeflate();
            }
            else
            {
                _zlibCodec?.EndInflate();
            }
            _zlibCodec = null;
        }

        public override void Close()
        {
            if (_stream == null) return;
            try
            {
                Finish();
            }
            finally
            {
                End();
                if (!_leaveOpen) _stream.Dispose();
                _stream = null;
            }
        }

        public override void Flush() => _stream?.Flush();

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
            //return _stream.Seek(offset, origin);
        }

        public override void SetLength(long value) => _stream?.SetLength(value);

        private string ReadZeroTerminatedString()
        {
            if (_stream == null) throw new InvalidOperationException("Stream disposed");

            var list = new System.Collections.Generic.List<byte>();
            bool done = false;
            do
            {
                // workitem 7740
                int n = _stream.Read(_buf1, 0, 1);
                if (n != 1)
                {
                    throw new ZlibException("Unexpected EOF reading GZIP header.");
                }
                else
                {
                    if (_buf1[0] == 0)
                        done = true;
                    else
                        list.Add(_buf1[0]);
                }
            } while (!done);
            byte[] a = list.ToArray();
            return GZipStream.iso8859dash1.GetString(a, 0, a.Length);
        }

        private int _ReadAndValidateGzipHeader()
        {
            if (_stream == null) throw new InvalidOperationException("Stream disposed");

            int totalBytesRead = 0;
            // read the header on the first read
            Span<byte> header = stackalloc byte[10];
            int n = _stream.Read(header);

            // workitem 8501: handle edge case (decompress empty stream)
            if (n == 0)
                return 0;

            if (n != 10)
                throw new ZlibException("Not a valid GZIP stream.");

            if (header[0] != 0x1F || header[1] != 0x8B || header[2] != 8)
                throw new ZlibException("Bad GZIP header.");

            var timet = BitConverter.ToInt32(header.Slice(4));
            _GzipMtime = GZipStream._unixEpoch.AddSeconds(timet);
            totalBytesRead += n;
            if ((header[3] & 0x04) == 0x04)
            {
                // read and discard extra field
                n = _stream.Read(header.Slice(0, 2)); // 2-byte length field
                totalBytesRead += n;

                var extraLength = (short)(header[0] + (header[1] * 256));
                Span<byte> extra = stackalloc byte[extraLength];
                n = _stream.Read(extra);
                if (n != extraLength)
                    throw new ZlibException("Unexpected end-of-file reading GZIP header.");
                totalBytesRead += n;
            }
            if ((header[3] & 0x08) == 0x08)
                _GzipFileName = ReadZeroTerminatedString();
            if ((header[3] & 0x10) == 0x010)
                _GzipComment = ReadZeroTerminatedString();
            if ((header[3] & 0x02) == 0x02)
                Read(_buf1, 0, 1); // CRC16, ignore

            return totalBytesRead;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_stream == null) throw new InvalidOperationException("Stream disposed");

            // According to MS documentation, any implementation of the IO.Stream.Read function must:
            // (a) throw an exception if offset & count reference an invalid part of the buffer,
            //     or if count < 0, or if buffer is null
            // (b) return 0 only upon EOF, or if count = 0
            // (c) if not EOF, then return at least 1 byte, up to <count> bytes

            var zlibCodec = ZlibCodec;

            if (_streamMode == StreamMode.Undefined)
            {
                if (!_stream.CanRead) throw new ZlibException("The stream is not readable.");
                // for the first read, set up some controls.
                _streamMode = StreamMode.Reader;
                // (The first reference to _z goes through the private accessor which
                // may initialize it.)
                zlibCodec.AvailableBytesIn = 0;
                if (_flavor == ZlibStreamFlavor.GZIP)
                {
                    _gzipHeaderByteCount = _ReadAndValidateGzipHeader();
                    // workitem 8501: handle edge case (decompress empty stream)
                    if (_gzipHeaderByteCount == 0)
                        return 0;
                }
            }

            if (_streamMode != StreamMode.Reader)
                throw new ZlibException("Cannot Read after Writing.");

            if (count == 0) return 0;
            if (_nomoreinput && _wantCompress) return 0;  // workitem 8557
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (offset < buffer.GetLowerBound(0)) throw new ArgumentOutOfRangeException(nameof(offset));
            if ((offset + count) > buffer.GetLength(0)) throw new ArgumentOutOfRangeException(nameof(count));

            // set up the output of the deflate/inflate codec:
            zlibCodec.OutputBuffer = buffer;
            zlibCodec.NextOut = offset;
            zlibCodec.AvailableBytesOut = count;

            // This is necessary in case _workingBuffer has been resized. (new byte[])
            // (The first reference to _workingBuffer goes through the private accessor which
            // may initialize it.)
            var workingBuffer = WorkingBuffer;
            zlibCodec.InputBuffer = WorkingBuffer;

            int rc;
            do
            {
                // need data in _workingBuffer in order to deflate/inflate.  Here, we check if we have any.
                if ((zlibCodec.AvailableBytesIn == 0) && (!_nomoreinput))
                {
                    // No data available, so try to Read data from the captive stream.
                    zlibCodec.NextIn = 0;
                    zlibCodec.AvailableBytesIn = _stream.Read(workingBuffer, 0, workingBuffer.Length);
                    if (zlibCodec.AvailableBytesIn == 0)
                        _nomoreinput = true;
                }
                // we have data in InputBuffer; now compress or decompress as appropriate
                rc = (_wantCompress)
                    ? zlibCodec.Deflate(_flushMode)
                    : zlibCodec.Inflate(_flushMode);

                if (_nomoreinput && (rc == ZlibConstants.Z_BUF_ERROR))
                    return 0;

                if (rc != ZlibConstants.Z_OK && rc != ZlibConstants.Z_STREAM_END)
                    throw new ZlibException($"{(_wantCompress ? "de" : "in")}flating:  rc={rc}  msg={zlibCodec.Message}");

                if ((_nomoreinput || rc == ZlibConstants.Z_STREAM_END) && (zlibCodec.AvailableBytesOut == count))
                    break; // nothing more to read
            }
            //while (zlibCodec.AvailableBytesOut == count && rc == ZlibConstants.Z_OK);
            while (zlibCodec.AvailableBytesOut > 0 && !_nomoreinput && rc == ZlibConstants.Z_OK);

            // workitem 8557
            // is there more room in output?
            if (zlibCodec.AvailableBytesOut > 0)
            {
                if (rc == ZlibConstants.Z_OK && zlibCodec.AvailableBytesIn == 0)
                {
                    // deferred
                }

                // are we completely done reading?
                if (_nomoreinput)
                {
                    // and in compression?
                    if (_wantCompress)
                    {
                        // no more input data available; therefore we flush to
                        // try to complete the read
                        rc = zlibCodec.Deflate(FlushType.Finish);

                        if (rc != ZlibConstants.Z_OK && rc != ZlibConstants.Z_STREAM_END)
                            throw new ZlibException($"Deflating:  rc={rc}  msg={zlibCodec.Message}");
                    }
                }
            }

            rc = (count - zlibCodec.AvailableBytesOut);

            // calculate CRC after reading
            crc?.SlurpBlock(buffer, offset, rc);

            return rc;
        }

        public override bool CanRead => _stream?.CanRead ?? false;

        public override bool CanSeek => _stream?.CanSeek ?? false;

        public override bool CanWrite => _stream?.CanWrite ?? false;

        public override long Length => _stream?.Length ?? -1;

        public override long Position
        {
            get => throw new NotImplementedException();
            set => throw new NotImplementedException();
        }

        internal enum StreamMode
        {
            Writer,
            Reader,
            Undefined,
        }

        public static void CompressString(string s, Stream compressor)
        {
            byte[] uncompressed = System.Text.Encoding.UTF8.GetBytes(s);
            using (compressor)
            {
                compressor.Write(uncompressed, 0, uncompressed.Length);
            }
        }

        public static void CompressBuffer(byte[] b, Stream compressor)
        {
            // workitem 8460
            using (compressor)
            {
                compressor.Write(b, 0, b.Length);
            }
        }

        public static string UncompressString(byte[] compressed, Stream decompressor)
        {
            // workitem 8460
            Span<byte> working = stackalloc byte[1024];
            var encoding = System.Text.Encoding.UTF8;
            using var output = new MemoryStream();
            using (decompressor)
            {
                int n;
                while ((n = decompressor.Read(working)) != 0)
                {
                    output.Write(working.Slice(0, n));
                }
            }

            // reset to allow read from start
            output.Seek(0, SeekOrigin.Begin);
            using var sr = new StreamReader(output, encoding);
            return sr.ReadToEnd();
        }

        public static byte[] UncompressBuffer(byte[] compressed, Stream decompressor)
        {
            // workitem 8460
            Span<byte> working = stackalloc byte[1024];
            using var output = new MemoryStream();
            using (decompressor)
            {
                int n;
                while ((n = decompressor.Read(working)) != 0)
                {
                    output.Write(working.Slice(0, n));
                }
            }
            return output.ToArray();
        }

        protected override void Dispose(bool disposing)
        {
            Return(ref _workingBuffer!);
            _zlibCodec?.Dispose();
            base.Dispose(disposing);
        }
    }
}
