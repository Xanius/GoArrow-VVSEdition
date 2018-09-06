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
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Xml;

using Decal.Adapter;
using Decal.Adapter.Wrappers;

using ICSharpCode.SharpZipLib.Zip;
using GoArrow.RouteFinding;

namespace GoArrow.Huds
{
	class ArrowHud : IManagedHud
	{
		private const string HudFont = "Arial";
		public const int MinTextSize = 8, MaxTextSize = 28, DefaultTextSize = 14;
		private const int CanvasWidth = 400;
		private const int CanvasHeight = 175;
		private static readonly Color Clear = Color.FromArgb(0);

		private Hud mHud;
		private HudManager mManager;

		// Painting
		private Bitmap[] mArrowImages = new Bitmap[0];
		private Rectangle mArrowRect = new Rectangle();
		private ZipFile mArrowZipFile;
		private SortedList<string, SortedList<int, int>> mArrowZipIndex;
		//private int mImageFrame = -1;
		private bool mNeedsRepaint = true;
		private bool mNeedToCalculateImageRotations = false;
		private Color mTextOutlineSolid = Color.Black;
		private Color mTextOutlineLight = Color.FromArgb(128, Color.Black);

		// Mouse handling
		private Point mMousePos;
		private bool mMouseHovering = false;
		private bool mMouseMovingHud = false;
		private Point mMouseHudOffset;
		private Rectangle mRoutePrevRect;
		private Rectangle mRouteNextRect;
		private Rectangle mCloseBoxRect;

		// Private fields with public accessors
		private bool mDisposed = false;
		private Point mLocation = new Point(100, 100);
		private bool mVisible = false;
		private bool mPositionLocked = true;
		private bool mDisplayIndoors = false;
		private bool mShowDestinationCoords = true;
		private bool mShowDistance = true;
		private bool mInDungeon = false;
		private Color mTextColor = Color.White;
		private int mTextSize = DefaultTextSize;
		private bool mTextBold = true;
		private int mAlpha = 255;
		private double mPlayerHeadingRadians;
		private string mArrowName = "";
		private bool mShowCloseButton = true;

		private Coordinates mPlayerCoords = new Coordinates();
		private Coordinates mDestinationCoords = new Coordinates();
		private Location mDestinationLocation = null;
		private Route mRoute = new Route(0.0);
		private int mRouteIndex = 0;
		private GameObject mDestinationObject = null;
		private Coordinates mLastDisplayedDestinationCoords = Coordinates.NO_COORDINATES;
		private bool mLastDisplayedObjectValid = false;

		public event EventHandler HudMoving;
		public event EventHandler HudMoveComplete;
		public event EventHandler<DestinationChangedEventArgs> DestinationChanged;
		public event EventHandler VisibleChanged;
		public event EventHandler HudEnabledChanged;

		private BackgroundWorker asyncLoadImageWorker = new BackgroundWorker();
		public event RunWorkerCompletedEventHandler AsyncLoadComplete
		{
			add { asyncLoadImageWorker.RunWorkerCompleted += value; }
			remove { asyncLoadImageWorker.RunWorkerCompleted -= value; }
		}

		Bitmap mFontMeasureBitmap = new Bitmap(1, 1);
		Graphics mFontMeasure;
		Font mFont;

