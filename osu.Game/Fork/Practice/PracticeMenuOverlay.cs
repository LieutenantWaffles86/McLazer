// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Localisation;
using osu.Game.Screens.Play;
using osuTK.Graphics;

namespace osu.Game.Fork.Practice
{
    /// <summary>
    /// A menu over gameplay, in the style of the pause menu. Esc picks the last button.
    /// </summary>
    public partial class PracticeMenuOverlay : GameplayMenuOverlay
    {
        public override LocalisableString Header { get; }

        public PracticeMenuOverlay(LocalisableString header)
        {
            Header = header;
        }

        /// <summary>
        /// Replace the buttons. Call after the overlay has loaded.
        /// </summary>
        public void SetButtons(params (LocalisableString Text, Color4 Colour, Action Action)[] buttons)
        {
            InternalButtons.Clear();

            foreach (var (text, colour, action) in buttons)
                AddButton(text, colour, action);
        }
    }
}
