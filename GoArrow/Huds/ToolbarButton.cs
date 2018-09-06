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

using Decal.Adapter.Wrappers;

namespace GoArrow.Huds
{
	class ToolbarButton : IDisposable
	{
		public const int Padding = 2, IconSize = 16, Height = 19, FontSize = 14;
		public const string FontName = "Times New Roman";

		internal static class Colors
		{
			public static readonly Color Text = Color.White;
			public static readonly Color Background = Color.Black;
			public static readonly Color BackgroundHighlight = Color.FromArgb(unchecked((int)0xFF3D2F18));
			public static readonly Color Border = Color.FromArgb(unchecked((int)0xFF7C6332));
			public static readonly Color BorderHighlight = Color.FromArgb(unchecked((int)0xFFFFCC00));
		}

		private string mLabel;
		private Bitmap mBitmapIcon = null;
		private int mPortalIcon = 0;

		private ToolbarHud mOwner;

		private bool mIsLabelOnly = false;
		private bool mMouseHovering = false;
		private bool mMousePressed = false;
		private bool mSelected = false;
		private bool mVisible = true;

		private bool mDisposed = false;

		private Point mOffset;
		private Size mSize = Size.Empty;

		public event EventHandler Click;

		internal ToolbarButton(string label)
		{
			mLabel = label;
		}

		internal ToolbarButton(Bitmap icon, string label)
		{
			mLabel = label;
			mBitmapIcon = icon;
		}

		internal ToolbarButton(int portalIcon, string label)
		{
			mLabel = label;
			mPortalIcon = portalIcon;
		}

		public void Dispose()
		{
			if (!mDisposed)
			{
				mDisposed = true;
			}
		}

		public bool Disposed
		{
			get { return mDisposed; }
		}

		public ToolbarHud Owner
		{
			get { return mOwner; }
			internal set { mOwner = value; }
		}

		public string Label
		{
			get { return mLabel; }
			set
			{
				if (mLabel != value)
				{
					mLabel = value;
					mSize = Size.Empty;
					NeedsRepaint = true;
				}
			}
		}

		public Bitmap BitmapIcon
		{
			get { return mBitmapIcon; }
			set
			{
				if (mBitmapIcon != value)
				{
					mBitmapIcon = value;
					mPortalIcon = 0;
					mSize = Size.Empty;
					NeedsRepaint = true;
				}
			}
		}

		public int PortalIcon
		{
			get { return mPortalIcon; }
			set
			{
				if (mPortalIcon != value)
				{
					mPortalIcon = value;
					mBitmapIcon = null;
					mSize = Size.Empty;
					NeedsRepaint = true;
				}
			}
		}

		public bool HasBitmapIcon
		{
			get { return mBitmapIcon != null; }
		}

		public bool HasPortalIcon
		{
			get { return mPortalIcon != 0; }
		}

		public bool HasIcon
		{
			get { return HasBitmapIcon || HasPortalIcon; }
		}

		public bool HasLabel
		{
			get { return Label != ""; }
		}

		internal bool NeedsRepaint
		{
			set { Owner.NeedsRepaint = Owner.NeedsRepaint || value; }
		}

		internal Point Offset
		{
			get { return mOffset; }
			set { mOffset = value; }
		}

		public bool Visible
		{
			get { return mVisible; }
			set
			{
				if (mVisible != value)
				{
					mVisible = value;
					NeedsRepaint = true;
				}
			}
		}

		public Size Size
		{
			get
			{
				if (mSize.IsEmpty)
				{
					mSize = new Size(0, Height);
					if (HasLabel && (Owner.Display & ToolbarDisplay.Text) != 0)
					{
						mSize.Width += Owner.MeasureString(Label).Width + 3 * Padding;
					}
					if (HasIcon && (Owner.Display & ToolbarDisplay.Icons) != 0)
					{
						mSize.Width += IconSize + 2 * Padding;
					}
					if (HasIcon && HasLabel && Owner.Display == (ToolbarDisplay.Icons | ToolbarDisplay.Text))
					{
						mSize.Width -= 2 * Padding;
					}
				}
				return mSize;
			}
		}

		public void RecalcSize()
		{
			mSize = Size.Empty;
		}

		public Rectangle Region
		{
			get
			{
				Rectangle r = new Rectangle(Offset, Size);
				if (Owner.Orientation == ToolbarOrientation.Vertical)
				{
					r.Width = Owner.LargestButtonWidth;
				}
				return r;
			}
		}

		public bool IsLabelOnly
		{
			get { return mIsLabelOnly; }
			set
			{
				if (mIsLabelOnly != value)
				{
					mIsLabelOnly = value;
					NeedsRepaint = true;
				}
			}
		}

		internal bool MouseHovering
		{
			get { return mMouseHovering; }
			set
			{
				if (mMouseHovering != value)
				{
					mMouseHovering = value;
					NeedsRepaint = true;
				}
			}
		}

		internal bool MousePressed
		{
			get { return mMousePressed; }
			set
			{
				if (mMousePressed != value)
				{
					mMousePressed = value;
					NeedsRepaint = true;
				}
			}
		}

		public bool Selected
		{
			get { return mSelected; }
			set
			{
				if (mSelected != value)
				{
					mSelected = value;
					NeedsRepaint = true;
				}
			}
		}

		private Color TextColor
		{
			get { return Colors.Text; }
		}

		private Color BorderColor
		{
			get
			{
				if (Selected)
					return Colors.BorderHighlight;
				return Colors.Border;
			}
		}

		private Color BackgroundColor
		{
			get
			{
				if (MouseHovering)
					return Colors.BackgroundHighlight;
				return Colors.Background;
			}
		}

		// The hud must NOT be in Render mode!
		internal void PaintBackground(Hud hud)
		{
			Rectangle r = Region;
			if (!IsLabelOnly)
			{
				hud.Fill(r, BorderColor);
				r = new Rectangle(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
				hud.Fill(r, BackgroundColor);
			}
			else
			{
				hud.Fill(r, Colors.Background);
			}
		}

		// The hud MUST be in Render AND Text mode!
		internal void PaintForeground(Hud hud)
		{
			NeedsRepaint = false;
			Rectangle r = Region;
			r.X += Padding;
			r.Width -= Padding;
			if (HasIcon && (Owner.Display & ToolbarDisplay.Icons) != 0)
			{
				Rectangle iconRect = new Rectangle(r.X, r.Y + (Height - IconSize) / 2, IconSize, IconSize);

				// Adjust the button's region for when drawing text
				r.X += IconSize + Padding;
				r.Width -= IconSize + Padding;

				if (HasBitmapIcon)
				{
					hud.DrawImage(BitmapIcon, iconRect);
				}
				else if (HasPortalIcon)
				{
					hud.DrawPortalImage(PortalIcon, iconRect);
				}
			}

			if (HasLabel && (Owner.Display & ToolbarDisplay.Text) != 0)
			{
				if ((Owner.Display & ToolbarDisplay.Icons) == 0)
				{
					r.X += Padding;
					r.Width -= Padding;
				}
				Rectangle textRect = new Rectangle(r.X + Padding, r.Y + (Height - FontSize) / 2, r.Width - 2 * Padding, FontSize);
				hud.WriteText(Label, TextColor, WriteTextFormats.SingleLine, textRect);
			}
		}

		/// <summary>
		/// Called by the toolbar when the user clicks on the button.
		/// </summary>
		internal void HandleClick()
		{
			if (!IsLabelOnly && Click != null)
			{
				Click(this, EventArgs.Empty);
			}
		}
	}
}
