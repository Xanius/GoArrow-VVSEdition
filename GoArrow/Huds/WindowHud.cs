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
using System.Xml;

namespace GoArrow.Huds
{
	abstract class WindowHud : IManagedHud
	{
		[DllImport("user32.dll")]
		private static extern bool ScreenToClient(IntPtr hWnd, ref Point lpPoint);
		#region WindowMessage Constants
		const short WM_MOUSEMOVE = 0x0200;
		const short WM_LBUTTONDOWN = 0x0201;
		const short WM_LBUTTONUP = 0x0202;
		const short WM_RBUTTONDOWN = 0x0204;
		const short WM_RBUTTONUP = 0x0205;
		const short WM_MBUTTONDOWN = 0x0207;
		const short WM_MBUTTONUP = 0x0208;
		const short WM_MOUSEWHEEL = 0x020A;

		// Range of WM_MOUSE* events
		const short WM_MOUSEFIRST = 0x0200;
		const short WM_MOUSELAST = 0x020A;
		#endregion
		const int BorderWidth = 4, BorderPadding = 2, BorderPaddedWidth = BorderWidth + BorderPadding;
		const int TitleBarHeight = 18, TitleBarPaddedHeight = TitleBarHeight - BorderPadding;
		const int ControlSize = 14, ControlPadding = 2, ControlPaddedSize = ControlSize + ControlPadding;
		const int NumControlBoxes = 3;
		const int MinimizedWidth = 200, MinimizedHeight = 2 * BorderWidth + TitleBarHeight;
		private static readonly Size msDblClickRect = SystemInformation.DoubleClickSize;
		private static readonly long msDblClickTicks = SystemInformation.DoubleClickTime * 10000L;

		private const int GoIcon = 0x06001F80;

		protected static readonly Color Clear = Color.FromArgb(0);

		#region Private Fields
		private HudManager mManager;
		private Hud mHud = null;
		private Bitmap mClientImage;
		private volatile bool mWindowNeedsRepaint = false, mBordersNeedRepaint = false;
		private volatile bool mClientNeedsRepaint = false, mClientImageLost = true;
		private Rectangle mRegion;       // Absolute position on screen
		private Rectangle mClientRegion; // Absolute position on screen; entirely contained in mRegion

		// Alpha fading
		private DecalTimer mFaderTimer;
		private DateTime mFadeBeginTime;
		private long mFadeDurationMillis;
		private bool mFadeInitialDelay;
		private int mFadeStartAlphaFrame, mFadeEndAlphaFrame;
		private int mAlphaFrame = 255;

		// Mouse stuff
		private Point mMouseLocation;
		private Point mMouseDownLocation;
		private Point mMouseLastClickLocation;
		private long mMouseLastClickTicks = long.MinValue;
		private MouseButtons mMouseLastClickButton = MouseButtons.None;
		private MouseButtons mMouseButtons = MouseButtons.None;
		private MouseButtons mClientMouseButtons = MouseButtons.None;
		// Used to make sure that if a mouse-down event is eaten, its corresponding mouse-up event is eaten as well
		private MouseButtons mMouseDownEaten = MouseButtons.None;
		private bool mMouseOnClose = false, mMouseOnMinRestore = false, mMouseOnSticky = false;
		[Flags]
		private enum MoveResizeMode
		{
			Idle = 0x0,
			Moving = 0x1,
			ResizeN = 0x10,
			ResizeS = 0x20,
			ResizeE = 0x40,
			ResizeW = 0x80,
			Resizing = ResizeN | ResizeS | ResizeE | ResizeW,
		}
		private Rectangle mOriginalRegion; // Used when moving or resizing
		private MoveResizeMode mMoveResizeMode = MoveResizeMode.Idle;

		[Flags]
		private enum Border
		{
			None = 0x0,
			North = 0x1,
			East = 0x2,
			South = 0x4,
			West = 0x8,
			Title = 0x10,
			All = North | East | South | West | Title,
		}
		private Border mHighlightedBorder = Border.None;

		// Private fields with public accessors
		private bool mDisposed = false;
		private bool mVisible = false, mMinimized = false, mSticky = false;
		private bool mMouseOnWindow = false, mMouseOnClient = false;
		private string mTitle;
		private int mAlphaFrameActive = 255;
		private int mAlphaFrameInactive = 255;
		private Size mMinSize = new Size(MinimizedWidth, 50);
		private Size mMaxSize = new Size(800, 600);
		private Color mBorderColor1 = Color.FromArgb(unchecked((int)0xFFA27B42));
		private Color mBorderColor2 = Color.FromArgb(unchecked((int)0xFF67553B));
		private Color mBorderColorHighlight = Color.FromArgb(unchecked((int)0xFFC9B169));
		private Color mBackgroundColor = Color.FromArgb(0xC0, Color.Black);
		private HudResizeDrawMode mResizeDrawMode = HudResizeDrawMode.Crop;
		#endregion

		#region Public Events
		/// <summary>
		/// Occurs once each frame before repainting.
		/// </summary>
		public event EventHandler Heartbeat;

		/// <summary>
		/// Occurs when the Visible property is changed either 
		/// programatically or by the user.
		/// </summary>
		public event EventHandler VisibleChanged;
		/// <summary>
		/// Occurs when the Minimized property is changed either 
		/// programatically or by the user.
		/// </summary>
		public event EventHandler MinimizedChaged;
		/// <summary>
		/// Occurs when the Sticky property is changed either 
		/// programatically or by the user.
		/// </summary>
		public event EventHandler StickyChaged;
		/// <summary>
		/// Occurs repeatedly while the window is being resized by the user.
		/// </summary>
		public event EventHandler Resizing;
		/// <summary>
		/// Occurs when a window resize is complete or when the window size is 
		/// changed programatically.
		/// </summary>
		public event EventHandler ResizeEnd;
		/// <summary>
		/// Occurs repeatedly while the window is being moved by the user.
		/// </summary>
		public event EventHandler Moving;
		/// <summary>
		/// Occurs when a window move is complete or when the window position 
		/// is changed programatically.
		/// </summary>
		public event EventHandler MoveEnd;

		/// <summary>
		/// Occurs when the mouse moves while over the window.
		/// Mouse coordinates are relative to the top left corner of the window.
		/// </summary>
		public event EventHandler<HudMouseEventArgs> MouseMove;
		/// <summary>
		/// Occurs when a mouse button is pressed while over the window.
		/// Mouse coordinates are relative to the top left corner of the window.
		/// </summary>
		public event EventHandler<HudMouseEventArgs> MouseDown;
		/// <summary>
		/// Occurs when a mouse button is released while over the window.
		/// Mouse coordinates are relative to the top left corner of the window. 
		/// The message can only be eaten if the cooresponding MouseDown 
		/// message was also eaten.
		/// </summary>
		public event EventHandler<HudMouseEventArgs> MouseUp;
		/// <summary>
		/// Occurs when a mouse button is double clicked while over the window.
		/// Mouse coordinates are relative to the top left corner of the window.
		/// </summary>
		public event EventHandler<HudMouseEventArgs> MouseDoubleClick;
		/// <summary>
		/// Occurs when the mouse wheel is rotated while over the window.
		/// Mouse coordinates are relative to the top left corner of the window.
		/// </summary>
		public event EventHandler<HudMouseEventArgs> MouseWheel;
		/// <summary>
		/// Occurs when the mouse enters the window.
		/// Mouse coordinates are an absolute position on the screen.
		/// </summary>
		public event EventHandler<HudMouseEventArgs> MouseEnter;
		/// <summary>
		/// Occurs when the mouse leaves the window.
		/// Mouse coordinates are an absolute position on the screen.
		/// </summary>
		public event EventHandler<HudMouseEventArgs> MouseLeave;
		#endregion

		#region Protected Events
		/// <summary>
		/// Occurs when the client area is either hidden or shown.
		/// </summary>
		protected event EventHandler ClientVisibleChanged;

