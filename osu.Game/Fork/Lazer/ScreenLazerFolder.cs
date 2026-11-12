// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.IO;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Platform;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;

namespace osu.Game.Fork.Lazer
{
    /// <summary>
    /// First-run step that asks for the osu!lazer data folder. Shown only when the default folder was not found.
    /// </summary>
    [System.ComponentModel.Description("Lazer folder")]
    public partial class ScreenLazerFolder : WizardScreen
    {
        [Resolved]
        private Storage storage { get; set; } = null!;

        private OsuDirectorySelector selector = null!;
        private OsuTextFlowContainer status = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            string ownFolder = storage.GetFullPath(string.Empty);

            Content.Children = new Drawable[]
            {
                new OsuTextFlowContainer(cp => cp.Font = OsuFont.Default.With(size: CONTENT_FONT_SIZE))
                {
                    Text = @"McLazer plays the beatmaps of your osu!lazer install. Choose lazer's data folder: the one that holds client.realm. McLazer only reads from it.",
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y
                },
                new Container
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 300,
                    // Lazer's default folder sits next to McLazer's own, so start there.
                    Child = selector = new OsuDirectorySelector(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(ownFolder)))
                    {
                        RelativeSizeAxes = Axes.Both,
                    }
                },
                new RoundedButton
                {
                    RelativeSizeAxes = Axes.X,
                    Text = @"Use this folder",
                    Action = () => Choose(selector.CurrentPath.Value.FullName),
                },
                status = new OsuTextFlowContainer(cp => cp.Font = OsuFont.Default.With(size: CONTENT_FONT_SIZE))
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y
                },
            };
        }

        public void Choose(string path)
        {
            if (LazerFolder.Resolve(path) == null)
            {
                status.Text = @"There is no lazer database (client.realm) in this folder.";
                return;
            }

            LazerFolder.SavePath(storage.GetFullPath(string.Empty), path);

            // The file store and the sync are set up at startup, so the choice applies from the next start.
            status.Text = @"Saved. Restart McLazer to load your beatmaps.";
        }
    }
}