		public ArrowHud(HudManager manager)
		{
			mManager = manager;
			asyncLoadImageWorker.DoWork += new DoWorkEventHandler(asyncLoadImageWorker_DoWork);
			mManager.RegisterHud(this, true);

			mArrowZipFile = new ZipFile(Util.FullPath(@"Huds\Arrows.zip"));
			mArrowZipIndex = new SortedList<string, SortedList<int, int>>(StringComparer.OrdinalIgnoreCase);
			Regex zipNameParser = new Regex(@"(.*)/arrow_f(\d+).png");
			foreach (ZipEntry entry in mArrowZipFile)
			{
				Match parsedName = zipNameParser.Match(entry.Name);
				if (parsedName.Success)
				{
					string arrowName = parsedName.Groups[1].Value;
					if (!mArrowZipIndex.ContainsKey(arrowName))
					{
						mArrowZipIndex[arrowName] = new SortedList<int, int>();
					}
					int index = int.Parse(parsedName.Groups[2].Value);
					mArrowZipIndex[arrowName][index] = entry.ZipFileIndex;
				}
			}
			List<string> invalidArrows = new List<string>();
			foreach (KeyValuePair<string, SortedList<int, int>> kvp in mArrowZipIndex)
			{
				SortedList<int, int> images = kvp.Value;

				// Since this list is sorted and each key is unique, this checks that 
				// there exists one image for every index in the range [1,n]
				if (!(images.IndexOfKey(1) == 0 && images.IndexOfKey(images.Count) == images.Count - 1))
				{
					invalidArrows.Add(kvp.Key);
				}
			}
			foreach (string arrowName in invalidArrows)
			{
				mArrowZipIndex.Remove(arrowName);
			}

			mFontMeasure = Graphics.FromImage(mFontMeasureBitmap);
			UpdateFont();
		}

		private void UpdateFont()
		{
			mFont = new Font(HudFont, mTextSize, GraphicsUnit.Pixel);
		}

		public void Dispose()
		{
			if (Disposed)
				return;

			mDestinationObject = null;

			asyncLoadImageWorker.DoWork -= asyncLoadImageWorker_DoWork;
			asyncLoadImageWorker.Dispose();
			asyncLoadImageWorker = null;

			if (mHud != null)
			{
				mHud.Enabled = false;
				mHud.Dispose();
				mHud = null;
			}

			Manager.UnregisterHud(this);

			mArrowZipFile.Close();

			mDisposed = true;
		}

		public bool Disposed
		{
			get { return mDisposed; }
		}

		public HudManager Manager
		{
			get { return mManager; }
		}

		public PluginHost Host
		{
			get { return Manager.Host; }
		}

		public CoreManager Core
		{
			get { return Manager.Core; }
		}

		#region HUD Position and Visibility
		public bool Visible
		{
			get { return mVisible; }
			set
			{
				if (mVisible != value)
				{
					bool visible = IsHudVisible();
					mVisible = value;
					if (VisibleChanged != null && visible != IsHudVisible())
						VisibleChanged(this, EventArgs.Empty);
					if (HudEnabledChanged != null)
						HudEnabledChanged(this, EventArgs.Empty);
					mNeedsRepaint = true;
				}
			}
		}

		public bool AlwaysOnTop
		{
			get { return Manager.IsAlwaysOnTop(this); }
			set { Manager.SetAlwaysOnTop(this, value); }
		}

		public bool IsHudVisible()
		{
			return Visible && (DisplayIndoors || !InDungeon) && mArrowImages.Length > 0;
		}

		public Point Location
		{
			get { return mLocation; }
			set
			{
				mLocation = value;
				if (mHud != null)
					mHud.Region = new Rectangle(mLocation, mHud.Region.Size);
			}
		}

		public void ResetPosition()
		{
			Location = new Point(100, 100);
			if (HudMoveComplete != null)
				HudMoveComplete(this, EventArgs.Empty);
		}

		public Rectangle Region
		{
			get
			{
				Point pos = mArrowRect.Location;
				pos.X += Location.X;
				pos.Y += Location.Y;
				return new Rectangle(pos, mArrowRect.Size);
			}
		}

		public bool DisplayIndoors
		{
			get { return mDisplayIndoors; }
			set
			{
				if (mDisplayIndoors != value)
				{
					bool visible = IsHudVisible();
					mDisplayIndoors = value;
					if (VisibleChanged != null && visible != IsHudVisible())
						VisibleChanged(this, EventArgs.Empty);
					PaintInternal(); // Repaint right away
				}
			}
		}
		#endregion

		#region Player and Destination Positions
		public double PlayerHeadingRadians
		{
			get { return mPlayerHeadingRadians; }
			set
			{
				if (mPlayerHeadingRadians != value)
				{
					mPlayerHeadingRadians = value;
					mNeedsRepaint = true;
				}
			}
		}