		/// <summary>
		/// Occurs when the mouse is moved over the client area.
		/// Mouse coordinates are relative to the client area.
		/// </summary>
		protected event EventHandler<HudMouseEventArgs> ClientMouseMove;
		/// <summary>
		/// Occurs when a mouse button is pressed over the client area.
		/// Mouse coordinates are relative to the client area.
		/// </summary>
		protected event EventHandler<HudMouseEventArgs> ClientMouseDown;
		/// <summary>
		/// Occurs when a mouse button is released over the client area.
		/// Mouse coordinates are relative to the client area. The message
		/// can only be eaten if the cooresponding MouseDown message was 
		/// also eaten.
		/// </summary>
		protected event EventHandler<HudMouseEventArgs> ClientMouseUp;
		/// <summary>
		/// Occurs when a mouse button is clicked twice over the client area.
		/// Mouse coordinates are relative to the client area.
		/// </summary>
		protected event EventHandler<HudMouseEventArgs> ClientMouseDoubleClick;
		/// <summary>
		/// Occurs when the mouse wheel is rotated over the client area.
		/// Mouse coordinates are relative to the client area.
		/// </summary>
		protected event EventHandler<HudMouseEventArgs> ClientMouseWheel;
		/// <summary>
		/// Occurs when the mouse is moved while one or more mouse buttons is 
		/// held down. The events will continue to occur until the button 
		/// is released, even if the mouse is dragged outside the client area.
		/// </summary>
		protected event EventHandler<HudMouseDragEventArgs> ClientMouseDrag;
		/// <summary>
		/// Occurs when the mouse enters the client area.
		/// Mouse coordinates are relative to the client area.
		/// </summary>
		protected event EventHandler<HudMouseEventArgs> ClientMouseEnter;
		/// <summary>
		/// Occurs when the mouse exits the client area.
		/// Mouse coordinates are relative to the client area.
		/// </summary>
		protected event EventHandler<HudMouseEventArgs> ClientMouseLeave;
		/// <summary>
		/// Lets the client handle window messages.
		/// </summary>
		protected event EventHandler<WindowMessageEventArgs> WindowMessage;
		/// <summary>
		/// The Alpha value has changed
		/// </summary>
		protected event EventHandler<AlphaChangedEventArgs> AlphaChanged;
		#endregion

		#region Creation and Disposal
		/// <summary>Creates a new instance of a WindowHud.</summary>
		/// <param name="region">The size and location of the entire window, 
		///		including the title bar.</param>
		/// <param name="title">The title of the window.</param>
		/// <param name="manager">The manager for this window.</param>
		public WindowHud(Rectangle region, string title, HudManager manager)
		{
			mRegion = ConstrainRegion(region);
			mClientRegion = CalculateClientRegion(mRegion);

			mClientImage = new Bitmap(mClientRegion.Width, mClientRegion.Height);

			mTitle = title;
			mManager = manager;

			mFaderTimer = new DecalTimer();
			mFaderTimer.Timeout += new Decal.Interop.Input.ITimerEvents_TimeoutEventHandler(FaderTimer_Timeout);

			// For fading
			MouseEnter += new EventHandler<HudMouseEventArgs>(FadeIn);
			MouseLeave += new EventHandler<HudMouseEventArgs>(FadeOut);

			// This will call RecreateHud()
			Manager.RegisterHud(this, false);
		}

		/// <summary>
		/// Performs cleanup operations like unregistering events and disabling 
		/// the HUD. To dispose all active windows at once, the manager's 
		/// Dispose() method.
		/// </summary>
		public virtual void Dispose()
		{
			if (mDisposed)
				return;

			MouseEnter -= FadeIn;
			MouseLeave -= FadeOut;

			if (mFaderTimer.Running) { mFaderTimer.Stop(); }
			mFaderTimer.Timeout -= FaderTimer_Timeout;

			Manager.UnregisterHud(this);
			DisposeHudsInternal();
			mVisible = false;

			mDisposed = true;
		}
		#endregion

		#region Public Accessors and Methods
		/// <summary>Gets the HudManager for this window.</summary>
		public HudManager Manager
		{
			get { return mManager; }
		}

		/// <summary>Gets or sets whether the window is visible to the user.</summary>
		public bool Visible
		{
			get { return mVisible; }
			set
			{
				if (mVisible != value)
				{
					bool client = ClientVisible;
					mVisible = value;
					if (VisibleChanged != null)
					{
						VisibleChanged(this, EventArgs.Empty);
					}
					if (ClientVisibleChanged != null && client != ClientVisible)
					{
						ClientVisibleChanged(this, EventArgs.Empty);
					}

					RepaintWindow();
					Repaint();
					if (mVisible)
					{
						if (MouseOnWindow || Sticky)
						{
							AlphaFrame = AlphaFrameActive;
						}
						else
						{
							AlphaFrame = AlphaFrameInactive;
						}
						Manager.BringToFront(this, false);
					}
					else
					{
						mMouseOnWindow = false;
						mMouseOnClient = false;
						mMouseOnClose = false;
						mMouseOnMinRestore = false;
						mMouseOnSticky = false;
						mMouseButtons = MouseButtons.None;
						mMouseLastClickButton = MouseButtons.None;

						DisposeHudsInternal();
					}
				}
				if (mVisible)
				{
					Minimized = false;
				}
			}
		}

		/// <summary>Gets or sets whether the window is minimized.</summary>
		public bool Minimized
		{
			get { return mMinimized; }
			set
			{
				if (mMinimized != value)
				{
					bool client = ClientVisible;
					mMinimized = value;
					if (MinimizedChaged != null)
					{
						MinimizedChaged(this, EventArgs.Empty);
					}
					if (ClientVisibleChanged != null && client != ClientVisible)
					{
						ClientVisibleChanged(this, EventArgs.Empty);
					}
					CalculateHighlightedBorder();
					RepaintWindow();
				}
			}
		}

		/// <summary>
		/// Gets or sets whether the window is sticky. If the window is sticky, 
		/// it will not fade out when the mouse leaves the window. This only
		/// has an effect when <see cref="AlphaFrameActive"/> has a different 
		/// value from <see cref="AlphaFrameInactive"/> and/or 
		/// <see cref="AlphaClientActive"/> has a different value from
		/// <see cref="AlphaClientInactive"/>.
		/// </summary>
		public bool Sticky
		{
			get { return mSticky; }
			set
			{
				if (mSticky != value)
				{
					mSticky = value;
					if (StickyChaged != null)
					{
						StickyChaged(this, EventArgs.Empty);
					}
					if (mSticky)
					{
						AlphaFrame = AlphaFrameActive;
					}
					else if (!MouseOnWindow)
					{
						FadeOut(null, null);
					}
					DrawControlBoxes(false);
				}
			}
		}

		/// <summary>
		/// Gets or sets whether this Hud is always on top of other Huds 
		/// managed by the same HudManager.
		/// </summary>
		public bool AlwaysOnTop
		{
			get { return Manager.IsAlwaysOnTop(this); }
			set { Manager.SetAlwaysOnTop(this, value); }
		}

		/// <summary>Gets whether Dispose() has been called for this window.</summary>
		public bool Disposed
		{
			get { return mDisposed; }
		}

		/// <summary>Gets whether the the user is moving the HUD.</summary>
		public bool IsMoving
		{
			get { return mMoveResizeMode == MoveResizeMode.Moving; }
		}

		/// <summary>Gets whether the user is resizing the HUD.</summary>
		public bool IsResizing
		{
			get { return (mMoveResizeMode & MoveResizeMode.Resizing) != 0; }
		}

		/// <summary>Gets whether the mouse is over the window.</summary>
		public bool MouseOnWindow
		{
			get { return mMouseOnWindow; }
		}

		/// <summary>Gets or sets the window's size and position.</summary>
		public Rectangle Region
		{
			get { return mRegion; }
			set
			{
				bool moved = mRegion.Location != value.Location;
				bool resized = mRegion.Size != value.Size;

				mRegion = ConstrainRegion(value);
				mClientRegion = CalculateClientRegion(mRegion);

				if (moved)
				{
					if (HudsAreCreated)
					{
						mHud.Region = new Rectangle(mRegion.Location, mHud.Region.Size);
					}
					if (MoveEnd != null)
					{
						MoveEnd(this, EventArgs.Empty);
					}
				}

				if (resized)
				{
					if (ResizeEnd != null)
					{
						ResizeEnd(this, EventArgs.Empty);
					}
					Repaint();
					RepaintWindow();
				}
			}
		}

		/// <summary>
		/// Gets or sets the minimum size of the entire window, including 
		/// the title bar. Must be >= 24.
		/// </summary>
		public Size MinSize
		{
			get { return mMinSize; }
			set
			{
				mMinSize = new Size(Math.Max(value.Width, MinimizedWidth), Math.Max(value.Height, 24));
				if (Width < mMinSize.Width || Height < mMinSize.Height)
				{
					int newWidth = Width, newHeight = Height;
					if (newWidth < mMinSize.Width)
						newWidth = mMinSize.Width;
					if (newHeight < mMinSize.Height)
						newHeight = mMinSize.Height;
					Size = new Size(newWidth, newHeight);
				}
			}
		}

		/// <summary>
		/// Gets or sets the maximum size of the entire window, including 
		/// the title bar. Must be &lt;= 1600.
		/// </summary>
		public Size MaxSize
		{
			get { return mMaxSize; }
			set
			{
				mMaxSize = new Size(Math.Min(value.Width, 1600), Math.Min(value.Height, 1600));
				if (Width > mMaxSize.Width || Height > mMaxSize.Height)
				{
					int newWidth = Width, newHeight = Height;
					if (newWidth > mMaxSize.Width)
						newWidth = mMaxSize.Width;
					if (newHeight > mMaxSize.Height)
						newHeight = mMaxSize.Height;
					Size = new Size(newWidth, newHeight);
				}
				Manager.RecreateHud(this);
			}
		}

