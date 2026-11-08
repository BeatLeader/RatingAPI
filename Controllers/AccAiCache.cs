using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace RatingAPI.Controllers
{
    // The Acc AI's raw output by its exact input, kept on disk between rerates: inference is 80% of a rerate's rating time, and
    // most rerates change neither the model nor the notes' features, only what is done with the predictions. An entry is found
    // by the hash of the windows the model would get, in a file per model, so a changed model or feature can't return an old
    // result. Off unless Load was called (portaBLe's RatingsRefresh.Overwrite); the API itself doesn't use it.
    public static class AccAiCache
    {
        private const int Version = 1;
        private static ConcurrentDictionary<UInt128, float[]>? _entries;
        private static ConcurrentDictionary<UInt128, float[]>? _used;
        private static string? _path;
        private static int _hits, _misses;

        public static bool Enabled => _entries != null;

        public static void Load(string folder, byte[] model)
        {
            Directory.CreateDirectory(folder);
            _path = Path.Combine(folder, $"accai-{Convert.ToHexString(SHA256.HashData(model))[..16]}.bin");
            _entries = new();
            _used = new();
            _hits = _misses = 0;
            if (!File.Exists(_path)) return;
            try
            {
                using var reader = new BinaryReader(new BufferedStream(File.OpenRead(_path), 1 << 20));
                if (reader.ReadInt32() != Version) return;
                int count = reader.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    var key = new UInt128(reader.ReadUInt64(), reader.ReadUInt64());
                    var values = new float[reader.ReadInt32()];
                    reader.BaseStream.ReadExactly(MemoryMarshal.AsBytes(values.AsSpan()));
                    _entries[key] = values;
                }
            }
            catch (Exception e) when (e is IOException)
            {
                Console.WriteLine($"Acc AI cache: {_path} unreadable, starting over ({e.Message})");
                _entries.Clear();
            }
        }

        // Saves what this run used (entries of older inputs go), and removes the files of other models. Off again after it.
        public static void Save()
        {
            if (_used == null || _path == null) return;
            string temp = _path + ".tmp";
            using (var writer = new BinaryWriter(new BufferedStream(File.Create(temp), 1 << 20)))
            {
                writer.Write(Version);
                writer.Write(_used.Count);
                foreach (var (key, values) in _used)
                {
                    writer.Write((ulong)(key >> 64));
                    writer.Write((ulong)key);
                    writer.Write(values.Length);
                    writer.Write(MemoryMarshal.AsBytes(values.AsSpan()));
                }
            }
            File.Move(temp, _path, true);
            foreach (var other in Directory.GetFiles(Path.GetDirectoryName(_path)!, "accai-*.bin"))
            {
                if (!string.Equals(other, _path, StringComparison.OrdinalIgnoreCase)) File.Delete(other);
            }
            Console.WriteLine($"Acc AI cache: {_hits} of {_hits + _misses} predictions reused, {new FileInfo(_path).Length / (1 << 20)} MB");
            _entries = null;
            _used = null;
        }

        public static UInt128 Key(List<List<double[]>> segments)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new float[256];
            foreach (var segment in segments)
            {
                foreach (var note in segment)
                {
                    if (note.Length > buffer.Length) buffer = new float[note.Length];
                    for (int i = 0; i < note.Length; i++) buffer[i] = (float)note[i];
                    hash.AppendData(MemoryMarshal.AsBytes(buffer.AsSpan(0, note.Length)));
                }
            }
            var digest = hash.GetHashAndReset();
            return new UInt128(BitConverter.ToUInt64(digest, 0), BitConverter.ToUInt64(digest, 8));
        }

        public static float[]? Get(UInt128 key)
        {
            if (_entries != null && _entries.TryGetValue(key, out var values))
            {
                _used![key] = values;
                Interlocked.Increment(ref _hits);
                return values;
            }
            Interlocked.Increment(ref _misses);
            return null;
        }

        public static void Put(UInt128 key, float[] values)
        {
            if (_entries == null) return;
            _entries[key] = values;
            _used![key] = values;
        }
    }
}
