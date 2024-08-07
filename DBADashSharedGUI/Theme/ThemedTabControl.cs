﻿using System.Runtime.Versioning;

namespace DBADashGUI.Theme
{
    [SupportedOSPlatform("windows")]
    public class ThemedTabControl : TabControl, IThemedControl
    {
        private BaseTheme theme = ThemeExtensions.CurrentTheme;

        public ThemedTabControl()
        {
            SetStyle(ControlStyles.UserPaint, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // Draw the background of the TabControl
            e.Graphics.Clear(theme.TabBackColor);

            // Draw each TabPage header
            for (int i = 0; i < TabCount; i++)
            {
                Rectangle rect = GetTabRect(i);

                var headerColor = theme.TabHeaderBackColor;
                var textColor = theme.TabHeaderForeColor;

                if (i == SelectedIndex)
                {
                    headerColor = theme.SelectedTabBackColor;
                    textColor = theme.SelectedTabForeColor;
                }

                using (Brush brush = new SolidBrush(headerColor))
                {
                    e.Graphics.FillRectangle(brush, rect);
                }
                using (var pen = new Pen(theme.TabBorderColor, 0.1f))
                {
                    e.Graphics.DrawRectangle(pen, rect);
                }

                using (Brush brush = new SolidBrush(textColor)) // Text color
                {
                    var sf = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    };

                    e.Graphics.DrawString(TabPages[i].Text, Font, brush, rect, sf);
                }
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Do nothing here to prevent flickering
        }

        public void ApplyTheme(BaseTheme theme)
        {
            this.theme = theme;
            Controls.ApplyTheme(theme);
            Invalidate();
        }
    }
}