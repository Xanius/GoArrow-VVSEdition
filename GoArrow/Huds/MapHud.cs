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
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using MouseButtons = System.Windows.Forms.MouseButtons;

using Decal.Adapter;
using Decal.Adapter.Wrappers;

using ICSharpCode.SharpZipLib.Zip;

using GoArrow.RouteFinding;

namespace GoArrow.Huds
{
	class MapHud : WindowHud
	{
		#region Constants
		const int TileCacheSize = 25;

		const double ZoomBase = 1.4142135623730950488016887242097; // sqrt(2)
		const double MaxZoomFactor = 10;

		private static readonly Rectangle DefaultRegion = new Rectangle(75, 100, 480, 360);

		/// <summary>12x14 arrow centered at (0,0)</summary>
		private static readonly Point[] PlayerArrowPolygon = {
			new Point( 0, -7),
			new Point( 6,  7),
			new Point( 0,  4),
			new Point(-6,  7),
		};

		private static readonly float[] PossibleCoordDeltas = 
			{ 0.01f, 0.02f, 0.05f, 0.1f, 0.2f, 0.5f, 1.0f, 2.0f, 5.0f, 10.0f, 20.0f, 50.0f };

		private static readonly Font CoordTextFont = new Font(FontFamily.GenericSansSerif, 8);
		private static readonly Pen CoordGridline = new Pen(Color.FromArgb(0x60, Color.Black));
		private static readonly Brush CoordGutterFill = new SolidBrush(Color.FromArgb(0x60, Color.Black));
		private const int CoordGutterSize = 15;

		private static readonly Pen RouteLine = new Pen(Color.FromArgb(unchecked((int)0xFFCCFFCC)), 1.75f);
		private static readonly Pen RouteLinePortal = new Pen(Color.FromArgb(unchecked((int)0xFFFF66FF)), 1.25f);
		private static readonly Pen RouteLineBackground = new Pen(Color.FromArgb(unchecked((int)0xC0000000)), 4.0f);

		private static readonly Brush TextGlow1 = new SolidBrush(Color.FromArgb(0xC0, Color.Black));
		private static readonly Brush TextGlow2 = new SolidBrush(Color.FromArgb(0x60, Color.Black));

		private static readonly StringFormat VerticalText = new StringFormat(StringFormatFlags.DirectionVertical);
		#endregion

		#region Private Fields
		private PluginCore mPluginCore;

		private int mMapSize;
		private float mPixPerClick;
		private float mCoordTickDelta;
		private int mTileSize, mTilePaddedSize;
		private int mMaxTile;
		private Dictionary<Point, Bitmap> mTileCache = new Dictionary<Point, Bitmap>(TileCacheSize);
		/// <summary>List for determining least recently used tile</summary>
		private LinkedList<Point> mTileCacheAccesses = new LinkedList<Point>();
		private Bitmap mDerethMapLowRes;
		private ZipFile mZipFile;

		private Hud mSpriteHud;
		private Bitmap mSpriteBuffer = new Bitmap(32, 32);

		private Brush mPlayerArrowFill = new SolidBrush(Color.FromArgb(unchecked((int)0xFF88E223))); // Green
		private Pen mPlayerArrowOutline = Pens.Black;

		private bool mNeedsSpriteRepaint = true;
		private bool mNeedsPlayerRecenter = true;
		private Coordinates mCenterOnCoords = Coordinates.NO_COORDINATES;

		private ToolbarHud mContextMenu = null;

		private bool mIsDragging = false;

		private PointF mOffset;
		private PointF mMouseDownOffset;
		private double mZoomFactor = 0.0;
		private float mZoomMultiplier = 1.0f;
		private float mActualZoom;
		private float mPreviousZoomMultiplier = 0.0f;
		private Size mPreviousClientSize;

		// Display
		private Route mRoute;
		private LocationDatabase mLocDb;
		private bool mShowRoute = true;
		private bool mShowCoords = true;
		private bool mShowLocations = true;
		private bool mShowLocationsAllZooms = false;
		private bool mShowLabels = true;

		private Rectangle mClickedHotspotRect = Rectangle.Empty;
		private LinkedList<Hotspot> mHotspots = new LinkedList<Hotspot>();
		private Hotspot mActiveHotspot = Hotspot.None;
		private struct Hotspot
		{
			public static readonly Hotspot None = new Hotspot();

			public readonly Rectangle rect;
			public readonly Location loc;

			public Hotspot(Rectangle r, Location l)
			{
				rect = r;
				loc = l;
			}

			public override string ToString() { return loc.ToString(); }
			public override bool Equals(object obj)
			{
				if (obj is Hotspot)
				{
					Hotspot h = (Hotspot)obj;
					return (this.rect == h.rect && this.loc == h.loc);
				}
				return false;
			}
			public override int GetHashCode() { return rect.GetHashCode() ^ (loc == null ? int.MaxValue : loc.GetHashCode()); }
			public static bool operator ==(Hotspot a, Hotspot b) { return a.rect == b.rect && a.loc == b.loc; }
			public static bool operator !=(Hotspot a, Hotspot b) { return !(a == b); }
		}

		private Coordinates mPlayerCoords, mLastPaintedPlayerCoords;
		private double mPlayerHeadingRadians;
		private bool mPlayerInDungeon;

		private bool mCenterOnPlayer = true;
		private bool mDrawCoords = true;
		private MouseButtons mDragButton = MouseButtons.Left | MouseButtons.Middle;
		private MouseButtons mSelectLocationButton = MouseButtons.Left;
		private MouseButtons mContextMenuButton = MouseButtons.Right;
		private MouseButtons mDetailsButton = MouseButtons.None;
		#endregion

		#region Initialization/Termination
		static MapHud()
		{
			RouteLine.StartCap = LineCap.Round;
			RouteLine.EndCap = LineCap.Custom;
			RouteLine.CustomEndCap = new AdjustableArrowCap(4.0f, 5.0f);

			RouteLinePortal.DashStyle = DashStyle.Dash;
			RouteLinePortal.DashCap = DashCap.Flat;
			RouteLinePortal.StartCap = LineCap.Round;
			RouteLinePortal.EndCap = LineCap.Custom;
			RouteLinePortal.CustomEndCap = new AdjustableArrowCap(4.0f, 5.0f);
		}

		public MapHud(HudManager manager, PluginCore pluginCore, LocationDatabase locationDatabase)
			: base(DefaultRegion, "Map of Dereth", manager)
		{

			mPluginCore = pluginCore;

			mZipFile = new ZipFile(Util.FullPath(@"Huds\DerethMap.zip"));

			using (StreamReader mapInfoReader = new StreamReader(mZipFile.GetInputStream(mZipFile.GetEntry("map.txt"))))
			{
				// File format has 3 lines: mapSize, tileSize, padding
				mMapSize = int.Parse(mapInfoReader.ReadLine());
				mTileSize = int.Parse(mapInfoReader.ReadLine());
				mTilePaddedSize = mTileSize + int.Parse(mapInfoReader.ReadLine());
				mMaxTile = (mMapSize - 1) / mTileSize;
				mPixPerClick = mMapSize / 204.1f;
			}

			mActualZoom = mZoomMultiplier;

			mCoordTickDelta = CalculateCoordTickDelta(Zoom);

			SetAlphaFading(255, 128);

			ClientMouseDown += new EventHandler<HudMouseEventArgs>(MapHud_ClientMouseDown);
			ClientMouseUp += new EventHandler<HudMouseEventArgs>(MapHud_ClientMouseUp);
			ClientMouseDrag += new EventHandler<HudMouseDragEventArgs>(MapHud_ClientMouseDrag);
			ClientMouseDoubleClick += new EventHandler<HudMouseEventArgs>(MapHud_ClientMouseDoubleClick);
			ClientMouseWheel += new EventHandler<HudMouseEventArgs>(MapHud_ClientMouseWheel);
			ClientMouseMove += new EventHandler<HudMouseEventArgs>(MapHud_ClientMouseMove);
			ClientMouseLeave += new EventHandler<HudMouseEventArgs>(MapHud_ClientMouseLeave);

			ResizeEnd += new EventHandler(MapHud_ResizeEnd);

			ClientVisibleChanged += new EventHandler(MapHud_ClientVisibleChanged);
			Moving += new EventHandler(MapHud_Moving);

			Heartbeat += new EventHandler(MapHud_Heartbeat);

			ResizeDrawMode = HudResizeDrawMode.Repaint;

			mLocDb = locationDatabase;

			WindowMessage += new EventHandler<WindowMessageEventArgs>(MapHud_WindowMessage);

			AlphaChanged += new EventHandler<AlphaChangedEventArgs>(MapHud_AlphaChanged);

			//MaxSize = new Size(1024, 1024);
		}