		/// <summary>
		/// Gets the mouse buttons that were pressed while over the client area
		/// and not yet released.  This can be used to determine if a drag is
		/// in progress.
		/// </summary>
		protected MouseButtons ClientMouseButtons
		{
			get { return mClientMouseButtons; }
		}

		#region Region Accessors
		/// <summary>Gets or sets the window's position.</summary>
		public Point Location
		{
			get { return mRegion.Location; }
			set { Region = new Rectangle(value, Region.Size); }
		}

		/// <summary>Gets or sets the window's size.</summary>
		public Size Size
		{
			get { return mRegion.Size; }
			set { Region = new Rectangle(Region.Location, value); }
		}

		/// <summary>Gets or sets the window's x-coordinate.</summary>
		public int X
		{
			get { return mRegion.X; }
			set { Region = new Rectangle(value, Region.Y, Region.Width, Region.Height); }
		}

		/// <summary>Gets or sets the window's y-coordinate.</summary>
		public int Y
		{
			get { return mRegion.Y; }
			set { Region = new Rectangle(Region.X, value, Region.Width, Region.Height); }
		}

		/// <summary>Gets or sets the window's width.</summary>
		public int Width
		{
			get { return mRegion.Width; }
			set { Region = new Rectangle(Region.X, Region.Y, value, Region.Height); }
		}

		/// <summary>Gets or sets the window's height.</summary>
		public int Height
		{
			get { return mRegion.Height; }
			set { Region = new Rectangle(Region.X, Region.Y, Region.Width, value); }
		}

		/// <summary>Gets the x-coordinate of the window's left edge.</summary>
		public int Left { get { return mRegion.Left; } }

		/// <summary>Gets the x-coordinate of the window's right edge.</summary>
		public int Right { get { return mRegion.Right; } }

		/// <summary>Gets the y-coordinate of the window's top edge.</summary>
		public int Top { get { return mRegion.Top; } }

		/// <summary>Gets the y-coordinate of the window's bottom edge.</summary>
		public int Bottom { get { return mRegion.Bottom; } }

		/// <summary>
		/// Gets or sets the minimum width of the entire window. Must be >= 24.
		/// </summary>
		public int MinWidth
		{
			get { return MinSize.Width; }
			set { MinSize = new Size(value, MinSize.Height); }
		}

		/// <summary>
		/// Gets or sets the minimum height of the entire window, including 
		/// the title bar. Must be >= 24.
		/// </summary>
		public int MinHeight
		{
			get { return MinSize.Height; }
			set { MinSize = new Size(MinSize.Width, value); }
		}

		/// <summary>
		/// Gets or sets the maximum width of the entire window. Must be 
		/// &lt;= 1600.
		/// </summary>
		public int MaxWidth
		{
			get { return MaxSize.Width; }
			set { MaxSize = new Size(value, MaxSize.Height); }
		}

		/// <summary>
		/// Gets or sets the maximum height of the entire window, including 
		/// the title bar. Must be &lt;= 1600.
		/// </summary>
		public int MaxHeight
		{
			get { return MaxSize.Height; }
			set { MaxSize = new Size(MaxSize.Width, value); }
		}

		/// <summary>
		/// Gets or sets the region of the AC window that the client part of 
		/// the window occupies. Setting the client region will move/resize 
		/// the entire window.
		/// </summary>
		protected Rectangle ClientRegion
		{
			get { return mClientRegion; }
			set
			{
				Region = new Rectangle(
					value.X + mRegion.X - mClientRegion.X,
					value.Y + mRegion.Y - mClientRegion.Y,
					value.Width + mRegion.Width - mClientRegion.Width,
					value.Height + mRegion.Height - mClientRegion.Height
				);
			}
		}

		/// <summary>
		/// Gets or sets the size of the client region. Setting the client 
		/// size will resize the entire window.
		/// </summary>
		protected Size ClientSize
		{
			get { return mClientRegion.Size; }
			set { ClientRegion = new Rectangle(mClientRegion.Location, value); }
		}

		/// <summary>
		/// Gets or sets the position of the client region in the AC window.
		/// Setting the client position will move the entire window.
		/// </summary>
		protected Point ClientLocation
		{
			get { return mClientRegion.Location; }
			set { ClientRegion = new Rectangle(value, mClientRegion.Size); }
		}

		private int DisplayedWidth
		{
			get { return Minimized ? MinimizedWidth : mRegion.Width; }
		}

		private int DisplayedHeight
		{
			get { return Minimized ? MinimizedHeight : mRegion.Height; }
		}
		#endregion

		/// <summary>Gets or sets the title of this window.</summary>
		public string Title
		{
			get { return mTitle; }
			set
			{
				if (mTitle != value)
				{
					mTitle = value;
					RepaintWindow();
				}
			}
		}

		/// <summary>
		/// Sets the alpha transparency of both the frame and content of the 
		/// window, and disables fading out when the mouse is not over the window. 
		/// Alpha must be between 0-255; 0 is transparent and 255 is opaque.
		/// </summary>
		/// <param name="alphaFrame">The alpha value for AlphaFrameActive and
		///		AlphaFrameInactive.</param>
		public void SetAlpha(int alphaFrame)
		{
			this.AlphaFrameActive = alphaFrame;
			this.AlphaFrameInactive = alphaFrame;
		}

		/// <summary>
		/// Sets the alpha transparencies for when the mouse is over and off 
		/// of the window.
		/// </summary>
		/// <param name="activeAlpha">The alpha value for AlphaFrameActive and 
		///		AlphaClientActive.</param>
		/// <param name="inactiveAlpha">The alpha value for AlphaFrameInactive 
		///		and AlphaClientInactive.</param>
		public void SetAlphaFading(int activeAlpha, int inactiveAlpha)
		{
			this.AlphaFrameActive = activeAlpha;
			this.AlphaFrameInactive = inactiveAlpha;
		}

		/// <summary>
		/// Gets or sets the alpha transparency of the border and title bar of 
		/// the window when the mouse is hovering over the window. Alpha must 
		/// be between 0-255; 0 is transparent and 255 is opaque.
		/// </summary>
		/// <seealso cref="AlphaClientActive"/>
		/// <seealso cref="AlphaFrameInactive"/>
		public int AlphaFrameActive
		{
			get { return mAlphaFrameActive; }
			set
			{
				if (value < 0) { mAlphaFrameActive = 0; }
				else if (value > 255) { mAlphaFrameActive = 255; }
				else { mAlphaFrameActive = value; }

				if (MouseOnWindow || Sticky)
					AlphaFrame = mAlphaFrameActive;

				RepaintWindow();
			}
		}

		/// <summary>
		/// Gets or sets the alpha transparency of the content of the window 
		/// when the mouse is not over the window. Alpha must be between 
		/// 0-255; 0 is transparent and 255 is opaque.
		/// </summary>
		/// <seealso cref="AlphaClientInactive"/>
		/// <seealso cref="AlphaFrameActive"/>
		public int AlphaFrameInactive
		{
			get { return mAlphaFrameInactive; }
			set
			{
				if (value < 0) { mAlphaFrameInactive = 0; }
				else if (value > 255) { mAlphaFrameInactive = 255; }
				else { mAlphaFrameInactive = value; }

				if (!MouseOnWindow && !Sticky)
					AlphaFrame = mAlphaFrameInactive;

				RepaintWindow();
			}
		}

		/// <summary>
		/// Gets or sets the outer color for the border.
		/// </summary>
		public Color BorderColor1
		{
			get { return mBorderColor1; }
			set { mBorderColor1 = value; RepaintWindow(); }
		}

		/// <summary>
		/// Gets or sets the inner color for the border.
		/// </summary>
		public Color BorderColor2
		{
			get { return mBorderColor2; }
			set { mBorderColor2 = value; RepaintWindow(); }
		}

		/// <summary>
		/// Gets or sets the color of the border when the user mouses over the border.
		/// </summary>
		public Color BorderColorHighlight
		{
			get { return mBorderColorHighlight; }
			set { mBorderColorHighlight = value; RepaintWindow(); }
		}

		/// <summary>
		/// Gets or sets the background color of the entire window.
		/// </summary>
		public Color BackgroundColor
		{
			get { return mBackgroundColor; }
			set { mBackgroundColor = value; RepaintWindow(); }
		}

		/// <summary>
		/// Gets or sets the method that the window will use to redraw the 
		/// client area as the window is resized.
		/// </summary>
		public HudResizeDrawMode ResizeDrawMode
		{
			get { return mResizeDrawMode; }
			set { mResizeDrawMode = value; }
		}
		#endregion

		#region Protected Accesors and Methods
		protected PluginHost Host
		{
			get { return Manager.Host; }
		}

		protected CoreManager Core
		{
			get { return Manager.Core; }
		}

		/// <summary>Gets the image used for drawing the client.</summary>
		protected Bitmap ClientImage
		{
			get { return mClientImage; }
		}

		/// <summary>Gets the mouse's absolute position in the AC window.</summary>
		protected Point MouseLocation
		{
			get { return mMouseLocation; }
		}

