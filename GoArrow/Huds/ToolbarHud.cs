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
using MouseButtons = System.Windows.Forms.MouseButtons;

using Decal.Adapter;
using Decal.Adapter.Wrappers;

namespace GoArrow.Huds
{
	public enum ToolbarOrientation { Horizontal, Vertical };

	[Flags]
	public enum ToolbarDisplay
	{
		Icons = 0x1,
		Text = 0x2,
	};

	class ToolbarHud : IManagedHud, IEnumerable<ToolbarButton>
	{
		private const int EdgeWidth = 2, ButtonSpacing = 2;
		private const int GrabberWidth = 8;
		private const int SnapToEdgePixels = 8;

		internal static class Colors
		{
			public static readonly Color Background = Color.FromArgb(unchecked((int)0xFF2D2413));
			public static readonly Color GrabberA = Color.FromArgb(unchecked((int)0xFFE8D7A4));
			public static readonly Color GrabberB = Color.FromArgb(unchecked((int)0xFF645D47));
		}

		private bool mDisposed = false;

		private HudManager mManager;
		private Hud mHud = null;

		private bool mVisible = false;
		private Point mLocation = new Point(40, 51);
		private Size mSize = Size.Empty;
		private int mLargestButtonWidth = 0;
		private bool mPositionLocked = false;
		private bool mRespondsToRightClick = false;
		private ToolbarOrientation mOrientation;
		private ToolbarDisplay mDisplay = ToolbarDisplay.Icons | ToolbarDisplay.Text;

		[Flags]
		enum EdgeEnum
		{
			None = 0x0,
			SouthInner = 0x1,
			NorthInner = 0x2,
			EastInner = 0x4,
			WestInner = 0x8,
			SouthOuter = 0x10,
			NorthOuter = 0x20,
			EastOuter = 0x40,
			WestOuter = 0x80,
		};
		private EdgeEnum mOnEdge = EdgeEnum.None;

		// Mouse handling
		private Point mMousePos, mMouseDownOffset;
		private bool mMovingHud = false, mMousePressedOverHud = false;
		private bool mMouseOverHud = false;
		private MouseButtons mMouseDownEaten = MouseButtons.None;

		private List<ToolbarButton> mButtons = new List<ToolbarButton>();

		private bool mNeedsRepaint = true;
		private Graphics mTextMeasureContext;
		private Font mTextMeasureFont;

		public event EventHandler VisibleChanged;
		public event EventHandler DisplayChanged;
		public event EventHandler OrientationChanged;

		public ToolbarHud(HudManager manager)
		{
			mManager = manager;

			mTextMeasureContext = Graphics.FromImage(new Bitmap(1, 1));
			mTextMeasureFont = new Font(ToolbarButton.FontName, 9);

			Manager.RegionChange3D += new EventHandler<RegionChange3DEventArgs>(Manager_RegionChange3D);

			// This will call RecreateHud()
			Manager.RegisterHud(this, true);
		}

		public void Dispose()
		{
			if (mDisposed)
				return;

			Manager.RegionChange3D -= Manager_RegionChange3D;

			foreach (ToolbarButton button in mButtons)
			{
				button.Dispose();
			}
			mButtons.Clear();

			Manager.UnregisterHud(this);
			DisposeHudInternal();
			mVisible = false;

			mDisposed = true;
		}

		public bool Visible
		{
			get { return mVisible && mButtons.Count > 0; }
			set
			{
				if (mVisible != value)
				{
					mVisible = value;
					mNeedsRepaint = true;
					if (!mVisible)
					{
						DisposeHudInternal();
					}
					if (VisibleChanged != null)
						VisibleChanged(this, EventArgs.Empty);
				}
			}
		}