		public override void Dispose()
		{
			if (!Disposed)
			{
				mSpriteHud.Enabled = false;

				if (mDerethMapLowRes != null)
				{
					mDerethMapLowRes.Dispose();
					mDerethMapLowRes = null;
				}
				mTileCache.Clear();
				mTileCacheAccesses.Clear();

				mZipFile.Close();
			}

			base.Dispose();
		}
		#endregion

		#region Properties
		public void ResetPosition()
		{
			Region = DefaultRegion;
		}

		public bool CenterOnPlayer
		{
			get { return mCenterOnPlayer; }
			set
			{
				mCenterOnPlayer = value;
				mNeedsPlayerRecenter = value;
				if (mCenterOnPlayer)
				{
					RepaintAll();
				}
			}
		}

		public bool DrawCoords
		{
			get { return mDrawCoords; }
			set
			{
				if (mDrawCoords != value)
				{
					mDrawCoords = value;
					RepaintAll();
				}
			}
		}

		public MouseButtons DragButton
		{
			get { return mDragButton; }
			set { mDragButton = value; }
		}

		public MouseButtons SelectLocationButton
		{
			get { return mSelectLocationButton; }
			set { mSelectLocationButton = value; }
		}

		public MouseButtons ContextMenuButton
		{
			get { return mContextMenuButton; }
			set { mContextMenuButton = value; }
		}

		public MouseButtons DetailsButton
		{
			get { return mDetailsButton; }
			set { mDetailsButton = value; }
		}

		public double PlayerHeadingRadians
		{
			get { return mPlayerHeadingRadians; }
			set
			{
				if (mPlayerHeadingRadians != value)
				{
					mPlayerHeadingRadians = value;
					RepaintSprites();
				}
			}
		}

		public Coordinates PlayerCoords
		{
			get { return mPlayerCoords; }
			set
			{
				if (Math.Abs(mPlayerCoords.NS - value.NS) > 0.005
						|| Math.Abs(mPlayerCoords.EW - value.EW) > 0.005)
				{
					mPlayerCoords = value;
					RepaintSprites();
					if (CenterOnPlayer)
					{
						mNeedsPlayerRecenter = true;

						// Number of pixels player can move before recentering
						const int pixelAllowance = 20;
						float delta = 0.5f / (mPixPerClick * Zoom) * pixelAllowance;

						PointF playerPix = CoordsToPix(mPlayerCoords, ActualZoom);

						if (playerPix.X < 0 || playerPix.X >= ClientSize.Width ||
								playerPix.Y < 0 || playerPix.Y >= ClientSize.Height ||
								Math.Abs(mLastPaintedPlayerCoords.NS - value.NS) > delta ||
								Math.Abs(mLastPaintedPlayerCoords.EW - value.EW) > delta)
						{
							mLastPaintedPlayerCoords = value;
							RepaintAll();
						}
					}
				}
			}
		}

		public bool PlayerInDungeon
		{
			get { return mPlayerInDungeon; }
			set
			{
				if (mPlayerInDungeon != value)
				{
					mPlayerInDungeon = value;
					if (mPlayerInDungeon)
						mNeedsPlayerRecenter = false;
					RepaintSprites();
				}
			}
		}

		public bool ShowCoords
		{
			get { return mShowCoords; }
			set
			{
				if (mShowCoords != value)
				{
					mShowCoords = value;
					RepaintAll();
				}
			}
		}

		public Route Route
		{
			get { return mRoute; }
			set
			{
				mRoute = value;
				RepaintAll();
			}
		}

		public bool ShowRoute
		{
			get { return mShowRoute; }
			set
			{
				if (mShowRoute != value)
				{
					mShowRoute = value;
					RepaintAll();
				}
			}
		}

		public bool ShowLocations
		{
			get { return mShowLocations; }
			set
			{
				if (mShowLocations != value)
				{
					mShowLocations = value;
					RepaintAll();
				}
			}
		}

		public bool ShowLocationsAllZooms
		{
			get { return mShowLocationsAllZooms; }
			set
			{
				if (mShowLocationsAllZooms != value)
				{
					mShowLocationsAllZooms = value;
					RepaintAll();
				}
			}
		}

		public bool ShowLabels
		{
			get { return mShowLabels; }
			set
			{
				if (mShowLabels != value)
				{
					mShowLabels = value;
					RepaintAll();
				}
			}
		}

		public LocationDatabase LocationDatabase
		{
			get { return mLocDb; }
			set
			{
				mLocDb = value;
				RepaintAll();
			}
		}

		public Coordinates CoordsAtCenter
		{
			get
			{
				PointF centerPt = new PointF(ClientSize.Width / 2.0f, ClientSize.Height / 2.0f);
				return PixToCoords(centerPt, ActualZoom);
			}
			set { CenterOnCoords(value); }
		}

		/// <summary>
		/// Gets the zoom multiplier, not accounting for any constraints that 
		/// occurred by painting when the map was zoom all the way out.
		/// </summary>
		public float Zoom
		{
			get { return mZoomMultiplier; }
			set { ZoomFactor = Math.Log(value, ZoomBase); }
		}

		/// <summary>
		/// Gets the current zoom multiplier, as it was last painted.  This 
		/// accounts for the zoom limiting that happens when the map is zoomed 
		/// all the way out.
		/// </summary>
		private float ActualZoom
		{
			get { return mActualZoom; }
			set { mActualZoom = value; }
		}

		private double ZoomFactor
		{
			get { return mZoomFactor; }
			set
			{

				mZoomFactor = value;
				if (mZoomFactor > MaxZoomFactor)
					mZoomFactor = MaxZoomFactor;
				mZoomMultiplier = (float)Math.Pow(ZoomBase, Math.Floor(mZoomFactor));

				float zoomedSize = mMapSize * mZoomMultiplier;
				if (zoomedSize < ClientSize.Width || zoomedSize < ClientSize.Height)
				{
					float z = Math.Max(ClientSize.Width / (float)mMapSize, ClientSize.Height / (float)mMapSize);
					if (z > 1) { z = 1; }
					mZoomFactor = Math.Log(z, ZoomBase);
					if (mZoomFactor > MaxZoomFactor)
						mZoomFactor = MaxZoomFactor;
					mZoomMultiplier = z;
				}

				mCoordTickDelta = CalculateCoordTickDelta(Zoom);
				ActualZoom = mZoomMultiplier;
			}
		}
		#endregion

