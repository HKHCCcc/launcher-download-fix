using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PclPatchHost
{
    internal sealed class LogView : Control
    {
        private sealed class Entry
        {
            public DateTime Time;
            public string Text;
            public Color Color;
        }

        private readonly List<Entry> entries = new List<Entry>();
        private readonly Font timeFont;
        private readonly Font bodyFont;
        private readonly int lineHeight;
        private int scrollY;
        private bool autoScroll = true;
        private bool draggingThumb;
        private int dragStartY;
        private int dragStartScroll;

        private const int SidePadding = 3;
        private const int EntryGap = 3;
        private const int ScrollBarWidth = 6;
        private const int ScrollBarGap = 4;

        public LogView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.LogBackground;
            timeFont = Theme.Mono(7.8f);
            bodyFont = Theme.Mono(8.8f);
            lineHeight = TextRenderer.MeasureText("Ag", bodyFont).Height;
            TabStop = false;
            Cursor = Cursors.Default;
        }

        public void Add(string text, Color color)
        {
            var entry = new Entry();
            entry.Time = DateTime.Now;
            entry.Text = text ?? "";
            entry.Color = color;
            entries.Add(entry);
            if (entries.Count > 600) entries.RemoveRange(0, entries.Count - 600);
            autoScroll = true;
            Invalidate();
        }

        public void Clear()
        {
            entries.Clear();
            scrollY = 0;
            autoScroll = true;
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timeFont.Dispose();
                bodyFont.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.LogBackground);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int contentWidth = Width - SidePadding * 2;
            int totalHeight = MeasureHeight(contentWidth);
            bool canScroll = totalHeight > Height;
            if (canScroll) contentWidth -= ScrollBarWidth + ScrollBarGap;

            totalHeight = MeasureHeight(contentWidth);
            int maxScroll = Math.Max(0, totalHeight - Height);
            if (autoScroll) scrollY = maxScroll;
            if (scrollY > maxScroll) scrollY = maxScroll;
            if (scrollY < 0) scrollY = 0;

            int y = SidePadding - scrollY;
            foreach (var entry in entries)
            {
                int timeWidth = TextRenderer.MeasureText(entry.Time.ToString("HH:mm:ss"), timeFont).Width + 7;
                int bodyWidth = Math.Max(20, contentWidth - timeWidth);
                int bodyHeight = Math.Max(lineHeight, TextRenderer.MeasureText(entry.Text, bodyFont,
                    new Size(bodyWidth, Int32.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height);
                int entryHeight = Math.Max(lineHeight, bodyHeight);
                if (y + entryHeight >= 0 && y <= Height)
                {
                    TextRenderer.DrawText(e.Graphics, entry.Time.ToString("HH:mm:ss"), timeFont,
                        new Point(SidePadding, y + 1), Theme.LogTime, Color.Transparent,
                        TextFormatFlags.NoPrefix);
                    TextRenderer.DrawText(e.Graphics, entry.Text, bodyFont,
                        new Rectangle(SidePadding + timeWidth, y, bodyWidth, entryHeight), entry.Color,
                        TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                }
                y += entryHeight + EntryGap;
            }

            if (canScroll)
                DrawScrollBar(e.Graphics, totalHeight, maxScroll);
        }

        private int MeasureHeight(int contentWidth)
        {
            int height = SidePadding * 2;
            foreach (var entry in entries)
            {
                int timeWidth = TextRenderer.MeasureText(entry.Time.ToString("HH:mm:ss"), timeFont).Width + 7;
                int bodyWidth = Math.Max(20, contentWidth - timeWidth);
                int bodyHeight = Math.Max(lineHeight, TextRenderer.MeasureText(entry.Text, bodyFont,
                    new Size(bodyWidth, Int32.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height);
                height += Math.Max(lineHeight, bodyHeight) + EntryGap;
            }
            return height;
        }

        private void DrawScrollBar(Graphics graphics, int totalHeight, int maxScroll)
        {
            int trackX = Width - ScrollBarWidth - 2;
            int trackY = SidePadding;
            int trackHeight = Math.Max(1, Height - SidePadding * 2);
            if (trackHeight <= 0) return;

            int thumbHeight = Math.Max(24, (int)(trackHeight * (double)Height / Math.Max(Height, totalHeight)));
            if (thumbHeight > trackHeight) thumbHeight = trackHeight;
            int travel = Math.Max(0, trackHeight - thumbHeight);
            int thumbY = trackY + (maxScroll <= 0 ? 0 : (int)(travel * (double)scrollY / maxScroll));

            using (var track = new SolidBrush(Theme.LogTrack))
            using (var thumb = new SolidBrush(Theme.LogThumb))
            {
                graphics.FillRectangle(track, trackX, trackY, ScrollBarWidth, trackHeight);
                graphics.FillRectangle(thumb, trackX, thumbY, ScrollBarWidth, thumbHeight);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (e.Delta != 0)
            {
                autoScroll = false;
                scrollY -= e.Delta > 0 ? lineHeight * 3 : -lineHeight * 3;
                scrollY = Math.Max(0, scrollY);
                Invalidate();
            }
            base.OnMouseWheel(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && e.X >= Width - ScrollBarWidth - 4)
            {
                draggingThumb = true;
                dragStartY = e.Y;
                dragStartScroll = scrollY;
                Capture = true;
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (draggingThumb)
            {
                int contentWidth = Width - SidePadding * 2 - ScrollBarWidth - ScrollBarGap;
                int totalHeight = MeasureHeight(contentWidth);
                int maxScroll = Math.Max(0, totalHeight - Height);
                int trackHeight = Math.Max(1, Height - SidePadding * 2);
                int thumbHeight = Math.Max(24, (int)(trackHeight * (double)Height / Math.Max(Height, totalHeight)));
                int travel = Math.Max(1, trackHeight - thumbHeight);
                autoScroll = false;
                scrollY = dragStartScroll + (int)((e.Y - dragStartY) * (double)maxScroll / travel);
                scrollY = Math.Max(0, Math.Min(maxScroll, scrollY));
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            draggingThumb = false;
            Capture = false;
            base.OnMouseUp(e);
        }
    }
}