		public Coordinates PlayerCoords
		{
			get { return mPlayerCoords; }
			set
			{
				if (Math.Abs(mPlayerCoords.NS - value.NS) > 0.00075
						|| Math.Abs(mPlayerCoords.EW - value.EW) > 0.00075)
				{
					mPlayerCoords = value;
					mNeedsRepaint = true;
				}
			}
		}

		public bool InDungeon
		{
			get { return mInDungeon; }
			set
			{
				if (mInDungeon != value)
				{
					bool visible = IsHudVisible();
					mInDungeon = value;
					if (visible != IsHudVisible())
					{
						if (VisibleChanged != null)
							VisibleChanged(this, EventArgs.Empty);
						mNeedsRepaint = true;
					}
				}
			}
		}

		public Coordinates DestinationCoords
		{
			get
			{
				if (HasDestinationLocation)
				{
					return DestinationLocation.Coords;
				}
				else if (HasDestinationObject)
				{
					return DestinationObject.Coords;
				}
				return mDestinationCoords;
			}
			set
			{
				mDestinationCoords = value;
				mDestinationLocation = null;
				mRoute.Clear();
				mRouteIndex = 0;
				mDestinationObject = null;
				mNeedsRepaint = true;
				if (DestinationChanged != null)
					DestinationChanged(this, DestinationChangedEventArgs.Coords);
			}
		}

		public Location DestinationLocation
		{
			get
			{
				if (Route.Count > 0)
					return Route[RouteIndex];
				return mDestinationLocation;
			}
			set
			{
				mDestinationLocation = value;
				mRoute.Clear();
				mRouteIndex = 0;
				mDestinationObject = null;
				mNeedsRepaint = true;
				if (DestinationChanged != null)
					DestinationChanged(this, DestinationChangedEventArgs.Location);
			}
		}

		public bool HasDestinationLocation
		{
			get { return mDestinationLocation != null || HasRoute; }
		}

		public Route Route
		{
			get { return mRoute; }
			set
			{
				if (value == null)
					mRoute = new Route(0.0);
				else
					mRoute = value;
				mRouteIndex = 0;
				mDestinationObject = null;
				mNeedsRepaint = true;
				if (DestinationChanged != null)
					DestinationChanged(this, DestinationChangedEventArgs.Route);
			}
		}

		public bool HasRoute
		{
			get { return mRoute.Count > 0; }
		}

		public int RouteIndex
		{
			get
			{
				if (mRouteIndex >= mRoute.Count)
					return mRoute.Count - 1;
				if (mRouteIndex < 0)
					return 0;
				return mRouteIndex;
			}
			set
			{
				if (value < 0 || mRoute.Count == 0)
					mRouteIndex = 0;
				else if (value >= mRoute.Count)
					mRouteIndex = mRoute.Count - 1;
				else
					mRouteIndex = value;
				mNeedsRepaint = true;
				if (DestinationChanged != null)
					DestinationChanged(this, DestinationChangedEventArgs.RouteIndex);
			}
		}

		public GameObject DestinationObject
		{
			get { return mDestinationObject; }
			set
			{
				mDestinationObject = value;
				mDestinationLocation = null;
				mRoute.Clear();
				mRouteIndex = 0;
				mNeedsRepaint = true;
				if (DestinationChanged != null)
					DestinationChanged(this, DestinationChangedEventArgs.Object);
			}
		}

		public bool HasDestinationObject
		{
			get { return mDestinationObject != null; }
		}

		public void SaveDestinationXml(XmlElement arrowNode)
		{
			XmlDocument doc = arrowNode.OwnerDocument;
			arrowNode.SetAttribute("coords", DestinationCoords.ToString());
			if (HasRoute)
			{
				XmlElement routeNode = (XmlElement)arrowNode.AppendChild(Route.ToXml(doc));
				routeNode.SetAttribute("routeIndex", RouteIndex.ToString());
			}
			else if (HasDestinationObject)
			{
				XmlElement objectNode = (XmlElement)arrowNode.AppendChild(doc.CreateElement("object"));
				objectNode.SetAttribute("id", DestinationObject.Id.ToString("X"));
				objectNode.SetAttribute("name", DestinationObject.Name);
				objectNode.SetAttribute("icon", DestinationObject.Icon.ToString("X8"));
				objectNode.SetAttribute("coords", DestinationObject.Coords.ToString());
			}
			else if (HasDestinationLocation)
			{
				XmlElement locationNode = (XmlElement)arrowNode.AppendChild(doc.CreateElement("location"));
				locationNode.SetAttribute("id", DestinationLocation.Id.ToString());
				locationNode.SetAttribute("name", DestinationLocation.Name);
			}
		}