		#region Mouse Handling
		private void MapHud_ClientMouseDown(object sender, HudMouseEventArgs e)
		{
			if ((e.Button & DragButton) != 0)
			{
				mMouseDownOffset.X = (e.Location.X - mOffset.X * Zoom) / Zoom;
				mMouseDownOffset.Y = (e.Location.Y - mOffset.Y * Zoom) / Zoom;
				mIsDragging = true;
				e.Eat = true;
			}

			if ((e.Button & (SelectLocationButton | ContextMenuButton | DetailsButton)) != 0
					&& ActiveHotspot != Hotspot.None && ActiveHotspot.rect.Contains(e.Location))
			{
				mClickedHotspotRect = ActiveHotspot.rect;
				e.Eat = true;
			}
		}

		private void MapHud_ClientMouseUp(object sender, HudMouseEventArgs e)
		{
			if (((ClientMouseButtons & ~e.Button) & DragButton) == 0 && mIsDragging)
			{
				mIsDragging = false;
				RepaintAll();
			}

			if (mClickedHotspotRect.Contains(e.Location))
			{
				if ((e.Button & SelectLocationButton) != 0)
				{
					if (e.Control)
					{
						mPluginCore.SetRouteEnd(ActiveHotspot.loc);
					}
					else if (e.Shift)
					{
						mPluginCore.SetRouteStart(ActiveHotspot.loc);
					}
					else
					{
						mPluginCore.mArrowHud.DestinationLocation = ActiveHotspot.loc;
						mPluginCore.mArrowHud.Visible = true;
					}
				}
				else if ((e.Button & ContextMenuButton) != 0)
				{
					ShowContextMenu(ActiveHotspot.loc);
				}
				else if ((e.Button & DetailsButton) != 0)
				{
					mPluginCore.ShowDetails(ActiveHotspot.loc);
				}
			}
			else if (ActiveHotspot == Hotspot.None && (e.Button & ContextMenuButton) != 0)
			{
				ShowContextMenu(PixToCoords(e.Location, ActualZoom));
			}
			mClickedHotspotRect = Rectangle.Empty;
		}

		private void MapHud_ClientMouseDrag(object sender, HudMouseDragEventArgs e)
		{
			if ((e.Button & DragButton) != 0)
			{
				mOffset.X = (e.Location.X - mMouseDownOffset.X * Zoom) / Zoom;
				mOffset.Y = (e.Location.Y - mMouseDownOffset.Y * Zoom) / Zoom;
				mNeedsPlayerRecenter = false;
				RepaintAll();
			}
		}

		private void MapHud_ClientMouseDoubleClick(object sender, HudMouseEventArgs e)
		{
			if ((e.Button & DragButton) != 0)
			{
				CenterOnPix(e.Location, Zoom);
				RepaintAll();
				e.Eat = true;
			}
		}

		private void MapHud_ClientMouseWheel(object sender, HudMouseEventArgs e)
		{
			float origZoom = ActualZoom;
			ZoomFactor += e.Delta / (double)HudMouseEventArgs.WHEEL_DELTA;

			if (CenterOnPlayer && IsCenteredOnPlayer(origZoom, ClientSize))
			{
				mNeedsPlayerRecenter = true;
			}
			else
			{
				PointF centerOn;

				if (!e.Control && !e.Shift)
				{
					// Keep point under mouse cursor in same spot
					centerOn = e.Location;
				}
				else
				{
					// Keep center in same spot
					centerOn = new PointF(ClientSize.Width / 2.0f, ClientSize.Height / 2.0f);
				}

				mOffset.X += centerOn.X * (1 / ActualZoom - 1 / origZoom);
				mOffset.Y += centerOn.Y * (1 / ActualZoom - 1 / origZoom);
			}

			RepaintAll();
			e.Eat = true;
		}

		private void MapHud_ClientMouseMove(object sender, HudMouseEventArgs e)
		{
			CalcActiveHotspot(e.Location);
		}

		private void MapHud_ClientMouseLeave(object sender, HudMouseEventArgs e)
		{
			ActiveHotspot = Hotspot.None;
		}

		private void MapHud_ResizeEnd(object sender, EventArgs e)
		{
			RepaintAll();
		}

		public void ArrowHud_DestinationChanged(object sender, DestinationChangedEventArgs e)
		{
			RepaintAll();
		}

		private void CalcActiveHotspot(Point mousePos)
		{
			if (MouseOnClient)
			{
				foreach (Hotspot h in mHotspots)
				{
					if (h.rect.Contains(mousePos))
					{
						ActiveHotspot = h;
						return;
					}
				}
			}
			ActiveHotspot = Hotspot.None;
		}

		private Hotspot ActiveHotspot
		{
			get { return mActiveHotspot; }
			set
			{
				if (mActiveHotspot != value)
				{
					mActiveHotspot = value;
					RepaintSprites();
					if (mActiveHotspot == Hotspot.None)
					{
						Manager.HideToolTip();
					}
					else
					{
						Point toolTipLocation = new Point(MouseLocation.X + 16, MouseLocation.Y + 16);
						Manager.ShowToolTip(toolTipLocation, mActiveHotspot.loc.Name, 4000);
					}
				}
			}
		}

		private void MapHud_WindowMessage(object sender, WindowMessageEventArgs e)
		{
			const short WM_MOUSEMOVE = 0x0200;
			const short WM_LBUTTONDOWN = 0x0201;
			const short WM_LBUTTONUP = 0x0202;
			const short WM_RBUTTONDOWN = 0x0204;
			const short WM_RBUTTONUP = 0x0205;
			const short WM_MBUTTONDOWN = 0x0207;
			const short WM_MBUTTONUP = 0x0208;

			if (mContextMenu != null && (
					e.Msg == WM_LBUTTONDOWN || e.Msg == WM_LBUTTONUP ||
					e.Msg == WM_MBUTTONDOWN || e.Msg == WM_MBUTTONUP ||
					e.Msg == WM_RBUTTONDOWN || e.Msg == WM_RBUTTONUP))
			{
				CloseContextMenu();
			}

			if (e.Msg == WM_MOUSEMOVE && mIsDragging && (ClientMouseButtons & DragButton) == 0)
			{
				mIsDragging = false;
				RepaintAll();
			}
		}
		#endregion

