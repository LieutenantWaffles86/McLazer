// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace osu.Game.Fork.Practice
{
    /// <summary>
    /// The map's strain graph, shown while the scrub key is held, as in McOsu. Clicking or dragging on it jumps there; right-clicking sets the checkpoint.
    /// </summary>
    /// <remarks>
    /// Markers: the checkpoint (green diamond), misses (red ticks), where health hit zero (yellow lines) and skipped time (light bands).
    /// </remarks>
    public partial class ScrubOverlay : CompositeDrawable
    {
        /// <summary>
        /// Graph columns. Objects are merged into this many, so long maps stay cheap to draw.
        /// </summary>
        private const int max_columns = 300;

        /// <summary>
        /// McOsu draws these two. Lazer has two aim skills (with and without sliders); the first one counts sliders.
        /// </summary>
        private static readonly (string Skill, Color4 Colour)[] layers =
        {
            (@"Aim", new Color4(255, 100, 150, 180)),
            (@"Speed", new Color4(100, 180, 255, 180)),
        };

        private readonly double startTime;
        private readonly double endTime;
        private readonly Func<double> currentTime;
        private readonly Action<double> seek;
        private readonly Action<double> setCheckpoint;

        private readonly Box position;
        private readonly Container markers;

        /// <param name="difficulties">Each skill's difficulty for every object, at the object's time.</param>
        /// <param name="startTime">The time at the graph's left edge: the first object.</param>
        /// <param name="endTime">The time at the graph's right edge: the last object.</param>
        /// <param name="currentTime">The gameplay time, for the position marker.</param>
        /// <param name="seek">Jumps to a time.</param>
        /// <param name="setCheckpoint">Sets the checkpoint at a time.</param>
        public ScrubOverlay(IReadOnlyList<(Type Skill, (double Time, double Difficulty)[] Objects)> difficulties, double startTime, double endTime, Func<double> currentTime,
                            Action<double> seek, Action<double> setCheckpoint)
        {
            this.startTime = startTime;
            this.endTime = endTime;
            this.currentTime = currentTime;
            this.seek = seek;
            this.setCheckpoint = setCheckpoint;

            Anchor = Anchor.BottomCentre;
            Origin = Anchor.BottomCentre;
            RelativeSizeAxes = Axes.X;
            Height = 150;
            Alpha = 0;

            var graphs = layers.Select(l => (Objects: difficulties.FirstOrDefault(d => d.Skill.Name == l.Skill).Objects, l.Colour))
                               .Where(g => g.Objects?.Length > 0)
                               .Select(g => createGraph(g.Objects, g.Colour));

            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Black, Alpha = 0.6f },
            }.Concat(graphs).Append(markers = new Container { RelativeSizeAxes = Axes.Both }).Append(position = new Box
            {
                RelativePositionAxes = Axes.X,
                RelativeSizeAxes = Axes.Y,
                Width = 2,
                Origin = Anchor.TopCentre,
                Colour = Color4.White,
            }).ToArray();
        }

        private Drawable createGraph((double Time, double Difficulty)[] objects, Color4 colour)
        {
            // The hardest object in each column, so short spikes still show. Each skill is scaled to its own hardest object.
            double[] columns = new double[max_columns];

            foreach (var (time, difficulty) in objects)
            {
                int column = Math.Clamp((int)((time - startTime) / Math.Max(1, endTime - startTime) * max_columns), 0, max_columns - 1);
                columns[column] = Math.Max(columns[column], difficulty);
            }

            double max = columns.Max();
            var container = new Container { RelativeSizeAxes = Axes.Both };

            for (int i = 0; i < max_columns; i++)
            {
                container.Add(new Box
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    RelativePositionAxes = Axes.X,
                    RelativeSizeAxes = Axes.Both,
                    X = (float)i / max_columns,
                    Width = 1f / max_columns,
                    Height = max > 0 ? (float)(columns[i] / max) : 0,
                    Colour = colour,
                });
            }

            return container;
        }

        protected override void Update()
        {
            base.Update();
            position.X = toX(currentTime());
        }

        public void SetMarkers(double? checkpoint, IEnumerable<double> misses, IEnumerable<double> failPoints, IEnumerable<(double Start, double End)> skipped)
        {
            markers.Clear();

            foreach (var (start, end) in skipped)
            {
                markers.Add(new Box
                {
                    RelativePositionAxes = Axes.X,
                    RelativeSizeAxes = Axes.Both,
                    X = toX(start),
                    Width = Math.Max(0.002f, toX(end) - toX(start)),
                    Colour = Color4.White,
                    Alpha = 0.15f,
                });
            }

            foreach (double time in misses)
                markers.Add(new Box { RelativePositionAxes = Axes.X, RelativeSizeAxes = Axes.Y, X = toX(time), Width = 2, Height = 0.3f, Origin = Anchor.TopCentre, Colour = Color4.Red });

            foreach (double time in failPoints)
                markers.Add(new Box { RelativePositionAxes = Axes.X, RelativeSizeAxes = Axes.Y, X = toX(time), Width = 2, Origin = Anchor.TopCentre, Colour = Color4.Yellow });

            if (checkpoint is double checkpointTime)
            {
                markers.Add(new Box
                {
                    RelativePositionAxes = Axes.X,
                    X = toX(checkpointTime),
                    Y = 4,
                    Size = new Vector2(12),
                    Rotation = 45,
                    Origin = Anchor.Centre,
                    Anchor = Anchor.TopLeft,
                    Colour = Color4.LimeGreen,
                });
            }
        }

        private float toX(double time) => (float)Math.Clamp((time - startTime) / Math.Max(1, endTime - startTime), 0, 1);

        public override void Show() => this.FadeIn(100);

        public override void Hide() => this.FadeOut(100);

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            if (e.Button == MouseButton.Right)
                setCheckpoint(timeAt(e.ScreenSpaceMousePosition));
            else
                seek(timeAt(e.ScreenSpaceMousePosition));

            return true;
        }

        protected override bool OnDragStart(DragStartEvent e) => e.Button == MouseButton.Left;

        protected override void OnDrag(DragEvent e) => seek(timeAt(e.ScreenSpaceMousePosition));

        private double timeAt(Vector2 screenSpacePosition)
        {
            float x = Math.Clamp(ToLocalSpace(screenSpacePosition).X / DrawWidth, 0, 1);
            return startTime + x * (endTime - startTime);
        }
    }
}