		public void LoadDestinationXml(XmlElement arrowNode, LocationDatabase locDb)
		{
			XmlElement ele;
			Coordinates coords;
			if ((ele = (XmlElement)arrowNode.SelectSingleNode("route")) != null)
			{
				try
				{
					int index;
					int.TryParse(ele.GetAttribute("routeIndex"), out index);
					Route route = Route.FromXml(ele, locDb);
					if (route.Count > 0)
					{
						this.Route = route;
						this.RouteIndex = index;
						return;
					}
				}
				catch (Exception ex) { Util.HandleException(ex); }
			}

			if ((ele = (XmlElement)arrowNode.SelectSingleNode("object")) != null)
			{
				const System.Globalization.NumberStyles hex = System.Globalization.NumberStyles.HexNumber;
				int id, icon;
				if (int.TryParse(ele.GetAttribute("id"), hex, null, out id)
					&& int.TryParse(ele.GetAttribute("icon"), hex, null, out icon)
					&& Coordinates.TryParse(ele.GetAttribute("coords"), out coords))
				{
					this.DestinationObject = new GameObject(id, ele.GetAttribute("name"), icon, Host.Actions, coords);
					return;
				}
			}

			if ((ele = (XmlElement)arrowNode.SelectSingleNode("location")) != null)
			{
				int locId;
				Location loc;
				if (int.TryParse(ele.GetAttribute("id"), out locId) && locDb.TryGet(locId, out loc))
				{
					this.DestinationLocation = loc;
					return;
				}
			}

			// No other methods succeeded
			if (Coordinates.TryParse(arrowNode.GetAttribute("coords"), out coords))
			{
				this.DestinationCoords = coords;
			}
		}
		#endregion

		#region HUD Style
		public Color TextColor
		{
			get { return mTextColor; }
			set
			{
				if (mTextColor != value)
				{
					mTextColor = value;
					double v = 0.299 * value.R + 0.587 * value.G + 0.114 * value.B;
					if (v < 0.5)
					{
						mTextOutlineSolid = Color.FromArgb(230, Color.White);
						mTextOutlineLight = Color.FromArgb(128, Color.White);
					}
					else
					{
						mTextOutlineSolid = Color.Black;
						mTextOutlineLight = Color.FromArgb(128, Color.Black);
					}
					mNeedsRepaint = true;
				}
			}
		}

		public int TextSize
		{
			get { return mTextSize; }
			set
			{
				if (mTextSize != value && value >= MinTextSize && value <= MaxTextSize)
				{
					mTextSize = value;
					UpdateFont();
					mNeedsRepaint = true;
				}
			}
		}

		public bool TextBold
		{
			get { return mTextBold; }
			set
			{
				if (mTextBold != value)
				{
					mTextBold = value;
					UpdateFont();
					mNeedsRepaint = true;
				}
			}
		}

		public int Alpha
		{
			get { return mAlpha; }
			set
			{
				if (value >= 0 && value <= 255)
				{
					mAlpha = value;
					if (mHud != null)
						mHud.Alpha = mAlpha;
				}
			}
		}

		public bool ShowDestinationOver
		{
			get { return mShowDestinationCoords; }
			set
			{
				if (mShowDestinationCoords != value)
				{
					mShowDestinationCoords = value;
					mNeedsRepaint = true;
				}
			}
		}

		public bool ShowDistanceUnder
		{
			get { return mShowDistance; }
			set
			{
				if (mShowDistance != value)
				{
					mShowDistance = value;
					mNeedsRepaint = true;
				}
			}
		}