		public Point Location
		{
			get { return mLocation; }
			set
			{
				if (mLocation != value)
				{
					mLocation = value;
					if (HudIsCreated)
					{
						mHud.Region = new Rectangle(value, mHud.Region.Size);
					}

					Rectangle me = new Rectangle(mLocation, mSize);
					Rectangle r3D = Util.Region3D;
					Rectangle rAC = Util.RegionWindow;

					mOnEdge = EdgeEnum.None;
					if (me.Left == r3D.Left)
					{
						mOnEdge |= EdgeEnum.WestInner;
					}
					else if (me.Left == rAC.Left)
					{
						mOnEdge |= EdgeEnum.WestOuter;
					}
					else if (me.Right == r3D.Right)
					{
						mOnEdge |= EdgeEnum.EastInner;
					}
					else if (me.Right == rAC.Right)
					{
						mOnEdge |= EdgeEnum.EastOuter;
					}

					if (me.Top == r3D.Top)
					{
						mOnEdge |= EdgeEnum.NorthInner;
					}
					else if (me.Top == rAC.Top)
					{
						mOnEdge |= EdgeEnum.NorthOuter;
					}
					else if (me.Bottom == r3D.Bottom)
					{
						mOnEdge |= EdgeEnum.SouthInner;
					}
					else if (me.Bottom == rAC.Bottom)
					{
						mOnEdge |= EdgeEnum.SouthOuter;
					}
				}
			}
		}

		public ToolbarDisplay Display
		{
			get { return mDisplay; }
			set
			{
				if (mDisplay != value)
				{
					if (value != 0)
						mDisplay = value;
					else if (mDisplay == ToolbarDisplay.Icons)
						mDisplay = ToolbarDisplay.Text;
					else if (mDisplay == ToolbarDisplay.Text)
						mDisplay = ToolbarDisplay.Icons;
					else
						mDisplay = ToolbarDisplay.Icons | ToolbarDisplay.Text;

					mNeedsRepaint = true;

					if (DisplayChanged != null)
						DisplayChanged(this, EventArgs.Empty);
				}
			}
		}

		public Rectangle Region
		{
			get { return new Rectangle(Location, mSize); }
		}

		public bool PositionLocked
		{
			get { return mPositionLocked; }
			set
			{
				if (mPositionLocked != value)
				{
					mPositionLocked = value;
					mNeedsRepaint = true;
				}
			}
		}

		public ToolbarOrientation Orientation
		{
			get { return mOrientation; }
			set
			{
				if (mOrientation != value)
				{
					mOrientation = value;
					mNeedsRepaint = true;

					if (OrientationChanged != null)
						OrientationChanged(this, EventArgs.Empty);
				}
			}
		}

		public bool RespondsToRightClick
		{
			get { return mRespondsToRightClick; }
			set { mRespondsToRightClick = value; }
		}

		public ToolbarButton this[int index]
		{
			get { return mButtons[index]; }
		}

		public int Count
		{
			get { return mButtons.Count; }
		}

		public ToolbarButton AddButton(ToolbarButton b)
		{
			return InsertButton(mButtons.Count, b);
		}

		public ToolbarButton InsertButton(int index, ToolbarButton b)
		{
			if (!mButtons.Contains(b))
			{
				b.Owner = this;
				if (index < 0) { index = 0; } else if (index > mButtons.Count) { index = mButtons.Count; }
				mButtons.Insert(index, b);
				if (b.Size.Width > LargestButtonWidth)
				{
					LargestButtonWidth = b.Size.Width;
				}
				mNeedsRepaint = true;
			}
			return b;
		}

		public ToolbarButton RemoveButton(ToolbarButton b)
		{
			if (mButtons.Contains(b))
			{
				mButtons.Remove(b);
				b.Owner = null;
				if (LargestButtonWidth == b.Size.Width)
				{
					LargestButtonWidth = 0;
					foreach (ToolbarButton button in mButtons)
					{
						if (button.Size.Width > LargestButtonWidth)
						{
							LargestButtonWidth = button.Size.Width;
						}
					}
				}
				mNeedsRepaint = true;
			}
			return b;
		}

		public void RemoveAllButtons()
		{
			mButtons.Clear();
			mNeedsRepaint = true;
		}

		public void ResetPosition()
		{
			Location = new Point(40, 51);
		}

		internal int LargestButtonWidth
		{
			get { return mLargestButtonWidth; }
			private set { mLargestButtonWidth = value; }
		}

		internal Size MeasureString(string text)
		{
			return Size.Ceiling(mTextMeasureContext.MeasureString(text, mTextMeasureFont));
		}