		#region Context Menus
		private void ShowContextMenu(Location loc)
		{
			CloseContextMenu();

			mContextMenu = new ToolbarHud(Manager);
			mContextMenu.Location = MouseLocation;
			mContextMenu.Orientation = ToolbarOrientation.Vertical;
			mContextMenu.PositionLocked = true;
			mContextMenu.RespondsToRightClick = true;

			ToolbarButton label;

			switch (loc.Type)
			{
				case LocationType.Bindstone:
					label = new ToolbarButton(Icons.Toolbar.HouseStone, loc.Name);
					break;
				case LocationType.Dungeon:
				case LocationType.AllegianceHall:
					label = new ToolbarButton(Icons.Toolbar.Dungeon, loc.Name);
					break;
				case LocationType.Landmark:
					label = new ToolbarButton(Icons.Toolbar.Landmark, loc.Name);
					break;
				case LocationType.Lifestone:
					label = new ToolbarButton(Icons.Toolbar.Lifestone, loc.Name);
					break;
				case LocationType.NPC:
					label = new ToolbarButton(Icons.Map.NPC, loc.Name);
					break;
				case LocationType.Outpost:
					label = new ToolbarButton(Icons.Map.TownSmall, loc.Name);
					break;
				case LocationType.Portal:
				case LocationType.AnyPortal:
				case LocationType.SettlementPortal:
				case LocationType.TownPortal:
				case LocationType.UndergroundPortal:
				case LocationType.WildernessPortal:
				case LocationType.PortalDevice:
					label = new ToolbarButton(Icons.Toolbar.Portal, loc.Name);
					break;
				case LocationType.PortalHub:
					label = new ToolbarButton(Icons.Toolbar.PortalHub, loc.Name);
					break;
				case LocationType.Village:
					label = new ToolbarButton(Icons.Map.Settlement, loc.Name);
					break;
				case LocationType.Town:
					label = new ToolbarButton(Icons.Map.Town, loc.Name);
					break;
				case LocationType.Vendor:
					label = new ToolbarButton(Icons.Map.Store, loc.Name);
					break;
				case LocationType.Custom:
					label = new ToolbarButton(Icons.Toolbar.Dereth, loc.Name);
					break;
				default:
					label = new ToolbarButton(loc.Name);
					break;
			}

			ToolbarButton coordsLabel = mContextMenu.AddButton(label);
			coordsLabel.IsLabelOnly = true;

			ToolbarButton showDetails = new ToolbarButton(Icons.Toolbar.MagnifyingGlass, "Show Details");
			showDetails.Click += new EventHandler(delegate(object s, EventArgs e)
			{
				mPluginCore.ShowDetails(loc);
				CloseContextMenu();
			});
			mContextMenu.AddButton(showDetails);

			ToolbarButton arrowDest = new ToolbarButton(Icons.Toolbar.GoArrow, "Point Arrow Here");
			arrowDest.Click += new EventHandler(delegate(object s, EventArgs e)
			{
				mPluginCore.mArrowHud.DestinationLocation = loc;
				mPluginCore.mArrowHud.Visible = true;
				CloseContextMenu();
			});
			mContextMenu.AddButton(arrowDest);

			ToolbarButton routeStart = new ToolbarButton(Icons.Toolbar.RouteStart, "Start Route Here");
			routeStart.Click += new EventHandler(delegate(object s, EventArgs e)
			{
				mPluginCore.SetRouteStart(loc);
				CloseContextMenu();
			});
			mContextMenu.AddButton(routeStart);

			ToolbarButton routeEnd = new ToolbarButton(Icons.Toolbar.RouteEnd, "End Route Here");
			routeEnd.Click += new EventHandler(delegate(object s, EventArgs e)
			{
				mPluginCore.SetRouteEnd(loc);
				CloseContextMenu();
			});
			mContextMenu.AddButton(routeEnd);

			if (mPluginCore.mDungeonHud.DungeonMapAvailable(loc))
			{
				ToolbarButton showDungeon = new ToolbarButton(Icons.Toolbar.Dungeon, "Show Dungeon Map");
				showDungeon.Click += new EventHandler(delegate(object s, EventArgs e)
				{
					mPluginCore.mDungeonHud.Visible = true;
					mPluginCore.mDungeonHud.LoadDungeonById(mPluginCore.mDungeonHud.GetDungeonId(loc));
					CloseContextMenu();
				});
				mContextMenu.AddButton(showDungeon);
			}

			if (loc.HasExitCoords)
			{
				ToolbarButton jumpToExit = new ToolbarButton(Icons.Toolbar.JumpToExit, "Jump to Exit (" + loc.ExitCoords + ")");
				jumpToExit.Click += new EventHandler(delegate(object s, EventArgs e)
				{
					mPluginCore.mMapHud.CenterOnCoords(loc.ExitCoords);
					CloseContextMenu();
				});
				mContextMenu.AddButton(jumpToExit);
			}

			mContextMenu.Visible = true;
		}

		private void ShowContextMenu(Coordinates coords)
		{
			CloseContextMenu();

			mContextMenu = new ToolbarHud(Manager);
			mContextMenu.Location = MouseLocation;
			mContextMenu.Orientation = ToolbarOrientation.Vertical;
			mContextMenu.PositionLocked = true;
			mContextMenu.RespondsToRightClick = true;

			ToolbarButton coordsLabel = mContextMenu.AddButton(new ToolbarButton(coords.ToString("0.0")));
			coordsLabel.IsLabelOnly = true;

			ToolbarButton arrowDest = new ToolbarButton(Icons.Toolbar.GoArrow, "Point Arrow Here");
			arrowDest.Click += new EventHandler(delegate(object s, EventArgs e)
			{
				mPluginCore.mArrowHud.DestinationCoords = coords;
				mPluginCore.mArrowHud.Visible = true;
				CloseContextMenu();
			});
			mContextMenu.AddButton(arrowDest);

			ToolbarButton routeStart = new ToolbarButton(Icons.Toolbar.RouteStart, "Start Route Here");
			routeStart.Click += new EventHandler(delegate(object s, EventArgs e)
			{
				mPluginCore.SetRouteStart(coords);
				CloseContextMenu();
			});
			mContextMenu.AddButton(routeStart);

			ToolbarButton routeEnd = new ToolbarButton(Icons.Toolbar.RouteEnd, "End Route Here");
			routeEnd.Click += new EventHandler(delegate(object s, EventArgs e)
			{
				mPluginCore.SetRouteEnd(coords);
				CloseContextMenu();
			});
			mContextMenu.AddButton(routeEnd);

			mContextMenu.Visible = true;
		}

		private void CloseContextMenu()
		{
			if (mContextMenu != null)
			{
				mContextMenu.Dispose();
				mContextMenu = null;
			}
		}
		#endregion

		#region Painting
		private void RepaintAll()
		{
			//mNeedsFullRepaint = true;
			Repaint();
		}

		private void RepaintSprites()
		{
			mNeedsSpriteRepaint = true;
		}

		private void MapHud_Heartbeat(object sender, EventArgs e)
		{
#if !USING_D3D_CONTAINER
			PlayerCoords = new Coordinates(Host.Actions.Landcell,
				Host.Actions.LocationY, Host.Actions.LocationX);

			PlayerInDungeon = IsDungeon(Host.Actions.Landcell);

			PlayerHeadingRadians = Host.Actions.HeadingRadians;
#endif
			if (ClientVisible && mNeedsSpriteRepaint)
			{
				PaintSprites(ActualZoom);
			}
		}

		public bool IsDungeon(int landblock)
		{
			return Util.IsDungeon(landblock);
			//return (landblock & 0x0000FF00) != 0;
		}

		public override void RecreateHud()
		{
			base.RecreateHud();

			if (mSpriteHud != null)
			{
				mSpriteHud.Enabled = false;
				Host.Render.RemoveHud(mSpriteHud);
				mSpriteHud.Dispose();
				mSpriteHud = null;
			}

			PaintSprites(Zoom);
		}

		private void MapHud_ClientVisibleChanged(object sender, EventArgs e)
		{
			if (ClientVisible)
			{
				if (CenterOnPlayer)
				{
					PointF playerPoint = CoordsToPix(PlayerCoords, Zoom);

					float dX = playerPoint.X - ClientSize.Width / 2.0f;
					float dY = playerPoint.X - ClientSize.Height / 2.0f;

					// More than 4 pixels away from the center
					if (dX * dX + dY * dY > 16)
					{
						mNeedsPlayerRecenter = true;
						RepaintAll();
					}
				}
				PaintSprites(Zoom);
				mSpriteHud.Enabled = ClientVisible;
				mSpriteHud.Alpha = AlphaFrame;
			}
			else if (mSpriteHud != null && !mSpriteHud.Lost)
			{
				mSpriteHud.Enabled = ClientVisible;
				mSpriteHud.Alpha = AlphaFrame;
			}
		}