		public bool ShowCloseButton
		{
			get { return mShowCloseButton; }
			set
			{
				if (mShowCloseButton != value)
				{
					mShowCloseButton = value;
					mNeedsRepaint = true;
				}
			}
		}

		public bool LoadArrowImage(string arrowName)
		{
			string dummy;
			return LoadArrowImage(arrowName, out dummy);
		}

		public bool LoadArrowImage(string arrowName, out string errMessage)
		{
			errMessage = "";

			if (!mArrowZipIndex.ContainsKey(arrowName))
			{
				errMessage = "Arrow '" + arrowName + "' does not exist.";
				return false;
			}

			bool visible = IsHudVisible();
			try
			{
				IList<int> arrowIndex = mArrowZipIndex[arrowName].Values;
				Bitmap[] images = new Bitmap[arrowIndex.Count];

				for (int i = 0; i < arrowIndex.Count; i++)
				{
					images[i] = (Bitmap)Bitmap.FromStream(mArrowZipFile.GetInputStream(arrowIndex[i]));
				}

				if (mNeedToCalculateImageRotations = (images.Length == 1))
				{
					Bitmap tmp = images[0];
					images = new Bitmap[2];
					images[0] = tmp;
					images[1] = new Bitmap(tmp.Width, tmp.Height, PixelFormat.Format32bppArgb);
				}

				mArrowRect = new Rectangle(new Point(0, 0), images[0].Size);

				mArrowImages = images;
				mArrowName = arrowName;
			}
			catch (Exception ex)
			{
				errMessage = ex.Message;
				return false;
			}

			mNeedsRepaint = true;
			if (VisibleChanged != null && visible != IsHudVisible())
				VisibleChanged(this, EventArgs.Empty);
			return true;
		}

		public void LoadArrowImageAsync(string arrowName)
		{
			asyncLoadImageWorker.RunWorkerAsync(arrowName);
		}

		private void asyncLoadImageWorker_DoWork(object sender, DoWorkEventArgs e)
		{
			string arrowName = (string)e.Argument;
			string errMsg;
			if (!LoadArrowImage(arrowName, out errMsg))
			{
				e.Result = errMsg;
				e.Cancel = true;
			}
			else
			{
				e.Result = null;
			}
		}

		public string ArrowName
		{
			get { return mArrowName; }
		}

		public IList<string> AvailableArrowNames
		{
			get { return mArrowZipIndex.Keys; }
		}
		#endregion

		#region Mouse Handling
		public bool PositionLocked
		{
			get { return mPositionLocked; }
			set
			{
				mPositionLocked = value;
				mNeedsRepaint = true;
			}
		}

		private bool IsMouseHovering()
		{
			return (!PositionLocked || Util.IsControlDown()) && IsHudVisible() && Region.Contains(mMousePos);
		}

		void IManagedHud.WindowMessage(WindowMessageEventArgs e)
		{
			const short WM_MOUSEMOVE = 0x200;
			const short WM_LBUTTONDOWN = 0x201;
			const short WM_LBUTTONUP = 0x202;
			// A hack to reduce the number of checks for every windows message
			const short HANDLED_MESSAGES = WM_MOUSEMOVE | WM_LBUTTONDOWN | WM_LBUTTONUP;

			try
			{
				if ((e.Msg & HANDLED_MESSAGES) != 0 && IsHudVisible())
				{

					mMousePos = new Point(e.LParam);

					switch (e.Msg)
					{
						case WM_MOUSEMOVE:
							if (mMouseMovingHud)
							{
								Location = new Point(mMousePos.X - mMouseHudOffset.X, mMousePos.Y - mMouseHudOffset.Y);
								if (HudMoving != null)
									HudMoving(this, EventArgs.Empty);
							}
							break;

						case WM_LBUTTONDOWN:
							if (HasRoute)
							{
								if (mRouteNextRect.Contains(mMousePos))
								{
									RouteIndex++;
									e.Eat = true;
								}
								else if (mRoutePrevRect.Contains(mMousePos))
								{
									RouteIndex--;
									e.Eat = true;
								}
							}

							if (IsMouseHovering())
							{
								Rectangle absCloseBoxRect = new Rectangle(Location.X + mCloseBoxRect.X,
									Location.Y + mCloseBoxRect.Y, mCloseBoxRect.Width, mCloseBoxRect.Height);

								if (absCloseBoxRect.Contains(mMousePos))
								{
									Visible = false;
								}
								else
								{
									mMouseMovingHud = true;
									mMouseHudOffset.X = mMousePos.X - mLocation.X;
									mMouseHudOffset.Y = mMousePos.Y - mLocation.Y;
								}
								e.Eat = true;
							}
							break;

						case WM_LBUTTONUP:
							if (mMouseMovingHud)
							{
								mMouseMovingHud = false;
								if (HudMoveComplete != null)
									HudMoveComplete(this, EventArgs.Empty);
								e.Eat = true;
							}
							break;
					}
				}
			}
			catch (Exception ex) { Util.HandleException(ex); }
		}
		#endregion

