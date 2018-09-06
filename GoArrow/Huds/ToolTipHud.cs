/* Copyright (c) 2007 Ben Howell
 * This software is licensed under the MIT License
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a 
 * copy of this software and associated documentation files (the "Software"), 
 * to deal in the Software without restriction, including without limitation 
 * the rights to use, copy, modify, merge, publish, distribute, sublicense, 
 * and/or sell copies of the Software, and to permit persons to whom the 
 * Software is furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in 
 * all copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR 
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
 * DEALINGS IN THE SOFTWARE.
 */

using System;
using System.Collections.Generic;
using System.Text;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Runtime.InteropServices;

using Decal.Adapter;
using Decal.Adapter.Wrappers;

using DecalTimer = Decal.Interop.Input.TimerClass;

namespace GoArrow.Huds
{
	class ToolTipHud
	{
		private static readonly Color Clear = Color.FromArgb(0);
		private static readonly Pen BorderPen = new Pen(Color.FromArgb(unchecked((int)0xFFEFA510)));
		private static readonly Brush BackgroundBrush = new SolidBrush(Color.FromArgb(0xA0, Color.Black));
		private static readonly Font TextFont = new Font(FontFamily.GenericSerif, 10);

		private HudManager mManager;
		private Hud mHud = null;
		private Bitmap mBmp;
		private Graphics gBmp;
		private StringFormat mFormat;

		private string mMessage;
		private DateTime mHideTime;

		public ToolTipHud(HudManager manager)
		{
			mManager = manager;
			mBmp = new Bitmap(256, 22);
			gBmp = Graphics.FromImage(mBmp);
			mFormat = new StringFormat();
			mFormat.Trimming = StringTrimming.EllipsisCharacter;

			Manager.Heartbeat += new EventHandler(ToolTipHud_Heartbeat);
		}

		public bool Visible
		{
			get { return mHud != null; }
		}

		public HudManager Manager
		{
			get { return mManager; }
		}

		private void ToolTipHud_Heartbeat(object sender, EventArgs e)
		{
			if (Visible && mHideTime < DateTime.Now)
			{
				Hide();
			}
		}

		public void Show(Point location, string message)
		{
			Show(location, message, DateTime.MaxValue);
		}

		public void Show(Point location, string message, int hideDelayMillis)
		{
			DateTime hideTime = DateTime.MaxValue;
			if (hideDelayMillis > 0)
				hideTime = DateTime.Now + new TimeSpan(0, 0, 0, 0, hideDelayMillis);
			Show(location, message, hideTime);
		}

		public void Show(Point location, string message, DateTime hideTime)
		{
			mHideTime = hideTime;

			if (Visible)
			{
				if (mMessage == message)
				{
					mHud.Region = new Rectangle(location, mHud.Region.Size);
					return;
				}
				Hide();
			}
			mMessage = message;

			gBmp.Clear(Clear);
			SizeF sz = gBmp.MeasureString(message, TextFont);
			int textWidth = Math.Min(250, (int)Math.Ceiling(sz.Width));
			Rectangle outlineRect = new Rectangle(0, 0, textWidth + 2, 20);
			gBmp.FillRectangle(BackgroundBrush, outlineRect);
			gBmp.DrawRectangle(BorderPen, outlineRect);
			gBmp.DrawString(message, TextFont, Brushes.White, new RectangleF(2, 2, textWidth, 16), mFormat);

			mHud = mManager.Host.Render.CreateHud(new Rectangle(location, new Size(480, 32)));
			mHud.Clear();
			mHud.BeginRender();
			mHud.DrawImage(mBmp, new Rectangle(0, 0, mBmp.Width, mBmp.Height));
			mHud.EndRender();
			mHud.Enabled = true;
		}

		public void Hide()
		{
			if (mHud != null)
			{
				if (!mHud.Lost)
				{
					mHud.Enabled = false;
					mManager.Host.Render.RemoveHud(mHud);
				}
				mHud.Dispose();
				mHud = null;
			}
		}
	}
}
