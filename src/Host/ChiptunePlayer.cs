using System;
using System.IO;
using System.IO.Compression;
using System.Media;
using System.Reflection;

namespace PclPatchHost
{
    internal sealed class ChiptunePlayer : IDisposable
    {
        private SoundPlayer player;
        private MemoryStream source;
        private bool loaded;

        public bool IsPlaying { get; private set; }

        public bool Toggle()
        {
            if (IsPlaying)
            {
                Stop();
                return false;
            }
            return Play();
        }

        public bool Play()
        {
            if (!loaded && !Load()) return false;
            try
            {
                player.PlayLooping();
                IsPlaying = true;
            }
            catch
            {
                IsPlaying = false;
            }
            return IsPlaying;
        }

        public void Stop()
        {
            try
            {
                if (player != null) player.Stop();
            }
            catch { }
            IsPlaying = false;
        }

        private bool Load()
        {
            try
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PclPatchHost.chiptune.wav.gz"))
                {
                    if (stream == null) return false;
                    using (var gzip = new GZipStream(stream, CompressionMode.Decompress))
                    {
                        source = new MemoryStream();
                        gzip.CopyTo(source);
                    }
                    source.Position = 0;
                    player = new SoundPlayer(source);
                    player.Load();
                    loaded = true;
                    return true;
                }
            }
            catch
            {
                loaded = false;
                return false;
            }
        }

        public void Dispose()
        {
            Stop();
            if (player != null) player.Dispose();
            if (source != null) source.Dispose();
            player = null;
            source = null;
            loaded = false;
        }
    }
}