		private void MapHud_Moving(object sender, EventArgs e)
		{
			if (mSpriteHud != null && !mSpriteHud.Lost)
			{
				mSpriteHud.Region = new Rectangle(ClientLocation, mSpriteHud.Region.Size);
			}
		}

		private void MapHud_AlphaChanged(object sender, AlphaChangedEventArgs e)
		{
			if (mSpriteHud != null && !mSpriteHud.Lost)
			{
				mSpriteHud.Alpha = e.Alpha;
			}
		}

		protected override void PaintClient(Graphics g, bool imageDataLost)
		{
			float z = Zoom;

			if (mPreviousClientSize != ClientSize)
			{
				if (!mNeedsPlayerRecenter && mCenterOnCoords == Coordinates.NO_COORDINATES)
				{
					if (CenterOnPlayer && IsCenteredOnPlayer(z, mPreviousClientSize))
						mNeedsPlayerRecenter = true;
					else
						CenterOnPix(mPreviousClientSize.Width / 2, mPreviousClientSize.Height / 2, z);
				}
				mPreviousClientSize = ClientSize;
			}

			float zoomedSize = mMapSize * z;
			if (zoomedSize < ClientSize.Width || zoomedSize < ClientSize.Height)
			{
				z = Math.Max(ClientSize.Width / (float)mMapSize, ClientSize.Height / (float)mMapSize);
				if (z > 1)
					z = 1;
				Zoom = z;
				ActualZoom = z;
				zoomedSize = mMapSize * z;
			}

			// Paint Map
			Matrix origTransform = g.Transform;
			g.Clear(Clear);

			float coordPadX = 0, coordPadY = 0;

			if (mCenterOnCoords != Coordinates.NO_COORDINATES)
			{
				CenterOnCoords(mCenterOnCoords, z);
				mCenterOnCoords = Coordinates.NO_COORDINATES;
				mNeedsPlayerRecenter = false;
			}
			else if (mNeedsPlayerRecenter && (ClientMouseButtons & DragButton) == 0)
			{
				mNeedsPlayerRecenter = false;
				if (!PlayerInDungeon)
					CenterOnCoords(mPlayerCoords, z);
			}

			if (ClientSize.Width > zoomedSize)
			{
				// Center horizontally
				mOffset.X = (ClientSize.Width - zoomedSize) / (2 * z);
				coordPadX = mOffset.X * z;
			}
			else if (mOffset.X > 0)
				mOffset.X = 0;
			else if (mOffset.X < (ClientSize.Width - zoomedSize) / z)
				mOffset.X = (ClientSize.Width - zoomedSize) / z;

			if (ClientSize.Height > zoomedSize)
			{
				// Center vertically
				mOffset.Y = (ClientSize.Height - zoomedSize) / (2 * z);
				coordPadY = mOffset.Y * z;
			}
			else if (mOffset.Y > 0)
				mOffset.Y = 0;
			else if (mOffset.Y < (ClientSize.Height - zoomedSize) / z)
				mOffset.Y = (ClientSize.Height - zoomedSize) / z;

			#region Draw Map
			if (z < 1)
			{
				// Lazy load low res map
				if (mDerethMapLowRes == null)
				{
					mDerethMapLowRes = new Bitmap(mZipFile.GetInputStream(mZipFile.GetEntry("lowres.png")));
				}

				float relSize = (float)mDerethMapLowRes.Width / mMapSize;

				if (z / relSize <= 0.5f)
					g.InterpolationMode = InterpolationMode.HighQualityBilinear;
				else
					g.InterpolationMode = InterpolationMode.Bilinear;

				RectangleF srcRect = new RectangleF(-mOffset.X * relSize, -mOffset.Y * relSize,
					ClientSize.Width * relSize / z, ClientSize.Height * relSize / z);
				RectangleF destRect = new RectangleF(0, 0, ClientSize.Width, ClientSize.Height);

				g.DrawImage(mDerethMapLowRes, destRect, srcRect, GraphicsUnit.Pixel);
			}
			else
			{
				float w = ClientSize.Width / z;

				int minTileX = Math.Max((int)(-mOffset.X / mTileSize), 0);
				int minTileY = Math.Max((int)(-mOffset.Y / mTileSize), 0);
				int maxTileX = Math.Min(
					(int)((-mOffset.X + ClientSize.Width / z) / mTileSize), mMaxTile);
				int maxTileY = Math.Min(
					(int)((-mOffset.Y + ClientSize.Height / z) / mTileSize), mMaxTile);

				if (z == 1)
				{
					int offX = (int)mOffset.X, offY = (int)mOffset.Y;
					int dX, dY;
					for (int i = minTileX; i <= maxTileX; i++)
					{
						dX = i * mTileSize + offX;
						for (int j = minTileY; j <= maxTileY; j++)
						{
							dY = j * mTileSize + offY;
							GraphicsUtil.BitBlt(GetTile(i, j), ClientImage, dX, dY);
						}
					}
				}
				else
				{
					g.InterpolationMode = InterpolationMode.NearestNeighbor;
					g.ScaleTransform(z, z);
					float dX, dY;
					for (int i = minTileX; i <= maxTileX; i++)
					{
						dX = i * mTileSize + mOffset.X;
						for (int j = minTileY; j <= maxTileY; j++)
						{
							dY = j * mTileSize + mOffset.Y;
							g.DrawImage(GetTile(i, j), dX, dY);
						}
					}
				}
			}
			#endregion

			g.Transform = origTransform;

			#region Draw Route-Finding Regions
#if false
			float regionZoom = z * mPixPerClick;
			g.ScaleTransform(z, z);
			g.TranslateTransform(mOffset.X + mMapSize / 2.0f - 1.0f, mOffset.Y + mMapSize / 2.0f);
			g.ScaleTransform(mPixPerClick, -mPixPerClick);
			Brush regionFill = new SolidBrush(Color.FromArgb(0x77CC0000));
			Brush innerRegionFill = new SolidBrush(Color.FromArgb(0x77AB10BC));
			RouteFinder.DrawRegions(g, regionFill, innerRegionFill);
			g.Transform = origTransform;
#endif
			#endregion

			Coordinates minCoords = PixToCoords(0, 0, z);
			Coordinates maxCoords = PixToCoords(ClientSize.Width, ClientSize.Height, z);

			#region Draw Coordinates (Part I: Gridlines)
			float lastTickNS = 0, firstTickEW = 0, lastTickEW = 0, firstTickNS = 0;
			RectangleF mapRect = new RectangleF(), insideGutter = new RectangleF();
			Region coordGutter = null;
			string precision = "";
			if (DrawCoords)
			{
				g.SmoothingMode = SmoothingMode.Default;

				if (mCoordTickDelta >= 1)
					precision = "0";
				else if (mCoordTickDelta >= 0.1)
					precision = "0.0";
				else
					precision = "0.00";

				lastTickNS = (float)(Math.Floor(minCoords.NS / mCoordTickDelta) * mCoordTickDelta);
				firstTickEW = (float)(Math.Floor(minCoords.EW / mCoordTickDelta) * mCoordTickDelta);
				lastTickEW = (float)(Math.Ceiling(maxCoords.EW / mCoordTickDelta) * mCoordTickDelta);
				firstTickNS = (float)(Math.Ceiling(maxCoords.NS / mCoordTickDelta) * mCoordTickDelta);

				mapRect = new RectangleF(coordPadX, coordPadY,
				   ClientSize.Width - 2 * coordPadX, ClientSize.Height - 2 * coordPadY);
				insideGutter = new RectangleF(
				   coordPadX + CoordGutterSize,
				   coordPadY + CoordGutterSize,
				   ClientSize.Width - 2 * (CoordGutterSize + coordPadX),
				   ClientSize.Height - 2 * (CoordGutterSize + coordPadY));
				coordGutter = new Region(mapRect);
				coordGutter.Xor(insideGutter);
				g.FillRegion(CoordGutterFill, coordGutter);

				// Draw Gridlines
				for (float ns = firstTickNS, ew = firstTickEW;
						ns <= lastTickNS || ew <= lastTickEW;
						ns += mCoordTickDelta, ew += mCoordTickDelta)
				{
					PointF pos = CoordsToPix(ns, ew, z);
					if (pos.Y > insideGutter.Top && pos.Y < insideGutter.Bottom)
					{
						// Draw horizontal NS gridline
						g.DrawLine(CoordGridline, CoordGutterSize + coordPadX, pos.Y,
							ClientSize.Width - CoordGutterSize - coordPadX - 1, pos.Y);
					}
					if (pos.X > insideGutter.Left && pos.X < insideGutter.Right)
					{
						// Draw vertical EW gridline
						g.DrawLine(CoordGridline, pos.X, CoordGutterSize + coordPadY,
							pos.X, ClientSize.Height - CoordGutterSize - coordPadY - 1);
					}
				}
			}
			#endregion

			#region Draw Locations
			mHotspots.Clear();
			float z2 = (float)(Math.Pow(z / ZoomBase, 0.25));
			if (ShowLocations)
			{
				g.SmoothingMode = SmoothingMode.Default;
				g.InterpolationMode = InterpolationMode.HighQualityBilinear;

				Dictionary<LocationType, List<Location>> visibleLocations
					= new Dictionary<LocationType, List<Location>>();

				foreach (Location loc in mLocDb.Locations.Values)
				{
					if (loc.Coords.NS <= minCoords.NS && loc.Coords.NS >= maxCoords.NS
							&& loc.Coords.EW >= minCoords.EW && loc.Coords.EW <= maxCoords.EW)
					{

						List<Location> addTo;
						LocationType type = loc.Type;
						if ((type & LocationType.AnyPortal) != 0)
						{
							type = LocationType.AnyPortal;
						}
						if (!visibleLocations.TryGetValue(type, out addTo))
						{
							addTo = new List<Location>();
							visibleLocations.Add(type, addTo);
						}
						addTo.Add(loc);
					}
				}

				if (ShowLocationsAllZooms || ZoomFactor >= 5)
				{
					DrawLocationType(visibleLocations, LocationType.NPC, Icons.Map.NPC, g, z, z2);
					DrawLocationType(visibleLocations, LocationType.Vendor, Icons.Map.Store, g, z, z2);
				}
				if (ShowLocationsAllZooms || ZoomFactor >= 2)
				{
					DrawLocationType(visibleLocations, LocationType.Village, Icons.Map.Settlement, g, z, z2);
					DrawLocationType(visibleLocations, LocationType.Landmark, Icons.Map.PointOfInterest, g, z, z2);
					DrawLocationType(visibleLocations, LocationType.AllegianceHall, Icons.Map.Dungeon, g, z, z2);
					DrawLocationType(visibleLocations, LocationType.Bindstone, Icons.Map.Bindstone, g, z, z2);
				}
				if (ShowLocationsAllZooms || ZoomFactor >= -1)
				{
					DrawLocationType(visibleLocations, LocationType.Custom, Icons.Map.PointOfInterest, g, z, z2);
					DrawLocationType(visibleLocations, LocationType.Lifestone, Icons.Map.Lifestone, g, z, z2);
					DrawLocationType(visibleLocations, LocationType.Dungeon, Icons.Map.Dungeon, g, z, z2);
					DrawLocationType(visibleLocations, LocationType.AnyPortal, Icons.Map.Portal, g, z, z2);
					DrawLocationType(visibleLocations, LocationType.PortalHub, Icons.Map.PortalHub, g, z, z2);
					DrawLocationType(visibleLocations, LocationType.Outpost, Icons.Map.TownSmall, g, z, z2);
				}

				DrawLocationType(visibleLocations, LocationType.Town, Icons.Map.Town, g, z, z2);

				//DrawLocationType(visibleLocations, LocationType._StartPoint, Icons.Map.StartPoint, g, z, z2);
				//DrawLocationType(visibleLocations, LocationType._EndPoint, Icons.Map.EndPoint, g, z, z2);

				if (ShowLabels)
				{
					float z3 = (float)(Math.Pow(z / ZoomBase, 0.1));
					Font f = new Font(FontFamily.GenericSerif, Math.Min(8 * z3, 10));
					Font fTown = new Font(FontFamily.GenericSerif, Math.Min(9 * z3, 12), FontStyle.Bold);
					List<RectangleF> usedLabelRects = new List<RectangleF>();

					if (ZoomFactor >= -4)
					{
						LabelLocationType(g, fTown, usedLabelRects, LocationType.Town);
					}
					if (ZoomFactor >= 1)
					{
						LabelLocationType(g, f, usedLabelRects, LocationType.Outpost);
						LabelLocationType(g, f, usedLabelRects, LocationType.PortalHub);
						LabelLocationType(g, f, usedLabelRects, LocationType.AnyPortal);
						LabelLocationType(g, f, usedLabelRects, LocationType.Dungeon);
						LabelLocationType(g, f, usedLabelRects, LocationType.Lifestone);
						LabelLocationType(g, f, usedLabelRects, LocationType.Custom);
					}
					if (ZoomFactor >= 4)
					{
						LabelLocationType(g, f, usedLabelRects, LocationType.Bindstone);
						LabelLocationType(g, f, usedLabelRects, LocationType.AllegianceHall);
						LabelLocationType(g, f, usedLabelRects, LocationType.Landmark);
						LabelLocationType(g, f, usedLabelRects, LocationType.Village);
					}
					//if (ZoomFactor >= 6) {
					//    LabelLocation(g, f, usedLabelRects, LocationType.Vendor);
					//    LabelLocation(g, f, usedLabelRects, LocationType.NPC);
					//}
				}
			}
			#endregion

			#region Draw Route
			if (ShowRoute && mRoute != null && mRoute.Count > 1)
			{
				g.SmoothingMode = SmoothingMode.AntiAlias;
				PointF lastPoint, curPoint = CoordsToPix(mRoute[0].Coords, z);
				for (int i = 1; i < mRoute.Count; i++)
				{
					// Running
					lastPoint = curPoint;
					curPoint = CoordsToPix(mRoute[i].Coords, z);

					double dX = lastPoint.X - curPoint.X;
					double dY = lastPoint.Y - curPoint.Y;
					double dist = Math.Sqrt(dX * dX + dY * dY);
					if (dist >= 5.0)
					{
						g.DrawLine(RouteLineBackground, lastPoint, curPoint);
						g.DrawLine(RouteLine, lastPoint, curPoint);
					}

					// Portals
					if (mRoute[i].HasExitCoords)
					{
						lastPoint = curPoint;
						curPoint = CoordsToPix(mRoute[i].ExitCoords, z);

						dX = lastPoint.X - curPoint.X;
						dY = lastPoint.Y - curPoint.Y;
						dist = Math.Sqrt(dX * dX + dY * dY);
						if (dist >= 5.0)
						{
							//g.DrawLine(RouteLineBackground, lastPoint, curPoint);
							g.DrawLine(RouteLinePortal, lastPoint, curPoint);
						}
					}
				}
			}
			#endregion

			#region Draw Arrow Destination
			PointF ptf = CoordsToPix(mPluginCore.mArrowHud.DestinationCoords, z);
			if (ptf.X >= 0 && ptf.Y >= 0 && ptf.X < ClientSize.Width && ptf.Y < ClientSize.Height)
			{
				float zw = z2 * Icons.Map.ArrowDest2.Width;
				float zh = z2 * Icons.Map.ArrowDest2.Height;
				RectangleF rectf = new RectangleF(ptf.X - zw / 2, ptf.Y - zh / 2, zw, zh);
				g.DrawImage(Icons.Map.ArrowDest2, rectf);
				Rectangle rect = new Rectangle(Point.Truncate(rectf.Location), Size.Ceiling(rectf.Size));
			}
			#endregion

			#region Draw Coordinates (Part II: Labels)
			if (DrawCoords)
			{
				Region clipNS = new Region(new RectangleF(mapRect.X, mapRect.Y + CoordGutterSize, mapRect.Width, mapRect.Height - 2 * CoordGutterSize));
				Region clipEW = new Region(new RectangleF(mapRect.X + CoordGutterSize, mapRect.Y, mapRect.Width - 2 * CoordGutterSize, mapRect.Height));
				Region origClip = g.Clip;

				// Draw Labels
				for (float ns = firstTickNS, ew = firstTickEW;
						ns <= lastTickNS || ew <= lastTickEW;
						ns += mCoordTickDelta, ew += mCoordTickDelta)
				{
					PointF pos = CoordsToPix(ns, ew, z);

					string nsString = Math.Abs(ns).ToString(precision) + (ns >= 0 ? "N" : "S");
					string ewString = Math.Abs(ew).ToString(precision) + (ew >= 0 ? "E" : "W");

					SizeF nsSize = g.MeasureString(nsString, CoordTextFont);
					SizeF ewSize = g.MeasureString(ewString, CoordTextFont);

					g.Clip = clipNS;
					float nsY = pos.Y - nsSize.Width / 2;
					g.DrawString(nsString, CoordTextFont, Brushes.White, coordPadX, nsY, VerticalText);
					g.DrawString(nsString, CoordTextFont, Brushes.White,
						ClientSize.Width - coordPadX - nsSize.Height, nsY, VerticalText);

					g.Clip = coordGutter;
					float ewX = pos.X - ewSize.Width / 2;
					g.DrawString(ewString, CoordTextFont, Brushes.White, ewX, coordPadY);
					g.DrawString(ewString, CoordTextFont, Brushes.White,
						ewX, ClientSize.Height - ewSize.Height - coordPadY);
				}
				g.Clip = origClip;
			}
			#endregion

			PaintSprites(z);

			mPreviousZoomMultiplier = z;
			mLastPaintedPlayerCoords = PlayerCoords;
		}

