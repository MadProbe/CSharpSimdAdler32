using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics;

BenchmarkRunner.Run<BenchmarkAdler32>();
namespace Benchmark {

    [DisassemblyDiagnoser(syntax: BenchmarkDotNet.Diagnosers.DisassemblySyntax.Intel)]
    public class BenchmarkAdler32 {
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private ref struct SpanBlockIterator<T> {
            private readonly Span<T> _values;
            private readonly int _blockSize;
            private readonly int _size;
            private int _index = -1;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public SpanBlockIterator(Span<T> values, int blockSize) {
                this._values = values;
                this._blockSize = blockSize;
                this._size = values.Length / blockSize;
            }
            public Span<T> Current {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => this._values[(this._index * this._blockSize)..];
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext() => ++this._index < this._size;
            public Span<T> Remaining {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => this._values[(this._size * this._blockSize)..];
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public SpanBlockIterator<T> GetEnumerator() => this;
        }
        [Benchmark]
        [ArgumentsSource(nameof(Data))]
        [MethodImpl(MethodImplOptions.NoInlining)]
        [SkipLocalsInit]
        public unsafe uint Vectorized(byte[] data) {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(data.Length, 0x20000, nameof(data)); // cannot guarantee safety (for now)
            ref byte dataRef = ref MemoryMarshal.GetArrayDataReference(data);
            Vector256<ulong> vadlerA_ = Vector256<ulong>.One;
            Vector256<uint> vadlerBmult_ = Vector256<uint>.Zero;
            Vector256<ulong> vadlerBA_ = Vector256<ulong>.Zero;
            ref Vector256<ulong> vadlerA = ref vadlerA_;
            ref Vector256<uint> vadlerBmult = ref vadlerBmult_;
            ref Vector256<ulong> vadlerBA = ref vadlerBA_;
            Vector256<byte> vbytezero = Vector256<byte>.Zero;

            var mults_vector = Vector256.Create(multipliers);
            Vector256<short> vone_short = Vector256<short>.One;
            for (ref byte end = ref Unsafe.AddByteOffset(ref dataRef, data.Length - data.Length % Vector256<byte>.Count);
                Unsafe.IsAddressLessThan(ref dataRef, ref end); dataRef = ref Unsafe.AddByteOffset(ref dataRef, Vector256<byte>.Count)) {
                var bytes = Vector256.LoadUnsafe(ref dataRef);
                vadlerBA = Avx2.Add(vadlerBA, vadlerA);
                vadlerBmult = Avx2.Add(vadlerBmult, Avx2.MultiplyAddAdjacent(Avx2.MultiplyAddAdjacent(bytes, mults_vector), vone_short).AsUInt32());
                vadlerA = Avx2.Add(vadlerA, Avx2.SumAbsoluteDifferences(bytes, vbytezero).AsUInt64());
            }
            (Vector256<ulong> lower, Vector256<ulong> upper) = Vector256.Widen(vadlerBmult);
            ulong adlerB = Vector512.Sum(Vector512.Create(lower, upper)) + Vector256.Sum(vadlerBA);
            ulong adlerA = Vector256.Sum(vadlerA);
            Span<byte> remaining = new SpanBlockIterator<byte>(data, Vector256<byte>.Count).Remaining;
            adlerA %= 0xFFF1UL;
            adlerB %= 0xFFF1UL;

            for (int i = 0, length = remaining.Length; i < length; i++) {
                adlerA += remaining[i];
                adlerB += adlerA;
            }

            return (uint)(adlerB % 0xFFF1 << 16 | adlerA % 0xFFF1);
        }
        private static readonly sbyte[] multipliers = new sbyte[(256 / (sizeof(sbyte) * 8))] { 32, 31, 30, 29, 28, 27, 26, 25, 24, 23, 22, 21, 20, 19, 18, 17, 16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 };
        [Benchmark(Baseline = true)]
        [ArgumentsSource(nameof(Data))]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public uint Naive(byte[] data) {
            ulong adlerA = 1;
            ulong adlerB = 0;
            adlerA %= 0xFFF1;
            adlerB %= 0xFFF1;

            for (int i = 0, length = data.Length; i < length; i++) {
                adlerA = (adlerA + data[i]) % 0xFFF1;
                adlerB = (adlerB + adlerA) % 0xFFF1;
            }

            return (uint)(adlerB % 0xFFF1 << 16 | adlerA % 0xFFF1);
        }
        public static byte[][] Data() {
            byte[][] bytes = new[] {
                GC.AllocateUninitializedArray<byte>(102400), // 100KB
            };
            Random.Shared.NextBytes(bytes[0]);
            return bytes;
        }
    }
}