		private Size CalcSize()
		{
			Size sz;
			int maxWidth = 0;
			foreach (ToolbarButton b in mButtons)
			{
				b.RecalcSize();
				if (b.Visible && b.Size.Width > maxWidth)
				{
					maxWidth = b.Size.Width;
				}
			}
			LargestButtonWidth = maxWidth;

			if (Orientation == ToolbarOrientation.Horizontal)
			{
				int width = EdgeWidth * 2;
				if (!PositionLocked) { width += GrabberWidth; }

				foreach (ToolbarButton b in mButtons)
				{
					if (b.Visible)
					{
						width += b.Size.Width + ButtonSpacing;
					}
				}
				width -= Math.Min(ButtonSpacing, EdgeWidth);

				sz = new Size(width, ToolbarButton.Height + 2 * EdgeWidth);

				// Calc button positions
				int prevRight = EdgeWidth + (!PositionLocked ? GrabberWidth : 0) - ButtonSpacing;
				foreach (ToolbarButton b in mButtons)
				{
					if (b.Visible)
					{
						b.Offset = new Point(prevRight + ButtonSpacing, EdgeWidth);
						prevRight = b.Region.Right;
					}
				}
			}
			else
			{
				int height = EdgeWidth * 2 - ButtonSpacing;

				foreach (ToolbarButton b in mButtons)
				{
					if (b.Visible)
					{
						height += ToolbarButton.Height + ButtonSpacing;
					}
				}

				if (!PositionLocked) { height += GrabberWidth; }

				sz = new Size(LargestButtonWidth + 2 * EdgeWidth, height);

				// Calc button positions
				int prevBottom = EdgeWidth + (!PositionLocked ? GrabberWidth : 0) - ButtonSpacing;
				foreach (ToolbarButton b in mButtons)
				{
					if (b.Visible)
					{
						b.Offset = new Point(EdgeWidth, prevBottom + ButtonSpacing);
						prevBottom = b.Region.Bottom;
					}
				}
			}

			return sz;
		}

		private bool HudIsCreated
		{
			get { return mHud != null && !mHud.Lost; }
		}

		private void DisposeHudInternal()
		{
			if (mHud != null)
			{
				mHud.Enabled = false;
				Manager.Host.Render.RemoveHud(mHud);
				mHud.Dispose();
				mHud = null;
			}
		}

		public void RecreateHud()
		{
			DisposeHudInternal();
			if (Visible)
			{
				mSize = CalcSize();
				KeepOnEdge();
				mHud = Manager.Host.Render.CreateHud(new Rectangle(Location, mSize));
				mHud.Enabled = true;
				PaintInternal();
			}
		}

		private void Manager_RegionChange3D(object sender, RegionChange3DEventArgs e)
		{
			KeepOnEdge();
		}

		private void KeepOnEdge()
		{
			// Keep hud on edge of screen if it already is
			if (mOnEdge != EdgeEnum.None)
			{
				Rectangle me = new Rectangle(mLocation, mSize);
				Rectangle r3D = Util.Region3D;
				Rectangle rAC = Util.RegionWindow;
				if ((mOnEdge & EdgeEnum.WestInner) != 0)
				{
					mLocation.X = r3D.Left;
				}
				else if (me.Left < rAC.Left || (mOnEdge & EdgeEnum.WestOuter) != 0)
				{
					mLocation.X = rAC.Left;
				}
				else if ((mOnEdge & EdgeEnum.EastInner) != 0)
				{
					mLocation.X = r3D.Right - mSize.Width;
				}
				else if (me.Right > rAC.Right || (mOnEdge & EdgeEnum.EastOuter) != 0)
				{
					mLocation.X = rAC.Right - mSize.Width;
				}

				if ((mOnEdge & EdgeEnum.NorthInner) != 0)
				{
					mLocation.Y = r3D.Top;
				}
				else if (me.Top < rAC.Top || (mOnEdge & EdgeEnum.NorthOuter) != 0)
				{
					mLocation.Y = rAC.Top;
				}
				else if ((mOnEdge & EdgeEnum.SouthInner) != 0)
				{
					mLocation.Y = r3D.Bottom - mSize.Height;
				}
				else if (me.Bottom > rAC.Bottom || (mOnEdge & EdgeEnum.SouthOuter) != 0)
				{
					mLocation.Y = rAC.Bottom - mSize.Height;
				}
			}
		}

		internal bool NeedsRepaint
		{
			get { return mNeedsRepaint; }
			set { mNeedsRepaint = value; }
		}