		private void DrawLocationType(Dictionary<LocationType, List<Location>> visible,
				LocationType typeToDraw, Bitmap imageToUse, Graphics drawOn, float zoom, float imageZoom)
		{
			List<Location> locs;
			List<Location> portalHubs;
			if ((typeToDraw & LocationType.AnyPortal) == 0 || !visible.TryGetValue(LocationType.PortalHub, out portalHubs))
			{
				portalHubs = null;
			}

			if (visible.TryGetValue(typeToDraw, out locs))
			{
				float zw = imageZoom * imageToUse.Width;
				float zh = imageZoom * imageToUse.Height;
				foreach (Location loc in locs)
				{
					PointF ptf = CoordsToPix(loc.Coords, zoom);
					RectangleF rectf = new RectangleF(ptf.X - zw / 2, ptf.Y - zh / 2, zw, zh);
					bool draw = true;

					// Hide portals that overlap a portal hub 
					// (likely that the portal is part of the hub)
					if (portalHubs != null)
					{
						foreach (Location portalHub in portalHubs)
						{
							PointF portalHubPt = CoordsToPix(portalHub.Coords, zoom);
							if (rectf.Contains(portalHubPt))
							{
								draw = false;
								break;
							}
						}
					}

					if (draw)
					{
						drawOn.DrawImage(loc.IsFavorite ? Icons.Map.Favorite : imageToUse, rectf);
						Rectangle rect = new Rectangle(Point.Truncate(rectf.Location), Size.Ceiling(rectf.Size));
						mHotspots.AddFirst(new Hotspot(rect, loc));
					}
				}
			}
		}

