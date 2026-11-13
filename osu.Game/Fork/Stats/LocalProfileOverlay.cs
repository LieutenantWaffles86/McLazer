// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Game.Database;
using osu.Game.Extensions;
using osu.Game.Fork.Profiles;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Scoring;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Fork.Stats
{
    /// <summary>
    /// A local profile: name, total pp, accuracy, play count, grades, top plays and recent plays.
    /// </summary>
    /// <remarks>
    /// Lazer's profile overlay is built around online API responses, so this is a small one of McLazer's own.
    /// </remarks>
    public partial class LocalProfileOverlay : OsuFocusedOverlayContainer
    {
        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        [Resolved]
        private ProfileStore profiles { get; set; } = null!;

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        private Container panel = null!;
        private FillFlowContainer content = null!;

        /// <summary>
        /// The profile shown last.
        /// </summary>
        public int? ProfileId { get; private set; }

        public LocalProfileOverlay()
        {
            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Children = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Black, Alpha = 0.6f },
                panel = new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Size = new Vector2(0.6f, 0.8f),
                    Masking = true,
                    CornerRadius = 10,
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = colours.Gray2 },
                        new OsuScrollContainer
                        {
                            RelativeSizeAxes = Axes.Both,
                            Child = content = new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Vertical,
                                Padding = new MarginPadding(20),
                                Spacing = new Vector2(0, 6),
                            }
                        }
                    }
                }
            };
        }

        public void ShowProfile(int profileId)
        {
            ProfileId = profileId;

            string name = profiles.Profiles.FirstOrDefault(p => p.Id == profileId)?.Username ?? ProfileStore.DELETED_PLAYER;
            var stats = LocalStats.For(realm, profileId);

            var children = new List<Drawable>
            {
                text(name, 30, FontWeight.Bold),
                text($@"{stats.TotalPP.ToString(@"N0", CultureInfo.InvariantCulture)}pp    {stats.Accuracy.ToString(@"P2", CultureInfo.InvariantCulture)} accuracy    {stats.PlayCount} plays", 20),
                text(string.Join(@"    ", new[] { ScoreRank.XH, ScoreRank.X, ScoreRank.SH, ScoreRank.S, ScoreRank.A }.Select(r => $@"{rankName(r)}: {stats.Grades.GetValueOrDefault(r)}")), 16),
                text(@"pp comes from your own passed scores on ranked maps. Practice runs and lazer's scores never count.", 13, colour: colours.Gray9),
                text(@"Top plays", 22, FontWeight.SemiBold),
            };

            children.AddRange(stats.TopPlays.Count == 0 ? new[] { text(@"No pp scores yet.", 14) } : stats.TopPlays.Select(row));
            children.Add(text(@"Recent plays", 22, FontWeight.SemiBold));
            children.AddRange(stats.RecentPlays.Count == 0 ? new[] { text(@"No scores yet.", 14) } : stats.RecentPlays.Select(row));

            content.Children = children;
            Show();
        }

        private Drawable row(ScoreInfo score)
        {
            string pp = score.PP is double value ? $@"{value.ToString(@"N0", CultureInfo.InvariantCulture)}pp" : @"-";
            string mods = string.Concat(score.Mods.Select(m => m.Acronym));

            return text($@"{pp}   {rankName(score.Rank)}   {score.Accuracy.ToString(@"P2", CultureInfo.InvariantCulture)}   {(mods.Length > 0 ? $@"+{mods}   " : string.Empty)}"
                        + $@"{score.BeatmapInfo?.GetDisplayString() ?? @"(beatmap removed)"}   {score.Date.ToLocalTime():yyyy-MM-dd}", 14);
        }

        private static Drawable text(string value, float size, FontWeight weight = FontWeight.Regular, Color4? colour = null) => new OsuTextFlowContainer(s =>
        {
            s.Font = OsuFont.GetFont(size: size, weight: weight);

            if (colour != null)
                s.Colour = colour.Value;
        })
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Text = value,
        };

        private static string rankName(ScoreRank rank) => rank switch
        {
            ScoreRank.XH => @"SSH",
            ScoreRank.X => @"SS",
            _ => rank.ToString(),
        };

        // Clicking beside the panel closes it.
        protected override bool OnClick(ClickEvent e)
        {
            if (!panel.ReceivePositionalInputAt(e.ScreenSpaceMousePosition))
                Hide();

            return true;
        }

        protected override void PopIn() => this.FadeIn(200, Easing.OutQuint);

        protected override void PopOut()
        {
            base.PopOut();
            this.FadeOut(200, Easing.OutQuint);
        }
    }
}
