// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.IO;
using osu.Framework.Platform;
using osu.Game.IO;

namespace osu.Game.Fork.Lazer
{
    /// <summary>
    /// A storage that reads a file from a fallback storage when it does not have the file itself.
    /// Writes and deletes only ever touch the main storage.
    /// </summary>
    public class FallbackReadStorage : WrappedStorage
    {
        private readonly Storage fallback;

        public FallbackReadStorage(Storage main, Storage fallback)
            : base(main)
        {
            this.fallback = fallback;
        }

        private bool useFallback(string path) => !base.Exists(path) && fallback.Exists(path);

        public override bool Exists(string path) => base.Exists(path) || fallback.Exists(path);

        public override Stream GetStream(string path, FileAccess access = FileAccess.Read, FileMode mode = FileMode.OpenOrCreate) =>
            access == FileAccess.Read && useFallback(path) ? fallback.GetStream(path) : base.GetStream(path, access, mode);

        public override string GetFullPath(string path, bool createIfNotExisting = false) =>
            !createIfNotExisting && useFallback(path) ? fallback.GetFullPath(path) : base.GetFullPath(path, createIfNotExisting);
    }
}
