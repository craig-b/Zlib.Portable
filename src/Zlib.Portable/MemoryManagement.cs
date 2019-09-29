using System.Buffers;

namespace Ionic.Zlib
{
    internal static class MemoryManagement
    {
        private static readonly ArrayPool<byte> byteArayPool = ArrayPool<byte>.Create();
        private static readonly ArrayPool<sbyte> sbyteArayPool = ArrayPool<sbyte>.Create();
        private static readonly ArrayPool<int> intArayPool = ArrayPool<int>.Create();
        private static readonly ArrayPool<short> shortArrayPool = ArrayPool<short>.Create();

        //private static readonly ArrayPool<byte> byteArayPool = ArrayPool<byte>.Shared;
        //private static readonly ArrayPool<sbyte> sbyteArayPool = ArrayPool<sbyte>.Shared;
        //private static readonly ArrayPool<int> intArayPool = ArrayPool<int>.Shared;
        //private static readonly ArrayPool<short> shortArrayPool = ArrayPool<short>.Shared;

        internal static void Return(ref byte[]? array) => RentAndReturn(ref array, -1);

        internal static void RentAndReturn(ref byte[]? array, int length)
        {
            //var arrayPool = ArrayPool<byte>.Shared;
            var arrayPool = byteArayPool;
            if (array != null)
                arrayPool.Return(array);
            if (length < 0)
                array = null;
            else
                array = arrayPool.Rent(length);
        }

        internal static void Return(ref short[]? array) => RentAndReturn(ref array, -1);

        internal static void RentAndReturn(ref short[]? array, int length)
        {
            //var arrayPool = ArrayPool<short>.Shared;
            var arrayPool = shortArrayPool;
            if (array != null)
                arrayPool.Return(array);
            if (length < 0)
                array = null;
            else
                array = arrayPool.Rent(length);
        }

        internal static void Return(ref int[]? array) => RentAndReturn(ref array, -1);

        internal static void RentAndReturn(ref int[]? array, int length)
        {
            //var arrayPool = ArrayPool<int>.Shared;
            var arrayPool = intArayPool;
            if (array != null)
                arrayPool.Return(array);
            if (length < 0)
                array = null;
            else
                array = arrayPool.Rent(length);
        }

        internal static void Return(ref sbyte[]? array) => RentAndReturn(ref array, -1);

        internal static void RentAndReturn(ref sbyte[]? array, int length)
        {
            //var arrayPool = ArrayPool<sbyte>.Shared;
            var arrayPool = sbyteArayPool;
            if (array != null)
                arrayPool.Return(array);
            if (length < 0)
                array = null;
            else
                array = arrayPool.Rent(length);
        }
    }
}