		/// <summary>Gets the mouse's position relative to the client window.</summary>
		protected Point MouseLocationClient
		{
			get { return new Point(mMouseLocation.X - mClientRegion.X, mMouseLocation.Y - mClientRegion.Y); }
		}

		/// <summary>Gets whether the mouse is over the client region.</summary>
		protected bool MouseOnClient
		{
			get { return mMouseOnClient; }
		}

		/// <summary>Gets whether the client area is visible.</summary>
		protected bool ClientVisible
		{
			get { return Visible && !Minimized; }
		}

		/// <summary>
		/// Converts absolute screen coordinates into coordinates relative to
		/// the client's location.
		/// </summary>
		/// <param name="p">The absolute screen coordinates.</param>
		/// <returns>Coordinates relative to the client's location.</returns>
		protected Point ScreenToClient(Point p)
		{
			return new Point(p.X - ClientLocation.X, p.Y - ClientLocation.Y);
		}

		/// <summary>
		/// Converts coordinates relative to the client's location into
		/// absolute screen coordinates.
		/// </summary>
		/// <param name="p">The coordinates relative to the client's location.</param>
		/// <returns>Absolute screen coordinates.</returns>
		protected Point ClientToScreen(Point p)
		{
			return new Point(p.X + ClientLocation.X, p.Y + ClientLocation.Y);
		}

		/// <summary>
		/// Causes the <see cref="PaintClient()"/> function to be called 
		/// on the next frame.  Multiple calls to this function before 
		/// the next frame will result in only one call to PaintClient().
		/// </summary>
		protected void Repaint()
		{
			mClientNeedsRepaint = true;
			// The msRepaintHeartbeat timer will call RepaintHeartbeat() next frame
		}

		/// <summary>
		/// This is where the client should do all of its painting onto the 
		/// GDI+ surface.
		/// <para>You cannot call this function directly; instead call 
		/// <see cref="Repaint()"/>, which will indirectly call this.</para>
		/// </summary>
		/// <param name="g">The GDI+ surface to draw on.</param>
		/// <param name="imageDataLost">Indicates whether the bitmap has been
		///		cleared since the last time this function was called (e.g.
		///		because the image was resized).</param>
		protected abstract void PaintClient(Graphics g, bool imageDataLost);

		/// <summary>
		/// Shrinks (or grows) a rectangle by the specified number in every 
		/// direction. That is, it increases the left and top, and decreases 
		/// the right and bottom of the rectangle by the specified amount.
		/// </summary>
		/// <param name="r">The rectangle to shrink or grow</param>
		/// <param name="shrinkBy">The amout to shrink. Use a negative number 
		///		to grow.</param>
		/// <returns>A new rectangle that is a shrunken/grown version of the
		///		original.</returns>
		protected Rectangle ShrinkRect(Rectangle r, int shrinkBy)
		{
			return new Rectangle(r.X + shrinkBy, r.Y + shrinkBy,
				r.Width - 2 * shrinkBy, r.Height - 2 * shrinkBy);
		}
		#endregion

		#region Utility Functions
		private bool CalcMouseOnClient()
		{
			return Visible && mMouseOnWindow && !Minimized &&
				mClientRegion.Contains(mMouseLocation);
		}

		private bool CalcMouseOnWindow()
		{
			return Visible && mRegion.Contains(mMouseLocation) &&
				(!Minimized || mMouseLocation.Y <= (mRegion.Y + TitleBarHeight + BorderWidth));
		}

		private Rectangle CalculateClientRegion(Rectangle region)
		{
			return new Rectangle(
				region.X + BorderPaddedWidth,
				region.Y + TitleBarPaddedHeight + 2 * BorderPaddedWidth,
				region.Width - 2 * BorderPaddedWidth,
				region.Height - TitleBarPaddedHeight - 3 * BorderPaddedWidth);
		}

		private Rectangle ConstrainRegion(Rectangle region)
		{
			if (region.Width < MinWidth) { region.Width = MinWidth; }
			else if (region.Width > MaxWidth) { region.Width = MaxWidth; }

			if (region.Height < MinHeight) { region.Height = MinHeight; }
			else if (region.Height > MaxHeight) { region.Height = MaxHeight; }

			if (region.X < MinWidth - BorderPaddedWidth - region.Width)
			{
				region.X = MinWidth - BorderPaddedWidth - region.Width;
			}
			if (region.Y < 24 - TitleBarPaddedHeight)
			{
				region.Y = 24 - TitleBarPaddedHeight;
			}

			return region;
		}

		private Rectangle CloseBoxRect
		{
			get
			{
				return new Rectangle(
					mRegion.X + DisplayedWidth - BorderPaddedWidth - ControlSize,
					mRegion.Top + BorderPaddedWidth, ControlSize, ControlSize);
			}
		}

		private Rectangle MinRestoreBoxRect
		{
			get
			{
				return new Rectangle(
					mRegion.X + DisplayedWidth - BorderPaddedWidth - ControlSize - ControlPaddedSize,
					mRegion.Top + BorderPaddedWidth, ControlSize, ControlSize);
			}
		}

		private Rectangle StickyBoxRect
		{
			get
			{
				return new Rectangle(
					mRegion.X + DisplayedWidth - BorderPaddedWidth - ControlSize - 2 * ControlPaddedSize,
					mRegion.Top + BorderPaddedWidth, ControlSize, ControlSize);
			}
		}

		private Rectangle TitleBarRect
		{
			get
			{
				return new Rectangle(mRegion.Left + BorderWidth, mRegion.Top + BorderWidth,
					DisplayedWidth - 2 * BorderWidth, TitleBarHeight + BorderWidth);
			}
		}

		private Rectangle RelToAbs(Rectangle r)
		{
			return new Rectangle(r.X + mRegion.X, r.Y + mRegion.Y, r.Width, r.Height);
		}

		private Rectangle AbsToRel(Rectangle r)
		{
			return new Rectangle(r.X - mRegion.X, r.Y - mRegion.Y, r.Width, r.Height);
		}

		private Border HighlightedBorder
		{
			get { return mHighlightedBorder; }
			set
			{
				if (mHighlightedBorder != value)
				{
					mHighlightedBorder = value;
					RepaintBorders();
				}
			}
		}

		private Point ClientOffset
		{
			get { return new Point(mClientRegion.X - mRegion.X, mClientRegion.Y - mRegion.Y); }
		}

		private Rectangle RelativeClientRegion
		{
			get { return new Rectangle(ClientOffset, ClientSize); }
		}
		#endregion

		#region Painting
		/// <summary>This is called by the manager each frame.</summary>
		void IManagedHud.RepaintHeartbeat()
		{
			if (Heartbeat != null)
				Heartbeat(this, EventArgs.Empty);

			if (!Visible)
			{
				DisposeHudsInternal();
			}
			else
			{
				if (!HudsAreCreated)
				{
					Manager.RecreateHud(this);
				}
				else if (mHud.Region.Location != mRegion.Location)
				{
					mHud.Region = new Rectangle(mRegion.Location, mHud.Region.Size);
				}

				if (mWindowNeedsRepaint)
				{
					PaintWindowInternal();
				}
				else
				{
					if (mBordersNeedRepaint)
					{
						PaintBorders(false, false);
					}
					if (mClientNeedsRepaint)
					{
						DrawClientImage(false, true);
					}
				}

				mHud.Enabled = Visible;
			}
		}

		/// <summary>
		/// Deletes and recreates the window huds, causing this window to be 
		/// the topmost window. <b>Do not use this function if this hud is 
		/// managed by a HudManager</b>.  Instead, use 
		/// <see cref="HudManager.RecreateHud(IManagedHud)"/>.
		/// </summary>
		public virtual void RecreateHud()
		{
			DisposeHudsInternal();
			if (Visible)
			{
				mHud = Host.Render.CreateHud(new Rectangle(mRegion.Location, MaxSize));
				mHud.Enabled = true;
				mHud.Alpha = AlphaFrame;

				PaintWindowInternal();
				//DrawClientImage();
			}
		}

		private bool HudsAreCreated
		{
			get { return mHud != null && !mHud.Lost; }
		}

		private void DisposeHudsInternal()
		{
			if (mHud != null)
			{
				mHud.Enabled = false;
				Host.Render.RemoveHud(mHud);
				mHud.Dispose();
				mHud = null;
			}
		}

		private void RepaintWindow()
		{
			mWindowNeedsRepaint = true;
			// The repaint will happen in RepaintHeartbeat(), next frame
		}

		private void RepaintBorders()
		{
			mBordersNeedRepaint = true;
		}

