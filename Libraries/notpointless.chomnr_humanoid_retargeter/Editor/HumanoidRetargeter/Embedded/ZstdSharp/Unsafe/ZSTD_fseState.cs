#nullable enable
namespace HumanoidRetargeterZstd.Unsafe
{
    public unsafe struct ZSTD_fseState
    {
        public nuint state;
        public ZSTD_seqSymbol* table;
    }
}