		#region Painting
		void IManagedHud.RecreateHud()
		{
			if (mHud != null && !mHud.Lost)
			{
				mHud.Enabled = false;
				Manager.Host.Render.RemoveHud(mHud);
				mHud.Dispose();
				mHud = null;
			}
			PaintInternal();
		}

		void IManagedHud.RepaintHeartbeat()
		{
#if !USING_D3D_CONTAINER
			//InDungeon = (Manager.Host.Actions.Landcell & 0x0000FF00) != 0;
			InDungeon = Util.IsDungeon(Manager.Host.Actions.Landcell);

			PlayerCoords = new Coordinates(Manager.Host.Actions.Landcell,
				Manager.Host.Actions.LocationY, Manager.Host.Actions.LocationX);

			PlayerHeadingRadians = Manager.Host.Actions.HeadingRadians;

			if (HasDestinationObject &&
					(mLastDisplayedDestinationCoords != DestinationCoords ||
					mLastDisplayedObjectValid != DestinationObject.IsValid))
			{
				mNeedsRepaint = true;
				mLastDisplayedObjectValid = DestinationObject.IsValid;
			}
#endif
			if (mMouseHovering != IsMouseHovering())
			{
				mMouseHovering = !mMouseHovering;
				mNeedsRepaint = true;
			}

			if (mNeedsRepaint)
				PaintInternal();
		}