		private void LabelLocationType(Graphics drawOn, Font textFont, List<RectangleF> usedLabelRects, LocationType typeToDraw)
		{
			foreach (Hotspot h in mHotspots)
			{
				if ((h.loc.Type & typeToDraw) != 0)
				{
					SizeF txtSize = drawOn.MeasureString(h.loc.Name, textFont);
					float mX = (h.rect.Left + h.rect.Right) / 2.0f;
					float mY = (h.rect.Top + h.rect.Bottom) / 2.0f;
					float mbY = (h.rect.Top + 3 * h.rect.Bottom) / 4.0f;
					RectangleF[] tryRects = { 
						// Bottom, Top, BelowCenter, Right, Left
						new RectangleF(new PointF(mX - txtSize.Width / 2.0f, h.rect.Bottom - 1), txtSize),
						new RectangleF(new PointF(mX - txtSize.Width / 2.0f, h.rect.Top - txtSize.Height + 1), txtSize),
						new RectangleF(new PointF(mX - txtSize.Width / 2.0f, mbY - txtSize.Height / 2.0f), txtSize),
						new RectangleF(new PointF(h.rect.Right - 1, mY - txtSize.Height / 2.0f), txtSize),
						new RectangleF(new PointF(h.rect.Left - txtSize.Width + 1, mY - txtSize.Height / 2.0f), txtSize),

						//// Farther Below, Above, Right, Left
						//new RectangleF(new PointF(mX - txtSize.Width / 2.0f, h.rect.Bottom + 4), txtSize),
						//new RectangleF(new PointF(mX - txtSize.Width / 2.0f, h.rect.Top - txtSize.Height - 4), txtSize),
						//new RectangleF(new PointF(h.rect.Right + 4, mY - txtSize.Height / 2.0f), txtSize),
						//new RectangleF(new PointF(h.rect.Left - txtSize.Width - 4, mY - txtSize.Height / 2.0f), txtSize),

						// Corners: TopRight, BottomRight, TopLeft, BottomLeft
						new RectangleF(new PointF(h.rect.Right - 1, h.rect.Top - txtSize.Height + 1), txtSize),
						new RectangleF(new PointF(h.rect.Right - 1, h.rect.Bottom - 1), txtSize),
						new RectangleF(new PointF(h.rect.Left - txtSize.Width + 1, h.rect.Top - txtSize.Height + 1), txtSize),
						new RectangleF(new PointF(h.rect.Left - txtSize.Width + 1, h.rect.Bottom - 1), txtSize),
					};

					for (int i = 0; i < tryRects.Length; i++)
					{
						bool intersects = false;
						foreach (RectangleF usedRect in usedLabelRects)
						{
							if (tryRects[i].IntersectsWith(usedRect))
							{
								intersects = true;
								break;
							}
						}
						if (!intersects)
						{
							PointF p = tryRects[i].Location;
							drawOn.DrawString(h.loc.Name, textFont, TextGlow1, new PointF(p.X - 1, p.Y));
							drawOn.DrawString(h.loc.Name, textFont, TextGlow1, new PointF(p.X, p.Y - 1));
							drawOn.DrawString(h.loc.Name, textFont, TextGlow1, new PointF(p.X + 1, p.Y));
							drawOn.DrawString(h.loc.Name, textFont, TextGlow1, new PointF(p.X, p.Y + 1));
							drawOn.DrawString(h.loc.Name, textFont, Brushes.White, p);
							usedLabelRects.Add(tryRects[i]);
							break;
						}
					}
				}
			}
		}

