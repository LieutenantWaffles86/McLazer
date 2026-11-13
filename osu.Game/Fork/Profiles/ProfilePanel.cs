// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Game.Fork.Stats;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Overlays.Dialog;
using osuTK;

namespace osu.Game.Fork.Profiles
{
    /// <summary>
    /// The profile picker. It sits where lazer's login form was, behind the toolbar's user button.
    /// </summary>
    public partial class ProfilePanel : FillFlowContainer
    {
        public Action? RequestHide;

        [Resolved]
        private ProfileStore profiles { get; set; } = null!;

        [Resolved]
        private IDialogOverlay? dialogOverlay { get; set; }

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        [Resolved]
        private LocalProfileOverlay? profileOverlay { get; set; }

        private FillFlowContainer list = null!;
        private OsuTextBox nameBox = null!;
        private OsuTextFlowContainer status = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
            Direction = FillDirection.Vertical;
            Spacing = new Vector2(0, 8);
            Padding = new MarginPadding(20);

            Children = new Drawable[]
            {
                new OsuSpriteText { Text = @"Profiles", Font = OsuFont.GetFont(size: 20, weight: FontWeight.Bold) },
                list = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 4),
                },
                nameBox = new OsuTextBox { RelativeSizeAxes = Axes.X, PlaceholderText = @"Profile name" },
                new RoundedButton { RelativeSizeAxes = Axes.X, Text = @"View profile", Action = viewProfile },
                new RoundedButton { RelativeSizeAxes = Axes.X, Text = @"New profile", Action = create },
                new RoundedButton { RelativeSizeAxes = Axes.X, Text = @"Rename", Action = rename },
                new DangerousRoundedButton { RelativeSizeAxes = Axes.X, Text = @"Delete", Action = delete },
                status = new OsuTextFlowContainer { RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y },
            };

            profiles.Changed += refresh;
            refresh();
        }

        private void refresh() => Schedule(() =>
        {
            list.Children = profiles.Profiles.Select(p => new RoundedButton
            {
                RelativeSizeAxes = Axes.X,
                Text = p.Username,
                BackgroundColour = p == profiles.Active ? colours.Green : colours.Gray4,
                Action = () =>
                {
                    profiles.Switch(p);
                    RequestHide?.Invoke();
                },
            }).ToArray();
        });

        private void viewProfile()
        {
            RequestHide?.Invoke();
            profileOverlay?.ShowProfile(profiles.Active.Id);
        }

        private void create() => report(profiles.Create(nameBox.Text) != null);

        private void rename() => report(profiles.Rename(profiles.Active, nameBox.Text));

        private void report(bool done)
        {
            status.Text = done ? string.Empty : @"Type a name that no other profile has.";

            if (done)
                nameBox.Text = string.Empty;
        }

        private void delete()
        {
            var profile = profiles.Active;

            if (profiles.Profiles.Count == 1)
            {
                status.Text = @"The last profile cannot be deleted.";
                return;
            }

            dialogOverlay?.Push(new DeleteProfileDialog(profile, deleteScores => profiles.Delete(profile, deleteScores)));
        }

        private partial class DeleteProfileDialog : PopupDialog
        {
            public DeleteProfileDialog(Profile profile, Action<bool> delete)
            {
                HeaderText = $@"Delete the profile ""{profile.Username}""?";
                Icon = FontAwesome.Regular.TrashAlt;
                Buttons = new PopupDialogButton[]
                {
                    new PopupDialogDangerousButton { Text = @"Delete profile and delete scores", Action = () => delete(true) },
                    new PopupDialogOkButton { Text = @"Delete profile and keep scores", Action = () => delete(false) },
                    new PopupDialogCancelButton { Text = @"Cancel" },
                };
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (profiles.IsNotNull())
                profiles.Changed -= refresh;
        }
    }
}