		// mHud must not be lost or null
		private void PaintWindowInternal()
		{
			mWindowNeedsRepaint = false;

			mHud.Clear();

			mHud.Fill(new Rectangle(BorderWidth, BorderWidth, DisplayedWidth - 2 * BorderWidth,
				DisplayedHeight - 2 * BorderWidth), BackgroundColor);
			PaintBorders(false, true);

			// Draw title
			Rectangle titleRect = new Rectangle(BorderPaddedWidth, BorderPaddedWidth,
				DisplayedWidth - 2 * BorderPaddedWidth - NumControlBoxes * ControlPaddedSize,
				TitleBarPaddedHeight);
			mHud.BeginText("Times New Roman", 14);
			mHud.WriteText(Title, Color.White, WriteTextFormats.Center, titleRect);
			mHud.EndText();
			DrawControlBoxes(true);

			DrawClientImage(true, false);

			mHud.EndRender();

			//if (ResizeDrawMode != HudResizeDrawMode.Repaint && IsResizing)
			//{
			//    DrawClientImage();
			//}
		}

		private void DrawClientImage(bool inRender, bool paintBackground)
		{
			if (!ClientVisible)
				return;

			bool startInRender = inRender;
			bool endInRender = inRender;

			if (mClientNeedsRepaint)
			{
				if (mClientImage.Size != mClientRegion.Size)
				{
					mClientImage = new Bitmap(mClientRegion.Width, mClientRegion.Height);
					mClientImageLost = true;
				}
				using (Graphics gClient = Graphics.FromImage(mClientImage))
				{
					PaintClient(gClient, mClientImageLost);
				}
				mClientNeedsRepaint = false;
				mClientImageLost = false;
			}

			Rectangle drawRect = RelativeClientRegion;

			if (paintBackground)
			{
				if (startInRender)
				{
					mHud.EndRender();
					startInRender = false;
				}
				mHud.Fill(drawRect, BackgroundColor);
			}

			Bitmap drawImage = mClientImage;
			if (mClientRegion.Size != mClientImage.Size)
			{
				if (ResizeDrawMode == HudResizeDrawMode.Scale)
				{
					drawImage = new Bitmap(mClientImage, mClientRegion.Size);
				}
				else if (ResizeDrawMode == HudResizeDrawMode.Crop)
				{
					if (mClientImage.Width < mClientRegion.Width
							&& mClientImage.Height < mClientRegion.Height)
					{
						drawRect.Size = mClientImage.Size;
					}
					else
					{
						Size sz = new Size(Math.Min(mClientRegion.Width, mClientImage.Width),
							Math.Min(mClientRegion.Height, mClientImage.Height));

						drawImage = new Bitmap(sz.Width, sz.Height);
						drawRect.Size = sz;
						Graphics.FromImage(drawImage).DrawImageUnscaled(mClientImage, 0, 0);
					}
				}
				else if (ResizeDrawMode == HudResizeDrawMode.CropCenter)
				{
					if (mClientImage.Width < mClientRegion.Width
							&& mClientImage.Height < mClientRegion.Height)
					{
						drawRect.X += (mClientRegion.Width - mClientImage.Width) / 2;
						drawRect.Y += (mClientRegion.Height - mClientImage.Height) / 2;
						drawRect.Size = mClientImage.Size;
					}
					else
					{
						drawImage = new Bitmap(mClientRegion.Width, mClientRegion.Height);
						drawRect.Size = mClientRegion.Size;
						Graphics.FromImage(drawImage).DrawImageUnscaled(mClientImage,
							(mClientRegion.Width - mClientImage.Width) / 2,
							(mClientRegion.Height - mClientImage.Height) / 2);
					}
				}
			}

			if (!startInRender)
				mHud.BeginRender();

			mHud.DrawImage(drawImage, drawRect);

			if (!endInRender)
				mHud.EndRender();
		}

		private void PaintBorders(bool startInRender, bool endInRender)
		{
			if (!HudsAreCreated)
				return;

			mBordersNeedRepaint = false;

			if (startInRender) { mHud.EndRender(); }

			Color north = ((HighlightedBorder & Border.North) == 0) ? BorderColor1 : BorderColorHighlight;
			mHud.Fill(new Rectangle(1, 1, DisplayedWidth - 2, 3), north);
			mHud.Fill(new Rectangle(1, 2, DisplayedWidth - 2, 1), BorderColor2);

			Color south = ((HighlightedBorder & Border.South) == 0) ? BorderColor1 : BorderColorHighlight;
			mHud.Fill(new Rectangle(1, DisplayedHeight - 4, DisplayedWidth - 2, 3), south);
			mHud.Fill(new Rectangle(1, DisplayedHeight - 3, DisplayedWidth - 2, 1), BorderColor2);

			Color west = ((HighlightedBorder & Border.West) == 0) ? BorderColor1 : BorderColorHighlight;
			mHud.Fill(new Rectangle(1, 1, 3, DisplayedHeight - 2), west);
			mHud.Fill(new Rectangle(2, 1, 1, DisplayedHeight - 2), BorderColor2);

			Color east = ((HighlightedBorder & Border.East) == 0) ? BorderColor1 : BorderColorHighlight;
			mHud.Fill(new Rectangle(DisplayedWidth - 4, 1, 3, DisplayedHeight - 2), east);
			mHud.Fill(new Rectangle(DisplayedWidth - 3, 1, 1, DisplayedHeight - 2), BorderColor2);

			if (!Minimized)
			{
				Color title = ((HighlightedBorder & Border.Title) == 0) ? BorderColor1 : BorderColorHighlight;
				mHud.Fill(new Rectangle(BorderWidth, BorderWidth + TitleBarHeight,
					DisplayedWidth - 2 * BorderWidth, 3), title);
				mHud.Fill(new Rectangle(BorderWidth - 1, BorderWidth + TitleBarHeight + 1,
					DisplayedWidth - 2 * BorderWidth + 2, 1), BorderColor2);
			}

			mHud.BeginRender();

			// Draw corners
			Rectangle cornerRect = new Rectangle(0, 0, 5, 5);
			mHud.DrawImage(Icons.Window.BorderCorner, cornerRect);
			cornerRect.X = DisplayedWidth - 5;
			mHud.DrawImage(Icons.Window.BorderCorner, cornerRect);
			cornerRect.Y = DisplayedHeight - 5;
			mHud.DrawImage(Icons.Window.BorderCorner, cornerRect);
			cornerRect.X = 0;
			mHud.DrawImage(Icons.Window.BorderCorner, cornerRect);

			if (!endInRender) { mHud.EndRender(); }
		}

		private void DrawControlBoxes(bool inRender)
		{
			if (!HudsAreCreated)
				return;

			if (!inRender) { mHud.BeginRender(); }

			if (mMouseOnClose && CloseBoxRect.Contains(mMouseLocation))
			{
				mHud.DrawImage(Icons.Window.CloseBox_pressed, AbsToRel(CloseBoxRect));
			}
			else
			{
				mHud.DrawImage(Icons.Window.CloseBox, AbsToRel(CloseBoxRect));
			}

			if (mMouseOnMinRestore && MinRestoreBoxRect.Contains(mMouseLocation))
			{
				if (Minimized)
					mHud.DrawImage(Icons.Window.RestoreBox_pressed, AbsToRel(MinRestoreBoxRect));
				else
					mHud.DrawImage(Icons.Window.MinimizeBox_pressed, AbsToRel(MinRestoreBoxRect));
			}
			else
			{
				if (Minimized)
					mHud.DrawImage(Icons.Window.RestoreBox, AbsToRel(MinRestoreBoxRect));
				else
					mHud.DrawImage(Icons.Window.MinimizeBox, AbsToRel(MinRestoreBoxRect));
			}

			if (AlphaFadingEnabled)
			{
				if (Sticky || (mMouseOnSticky && StickyBoxRect.Contains(mMouseLocation)))
				{
					mHud.DrawImage(Icons.Window.StickyBox_pressed, AbsToRel(StickyBoxRect));
				}
				else
				{
					mHud.DrawImage(Icons.Window.StickyBox, AbsToRel(StickyBoxRect));
				}
			}

			if (!inRender) { mHud.EndRender(); }
		}
		#endregion

		#region Alpha Fading
		private bool AlphaFadingEnabled
		{
			get { return AlphaFrameActive != AlphaFrameInactive; }
		}

		// Handles the MouseEnter event
		private void FadeIn(object sender, HudMouseEventArgs e)
		{
			FadeAlpha(AlphaFrameActive, 300, 0);
		}

		// Handles the MouseLeave event
		private void FadeOut(object sender, HudMouseEventArgs e)
		{
			if (!Sticky)
			{
				FadeAlpha(AlphaFrameInactive, 500, 0);
			}
		}

		protected int AlphaFrame
		{
			get { return mAlphaFrame; }
			set
			{
				if (mAlphaFrame != value)
				{
					if (value < 0) { mAlphaFrame = 0; }
					else if (value > 255) { mAlphaFrame = 255; }
					else { mAlphaFrame = value; }

					if (AlphaChanged != null)
						AlphaChanged(this, new AlphaChangedEventArgs(mAlphaFrame));
				}

				if (HudsAreCreated)
					mHud.Alpha = mAlphaFrame;
			}
		}

