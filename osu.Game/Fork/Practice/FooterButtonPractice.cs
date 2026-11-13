// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Screens.Footer;

namespace osu.Game.Fork.Practice
{
    /// <summary>
    /// The practice toggle at song select. When on, runs start as practice runs.
    /// </summary>
    public partial class FooterButtonPractice : ScreenFooterButton
    {
        public readonly BindableBool Current = new BindableBool();

        [BackgroundDependencyLoader]
        private void load(OsuColour colours)
        {
            Text = @"Practice";
            Icon = FontAwesome.Solid.Dumbbell;
            AccentColour = colours.Lime1;
            Action = Current.Toggle;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // The footer lights a button while its overlay is open. Practice has no overlay, so it lights while on.
            Current.BindValueChanged(on => OverlayState.Value = on.NewValue ? Visibility.Visible : Visibility.Hidden, true);
        }
    }
}