		public void RepaintHeartbeat()
		{
			if (!Visible)
			{
				DisposeHudInternal();
			}
			else
			{
				//if (!mNeedsRepaint)
				//{
				//    for (int i = 0, ct = mButtons.Count; i < ct; i++)
				//    {
				//        if (mButtons[i].NeedsRepaint)
				//        {
				//            mNeedsRepaint = true;
				//            break;
				//        }
				//    }
				//}

				if (mNeedsRepaint)
				{
					Size sz = CalcSize();
					if (mSize != sz)
					{
						Manager.RecreateHud(this);
					}
				}

				if (!HudIsCreated)
				{
					Manager.RecreateHud(this);
				}
				else if (mHud.Region.Location != mLocation)
				{
					mHud.Region = new Rectangle(mLocation, mHud.Region.Size);
				}

				if (mNeedsRepaint)
				{
					PaintInternal();
				}

				mHud.Enabled = Visible;
			}
		}

		private void PaintInternal()
		{
			mNeedsRepaint = false;

			mHud.Clear();

			// Draw the background
			mHud.Fill(Colors.Background);

			Point pt;
			if (PositionLocked)
			{
				pt = new Point(EdgeWidth, EdgeWidth);
			}
			else if (Orientation == ToolbarOrientation.Horizontal)
			{
				pt = new Point(EdgeWidth + GrabberWidth, EdgeWidth);
				// Draw the grabber
				Rectangle r = new Rectangle(EdgeWidth + 1, EdgeWidth, 1, mSize.Height - 2 * EdgeWidth);
				mHud.Fill(r, Colors.GrabberA);
				r.X++;
				mHud.Fill(r, Colors.GrabberB);
				r = new Rectangle(r.X + 2, r.Y + 2, 1, r.Height - 4);
				mHud.Fill(r, Colors.GrabberA);
				r.X++;
				mHud.Fill(r, Colors.GrabberB);
			}
			else if (Orientation == ToolbarOrientation.Vertical)
			{
				pt = new Point(EdgeWidth, EdgeWidth + GrabberWidth);
				// Draw the grabber
				Rectangle r = new Rectangle(EdgeWidth, EdgeWidth + 1, mSize.Width - 2 * EdgeWidth, 1);
				mHud.Fill(r, Colors.GrabberA);
				r.Y++;
				mHud.Fill(r, Colors.GrabberB);
				r = new Rectangle(r.X + 2, r.Y + 2, r.Width - 4, 1);
				mHud.Fill(r, Colors.GrabberA);
				r.Y++;
				mHud.Fill(r, Colors.GrabberB);
			}

			// Paint the buttons
			foreach (ToolbarButton button in mButtons)
			{
				if (button.Visible)
				{
					button.PaintBackground(mHud);
				}
			}
			mHud.BeginRender();
			mHud.BeginText(ToolbarButton.FontName, ToolbarButton.FontSize);
			foreach (ToolbarButton button in mButtons)
			{
				if (button.Visible)
				{
					button.PaintForeground(mHud);
				}
			}
			mHud.EndText();
			mHud.EndRender();
		}