		/// <summary>Fades the window's opacity to the specified value.</summary>
		/// <param name="finalAlphaFrame">The target alpha value for the 
		///		border and title bar. Must be between 0-255.</param>
		/// <param name="finalAlphaClient">The target alpha value for the 
		///		content of the window. Must be between 0-255.</param>
		/// <param name="durationMillis">The duration of the fade in 
		///		milliseconds. Must be >= 0.</param>
		///	<param name="initialDelayMillis">The initial delay before 
		///		fading.</param>
		///	<exception cref="ArgumentException">If any of the arguments are
		///		not within valid ranges.</exception>
		private void FadeAlpha(int finalAlphaFrame, long durationMillis, int initialDelayMillis)
		{

			if (finalAlphaFrame < 0) { finalAlphaFrame = 0; }
			if (finalAlphaFrame > 255) { finalAlphaFrame = 255; }

			if (mFaderTimer.Running)
			{
				mFaderTimer.Stop();
				int desiredDist = Math.Abs(finalAlphaFrame - mFadeEndAlphaFrame);
				int actualDist = Math.Abs(finalAlphaFrame - AlphaFrame);

				if (desiredDist == 0)
					mFadeDurationMillis = durationMillis;
				else
					mFadeDurationMillis = durationMillis * actualDist / desiredDist;
			}
			else
			{
				mFadeDurationMillis = durationMillis;
			}

			if (!Visible || mFadeDurationMillis <= 0)
			{
				AlphaFrame = finalAlphaFrame;
				return;
			}

			if (AlphaFrame == finalAlphaFrame)
				return;

			mFadeEndAlphaFrame = finalAlphaFrame;

			mFadeStartAlphaFrame = AlphaFrame;

			mFadeBeginTime = DateTime.Now;
			if (initialDelayMillis <= 0)
			{
				mFadeInitialDelay = false;
				mFaderTimer.Start(1);
			}
			else
			{
				mFadeInitialDelay = true;
				mFaderTimer.Start(initialDelayMillis);
			}
		}

		private void FaderTimer_Timeout(Decal.Interop.Input.Timer Source)
		{
			try
			{
				if (mFadeInitialDelay)
				{
					mFadeInitialDelay = false;
					mFaderTimer.Stop();
					mFaderTimer.Start(1);
					mFadeBeginTime = DateTime.Now;
					return;
				}

				long elapsedMillis = ((TimeSpan)DateTime.Now.Subtract(mFadeBeginTime)).Milliseconds;
				int newAlphaFrame = (int)(mFadeStartAlphaFrame +
					(mFadeEndAlphaFrame - mFadeStartAlphaFrame) * elapsedMillis / mFadeDurationMillis);

				bool fadingUpFrame = mFadeEndAlphaFrame > mFadeStartAlphaFrame;
				bool done = true;

				if (fadingUpFrame && newAlphaFrame >= mFadeEndAlphaFrame ||
					!fadingUpFrame && newAlphaFrame <= mFadeEndAlphaFrame)
				{
					AlphaFrame = mFadeEndAlphaFrame;
				}
				else
				{
					AlphaFrame = newAlphaFrame;
					done = false;
				}

				if (done) { mFaderTimer.Stop(); }
			}
			catch (Exception ex) { Manager.HandleException(ex); }
		}
		#endregion

