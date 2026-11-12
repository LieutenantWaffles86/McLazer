// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.IO;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Testing;
using osu.Game.Tests.Visual;

namespace osu.Game.Tests.Fork
{
    [HeadlessTest]
    public partial class TestSceneForkResources : OsuTestScene
    {
        [Resolved]
        private OsuGameBase game { get; set; } = null!;

        [Test]
        public void TestForkLogoReplacesUpstreamLogo()
        {
            AddAssert("menu logo is the fork's", () => game.Resources.Get(@"Textures/Menu/logo.png"), () => Is.EqualTo(forkLogo()));
        }

        private static byte[] forkLogo()
        {
            using (var stream = typeof(OsuGameBase).Assembly.GetManifestResourceStream(@"osu.Game.Resources.Textures.Menu.logo.png")!)
            using (var memory = new MemoryStream())
            {
                stream.CopyTo(memory);
                return memory.ToArray();
            }
        }
    }
}