		private void PaintInternal()
		{
			try
			{
				mNeedsRepaint = false;

				if (mHud == null || mHud.Lost)
				{
					if (mHud != null)
						mHud.Dispose();
					mHud = Manager.Host.Render.CreateHud(
						new Rectangle(Location.X, Location.Y, CanvasWidth, CanvasHeight));
					mHud.Alpha = Alpha;
					mHud.Enabled = Visible;
				}

				if (!IsHudVisible())
				{
					mHud.Enabled = false;
					return;
				}

				double arrowAngleRadians = PlayerCoords.AngleTo(DestinationCoords) - PlayerHeadingRadians;
				while (arrowAngleRadians < 0)
					arrowAngleRadians += 2 * Math.PI;

				int imageFrame;
				if (mNeedToCalculateImageRotations)
				{
					imageFrame = 1;
					Graphics g = Graphics.FromImage(mArrowImages[1]);
					g.Clear(Clear);
					g.TranslateTransform(mArrowImages[0].Width / 2, mArrowImages[0].Height / 2);
					g.RotateTransform((float)(arrowAngleRadians * 180.0 / Math.PI));
					g.TranslateTransform(-mArrowImages[0].Width / 2, -mArrowImages[0].Height / 2);
					g.DrawImage(mArrowImages[0], 0, 0, mArrowImages[0].Width, mArrowImages[0].Height);
				}
				else
				{
					imageFrame = ((int)(arrowAngleRadians / (2 * Math.PI) * mArrowImages.Length))
						% mArrowImages.Length;
				}

				mArrowRect.Y = 1;
				mArrowRect.X = (CanvasWidth - mArrowRect.Width) / 2;
				Rectangle distRect = new Rectangle(1, mArrowRect.Height, CanvasWidth - 2, TextSize + 2);
				if (ShowDestinationOver)
				{
					mArrowRect.Y += 2 * TextSize;
					distRect.Y += 2 * TextSize;
				}

				//mImageFrame = newImageFrame;
				mHud.Clear();
				// Draw move border
				if (mMouseHovering)
				{
					Rectangle r = new Rectangle(mArrowRect.Location, mArrowRect.Size);
					mHud.Fill(r, Color.Gold);
					r = new Rectangle(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
					mHud.Clear(r);
				}
				mHud.BeginRender();
				// Draw close button
				if (mMouseHovering && ShowCloseButton)
				{
					Bitmap closeBox = Icons.Window.CloseBox;
					mCloseBoxRect = new Rectangle(mArrowRect.Right - closeBox.Width - 2,
						mArrowRect.Top + 2, closeBox.Width, closeBox.Height);
					mHud.DrawImage(Icons.Window.CloseBox, mCloseBoxRect);
				}
				else
				{
					mCloseBoxRect = Rectangle.Empty;
				}
				mRoutePrevRect = mRouteNextRect = new Rectangle(-1, -1, 0, 0);

				if (ShowDestinationOver || ShowDistanceUnder)
				{
					mHud.BeginText(HudFont, TextSize, TextBold ? FontWeight.Bold : FontWeight.Normal, false);
					if (ShowDestinationOver)
					{
						if (HasDestinationLocation)
						{
							string locationName = DestinationLocation.Name;
							if (HasRoute)
							{
								// Left and Right arrows on each end
								if (RouteIndex > 0) { locationName = "\u25C4  " + locationName; }
								if (RouteIndex < Route.Count - 1) { locationName += "  \u25BA"; }
								SizeF sz = mFontMeasure.MeasureString(locationName, mFont);
								mRoutePrevRect = mRouteNextRect = new Rectangle(-1, -1, 0, 0);
								float width = 0.2f * sz.Width;
								if (RouteIndex > 0)
								{
									mRoutePrevRect = Rectangle.Round(new RectangleF(
										Location.X + (CanvasWidth - 0.9f * sz.Width) / 2.0f,
										Location.Y - 1, width, TextSize + 2));
								}
								if (RouteIndex < Route.Count - 1)
								{
									mRouteNextRect = Rectangle.Round(new RectangleF(
										Location.X + (CanvasWidth + 0.9f * sz.Width) / 2.0f - width,
										Location.Y - 1, width, TextSize + 2));
								}
							}

							WriteText(locationName, WriteTextFormats.Center,
								new Rectangle(1, 1, CanvasWidth - 2, TextSize + 2));
						}
						else if (HasDestinationObject)
						{
							WriteText(DestinationObject.Name + (DestinationObject.IsValid ? "" : " (out of range)"), WriteTextFormats.Center,
								new Rectangle(1, 1, CanvasWidth - 2, TextSize + 2));
						}
						WriteText(DestinationCoords.ToString(), WriteTextFormats.Center,
							new Rectangle(1, TextSize, CanvasWidth - 2, TextSize + 2));
					}
					if (ShowDistanceUnder)
					{
						double dist = PlayerCoords.DistanceTo(DestinationCoords);
						WriteText(dist.ToString("0.00"), WriteTextFormats.Center, distRect);
					}
					mHud.EndText();
				}
				mHud.DrawImage(mArrowImages[imageFrame], mArrowRect);
				mHud.EndRender();
				mHud.Enabled = true;

				mLastDisplayedDestinationCoords = DestinationCoords;
			}
			catch (Exception ex) { Util.HandleException(ex); }
		}

		private void WriteText(string text, WriteTextFormats format, Rectangle r)
		{
			mHud.WriteText(text, mTextOutlineLight, format, new Rectangle(r.X + 1, r.Y + 1, r.Width, r.Height));
			mHud.WriteText(text, mTextOutlineLight, format, new Rectangle(r.X - 1, r.Y + 1, r.Width, r.Height));
			mHud.WriteText(text, mTextOutlineLight, format, new Rectangle(r.X + 1, r.Y - 1, r.Width, r.Height));
			mHud.WriteText(text, mTextOutlineLight, format, new Rectangle(r.X - 1, r.Y - 1, r.Width, r.Height));
			mHud.WriteText(text, mTextOutlineSolid, format, new Rectangle(r.X + 1, r.Y, r.Width, r.Height));
			mHud.WriteText(text, mTextOutlineSolid, format, new Rectangle(r.X - 1, r.Y, r.Width, r.Height));
			mHud.WriteText(text, mTextOutlineSolid, format, new Rectangle(r.X, r.Y + 1, r.Width, r.Height));
			mHud.WriteText(text, mTextOutlineSolid, format, new Rectangle(r.X, r.Y - 1, r.Width, r.Height));
			mHud.WriteText(text, TextColor, format, r);
		}
		#endregion

		bool IManagedHud.MouseHoveringObscuresOther
		{
			get { return false; }
		}
	}