		#region WindowMessage Handling
		void IManagedHud.WindowMessage(WindowMessageEventArgs e)
		{
			if (WindowMessage != null)
				WindowMessage(this, e);

			// Only process mouse messages when the window is visible
			if (!Visible || e.Msg < WM_MOUSEFIRST || e.Msg > WM_MOUSELAST)
				return;

			Point prevLocation = mMouseLocation;
			bool prevOnWindow = mMouseOnWindow;
			bool prevOnClient = mMouseOnClient;

			mMouseLocation = new Point(e.LParam);
			if (e.Msg == WM_MOUSEWHEEL)
			{
				// WM_MOUSEWHEEL messages are apparently absolute on the screen, 
				// not relative to the AC window; fix with ScreenToClient
				ScreenToClient(Host.Decal.Hwnd, ref mMouseLocation);
			}
			mMouseOnWindow = CalcMouseOnWindow();
			mMouseOnWindow = mMouseOnWindow && Manager.MouseHoveringOnHud(this);
			mMouseOnClient = CalcMouseOnClient();

			// Calculate relative coordinates
			int wX = mMouseLocation.X - mRegion.X;
			int wY = mMouseLocation.Y - mRegion.Y;
			int cX = mMouseLocation.X - mClientRegion.X;
			int cY = mMouseLocation.Y - mClientRegion.Y;

			short fwKeys = (short)(e.WParam & 0xFFFF);
			MouseButtons button = MouseButtons.None;
			if (e.Msg == WM_MOUSEMOVE) { }
			else if (e.Msg == WM_LBUTTONDOWN || e.Msg == WM_LBUTTONUP)
				button = MouseButtons.Left;
			else if (e.Msg == WM_RBUTTONDOWN || e.Msg == WM_RBUTTONUP)
				button = MouseButtons.Right;
			else if (e.Msg == WM_MBUTTONDOWN || e.Msg == WM_MBUTTONUP)
				button = MouseButtons.Middle;


			bool dblClick = false;
			if (e.Msg == WM_LBUTTONDOWN || e.Msg == WM_RBUTTONDOWN || e.Msg == WM_MBUTTONDOWN)
			{
				dblClick = (mMouseLastClickButton == button)
					&& Math.Abs(mMouseLocation.X - mMouseLastClickLocation.X) <= msDblClickRect.Width
					&& Math.Abs(mMouseLocation.Y - mMouseLastClickLocation.Y) <= msDblClickRect.Height
					&& Math.Abs(DateTime.Now.Ticks - mMouseLastClickTicks) <= msDblClickTicks;

				mMouseLastClickTicks = dblClick ? long.MinValue : DateTime.Now.Ticks;
				mMouseLastClickLocation = mMouseLocation;
				mMouseLastClickButton = button;
			}

			#region Handle Moving, Resizing, and Control Button Clicks
			if (e.Msg == WM_MOUSEMOVE)
			{
				// Handle moving the window
				if (IsMoving)
				{
					mRegion.X = mOriginalRegion.X + mMouseLocation.X - mMouseDownLocation.X;
					mRegion.Y = mOriginalRegion.Y + mMouseLocation.Y - mMouseDownLocation.Y;
					mRegion = ConstrainRegion(mRegion);
					mClientRegion = CalculateClientRegion(mRegion);

					// Recalculate whether the mouse is on the window and
					// client, since the regions have changed
					mMouseOnWindow = CalcMouseOnWindow();
					mMouseOnClient = CalcMouseOnClient();

					if (HudsAreCreated)
					{
						mHud.Region = new Rectangle(mRegion.Location, mHud.Region.Size);
					}
					if (Moving != null)
					{
						Moving(this, EventArgs.Empty);
					}
				}
				// Handle resizing the window
				else if (IsResizing)
				{
					if ((mMoveResizeMode & MoveResizeMode.ResizeN) != 0)
					{
						int oldBottom = mRegion.Bottom;
						mRegion.Y = mOriginalRegion.Y + mMouseLocation.Y - mMouseDownLocation.Y;
						mRegion.Height = oldBottom - mRegion.Y;
						if (mRegion.Height < MinHeight)
						{
							mRegion.Y = oldBottom - MinHeight;
							mRegion.Height = MinHeight;
						}
						else if (mRegion.Height > MaxHeight)
						{
							mRegion.Y = oldBottom - MaxHeight;
							mRegion.Height = MaxHeight;
						}
					}
					else if ((mMoveResizeMode & MoveResizeMode.ResizeS) != 0)
					{
						mRegion.Height = mOriginalRegion.Height + mMouseLocation.Y - mMouseDownLocation.Y;
					}

					if ((mMoveResizeMode & MoveResizeMode.ResizeW) != 0)
					{
						int oldRight = mRegion.Right;
						mRegion.X = mOriginalRegion.X + mMouseLocation.X - mMouseDownLocation.X;
						mRegion.Width = oldRight - mRegion.X;
						if (mRegion.Width < MinWidth)
						{
							mRegion.X = oldRight - MinWidth;
							mRegion.Width = MinWidth;
						}
						else if (mRegion.Width > MaxWidth)
						{
							mRegion.X = oldRight - MaxWidth;
							mRegion.Width = MaxWidth;
						}
					}
					else if ((mMoveResizeMode & MoveResizeMode.ResizeE) != 0)
					{
						mRegion.Width = mOriginalRegion.Width + mMouseLocation.X - mMouseDownLocation.X;
					}

					mRegion = ConstrainRegion(mRegion);
					mClientRegion = CalculateClientRegion(mRegion);

					// Recalculate whether the mouse is on the window and
					// client, since the regions have changed
					mMouseOnWindow = CalcMouseOnWindow();
					mMouseOnClient = CalcMouseOnClient();

					if (Resizing != null)
					{
						Resizing(this, EventArgs.Empty);
					}

					RepaintWindow();
					if (ResizeDrawMode == HudResizeDrawMode.Repaint)
						Repaint();
				}

				CalculateHighlightedBorder();

				// Handle the mouse entering/leaving the control boxes
				if (mMouseOnClose || mMouseOnMinRestore || mMouseOnSticky)
				{
					DrawControlBoxes(false);
				}
			} // if (e.Msg == WM_MOUSEMOVE)

			else if (e.Msg == WM_LBUTTONDOWN && mMouseOnWindow)
			{
				// Put this window on top of other windows
				Manager.BringToFront(this, false);

				if (CloseBoxRect.Contains(mMouseLocation))
				{
					mMouseOnClose = true;
					DrawControlBoxes(false);
					e.Eat = true;
				}
				else if (MinRestoreBoxRect.Contains(mMouseLocation))
				{
					mMouseOnMinRestore = true;
					DrawControlBoxes(false);
					e.Eat = true;
				}
				else if (AlphaFadingEnabled && StickyBoxRect.Contains(mMouseLocation))
				{
					mMouseOnSticky = true;
					DrawControlBoxes(false);
					e.Eat = true;
				}
				else if (TitleBarRect.Contains(mMouseLocation))
				{
					if (dblClick)
					{
						Minimized = !Minimized;
					}
					else
					{
						mMoveResizeMode = MoveResizeMode.Moving;
						mMouseDownLocation = mMouseLocation;
						mOriginalRegion = mRegion;
					}
					e.Eat = true;
				}
				else
				{
					mMoveResizeMode = MoveResizeMode.Idle;

					if (!Minimized)
					{
						if (mMouseLocation.X <= mRegion.Left + BorderPaddedWidth)
							mMoveResizeMode |= MoveResizeMode.ResizeW;
						else if (mMouseLocation.X >= mRegion.Right - BorderPaddedWidth)
							mMoveResizeMode |= MoveResizeMode.ResizeE;

						if (mMouseLocation.Y <= mRegion.Top + BorderPaddedWidth)
							mMoveResizeMode |= MoveResizeMode.ResizeN;
						else if (mMouseLocation.Y >= mRegion.Bottom - BorderPaddedWidth)
							mMoveResizeMode |= MoveResizeMode.ResizeS;

						if (mMoveResizeMode != MoveResizeMode.Idle)
						{
							mMouseDownLocation = mMouseLocation;
							mOriginalRegion = mRegion;
							e.Eat = true;
						}
					}
				}
			} // else if (e.Msg == WM_LBUTTONDOWN && mMouseOnWindow)

			else if (e.Msg == WM_LBUTTONUP)
			{
				if (MoveEnd != null && mMoveResizeMode == MoveResizeMode.Moving)
				{
					MoveEnd(this, EventArgs.Empty);
				}
				else if ((mMoveResizeMode & MoveResizeMode.Resizing) != 0)
				{
					if (ResizeEnd != null)
					{
						ResizeEnd(this, EventArgs.Empty);
					}
					// If the ResizeDrawMode is something other than Repaint, 
					// we need to repaint it now that we're done resizing.
					if (ResizeDrawMode != HudResizeDrawMode.Repaint)
					{
						Repaint();
					}
					if (!mMouseOnWindow)
					{
						MouseEvent(MouseLeave, e, mMouseButtons, 0, wX, wY, 0, fwKeys);
					}
				}
				mMoveResizeMode = MoveResizeMode.Idle;

				if (mMouseOnClose || mMouseOnMinRestore || mMouseOnSticky)
				{
					if (mMouseOnClose && CloseBoxRect.Contains(mMouseLocation))
					{
						Visible = false;
					}
					else if (mMouseOnMinRestore && MinRestoreBoxRect.Contains(mMouseLocation))
					{
						Minimized = !Minimized;
					}
					else if (mMouseOnSticky && StickyBoxRect.Contains(mMouseLocation))
					{
						Sticky = !Sticky;
					}
					mMouseOnClose = false;
					mMouseOnMinRestore = false;
					mMouseOnSticky = false;
					DrawControlBoxes(false);
				}
			} // else if (e.Msg == WM_LBUTTONUP)
			#endregion

			#region Generate Mouse Events
			if (mMouseOnWindow)
			{

				switch (e.Msg)
				{
					case WM_MOUSEMOVE:
						MouseEvent(MouseMove, e, mMouseButtons, 0, wX, wY, 0, fwKeys);

						if (mMouseOnClient)
						{
							MouseEvent(ClientMouseMove, e, mClientMouseButtons, 0, cX, cY, 0, fwKeys);
						}
						break;

					case WM_LBUTTONDOWN:
					case WM_RBUTTONDOWN:
					case WM_MBUTTONDOWN:
						mMouseButtons |= button;

						if (dblClick)
						{
							MouseEvent(MouseDoubleClick, e, button, 2, wX, wY, 0, fwKeys);
						}
						else
						{
							MouseEvent(MouseDown, e, button, 1, wX, wY, 0, fwKeys);
						}

						if (mMouseOnClient)
						{
							if (mClientMouseButtons == MouseButtons.None)
							{
								mMouseDownLocation = mMouseLocation;
							}
							mClientMouseButtons |= button;

							if (dblClick)
							{
								MouseEvent(ClientMouseDoubleClick, e, button, 2, cX, cY, 0, fwKeys);
							}
							else
							{
								MouseEvent(ClientMouseDown, e, button, 1, cX, cY, 0, fwKeys);
							}
						}
						break;

					case WM_LBUTTONUP:
					case WM_RBUTTONUP:
					case WM_MBUTTONUP:
						MouseEvent(MouseUp, e, button, 1, wX, wY, 0, fwKeys, false);

						if (mMouseOnClient)
						{
							MouseEvent(ClientMouseUp, e, button, 1, cX, cY, 0, fwKeys, false);
						}
						break;

					case WM_MOUSEWHEEL:
						int zDelta = e.WParam >> 16;
						MouseEvent(MouseWheel, e, button, 0, wX, wY, zDelta, fwKeys);

						if (mMouseOnClient)
						{
							MouseEvent(ClientMouseWheel, e, button, 0, cX, cY, zDelta, fwKeys);
						}
						break;

				} // switch (e.Msg)
			} // if (mMouseOnWindow)

			// Additional WM_MOUSEMOVE messages that don't require the mouse to be on the window
			if (e.Msg == WM_MOUSEMOVE)
			{
				// Alert the client about dragging
				if (ClientMouseDrag != null && mClientMouseButtons != MouseButtons.None)
				{
					int x = mMouseLocation.X - mClientRegion.X;
					int y = mMouseLocation.Y - mClientRegion.Y;
					int dX = mMouseLocation.X - prevLocation.X;
					int dY = mMouseLocation.Y - prevLocation.Y;
					HudMouseDragEventArgs args = new HudMouseDragEventArgs(mClientMouseButtons,
						0, x, y, 0, fwKeys, mMouseDownLocation.X, mMouseDownLocation.Y, dX, dY);
					ClientMouseDrag(this, args);
					if (args.Eat)
						e.Eat = true;
				}

				// Handle mouse entering/leaving the window or client
				// This must be done after moving/resizing

				if (!IsResizing)
				{
					// Mouse enter window
					if (mMouseOnWindow && !prevOnWindow)
					{
						MouseEvent(MouseEnter, e, mMouseButtons, 0, wX, wY, 0, fwKeys);
					}
					// Mouse leave window
					else if (!mMouseOnWindow && prevOnWindow)
					{
						MouseEvent(MouseLeave, e, mMouseButtons, 0, wX, wY, 0, fwKeys);
					}
				}

				// Mouse enter client
				if (mMouseOnClient && !prevOnClient)
				{
					MouseEvent(ClientMouseEnter, e, mClientMouseButtons, 0, cX, cY, 0, fwKeys);
				}
				// Mouse leave client
				else if (!mMouseOnClient && prevOnClient)
				{
					MouseEvent(ClientMouseLeave, e, mClientMouseButtons, 0, cX, cY, 0, fwKeys);
				}
			}
			#endregion

			if (e.Eat && (e.Msg == WM_LBUTTONDOWN || e.Msg == WM_RBUTTONDOWN || e.Msg == WM_MBUTTONDOWN))
			{
				mMouseDownEaten |= button;
			}

			// Update which buttons are on the window and client
			// This must be done after mouse events are generated
			if (e.Msg == WM_LBUTTONUP || e.Msg == WM_RBUTTONUP || e.Msg == WM_MBUTTONUP)
			{
				mMouseButtons &= ~button;
				mClientMouseButtons &= ~button;
				if ((mMouseDownEaten & button) != 0) { e.Eat = true; }
				mMouseDownEaten &= ~button;
			}
		}