		public void WindowMessage(Decal.Adapter.WindowMessageEventArgs e)
		{
			const short WM_MOUSEMOVE = 0x200;
			const short WM_LBUTTONDOWN = 0x201;
			const short WM_LBUTTONUP = 0x202;
			const short WM_RBUTTONDOWN = 0x0204;
			const short WM_RBUTTONUP = 0x0205;
			// A hack to reduce the number of checks for every windows message
			const short HANDLED_MESSAGES = WM_RBUTTONDOWN | WM_LBUTTONDOWN
				| WM_LBUTTONUP | WM_RBUTTONUP | WM_MOUSEMOVE;

			if (!Visible || (e.Msg & HANDLED_MESSAGES) == 0)
				return;

			Point prevMousePos = mMousePos;
			mMousePos = new Point(e.LParam);

			bool prevMouseOverHud = mMouseOverHud;
			mMouseOverHud = Region.Contains(mMousePos);

			MouseButtons mouseButton = MouseButtons.None;
			if (e.Msg == WM_MOUSEMOVE)
			{

			}
			else if (e.Msg == WM_LBUTTONDOWN || e.Msg == WM_LBUTTONUP)
			{
				mouseButton = MouseButtons.Left;
			}
			else if (e.Msg == WM_RBUTTONDOWN || e.Msg == WM_RBUTTONUP)
			{
				mouseButton = MouseButtons.Right;
			}

			Point relativeMousePos = new Point(mMousePos.X - Location.X, mMousePos.Y - Location.Y);
			switch (e.Msg)
			{
				case WM_MOUSEMOVE:
					if (mMovingHud)
					{
						// Snap to edge
						int x = mMousePos.X + mMouseDownOffset.X;
						int y = mMousePos.Y + mMouseDownOffset.Y;

						Rectangle me = new Rectangle(x, y, mSize.Width, mSize.Height);
						Rectangle r3D = Util.Region3D;
						Rectangle rAC = Util.RegionWindow;

						if (Math.Abs(me.Left - r3D.Left) < SnapToEdgePixels)
						{
							me.X = r3D.Left;
						}
						else if (me.Left - rAC.Left < SnapToEdgePixels)
						{
							me.X = rAC.Left;
						}
						else if (Math.Abs(me.Right - r3D.Right) < SnapToEdgePixels)
						{
							me.X = r3D.Right - me.Width;
						}
						else if (rAC.Right - me.Right < SnapToEdgePixels)
						{
							me.X = rAC.Right - me.Width;
						}

						if (Math.Abs(me.Top - r3D.Top) < SnapToEdgePixels)
						{
							me.Y = r3D.Top;
						}
						else if (me.Top - rAC.Top < SnapToEdgePixels)
						{
							me.Y = rAC.Top;
						}
						else if (Math.Abs(me.Bottom - r3D.Bottom) < SnapToEdgePixels)
						{
							me.Y = r3D.Bottom - me.Height;
						}
						else if (rAC.Bottom - me.Bottom < SnapToEdgePixels)
						{
							me.Y = rAC.Bottom - me.Height;
						}

						Location = new Point(me.X, me.Y);
					}
					else if (mMousePressedOverHud || mMouseOverHud)
					{
						foreach (ToolbarButton b in mButtons)
						{
							b.MouseHovering = b.Region.Contains(relativeMousePos);
						}
					}
					break;

				case WM_LBUTTONDOWN:
				case WM_RBUTTONDOWN:
					if (e.Msg == WM_RBUTTONDOWN && !RespondsToRightClick)
						break;

					if (mMouseOverHud)
					{
						e.Eat = true;
						mMousePressedOverHud = true;
						bool onButton = false;
						foreach (ToolbarButton b in mButtons)
						{
							if (b.Region.Contains(relativeMousePos))
							{
								b.MousePressed = true;
								onButton = true;
								break;
							}
						}
						if (!onButton)
						{
							mMovingHud = true;
							mMouseDownOffset = new Point(Location.X - mMousePos.X, Location.Y - mMousePos.Y);
						}
					}

					if (e.Eat)
					{
						mMouseDownEaten |= mouseButton;
					}
					break;

				case WM_LBUTTONUP:
				case WM_RBUTTONUP:
					if (e.Msg == WM_RBUTTONUP && !RespondsToRightClick)
						break;

					mMovingHud = false;

					if (mMousePressedOverHud || mMouseOverHud)
					{
						foreach (ToolbarButton b in mButtons)
						{
							if (b.MousePressed && b.Region.Contains(relativeMousePos))
							{
								b.HandleClick();
								break;
							}
							b.MousePressed = false;
						}
					}

					if ((mMouseDownEaten & mouseButton) != 0)
					{
						e.Eat = true;
						mMouseDownEaten &= ~mouseButton;
					}
					break;
			}

			if (prevMouseOverHud && !mMouseOverHud)
			{
				foreach (ToolbarButton b in mButtons)
				{
					b.MouseHovering = false;
				}
			}
		}

		public HudManager Manager
		{
			get { return mManager; }
		}

		public bool MouseHoveringObscuresOther
		{
			get { return false; }
		}

		public bool Disposed
		{
			get { return mDisposed; }
		}

		public System.Collections.ObjectModel.ReadOnlyCollection<ToolbarButton> Buttons
		{
			get
			{
				return new System.Collections.ObjectModel.ReadOnlyCollection<ToolbarButton>(mButtons);
			}
		}

		#region IEnumerable<ToolbarButton> Members

		public IEnumerator<ToolbarButton> GetEnumerator()
		{
			return mButtons.GetEnumerator();
		}

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}

		#endregion
	}
}