		private void PaintSprites(float z)
		{
			mNeedsSpriteRepaint = false;

			if (mSpriteHud == null || mSpriteHud.Lost)
			{
				if (mSpriteHud != null)
					mSpriteHud.Dispose();
				mSpriteHud = Host.Render.CreateHud(new Rectangle(ClientLocation, MaxSize));
				mSpriteHud.Alpha = AlphaFrame;
				mSpriteHud.Enabled = ClientVisible;
			}

			if (mSpriteHud.Region.Location != ClientLocation)
			{
				mSpriteHud.Region = new Rectangle(ClientLocation, mSpriteHud.Region.Size);
			}

			mSpriteHud.Clear();

			// Paint hotspot rectangle
			CalcActiveHotspot(MouseLocationClient);
			if (ActiveHotspot != Hotspot.None)
			{
				Rectangle border = ConstrainRectangle(ShrinkRect(ActiveHotspot.rect, -1), 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
				Rectangle inner = ConstrainRectangle(ActiveHotspot.rect, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);

				if (inner.Width > 0 && inner.Height > 0)
				{
					mSpriteHud.Fill(border, Color.Gold);
					mSpriteHud.Clear(inner);
				}
			}

			// Paint Arrows
			if (!PlayerInDungeon)
			{
				using (Graphics g = Graphics.FromImage(mSpriteBuffer))
				{
					g.SmoothingMode = SmoothingMode.AntiAlias;
					//Matrix origTransform = g.Transform;
					float zArrow = z < 1.0f ? (float)Math.Pow(z, 1.0 / 3.0) : 1.0f;

					// Paint Player Arrow
					PointF playerPointPix = CoordsToPix(mPlayerCoords, z);
					g.Clear(Clear);

					Point hudDrawPoint = new Point(
						(int)playerPointPix.X - mSpriteBuffer.Width / 2,
						(int)playerPointPix.Y - mSpriteBuffer.Height / 2);

					if (hudDrawPoint.X < 0)
						hudDrawPoint.X = 0;
					else if (hudDrawPoint.X > ClientSize.Width - mSpriteBuffer.Width)
						hudDrawPoint.X = ClientSize.Width - mSpriteBuffer.Width;

					if (hudDrawPoint.Y < 0)
						hudDrawPoint.Y = 0;
					else if (hudDrawPoint.Y > ClientSize.Height - mSpriteBuffer.Height)
						hudDrawPoint.Y = ClientSize.Height - mSpriteBuffer.Height;

					float offX = playerPointPix.X - hudDrawPoint.X;
					float offY = playerPointPix.Y - hudDrawPoint.Y;
					if (offX >= -4 && offX <= mSpriteBuffer.Width + 4 && offY >= -4 && offY <= mSpriteBuffer.Height + 4)
					{
						g.TranslateTransform(offX, offY);
						g.RotateTransform((float)(PlayerHeadingRadians * 180.0 / Math.PI));
						g.ScaleTransform(zArrow, zArrow);

						g.FillPolygon(mPlayerArrowFill, PlayerArrowPolygon);
						g.DrawPolygon(mPlayerArrowOutline, PlayerArrowPolygon);
						//g.Transform = origTransform;

						mSpriteHud.BeginRender();
						mSpriteHud.DrawImage(mSpriteBuffer, new Rectangle(hudDrawPoint, mSpriteBuffer.Size));
						mSpriteHud.EndRender();
					}
				}
			}
		}

		private Rectangle ConstrainRectangle(Rectangle rect, int minX, int minY, int maxX, int maxY)
		{
			int left = rect.Left, top = rect.Top, right = rect.Right, bottom = rect.Bottom;
			if (left < minX) { left = minX; }
			if (top < minY) { top = minY; }
			if (right > maxX) { right = maxX; }
			if (bottom > maxY) { bottom = maxY; }
			return Rectangle.FromLTRB(left, top, right, bottom);
		}

		private Bitmap GetTile(int x, int y)
		{
			return GetTile(new Point(x, y));
		}

		private Bitmap GetTile(Point p)
		{
			Bitmap tile;
			if (mTileCache.TryGetValue(p, out tile))
			{
				mTileCacheAccesses.Remove(p);
			}
			else
			{
				if (mTileCache.Count >= TileCacheSize)
				{
					mTileCache.Remove(mTileCacheAccesses.Last.Value);
					mTileCacheAccesses.RemoveLast();
				}
				tile = new Bitmap(mZipFile.GetInputStream(mZipFile.GetEntry(p.X + "," + p.Y + ".png")));
				mTileCache.Add(p, tile);
			}
			mTileCacheAccesses.AddFirst(p);
			return tile;
		}

		private float CalculateCoordTickDelta(float zoom)
		{
			const int DesiredPixBetweenTicks = 40;
			float delta = DesiredPixBetweenTicks / (mPixPerClick * zoom);

			float minDist = float.PositiveInfinity;
			for (int i = 0; i < PossibleCoordDeltas.Length; i++)
			{
				float dist = Math.Abs(PossibleCoordDeltas[i] - delta);
				if (dist < minDist || i == 0)
					minDist = dist;
				else
					return PossibleCoordDeltas[i];
			}
			return PossibleCoordDeltas[PossibleCoordDeltas.Length - 1];
		}
		#endregion

		#region Coordinate/Pixel Translation
		private PointF CoordsToPix(Coordinates coords, float zoom)
		{
			return CoordsToPix((float)coords.NS, (float)coords.EW, zoom);
		}

		private PointF CoordsToPix(float NS, float EW, float zoom)
		{
			return new PointF(
				(mMapSize / 2.0f + mOffset.X + EW * mPixPerClick - 1.0f) * zoom,
				(mMapSize / 2.0f + mOffset.Y - NS * mPixPerClick) * zoom);
		}

		private Coordinates PixToCoords(PointF pix, float zoom)
		{
			return PixToCoords(pix.X, pix.Y, zoom);
		}

		private Coordinates PixToCoords(float x, float y, float zoom)
		{
			return new Coordinates(
				-(y / zoom - mMapSize / 2.0f - mOffset.Y) / mPixPerClick,
				(x / zoom - mMapSize / 2.0f - mOffset.X + 1.0f) / mPixPerClick);
		}

		private bool IsCenteredOnPlayer(float zoom, Size clientSize)
		{
			if (PlayerInDungeon)
				return false;
			PointF player = CoordsToPix(mPlayerCoords, zoom);
			PointF center = new PointF(clientSize.Width / 2.0f, clientSize.Height / 2.0f);
			return Math.Abs(player.X - center.X) < 5.0f && Math.Abs(player.Y - center.Y) < 5.0f;
		}

		private bool CenterOnPix(float x, float y, float zoom)
		{
			float deltaX = (float)Math.Round(ClientSize.Width / 2.0f - x) / zoom;
			float deltaY = (float)Math.Round(ClientSize.Height / 2.0f - y) / zoom;

			if (deltaX != 0 || deltaY != 0)
			{
				mOffset.X += deltaX;
				mOffset.Y += deltaY;
				mNeedsPlayerRecenter = false;
				return true;
			}
			return false;
		}

		private bool CenterOnPix(PointF centerOn, float zoom)
		{
			return CenterOnPix(centerOn.X, centerOn.Y, zoom);
		}

		private bool CenterOnCoords(Coordinates centerOn, float zoom)
		{
			return CenterOnPix(CoordsToPix(centerOn, zoom), zoom);
		}

		public void CenterOnCoords(Coordinates centerOn)
		{
			mCenterOnCoords = centerOn;
			RepaintAll();
		}
		#endregion
	}
}