		private void CalculateHighlightedBorder()
		{
			// Highlight the border if the mouse is on it
			if (!mMouseOnWindow)
			{
				HighlightedBorder = Border.None;
			}
			else if (TitleBarRect.Contains(mMouseLocation))
			{
				HighlightedBorder = Border.All;
			}
			else if (mMouseOnWindow && !Minimized)
			{
				Border b = Border.None;

				if (mMouseLocation.Y <= mRegion.Top + BorderPaddedWidth)
					b |= Border.North;
				else if (mMouseLocation.Y >= mRegion.Bottom - BorderPaddedWidth)
					b |= Border.South;

				if (mMouseLocation.X >= mRegion.Right - BorderPaddedWidth)
					b |= Border.East;
				else if (mMouseLocation.X <= mRegion.Left + BorderPaddedWidth)
					b |= Border.West;

				HighlightedBorder = b;
			}
			else
			{
				HighlightedBorder = Border.None;
			}
		}

		private void MouseEvent(EventHandler<HudMouseEventArgs> EventToFire, WindowMessageEventArgs e,
				MouseButtons button, int clicks, int x, int y, int delta, short fwKeys)
		{
			MouseEvent(EventToFire, e, button, clicks, x, y, delta, fwKeys, true);
		}

		private void MouseEvent(EventHandler<HudMouseEventArgs> EventToFire, WindowMessageEventArgs e,
				MouseButtons button, int clicks, int x, int y, int delta, short fwKeys, bool allowEat)
		{
			if (EventToFire != null)
			{
				HudMouseEventArgs args = new HudMouseEventArgs(button, clicks, x, y, delta, fwKeys);
				EventToFire(this, args);
				if (allowEat && args.Eat)
					e.Eat = true;
			}
		}

		bool IManagedHud.MouseHoveringObscuresOther
		{
			get { return mMouseOnWindow; }
		}
		#endregion
	}

	#region Helper Classes
	public class HudMouseEventArgs : MouseEventArgs
	{
		/// <summary>
		/// One detent of the mouse wheel will produce a Delta equal to this.
		/// A detent is one notch of the mouse wheel.
		/// </summary>
		public const int WHEEL_DELTA = 120;

		private short mModifierKeys;
		private bool mEat = false;

		public HudMouseEventArgs(MouseButtons button, int clicks, int x, int y, int delta, short fwKeys)
			: base(button, clicks, x, y, delta)
		{
			mModifierKeys = fwKeys;
		}

		/// <summary>
		/// Indicates whether the Control Key was held down while this event 
		/// was generated.
		/// </summary>
		public bool Control
		{
			get { return (mModifierKeys & KeyModifiers.Control) != 0; }
		}

		/// <summary>
		/// Indicates whether the Shift Key was held down while this event 
		/// was generated.
		/// </summary>
		public bool Shift
		{
			get { return (mModifierKeys & KeyModifiers.Shift) != 0; }
		}

		/// <summary>
		/// Indicates whether the Left Mouse Button was held down while this 
		/// event was generated.
		/// </summary>
		public bool LeftButton
		{
			get { return (mModifierKeys & KeyModifiers.LeftButton) != 0; }
		}

		/// <summary>
		/// Indicates whether the Right Mouse Button was held down while this 
		/// event was generated.
		/// </summary>
		public bool RightButton
		{
			get { return (mModifierKeys & KeyModifiers.RightButton) != 0; }
		}

		/// <summary>
		/// Indicates whether the Middle Mouse Button was held down while this 
		/// event was generated.
		/// </summary>
		public bool MiddleButton
		{
			get { return (mModifierKeys & KeyModifiers.MiddleButton) != 0; }
		}

		/// <summary>
		/// Indicates whether the First X Mouse Button was held down while this 
		/// event was generated.
		/// </summary>
		public bool XButton1
		{
			get { return (mModifierKeys & KeyModifiers.XButton1) != 0; }
		}

		/// <summary>
		/// Indicates whether the Second X Mouse Button was held down while this 
		/// event was generated.
		/// </summary>
		public bool XButton2
		{
			get { return (mModifierKeys & KeyModifiers.XButton2) != 0; }
		}

		/// <summary>
		/// Gets the modifier key bitmask for this event.
		/// </summary>
		public short ModifierKeys
		{
			get { return mModifierKeys; }
		}

		/// <summary>
		/// Set this to True if your code handles the event, which will cause 
		/// the event not to be passed to AC or Decal. Leave at its default 
		/// value if your code does not handle the event (explicitly setting 
		/// this to false may have unexpeted results when the event is handled 
		/// by other handlers).
		/// </summary>
		public bool Eat
		{
			get { return mEat; }
			set { mEat = value; }
		}
	}
	public class HudMouseDragEventArgs : HudMouseEventArgs
	{
		int mMouseDownX, mMouseDownY, mDeltaX, mDeltaY;

		/// <summary>
		/// Gets the X coordinate of where the mouse drag started.
		/// </summary>
		public int MouseDownX
		{
			get { return mMouseDownX; }
		}

		/// <summary>
		/// Gets the Y coordinate of where the mouse drag started.
		/// </summary>
		public int MouseDownY
		{
			get { return mMouseDownY; }
		}

		/// <summary>
		/// Gets the coordinates of where the mouse drag started.
		/// </summary>
		public Point MouseDownLocation
		{
			get { return new Point(mMouseDownX, mMouseDownY); }
		}

		/// <summary>
		/// Gets the amount that the X coordinate has changed since the last
		/// MouseDrag event.  Or, if this is the first MouseDrag event for 
		/// this drag, gets the amount that the X coordinate has changed 
		/// since the MouseDown event prior to this event.
		/// </summary>
		public int DeltaX
		{
			get { return mDeltaX; }
		}

		/// <summary>
		/// Gets the amount that the Y coordinate has changed since the last
		/// MouseDrag event.  Or, if this is the first MouseDrag event for 
		/// this drag, gets the amount that the Y coordinate has changed 
		/// since the MouseDown event prior to this event.
		/// </summary>
		public int DeltaY
		{
			get { return mDeltaY; }
		}

		public HudMouseDragEventArgs(MouseButtons button, int clicks, int x, int y, int delta, short fwKeys,
				int mouseDownX, int mouseDownY, int deltaX, int deltaY)
			: base(button, clicks, x, y, delta, fwKeys)
		{
			mMouseDownX = mouseDownX;
			mMouseDownY = mouseDownY;
			mDeltaX = deltaX;
			mDeltaY = deltaY;
		}
	}
	public enum HudResizeDrawMode
	{
		/// <summary>
		/// The client's image will be kept the same as the window is resized,
		/// and repainted when the resize is done. This is the fastest option.
		/// </summary>
		Crop,
		/// <summary>
		/// Same as Crop, but the image will remain centered instead of 
		/// attached to the top-left corner.
		/// </summary>
		CropCenter,
		/// <summary>
		/// The client's image will be scaled as the window is resized, and 
		/// repainted when the resize is done.
		/// </summary>
		Scale,
		/// <summary>
		/// The client will be repainted continuously as the window is resized.
		/// This is the slowest option.
		/// </summary>
		Repaint,
	}
	public class MouseButtonsEx
	{
		private MouseButtons mButtons;
		private short mModifierKeys;

		public MouseButtonsEx(MouseButtons buttons)
		{
			mButtons = buttons;
			mModifierKeys = 0;
		}

		public MouseButtonsEx(MouseButtons buttons, short modifierKeys)
		{
			mButtons = buttons;
			mModifierKeys = modifierKeys;
		}

		public MouseButtons Buttons
		{
			get { return mButtons; }
			set { mButtons = value; }
		}

		public short ModifierKeys
		{
			get { return mModifierKeys; }
			set { mModifierKeys = value; }
		}

		public bool Matches(HudMouseEventArgs e)
		{
			return ((mButtons & e.Button) != 0) && ((e.ModifierKeys & mModifierKeys) == mModifierKeys);
		}

		public void AddModifierKey(short keysToAdd)
		{
			mModifierKeys |= keysToAdd;
		}

		public void RemoveModifierKey(short keysToRemove)
		{
			mModifierKeys &= (short)~keysToRemove;
		}

		public override string ToString()
		{
			return Buttons + "|" + ModifierKeys;
		}

		public static bool TryParse(string str, out MouseButtonsEx value)
		{
			string[] parts = str.Split('|');
			short modifierKeys;
			try
			{
				if (parts.Length == 2 && short.TryParse(parts[1], out modifierKeys))
				{
					MouseButtons buttons = (MouseButtons)Enum.Parse(typeof(MouseButtons), parts[0]);
					value = new MouseButtonsEx(buttons, modifierKeys);
					return true;
				}
			}
			catch (ArgumentException) { /* Failed to parse enum */ }
			value = null;
			return false;
		}
	}

	public class AlphaChangedEventArgs : EventArgs
	{
		private readonly int mAlpha;

		public AlphaChangedEventArgs(int alpha)
		{
			mAlpha = alpha;
		}

		public int Alpha { get { return mAlpha; } }
	}
	#endregion
}