	#region Game Physics Object
	[StructLayout(LayoutKind.Sequential)]
	struct PhysicsFrame
	{
		public int VTable;
		public int Landblock;

		[StructLayout(LayoutKind.Sequential)]
		public struct QuaternionT { public float W, X, Y, Z; }
		public QuaternionT Quaternion;

		[StructLayout(LayoutKind.Sequential)]
		public struct HeadingT { public float X, Y, Z; }
		public HeadingT Heading;

		[StructLayout(LayoutKind.Sequential)]
		public struct MatrixT { public float m0, m1, m2, m3, m4, m5; }
		public MatrixT Matrix;

		[StructLayout(LayoutKind.Sequential)]
		public struct PositionT { public float X, Y, Z; }
		public PositionT Position;
	}

	class GameObject
	{
		private int mId;
		private string mName;
		private int mIcon;
		private HooksWrapper mHooks;
		private Coordinates mLastKnownCoords;

		public GameObject(int id, string name, int icon, HooksWrapper hooks)
			: this(id, name, icon, hooks, Coordinates.NO_COORDINATES) { }

		public GameObject(int id, string name, int icon, HooksWrapper hooks, Coordinates lastKnownCoords)
		{
			mId = id;
			mName = name;
			mIcon = icon;
			if (mIcon < 0x6000000)
			{
				mIcon += 0x6000000;
			}
			mHooks = hooks;
			mLastKnownCoords = lastKnownCoords;
		}

		public int Id
		{
			get { return mId; }
		}

		public string Name
		{
			get { return mName; }
		}

		public int Icon
		{
			get { return mIcon; }
		}

		public bool IsValid
		{
			get { return mHooks.IsValidObject(Id); }
		}

		unsafe public Coordinates Coords
		{
			get
			{
				if (IsValid)
				{
					IntPtr ptr = mHooks.PhysicsObject(Id);
					PhysicsFrame p = *((PhysicsFrame*)((byte*)ptr.ToPointer() + 0x48));
					mLastKnownCoords = new Coordinates(p.Landblock, p.Position.Y, p.Position.X);
				}
				return mLastKnownCoords;
			}
		}
	}

	#endregion

	enum DestinationChangeType { Coords, Location, Route, RouteIndex, Object }
	class DestinationChangedEventArgs : EventArgs
	{
		public static readonly DestinationChangedEventArgs Coords = new DestinationChangedEventArgs(DestinationChangeType.Coords);
		public static readonly DestinationChangedEventArgs Location = new DestinationChangedEventArgs(DestinationChangeType.Location);
		public static readonly DestinationChangedEventArgs Route = new DestinationChangedEventArgs(DestinationChangeType.Route);
		public static readonly DestinationChangedEventArgs RouteIndex = new DestinationChangedEventArgs(DestinationChangeType.RouteIndex);
		public static readonly DestinationChangedEventArgs Object = new DestinationChangedEventArgs(DestinationChangeType.Object);

		private readonly DestinationChangeType mChangeType;
		private DestinationChangedEventArgs(DestinationChangeType type)
		{
			mChangeType = type;
		}

		public DestinationChangeType ChangeType
		{
			get { return mChangeType; }
		}
	}
}
