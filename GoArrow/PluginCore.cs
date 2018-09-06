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
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.IO;
using System.Xml;
using WindowsTimer = System.Windows.Forms.Timer;

using Decal.Adapter;
using Decal.Adapter.Wrappers;

using Decal.Interop.Input;

using ICSharpCode.SharpZipLib.Zip;

using GoArrow.Huds;
using GoArrow.RouteFinding;

namespace GoArrow
{
	[FriendlyName("GoArrow")]
	public partial class PluginCore : PluginBase
	{
		const string AttachArrowCommand = "GoArrow_AttachArrow";
		const string SelectItemCommand = "GoArrow_SelectItem";

		internal HudManager mHudManager;
		internal MapHud mMapHud;
		internal DungeonHud mDungeonHud;
		internal ArrowHud mArrowHud;

		private ToolbarHud mToolbar;
		private ToolbarButton mMainViewToolButton;
		private ToolbarButton mArrowToolButton;
		private ToolbarButton mDerethToolButton;
		private ToolbarButton mDungeonToolButton;

		private LocationDatabase mLocDb;

		private bool mLoggedIn = false;
		private bool mLoginCompleted = false;
		private bool mInitFinished = false;

		private int mMonarchId = 0;
		private string mMonarchName = "";

		private int mLastSpellId;
		private int mLastSpellTarget;

		private SortedDictionary<string, RouteStart> mStartLocations;
		private SortedDictionary<string, PortalDevice> mPortalDevices;

		private enum RecallStep { NotRecalling, RecallStarted, EnteredPortal };
		private RecallStep mRecallingToLSBind;
		private RecallStep mRecallingToLSTie;
		private RecallStep mRecallingToBindstone;
		private RecallStep mRecallingToPrimaryPortal;
		private RecallStep mRecallingToSecondaryPortal;
		private enum IdStep { Idle, Requested };
		private IdStep mIdPrimaryTie;
		private IdStep mIdSecondaryTie;
		private WindowsTimer mRecallTimeout;
		private Coordinates mHouseCoords = Coordinates.NO_COORDINATES;

		private WindowsTimer mDatabaseReminderDelay;
		private DateTime mLoginTime;

		#region Startup and Shutdown
		protected override void Startup()
		{
			try
			{
                MyClasses.MetaViewWrappers.MVWireupHelper.WireupStart(this, Host);

                //Wire up base events
                ServerDispatch += new EventHandler<NetworkMessageEventArgs>(PluginCore_ServerDispatch);
                Core.CharacterFilter.LoginComplete += new EventHandler(CharacterFilter_LoginComplete);
                Core.CharacterFilter.Logoff += new EventHandler<LogoffEventArgs>(CharacterFilter_Logoff);
                Core.PluginInitComplete += new EventHandler<EventArgs>(Core_PluginInitComplete);
                Core.ChatBoxMessage += new EventHandler<ChatTextInterceptEventArgs>(ChatLinkHandler);
                Core.ChatNameClicked += new EventHandler<ChatClickInterceptEventArgs>(Core_ChatNameClicked);
                Core.CommandLineText += new EventHandler<ChatParserInterceptEventArgs>(ChatCommandHandler);
                Core.CharacterFilter.SpellCast += new EventHandler<SpellCastEventArgs>(CharacterFilter_SpellCast);
                Core.CharacterFilter.Death += new EventHandler<DeathEventArgs>(CharacterFilter_Death);
                Core.CommandLineText += new EventHandler<ChatParserInterceptEventArgs>(RecallChatCommandHandler);
                Core.ChatBoxMessage += new EventHandler<ChatTextInterceptEventArgs>(RecallChatTextHandler);
                Core.WorldFilter.ChangeObject += new EventHandler<ChangeObjectEventArgs>(WorldFilter_ChangeObject);
                Core.WorldFilter.CreateObject += new EventHandler<CreateObjectEventArgs>(WorldFilter_CreateObject);
                Core.CharacterFilter.ChangePortalMode += new EventHandler<ChangePortalModeEventArgs>(CharacterFilter_ChangePortalMode);
                //*******************

				Util.Initialize("GoArrow", Host, Core, base.Path);
				System.Threading.Thread.CurrentThread.CurrentCulture
					= new System.Globalization.CultureInfo("en-US", false);

				mSettingsLoaded = false;
				mLoggedIn = false;
				mLoginCompleted = false;
                LoadSettings();
				FileInfo locationsFile = new FileInfo(Util.FullPath("locations.xml"));
				if (locationsFile.Exists)
				{
					mLocDb = new LocationDatabase(locationsFile.FullName);
				}
				else
				{
					// Load from resource
					XmlDocument locDoc = new XmlDocument();
					locDoc.LoadXml(RouteFinding.Data.LocationsDatabase);
					mLocDb = new LocationDatabase(locDoc);
				}

				mLastSpellId = 0;
				mLastSpellTarget = 0;
				mRecallingToLSBind = RecallStep.NotRecalling;
				mRecallingToLSTie = RecallStep.NotRecalling;
				mRecallingToBindstone = RecallStep.NotRecalling;

				mHudManager = new HudManager(Host, Core, MyClasses.MetaViewWrappers.MVWireupHelper.GetDefaultView(this), delegate() { return mDefaultViewActive; }, false);
				mHudManager.ExceptionHandler += new EventHandler<ExceptionEventArgs>(HudManager_ExceptionHandler);
				GraphicsReset += new EventHandler(mHudManager.GraphicsReset);
				WindowMessage += new EventHandler<WindowMessageEventArgs>(mHudManager.DispatchWindowMessage);
				RegionChange3D += new EventHandler<RegionChange3DEventArgs>(mHudManager.DispatchRegionChange3D);

				mDungeonHud = new DungeonHud(mHudManager);

				mMapHud = new MapHud(mHudManager, this, mLocDb);

				mArrowHud = new ArrowHud(mHudManager);
				mArrowHud.AsyncLoadComplete += new RunWorkerCompletedEventHandler(ArrowHud_AsyncLoadComplete);
				mArrowHud.DestinationChanged += new EventHandler<DestinationChangedEventArgs>(mMapHud.ArrowHud_DestinationChanged);

				mToolbar = new ToolbarHud(mHudManager);
				mMainViewToolButton = mToolbar.AddButton(new ToolbarButton(Icons.Toolbar.Settings, "Settings"));
				mArrowToolButton = mToolbar.AddButton(new ToolbarButton(Icons.Toolbar.SimpleArrow, "Arrow"));
				mDerethToolButton = mToolbar.AddButton(new ToolbarButton(Icons.Toolbar.Dereth, "Dereth"));
				mDungeonToolButton = mToolbar.AddButton(new ToolbarButton(Icons.Toolbar.Dungeon, "Dungeon"));
				mMainViewToolButton.Click += new EventHandler(MainViewToolButton_Click);
				mArrowToolButton.Click += new EventHandler(ArrowToolButton_Click);
				mDerethToolButton.Click += new EventHandler(DerethToolButton_Click);
				mDungeonToolButton.Click += new EventHandler(DungeonToolButton_Click);

				mStartLocations = new SortedDictionary<string, RouteStart>(StringComparer.OrdinalIgnoreCase);

				// Load portal devices
				// Try to load from file. If that fails, load from resource
				XmlDocument portalDevicesDoc = new XmlDocument();
				string portalDevicesPath = Util.FullPath("PortalDevices.xml");
				if (File.Exists(portalDevicesPath))
				{
					portalDevicesDoc.Load(portalDevicesPath);
				}
				else
				{
					portalDevicesDoc.LoadXml(RouteFinding.Data.PortalDevices);
				}

				mPortalDevices = new SortedDictionary<string, PortalDevice>(StringComparer.OrdinalIgnoreCase);
				foreach (XmlElement portalDeviceEle in portalDevicesDoc.DocumentElement.GetElementsByTagName("device"))
				{
					PortalDevice device;
					if (PortalDevice.TryLoadXmlDefinition(portalDeviceEle, out device))
					{
						mPortalDevices[device.Name] = device;
					}
				}

				InitMainViewBeforeSettings();

				mRecallTimeout = new WindowsTimer();
				mRecallTimeout.Tick += new EventHandler(RecallTimeout_Tick);

				mLoginTime = DateTime.Now;

#if USING_D3D_CONTAINER
				RouteStart.Initialize(110011, "Digero", 220022, "DaBug", "DebugAccount");
				LoadSettings();
				InitMainViewAfterSettings();

				mHudManager.StartHeartbeat();
#endif

                VVSHudBarButtons_HandleToManagedHud.Clear();
                VVSHudBarButtons_HandleToVVSButtonInfo.Clear();
                VVSHudBarButtons_ManagedHudToHandle.Clear();

                //Do startup stuff that can only be called when the VVS assembly is loaded
                if (MyClasses.MetaViewWrappers.ViewSystemSelector.VirindiViewsPresent(Host, new Version("1.0.0.39")))
                    Curtain_VVSEnabledStartup();
                else
                    VVSEnabled = false;

			}
            catch (Exception ex) { System.Windows.Forms.MessageBox.Show(ex.ToString()); Util.HandleException(ex); }
		}

		protected override void Shutdown()
		{
			try
			{
				mLoggedIn = false;
				mLoginCompleted = false;

				SaveSettings();

                //Shutdown VVS-only functions
                if (VVSEnabled)
                    Curtain_VVSEnabledShutdown();

                //Unwire up base events
                ServerDispatch -= new EventHandler<NetworkMessageEventArgs>(PluginCore_ServerDispatch);
                Core.CharacterFilter.LoginComplete -= new EventHandler(CharacterFilter_LoginComplete);
                Core.CharacterFilter.Logoff -= new EventHandler<LogoffEventArgs>(CharacterFilter_Logoff);
                Core.PluginInitComplete -= new EventHandler<EventArgs>(Core_PluginInitComplete);
                Core.ChatBoxMessage -= new EventHandler<ChatTextInterceptEventArgs>(ChatLinkHandler);
                Core.ChatNameClicked -= new EventHandler<ChatClickInterceptEventArgs>(Core_ChatNameClicked);
                Core.CommandLineText -= new EventHandler<ChatParserInterceptEventArgs>(ChatCommandHandler);
                Core.CharacterFilter.SpellCast -= new EventHandler<SpellCastEventArgs>(CharacterFilter_SpellCast);
                Core.CharacterFilter.Death -= new EventHandler<DeathEventArgs>(CharacterFilter_Death);
                Core.CommandLineText -= new EventHandler<ChatParserInterceptEventArgs>(RecallChatCommandHandler);
                Core.ChatBoxMessage -= new EventHandler<ChatTextInterceptEventArgs>(RecallChatTextHandler);
                Core.WorldFilter.ChangeObject -= new EventHandler<ChangeObjectEventArgs>(WorldFilter_ChangeObject);
                Core.WorldFilter.CreateObject -= new EventHandler<CreateObjectEventArgs>(WorldFilter_CreateObject);
                Core.CharacterFilter.ChangePortalMode -= new EventHandler<ChangePortalModeEventArgs>(CharacterFilter_ChangePortalMode);
                //*******************

				Util.Dispose();
				DisposeMainView();

				if (mHudManager != null)
				{
					GraphicsReset -= mHudManager.GraphicsReset;
					WindowMessage -= mHudManager.DispatchWindowMessage;
					RegionChange3D -= mHudManager.DispatchRegionChange3D;
					mHudManager.Dispose();
					mHudManager = null;
				}

				mArrowHud = null;
				mMapHud = null;
				mDungeonHud = null;

				if (mRecallTimeout != null)
				{
					mRecallTimeout.Dispose();
					mRecallTimeout = null;
				}

				if (mDatabaseReminderDelay != null)
				{
					mDatabaseReminderDelay.Dispose();
					mDatabaseReminderDelay = null;
				}

				if (Core.HotkeySystem != null)
				{
					Core.HotkeySystem.Hotkey -= HotkeySystem_Hotkey;
				}

				mLocDb.Dispose();
				mLocDb = null;



                MyClasses.MetaViewWrappers.MVWireupHelper.WireupEnd(this);
			}
            catch (Exception ex) { System.Windows.Forms.MessageBox.Show(ex.ToString()); Util.HandleException(ex); }
		}

		private void PluginCore_ServerDispatch(object sender, NetworkMessageEventArgs e)
		{
			try
			{
				// Game Event
				if (e.Message.Type == 0xF7B0)
				{
					int eventId;
					try { eventId = e.Message.Value<int>("event"); }
					catch (ArgumentOutOfRangeException) { return; }

					// Allegiance Info
					if (eventId == 0x0020)
					{
						MessageStruct records = e.Message.Struct("records");
						if (records.Count == 0)
						{
							MonarchId = 0;
							MonarchName = "";
						}
						else
						{
							// Monarch info is always the first record, whether this character 
							// is monarch, direct vassal to the monarch, or just a peon.
							MessageStruct monarch = records.Struct(0);
							MonarchId = monarch.Value<int>("character");
							MonarchName = monarch.Value<string>("name");
						}

						if (!mInitFinished)
						{
							FinishInitializing();
						}
					}

					// House Information for Owners
					else if (eventId == 0x0225)
					{
						MessageStruct position = e.Message.Struct("position");
						mHouseCoords = new Coordinates(position.Value<int>("landcell"),
							position.Value<float>("y"), position.Value<float>("x"), 1);

						if (mLoginCompleted && chkAutoUpdateRecalls.Checked)
						{
							// Don't notify if the user logged in less than 1 minute ago
							// If the user logged in more than 1 minute ago, chances are
							// they're purchasing a house.
							bool quiet = (DateTime.Now - mLoginTime) < TimeSpan.FromMinutes(1);
							UpdateStartLocation(mHouseCoords, RouteStartType.HouseRecall, quiet);
						}
					}
				}
			}
			catch (Exception ex) { Util.HandleException(ex); }
		}

		private void CharacterFilter_LoginComplete(object sender, EventArgs e)
		{
			try
			{
				if (!mInitFinished)
				{
					FinishInitializing();
				}

				mLoginTime = DateTime.Now;

				// Update house recall; coords obtained from the House Information game event
				if (chkAutoUpdateRecalls.Checked)
				{
					UpdateStartLocation(mHouseCoords, RouteStartType.HouseRecall, true);
				}

				if (!mLoginCompleted)
				{
					mLoginCompleted = true;
					mHudManager.StartHeartbeat();
					mHudManager.GraphicsReset(null, null);
				}
			}
			catch (Exception ex) { Util.HandleException(ex); }
		}

		private void FinishInitializing()
		{
			mInitFinished = true;
			mLoggedIn = true;
			LoadSettings();
			InitMainViewAfterSettings();
			PortalDevice.MansionRunDistance = GetStartLocationByType(RouteStartType.MansionRecall).RunDistance;

			if (chkUpdateRemind.Checked && (DateTime.Now - mLocDb.LastUpdate).TotalDays > 30)
			{
				mDatabaseReminderDelay = new WindowsTimer();
				mDatabaseReminderDelay.Interval = 10000;
				mDatabaseReminderDelay.Tick += new EventHandler(DatabaseReminderDelay_Tick);
				mDatabaseReminderDelay.Start();
			}
		}

		private void CharacterFilter_Logoff(object sender, LogoffEventArgs e)
		{
			try
			{
				if (Core.HotkeySystem != null)
				{
					Core.HotkeySystem.Hotkey -= HotkeySystem_Hotkey;
				}
				mLoggedIn = false;
			}
			catch (Exception ex) { Util.HandleException(ex); }
		}

		private void ArrowHud_AsyncLoadComplete(object sender, RunWorkerCompletedEventArgs e)
		{
			try
			{
				if (e.Error != null)
				{
					Util.HandleException(e.Error);
				}
				else if (e.Cancelled)
				{
					if (e.Result != null)
					{
						Util.Error(e.Result.ToString());
					}
				}
			}
			catch (Exception ex) { Util.HandleException(ex); }
		}
		#endregion

		#region DHS, Map, and Chat Name commands
		private void Core_PluginInitComplete(object sender, EventArgs e)
		{
			try
			{
				if (Core.HotkeySystem != null)
				{
					CheckHotkey("GA:GUI", "GoArrow: Shows or hides the main GoArrow GUI");
					CheckHotkey("GA:ArrowHUD", "GoArrow: Shows or hides the Arrow HUD");
					CheckHotkey("GA:DerethMap", "GoArrow: Shows or hides the Dereth Map");
					CheckHotkey("GA:DungeonMap", "GoArrow: Shows or hides the Dungeon Map");
					CheckHotkey("GA:AutoMap", "GoArrow: Auto selects Dungeon or Dereth Map");
					CheckHotkey("GA:FaceDest", "GoArrow: Faces character towards arrow destination");
					CheckHotkey("GA:Attach", "GoArrow: Attach the arrow to your current selection");
					Core.HotkeySystem.Hotkey += new EventHandler<HotkeyEventArgs>(HotkeySystem_Hotkey);
				}
			}
			catch (Exception ex) { Util.HandleException(ex); }
		}

		private void CheckHotkey(string name, string descr)
		{
			if (!Core.HotkeySystem.Exists(name))
			{
				Core.HotkeySystem.AddHotkey("GoArrow", name, descr);
			}
		}

		private void HotkeySystem_Hotkey(object sender, HotkeyEventArgs e)
		{
			try
			{
				if (!mLoggedIn)
					return;
				switch (e.Title)
				{
					case "GA:GUI":
						if (mDefaultViewActive)
							MyClasses.MetaViewWrappers.MVWireupHelper.GetDefaultView(this).Deactivate();
						else
							MyClasses.MetaViewWrappers.MVWireupHelper.GetDefaultView(this).Activate();
						e.Eat = true;
						break;
					case "GA:ArrowHUD":
						ShowHideArrow(!mArrowHud.Visible);
						e.Eat = true;
						break;
					case "GA:DerethMap":
						mMapHud.Visible = !mMapHud.Visible;
						e.Eat = true;
						break;
					case "GA:DungeonMap":
						mDungeonHud.Visible = !mDungeonHud.Visible;
						e.Eat = true;
						break;
					case "GA:AutoMap":
						if (mDungeonHud.IsDungeon(Host.Actions.Landcell))
						{
							mMapHud.Visible = false;
							mDungeonHud.Visible = !mDungeonHud.Visible;
						}
						else
						{
							mMapHud.Visible = !mMapHud.Visible;
							mDungeonHud.Visible = false;
						}
						e.Eat = true;
						break;
					case "GA:FaceDest":
						double angle = PlayerCoords.AngleTo(mArrowHud.DestinationCoords);
						while (angle < 0)
							angle += 2 * Math.PI;
						while (angle > 2 * Math.PI)
							angle -= 2 * Math.PI;

						Host.Actions.SetAutorun(false);
						Host.Actions.FaceHeading(angle * 180 / Math.PI, true);
						e.Eat = true;
						break;
					case "GA:Attach":
						AttachCommand(Host.Actions.CurrentSelection, true);
						e.Eat = true;
						break;
				}
			}
			catch (Exception ex) { Util.HandleException(ex); }
		}

		private void MainViewToolButton_Click(object sender, EventArgs e)
		{
			MyClasses.MetaViewWrappers.MVWireupHelper.GetDefaultView(this).Activated = !mDefaultViewActive;
		}

		private void ArrowToolButton_Click(object sender, EventArgs e)
		{
			mArrowHud.Visible = !mArrowHud.Visible;
		}

		private void DerethToolButton_Click(object sender, EventArgs e)
		{
			mMapHud.Visible = !mMapHud.Visible;
		}

		private void DungeonToolButton_Click(object sender, EventArgs e)
		{
			mDungeonHud.Visible = !mDungeonHud.Visible;
		}

		private void ChatLinkHandler(object sender, ChatTextInterceptEventArgs e)
		{
			try
			{
				if (chkLinkCoords.Checked)
				{
					MatchCollection matches = Coordinates.FindAllCoords(e.Text);
					if (matches.Count > 0)
					{
						string replacementText = e.Text;
						for (int i = matches.Count - 1; i >= 0; i--)
						{
							Match m = matches[i];

							// Workaround for a "bug" in AC where, if two links are right next to 
							// each other (w/out space), the second will not be parsed into a link 
							// and the markup will be displayed
							string spacer = "";
							if (i > 0 && (matches[i - 1].Index + matches[i - 1].Length) == m.Index)
								spacer = " ";

							//<Tell:IIDString:1342670765:Digero>Digero<\Tell>
							replacementText = replacementText.Substring(0, m.Index) + spacer
								+ MakeCoordsChatLink(m.Value) + replacementText.Substring(m.Index + m.Length);
						}
						e.Eat = true;
						Host.Actions.AddChatText(replacementText, e.Color);
					}
				}
			}
			catch (Exception ex) { Util.HandleException(ex); }
		}

		private void Core_ChatNameClicked(object sender, ChatClickInterceptEventArgs e)
		{
			try
			{
				if (e.Id == 110011)
				{
					if (e.Text == "GoArrow_Example")
					{
						Util.Message("That was just an example.");
						e.Eat = true;
					}
					else
					{
						Coordinates coords;
						if (Coordinates.TryParse(e.Text, out coords))
						{
							e.Eat = true;
							if (Util.IsControlDown())
							{
								SetRouteEnd(coords);
							}
							else if (Util.IsShiftDown())
							{
								SetRouteStart(coords);
							}
							else
							{
								Location loc = mLocDb.GetLocationAt(coords);
								if (loc != null)
									mArrowHud.DestinationLocation = loc;
								else
									mArrowHud.DestinationCoords = coords;
								mArrowHud.Visible = true;
							}
						}
					}
				}
				else if (e.Text == SelectItemCommand)
				{
					Host.Actions.SelectItem(e.Id);
					e.Eat = true;
				}
				else if (e.Text == AttachArrowCommand)
				{
					AttachCommand(e.Id, false);
					e.Eat = true;
				}

				Util.HandleChatCommand(sender, e);
			}
			catch (Exception ex) { Util.HandleException(ex); }
		}

		public string MakeCoordsChatLink(Coordinates coords)
		{
			return MakeCoordsChatLink(coords.ToString());
		}

		public string MakeCoordsChatLink(string coords)
		{
			return "<Tell:IIDString:110011:" + coords + ">" + coords + @"<\Tell>";
		}

		public string MakeAttachArrowChatLink(int objectId, string text)
		{
			return "<Tell:IIDString:" + objectId + ":" + AttachArrowCommand + ">" + text + @"<\Tell>";
		}

		private string MakeSelectItemChatLink(int objectId, string text)
		{
			return "<Tell:IIDString:" + objectId + ":" + SelectItemCommand + ">" + text + @"<\Tell>";
		}
		#endregion

		#region Chat Commands and Help
		private void ShowHelp()
		{
			string cmdTrim = "/" + edtChatCommand.Text.ToLower();
			string cmd = "    " + cmdTrim;
			string msg = "You're using " + Util.PluginNameVer + ". Here are the commands available:\n"
				+ cmd + " [arrow] [on|off]  -  Show or hide the arrow\n"
				+ cmd + " map [on|off]  -  Show or hide either the dereth or dungeon map, depending if you're on the landscape or in a dungeon\n"
				+ cmd + " dereth [on|off]  -  Show or hide the dereth map\n"
				+ cmd + " dungeon [on|off]  -  Show or hide the dungeon map\n"
				+ cmd + " toolbar [on|off]  -  Show or hide the toolbar\n"
				+ cmd + " show|hide arrow|map|dereth|dungeon|all  -  Show or hide the specified hud(s)\n"
				+ cmd + " to (coords)|(location)|here  -  Set the arrow's destination coordinates (use \"here\" for your current coords)\n"
				+ cmd + " start (coords)|(location)|here  -  Set the route start coordinates\n"
				+ cmd + " end (coords)|(location)|here  -  Set the route end coordinates\n"
				+ cmd + " search (location)  -  Search the database for a location.\n"
				+ cmd + " tag  -  Attach the arrow to your current selection.\n"
				+ cmd + " loc (target)  -  Send your current coordinates to (target)\n"
				+ cmd + " dest (target)  -  Send the arrow's destination coordinates to (target)\n"
				+ cmd + " find (name)  -  Find (and select) an object by name.\n"
				+ cmd + " reset  -  Move the HUDs and GUI to their default positions on the screen\n";

			if (chkEnableCoordsCommand.Checked)
				msg += "    /" + edtCoordsCommand.Text + " (target)  -  Same as " + cmdTrim + " loc (target)\n";

			if (chkEnableDestCommand.Checked)
				msg += "    /" + edtDestCommand.Text + " (target)  -  Same as " + cmdTrim + " dest (target)\n";

			if (chkEnableFindCommand.Checked)
				msg += "    /" + edtFindCommand.Text + " (name)  -  Same as " + cmdTrim + " find (name)\n";

			msg += cmd + " help loc  -  Show help for the " + cmdTrim + " loc and " + cmdTrim + " dest commands";
			Util.AddChatText(msg, Util.HelpColor);
		}

		private void ShowLocCommandHelp(bool isLocCmd)
		{
			string cmd, yourCoords, coordsStr;
			if (isLocCmd)
			{
				cmd = "/" + edtChatCommand.Text.ToLower() + " loc";
				yourCoords = "your current coords";
				coordsStr = PlayerCoords.ToString("0.0");
			}
			else
			{
				cmd = "/" + edtChatCommand.Text.ToLower() + " dest";
				yourCoords = "the arrow's destination coords";
				coordsStr = mArrowHud.DestinationCoords.ToString();
			}
			Util.HelpMessage("The " + cmd + " command sends " + yourCoords + " to a specified target. The target "
				+ "can be any chat channel, such as fellowship (f), local chat (say), tell (t [name]), etc.\n"
				+ "You can include an optional message after the target.  If the message contains %c, that "
				+ "will be replaced with your coords; otherwise your coords will be at the end of the message.");
			Util.HelpMessage("Here are some examples of using " + cmd + "\n"
				+ "    \"" + cmd + " say\"  =>  You say, \"" + coordsStr + "\"\n"
				+ "    \"" + cmd + " t Digero, I'm at\"  =>  You tell Digero, \"I'm at " + coordsStr + "\"\n"
				+ "    \"" + cmd + " a Come to %c for the quest\"  =>  [Allegiance] "
				+ Core.CharacterFilter.Name + " says, \"Come to " + coordsStr + " for the quest\"");
		}

		private void ChatCommandHandler(object sender, ChatParserInterceptEventArgs e)
		{
			try
			{
				string text = e.Text.ToLower();
				string chatCommand = edtChatCommand.Text.ToLower();
				string coordsCommand = edtCoordsCommand.Text.ToLower();
				string destCommand = edtDestCommand.Text.ToLower();
				string findCommand = edtFindCommand.Text.ToLower();

				if (text.StartsWith("@" + chatCommand) || text.StartsWith("/" + chatCommand))
				{
					if (text.Length == 1 + chatCommand.Length)
					{
						e.Eat = true;
						ShowHelp();
						return;
					}

					if (!char.IsWhiteSpace(text[1 + chatCommand.Length]))
						return;

					e.Eat = true;
					text = text.Substring(1 + chatCommand.Length).Trim();
					string arg = "";
					int idx = text.IndexOf(' ');
					if (idx > 0)
					{
						arg = text.Substring(idx).Trim();
						text = text.Substring(0, idx).Trim();
					}

					if (arg == "[coords]")
					{
						Util.Error("You're not actually supposed to type [coords] ... "
							+ "replace that with coordinates or \"here\"");
						return;
					}

					Coordinates coords = PlayerCoords;
					Location loc;
					switch (text)
					{
						case "":
						case "help":
							if (arg == "loc" || arg == "dest")
								ShowLocCommandHelp(arg == "loc");
							else
								ShowHelp();
							break;
						case "on/off":
						case "onoff":
						case "show/hide":
						case "showhide":
						case "toggle":
							if (arg == "" || arg == "arrow")
							{
								ShowHideArrow(!mArrowHud.Visible);
							}
							else if (arg == "map" || arg == "auto")
							{
								if (mDungeonHud.IsDungeon(Host.Actions.Landcell))
								{
									mMapHud.Visible = false;
									mDungeonHud.Visible = !mDungeonHud.Visible;
								}
								else
								{
									mMapHud.Visible = !mMapHud.Visible;
									mDungeonHud.Visible = false;
								}
							}
							else if (arg == "dereth" || arg == "derethmap" || arg == "dereth map")
							{
								mMapHud.Visible = !mMapHud.Visible;
							}
							else if (arg == "dungeon" || arg == "dung" || arg == "dungeonmap" || arg == "dungeon map")
							{
								mDungeonHud.Visible = !mDungeonHud.Visible;
							}
							else if (arg == "toolbar" || arg == "tool" || arg == "tool bar")
							{
								mToolbar.Visible = !mToolbar.Visible;
							}
							else if (arg == "all")
							{
								ShowHideArrow(!mArrowHud.Visible);
								mDungeonHud.Visible = !mDungeonHud.Visible;
								mMapHud.Visible = !mMapHud.Visible;
								mToolbar.Visible = !mToolbar.Visible;
							}
							else
							{
								Util.Error("Valid values are: 'arrow', 'map', 'dereth', 'dungeon', 'toolbar', and 'all'");
							}
							break;
						case "on":
						case "show":
							if (arg == "" || arg == "arrow")
							{
								ShowHideArrow(true);
							}
							else if (arg == "map" || arg == "auto")
							{
								if (mDungeonHud.IsDungeon(Host.Actions.Landcell))
								{
									mMapHud.Visible = false;
									mDungeonHud.Visible = true;
								}
								else
								{
									mMapHud.Visible = true;
									mDungeonHud.Visible = false;
								}
							}
							else if (arg == "dereth" || arg == "derethmap" || arg == "dereth map")
							{
								mMapHud.Visible = true;
							}
							else if (arg == "dungeon" || arg == "dung" || arg == "dungeonmap" || arg == "dungeon map")
							{
								mDungeonHud.Visible = true;
							}
							else if (arg == "toolbar" || arg == "tool" || arg == "tool bar")
							{
								mToolbar.Visible = true;
							}
							else if (arg == "all")
							{
								ShowHideArrow(true);
								if (mDungeonHud.IsDungeon(Host.Actions.Landcell))
								{
									mMapHud.Visible = true;
									mDungeonHud.Visible = true;
								}
								else
								{
									mDungeonHud.Visible = true;
									mMapHud.Visible = true;
								}
								mToolbar.Visible = true;
							}
							else
							{
								Util.Error("Valid values are: 'arrow', 'map', 'dereth', 'dungeon', 'toolbar', and 'all'");
							}
							break;
						case "off":
						case "hide":
							if (arg == "" || arg == "arrow")
							{
								ShowHideArrow(false);
							}
							else if (arg == "map" || arg == "auto")
							{
								mMapHud.Visible = false;
								mDungeonHud.Visible = false;
							}
							else if (arg == "dereth" || arg == "derethmap" || arg == "dereth map")
							{
								mMapHud.Visible = false;
							}
							else if (arg == "dungeon" || arg == "dung" || arg == "dungeonmap" || arg == "dungeon map")
							{
								mDungeonHud.Visible = false;
							}
							else if (arg == "toolbar" || arg == "tool" || arg == "tool bar")
							{
								mToolbar.Visible = false;
							}
							else if (arg == "all")
							{
								ShowHideArrow(false);
								mMapHud.Visible = false;
								mDungeonHud.Visible = false;
								mToolbar.Visible = false;
							}
							else
							{
								Util.Error("Valid values are: 'arrow', 'map', 'dereth', 'dungeon', 'toolbar', and 'all'");
							}
							break;
						case "map":
							if (arg == "on" || arg == "show")
							{
								if (mDungeonHud.IsDungeon(Host.Actions.Landcell))
								{
									mMapHud.Visible = false;
									mDungeonHud.Visible = true;
								}
								else
								{
									mMapHud.Visible = true;
									mDungeonHud.Visible = false;
								}
							}
							else if (arg == "off" || arg == "hide")
							{
								mMapHud.Visible = false;
								mDungeonHud.Visible = false;
							}
							else if (arg == "" || arg == "toggle")
							{
								if (mDungeonHud.IsDungeon(Host.Actions.Landcell))
								{
									mMapHud.Visible = false;
									mDungeonHud.Visible = !mDungeonHud.Visible;
								}
								else
								{
									mMapHud.Visible = !mMapHud.Visible;
									mDungeonHud.Visible = false;
								}
							}
							else { goto invalidCommand; }
							break;
						case "arrow":
							if (arg == "on" || arg == "show")
							{
								ShowHideArrow(true);
							}
							else if (arg == "off" || arg == "hide")
							{
								ShowHideArrow(false);
							}
							else if (arg == "" || arg == "toggle")
							{
								ShowHideArrow(!mArrowHud.Visible);
							}
							else { goto invalidCommand; }
							break;
						case "dereth":
							if (arg == "on" || arg == "show")
							{
								mMapHud.Visible = true;
							}
							else if (arg == "off" || arg == "hide")
							{
								mMapHud.Visible = false;
							}
							else if (arg == "" || arg == "toggle")
							{
								mMapHud.Visible = !mMapHud.Visible;
							}
							else { goto invalidCommand; }
							break;
						case "dungeon":
						case "dung":
							if (arg == "on" || arg == "show")
							{
								mDungeonHud.Visible = true;
							}
							else if (arg == "off" || arg == "hide")
							{
								mDungeonHud.Visible = false;
							}
							else if (arg == "" || arg == "toggle")
							{
								mDungeonHud.Visible = !mDungeonHud.Visible;
							}
							else { goto invalidCommand; }
							break;
						case "toolbar":
						case "tool":
							if (arg == "on" || arg == "show")
							{
								mToolbar.Visible = true;
							}
							else if (arg == "off" || arg == "hide")
							{
								mToolbar.Visible = false;
							}
							else if (arg == "" || arg == "toggle")
							{
								mToolbar.Visible = !mToolbar.Visible;
							}
							else { goto invalidCommand; }
							break;
						case "to":
							if (arg == "here" || arg == "\"here\"" || Coordinates.TryParse(arg, out coords))
							{
								loc = mLocDb.GetLocationAt(coords);
								if (loc != null)
									mArrowHud.DestinationLocation = loc;
								else
									mArrowHud.DestinationCoords = coords;
								mArrowHud.Visible = true;
								//SaveSettings();
							}
							else if (mLocDb.TryGet(arg, out loc))
							{
								mArrowHud.DestinationLocation = loc;
								mArrowHud.Visible = true;
							}
							else
							{
								Util.Error("Invalid coordinates or unknown location name. Coordinates "
									+ "must be in the form \"00.0N, 00.0E\" and location names must "
									+ "match the name in the database exactly.");
							}
							break;
						case "start":
						case "from":
							if (arg == "here" || Coordinates.TryParse(arg, out coords))
							{
								SetRouteStart(coords);
							}
							else if (mLocDb.TryGet(arg, out loc))
							{
								SetRouteStart(loc);
							}
							else
							{
								Util.Error("Invalid coordinates or unknown location name. Coordinates "
									+ "must be in the form \"00.0N, 00.0E\" and location names must "
									+ "match the name in the database exactly.");
							}
							break;
						case "end":
							if (arg == "here" || Coordinates.TryParse(arg, out coords))
							{
								SetRouteEnd(coords);
							}
							else if (mLocDb.TryGet(arg, out loc))
							{
								SetRouteEnd(loc);
							}
							else
							{
								Util.Error("Invalid coordinates or unknown location name. Coordinates "
									+ "must be in the form \"00.0N, 00.0E\" and location names must "
									+ "match the name in the database exactly.");
							}
							break;
						case "reset":
							SetViewLocation(new Point(40, 75));
							ResetHudPositions(true);
							break;
						case "lock":
							mArrowHud.PositionLocked = true;
							Util.Message("Arrow HUD position locked");
							break;
						case "unlock":
							mArrowHud.PositionLocked = false;
							Util.Message("Arrow HUD position unlocked");
							break;
						case "loc":
							idx = e.Text.IndexOf(text, 1 + chatCommand.Length, StringComparison.OrdinalIgnoreCase);
							SendCoordinates(e.Text.Substring(idx + text.Length), PlayerCoords.ToString("0.0"));
							break;
						case "dest":
							idx = e.Text.IndexOf(text, 1 + chatCommand.Length, StringComparison.OrdinalIgnoreCase);
							SendCoordinates(e.Text.Substring(idx + text.Length), ArrowDestinationDescription);
							break;
						case "search":
							if (arg != "")
							{
								edtSearchName.Text = arg;
								chkSearchName.Checked = true;
								edtSearchCoords.Text = "";
								chkSearchNearby.Checked = false;
								choSearchLimit.Selected = 0;
								btnSearchGo_Click(null, null);
							}
							nbkMain.ActiveTab = MainTab.Atlas;
							nbkAtlas.ActiveTab = AtlasTab.Search;
							MyClasses.MetaViewWrappers.MVWireupHelper.GetDefaultView(this).Activate();
							break;
						case "find":
							FindCommand(arg);
							break;
						case "attach":
						case "tag":
							AttachCommand(Host.Actions.CurrentSelection, true);
							break;
						default:
						invalidCommand:
							Util.Error("Invalid command: " + text + ". "
								+ "Type /" + chatCommand + " help for a list of available commands");
							break;
					}
				}
				else if (chkEnableCoordsCommand.Checked && (text.StartsWith("@" + coordsCommand + " ")
						|| text.StartsWith("/" + coordsCommand + " ")))
				{
					e.Eat = true;
					SendCoordinates(e.Text.Substring(1 + coordsCommand.Length), PlayerCoords.ToString("0.0"));
				}
				else if (chkEnableDestCommand.Checked && (text.StartsWith("@" + destCommand + " ")
						|| text.StartsWith("/" + destCommand + " ")))
				{
					e.Eat = true;
					SendCoordinates(e.Text.Substring(1 + destCommand.Length), ArrowDestinationDescription);
				}
				else if (chkEnableFindCommand.Checked && (text.StartsWith("@" + findCommand + " ")
						|| text.StartsWith("/" + findCommand + " ")))
				{
					e.Eat = true;
					FindCommand(e.Text.Substring(1 + findCommand.Length).Trim());
				}
			}
			catch (Exception ex) { Util.HandleException(ex); }
		}

		private void ResetHudPositions(bool verbose)
		{
			mArrowHud.ResetPosition();
			mMapHud.ResetPosition();
			mDungeonHud.ResetPosition();
			mToolbar.ResetPosition();
			if (verbose)
				Util.Message("The HUDs should now be in visible locations on the screen");
		}

		private string ArrowDestinationDescription
		{
			get
			{
				if (mArrowHud.HasDestinationLocation)
				{
					return mArrowHud.DestinationLocation.ToString();
				}
				else if (mArrowHud.HasDestinationObject)
				{
					return mArrowHud.DestinationObject.Name
						+ (mArrowHud.DestinationObject.IsValid ? "" : " (out of range)")
						+ " [" + mArrowHud.DestinationCoords + "]";
				}

				return mArrowHud.DestinationCoords.ToString();
			}
		}

		private void SendCoordinates(string arg, string coordsStr)
		{
			arg = arg.Replace("/", "").Replace("@", "").TrimStart();

			if ((arg.StartsWith("t", StringComparison.OrdinalIgnoreCase)
					|| arg.StartsWith("tell", StringComparison.OrdinalIgnoreCase))
					&& !arg.Contains(","))
			{
				arg = arg.TrimEnd() + ", " + coordsStr;
			}
			else if (arg.Contains("%c"))
			{
				arg = arg.Replace("%c", coordsStr);
			}
			else
			{
				arg = arg.TrimEnd() + " " + coordsStr;
			}

			Host.Actions.InvokeChatParser("/" + arg);
		}

		private void FindCommand(string arg)
		{
			arg = arg.ToLower();
			bool matched = false;
			foreach (Decal.Adapter.Wrappers.WorldObject obj in Core.WorldFilter.GetLandscape())
			{
				if (obj.Name.ToLower().Contains(arg))
				{
					if (!matched)
					{
						Host.Actions.SelectItem(obj.Id);
					}

					Coordinates coords = new Coordinates(obj.Coordinates());

					Util.Message(obj.Name + " ("
						+ MakeAttachArrowChatLink(obj.Id, coords.ToString("0.0")) + ") | "
						+ MakeSelectItemChatLink(obj.Id, "Select"));

					matched = true;
				}
			}
			if (!matched)
			{
				Util.Message("No matches found for '" + arg + "'");
			}
		}

		private bool AttachCommand(int objId, bool warnUserInvalid)
		{
			WorldObject obj = Core.WorldFilter[objId];
			if (obj == null)
			{
				if (warnUserInvalid)
					Util.Warning("Select something to attach the arrow to.");
				return false;
			}
			else
			{
				mArrowHud.DestinationObject = new GameObject(objId, obj.Name, obj.Icon, Host.Actions);
				mArrowHud.Visible = true;
				return true;
			}
		}
		#endregion

		#region Recall Detection
		private void CharacterFilter_SpellCast(object sender, SpellCastEventArgs e)
		{
			try
			{
				mLastSpellId = e.SpellId;
				mLastSpellTarget = e.TargetId;
				if (e.SpellId == Spells.PrimaryPortalRecall)
				{
					mRecallingToPrimaryPortal = RecallStep.RecallStarted;
					if (mRecallTimeout.Enabled)
						RecallTimeout_Tick(null, null);
					mRecallTimeout.Interval = 30000; // 30 seconds
					mRecallTimeout.Start();
				}
				else if (e.SpellId == Spells.SecondaryPortalRecall)
				{
					mRecallingToSecondaryPortal = RecallStep.RecallStarted;
					if (mRecallTimeout.Enabled)
						RecallTimeout_Tick(null, null);
					mRecallTimeout.Interval = 30000; // 30 seconds
					mRecallTimeout.Start();
				}
				else if (e.SpellId == Spells.LifestoneRecall)
				{
					mRecallingToLSTie = RecallStep.RecallStarted;
					if (mRecallTimeout.Enabled)
						RecallTimeout_Tick(null, null);
					mRecallTimeout.Interval = 30000; // 30 seconds
					mRecallTimeout.Start();
				}
			}
			catch (Exception ex) { Util.HandleException(ex); }
		}

		private void CharacterFilter_Death(object sender, DeathEventArgs e)
		{
			try
			{
				if (chkAutoUpdateRecalls.Checked)
				{
					mRecallingToLSBind = RecallStep.RecallStarted;
					if (mRecallTimeout.Enabled)
						RecallTimeout_Tick(null, null);
					mRecallTimeout.Interval = 30000; // 30 seconds
					mRecallTimeout.Start();
				}

				if (chkTrackCorpses.Checked)
				{
					string name = "Corpse of " + Core.CharacterFilter.Name + " [" + DateTime.Now.ToShortTimeString() + "]";
					string descr = "Corpse of " + Core.CharacterFilter.Name + "\n" + DateTime.Now.ToShortDateString() + " "
							+ DateTime.Now.ToShortTimeString() + "\n" + e.Text;

					string dungeonName = mDungeonHud.GetDungeonNameByLandblock(Host.Actions.Landcell);
					if (dungeonName != "")
					{
						name += " in " + dungeonName;
						descr += "\nIn Dungeon: " + dungeonName;
					}

					Location corpse = new Location(Location.GetNextInternalId(), name, LocationType.Custom, PlayerCoords, descr);
					corpse.Icon = AcIcons.Corpse;
					mArrowHud.DestinationLocation = corpse;
				}
			}
			catch (Exception ex) { Util.HandleException(ex); }
		}

		private void RecallChatCommandHandler(object sender, ChatParserInterceptEventArgs e)
		{
			try
			{
				if (chkAutoUpdateRecalls.Checked)
				{
					string text = e.Text.ToLower().TrimEnd();
					if (text == "/allegiance hometown")
					{
						mRecallingToBindstone = RecallStep.RecallStarted;
						if (mRecallTimeout.Enabled)
							RecallTimeout_Tick(null, null);
						mRecallTimeout.Interval = 40000; // 40 seconds
						mRecallTimeout.Start();
					}
					else if (text == "/lifestone")
					{
						mRecallingToLSBind = RecallStep.RecallStarted;
						if (mRecallTimeout.Enabled)
							RecallTimeout_Tick(null, null);
						mRecallTimeout.Interval = 40000; // 40 seconds
						mRecallTimeout.Start();
					}
				}
			}
			catch (Exception ex) { Util.HandleException(ex); }
		}

		private void RecallChatTextHandler(object sender, ChatTextInterceptEventArgs e)
		{
			try
			{
				if (chkAutoUpdateRecalls.Checked)
				{
					string text = e.Text.Trim();

					// Spell Casting
					if (e.Color == 7)
					{
						if (text == "You successfully link with the portal!" || text == "You successfully link with the lifestone!")
						{
							WorldObject spellTarget = Core.WorldFilter[mLastSpellTarget];
							if (spellTarget != null)
							{
								if (mLastSpellId == Spells.PrimaryPortalTie)
								{
									if (spellTarget.HasIdData)
									{
										ProcessPortalTie(spellTarget, RouteStartType.PrimaryPortalTie);
									}
									else
									{
										Host.Actions.RequestId(mLastSpellTarget);
										mIdPrimaryTie = IdStep.Requested;
										if (mRecallTimeout.Enabled)
											RecallTimeout_Tick(null, null);
										mRecallTimeout.Interval = 10000; // 10 seconds
										mRecallTimeout.Start();
									}
								}
								else if (mLastSpellId == Spells.SecondaryPortalTie)
								{
									if (spellTarget.HasIdData)
									{
										ProcessPortalTie(spellTarget, RouteStartType.SecondaryPortalTie);
									}
									else
									{
										Host.Actions.RequestId(mLastSpellTarget);
										mIdSecondaryTie = IdStep.Requested;
										if (mRecallTimeout.Enabled)
											RecallTimeout_Tick(null, null);
										mRecallTimeout.Interval = 10000; // 10 seconds
										mRecallTimeout.Start();
									}
								}
								else if (mLastSpellId == Spells.LifestoneTie)
								{
									UpdateStartLocation(spellTarget, RouteStartType.LifestoneTie);
								}
							}
						}
						else if (e.Text.StartsWith("You have attuned your spirit to this Lifestone."))
						{
							WorldObject lifestone = Core.WorldFilter[Host.Actions.CurrentSelection];
							Coordinates coords;
							RouteStart startLoc = GetStartLocationByType(RouteStartType.LifestoneBind);
							if (lifestone == null || lifestone.ObjectClass != ObjectClass.Lifestone)
							{
								coords = PlayerCoords;
							}
							else
							{
								coords = new Coordinates(lifestone.Coordinates(), 1);
							}
							if (startLoc.Coords != coords)
							{
								startLoc.Coords = coords;
								RefreshStartLocationListCoords();
								if (startLoc.Enabled)
									Util.Message(startLoc.Name + " start location set to " + startLoc.Coords);
							}
						}
					}

					// Recall Text
					else if (e.Color == 23)
					{
						string name = Core.CharacterFilter.Name;
						if (text == name + " is going to the Allegiance hometown.")
						{
							mRecallingToBindstone = RecallStep.RecallStarted;
							if (mRecallTimeout.Enabled)
								RecallTimeout_Tick(null, null);
							mRecallTimeout.Interval = 40000; // 40 seconds
							mRecallTimeout.Start();
						}
						else if (text == name + " is recalling to the lifestone.")
						{
							mRecallingToLSBind = RecallStep.RecallStarted;
							if (mRecallTimeout.Enabled)
								RecallTimeout_Tick(null, null);
							mRecallTimeout.Interval = 40000; // 40 seconds
							mRecallTimeout.Start();
						}
					}
				}
			}
			catch (Exception ex) { Util.HandleException(ex); }
		}

		private void ProcessPortalTie(WorldObject spellTarget, RouteStartType type)
		{
			RouteStart startLoc = GetStartLocationByType(type);
			Coordinates coords;
			string dest = spellTarget.Values(StringValueKey.PortalDestination, "<None>");
			if (Coordinates.TryParse(dest, out coords))
			{
				if (startLoc.Coords != coords)
				{
					startLoc.Coords = coords;
					RefreshStartLocationListCoords();
					if (startLoc.Enabled)
						Util.Message(startLoc.Name + " start location set to " + startLoc.Coords);
				}
			}
			else
			{
				startLoc.Coords = Coordinates.NO_COORDINATES;
				RefreshStartLocationListCoords();
				if (startLoc.Enabled)
				{
					Util.Message("Could not determine destination of primary portal tie. "
						+ startLoc.Name + " start location set to " + startLoc.Coords);
				}
			}
		}

		private void WorldFilter_ChangeObject(object sender, ChangeObjectEventArgs e)
		{
			try
			{
				if (mLoggedIn && e.Change == WorldChangeType.IdentReceived
						&& e.Changed.ObjectClass == ObjectClass.Portal)
				{
					if (mIdPrimaryTie == IdStep.Requested)
					{
						ProcessPortalTie(e.Changed, RouteStartType.PrimaryPortalTie);
						mIdPrimaryTie = IdStep.Idle;
					}
					if (mIdSecondaryTie == IdStep.Requested)
					{
						ProcessPortalTie(e.Changed, RouteStartType.SecondaryPortalTie);
						mIdSecondaryTie = IdStep.Idle;
					}
				}
			}
			catch (Exception ex) { Util.HandleException(ex); }
		}

		private void WorldFilter_CreateObject(object sender, CreateObjectEventArgs e)
		{
			if (!mLoggedIn)
				return;

			try
			{
				int isMansion = -1;
				if (chkAutoUpdateRecalls.Checked)
				{
					isMansion = e.New.Values(LongValueKey.HouseOwner) == MonarchId
						&& (e.New.Name.EndsWith("'s Mansion") || e.New.Name.EndsWith("'s Villa")) ? 1 : 0;
					if (isMansion == 1)
					{
						UpdateStartLocation(e.New, RouteStartType.MansionRecall);
					}
					else if (mRecallingToBindstone == RecallStep.EnteredPortal && e.New.Name == "Bind Stone")
					{
						UpdateStartLocation(e.New, RouteStartType.AllegianceBindstone);
					}
					else if (mRecallingToLSBind == RecallStep.EnteredPortal && e.New.ObjectClass == ObjectClass.Lifestone)
					{
						UpdateStartLocation(e.New, RouteStartType.LifestoneBind);
					}
					else if (mRecallingToLSTie == RecallStep.EnteredPortal && e.New.ObjectClass == ObjectClass.Lifestone)
					{
						UpdateStartLocation(e.New, RouteStartType.LifestoneTie);
					}
				}

				if (chkAutoDetectPortalDevices.Checked)
				{
					if (isMansion == 1 || (isMansion == -1 && e.New.Values(LongValueKey.HouseOwner) == MonarchId
							&& (e.New.Name.EndsWith("'s Mansion") || e.New.Name.EndsWith("'s Villa"))))
					{
						// Look for every portal device
						foreach (PortalDevice device in mPortalDevices.Values)
						{
							bool found = false;
							foreach (WorldObject obj in Core.WorldFilter.GetByName(device.Name))
							{
								if (obj.Values(LongValueKey.HouseOwner) == MonarchId)
								{
									device.Coords = new Coordinates(obj.Coordinates(), 2);
									found = true;
									break;
								}
							}
							if (!found)
							{
								device.Coords = Coordinates.NO_COORDINATES;
							}
						}
					}
					else if (e.New.Values(LongValueKey.HouseOwner) == MonarchId)
					{
						// See if it matches any portal device
						PortalDevice device;
						if (mPortalDevices.TryGetValue(e.New.Name, out device))
						{
							device.Coords = new Coordinates(e.New.Coordinates(), 2);
						}
					}
				}
			}
			catch (Exception ex) { Util.HandleException(ex); }
		}

		private void UpdateStartLocation(WorldObject obj, RouteStartType type)
		{
			RecallTimeout_Tick(null, null);
			UpdateStartLocation(new Coordinates(obj.Coordinates(), 1), type);
		}

		private void UpdateStartLocation(Coordinates coords, RouteStartType type)
		{
			UpdateStartLocation(coords, type, false);
		}

		private void UpdateStartLocation(Coordinates coords, RouteStartType type, bool quiet)
		{
			if (!chkAutoUpdateRecalls.Checked)
				return;

			RouteStart startLoc = GetStartLocationByType(type);
			if (startLoc != null && startLoc.Coords != coords)
			{
				startLoc.Coords = coords;
				RefreshStartLocationListCoords();
				if (startLoc.Enabled && !quiet)
					Util.Message(startLoc.Name + " start location set to " + startLoc.Coords);
			}
		}

		private int MonarchId
		{
			get { return mMonarchId; }
			set
			{
				// If monarch was previously set and differs from the value given
				if (mMonarchId != value && mMonarchId != 0)
				{
					// Reset start locations
					foreach (RouteStart start in mStartLocations.Values)
					{
						if (start.SavesPer == SavesPer.Monarchy)
						{
							start.Coords = Coordinates.NO_COORDINATES;
						}
					}
					RefreshStartLocationListCoords();

					// Reset portal devices
					foreach (PortalDevice device in mPortalDevices.Values)
					{
						device.Coords = Coordinates.NO_COORDINATES;
					}
				}

				mMonarchId = value;
			}
		}

		private string MonarchName
		{
			get { return mMonarchName; }
			set { mMonarchName = value; }
		}

		private void CharacterFilter_ChangePortalMode(object sender, ChangePortalModeEventArgs e)
		{
			try
			{
				if (mLoggedIn && chkAutoUpdateRecalls.Checked)
				{
					if (e.Type == PortalEventType.EnterPortal)
					{
						if (mRecallingToBindstone == RecallStep.RecallStarted)
							mRecallingToBindstone = RecallStep.EnteredPortal;

						if (mRecallingToLSBind == RecallStep.RecallStarted)
							mRecallingToLSBind = RecallStep.EnteredPortal;

						if (mRecallingToLSTie == RecallStep.RecallStarted)
							mRecallingToLSTie = RecallStep.EnteredPortal;

						if (mRecallingToPrimaryPortal == RecallStep.RecallStarted)
							mRecallingToPrimaryPortal = RecallStep.EnteredPortal;

						if (mRecallingToSecondaryPortal == RecallStep.RecallStarted)
							mRecallingToSecondaryPortal = RecallStep.EnteredPortal;
					}
					else if (e.Type == PortalEventType.ExitPortal)
					{
						RouteStartType type = RouteStartType.Regular;

						if (mRecallingToPrimaryPortal == RecallStep.EnteredPortal)
						{
							type = RouteStartType.PrimaryPortalTie;
						}
						else if (mRecallingToSecondaryPortal == RecallStep.EnteredPortal)
						{
							type = RouteStartType.SecondaryPortalTie;
						}

						if (type != RouteStartType.Regular && !mDungeonHud.IsDungeon(Host.Actions.Landcell))
						{
							RouteStart startLoc = GetStartLocationByType(type);
							Coordinates coords = Coordinates.Round(PlayerCoords, 1);
							if (startLoc.Coords != coords)
							{
								startLoc.Coords = coords;
								RefreshStartLocationListCoords();
								if (startLoc.Enabled)
									Util.Message(startLoc.Name + " start location set to " + startLoc.Coords);
							}
						}

						mRecallingToPrimaryPortal = RecallStep.NotRecalling;
						mRecallingToSecondaryPortal = RecallStep.NotRecalling;
					}
				}
			}
			catch (Exception ex) { Util.HandleException(ex); }
		}

		private void RecallTimeout_Tick(object sender, EventArgs e)
		{
			try
			{
				mRecallTimeout.Stop();
				mRecallingToBindstone = RecallStep.NotRecalling;
				mRecallingToLSBind = RecallStep.NotRecalling;
				mRecallingToLSTie = RecallStep.NotRecalling;
				mRecallingToPrimaryPortal = RecallStep.NotRecalling;
				mRecallingToSecondaryPortal = RecallStep.NotRecalling;

				mIdPrimaryTie = IdStep.Idle;
				mIdSecondaryTie = IdStep.Idle;
			}
			catch (Exception ex) { Util.HandleException(ex); }
		}

		private void DatabaseReminderDelay_Tick(object sender, EventArgs e)
		{
			try
			{
				mDatabaseReminderDelay.Stop();
				TimeSpan timeSinceUpdate = DateTime.Now - mLocDb.LastUpdate;
				if (!(chkUpdateRemind.Checked && timeSinceUpdate.TotalDays > 30))
					return;
				QueuedAction openUpdateTabAction = new QueuedAction(delegate()
				{
					nbkMain.ActiveTab = MainTab.Atlas;
					nbkAtlas.ActiveTab = AtlasTab.Update;
					MyClasses.MetaViewWrappers.MVWireupHelper.GetDefaultView(this).Activate();
				});
				QueuedAction updateAction = new QueuedAction(delegate()
				{
					openUpdateTabAction();
					btnLocationsUpdate_Click(null, null);
				});
				QueuedAction disableReminderAction = new QueuedAction(delegate()
				{
					if (chkUpdateRemind.Checked)
					{
						chkUpdateRemind.Checked = false;
						Util.Message("Database update reminders have been disabled. "
							+ "You can re-enable the reminders on the "
							+ Util.CreateChatCommand("Atlas > Update Tab", openUpdateTabAction));
					}
					else
					{
						Util.Warning("Database update reminders are already disabled.");
					}
				});

				string updateCommand = Util.CreateChatCommand("Update Now", updateAction);
				string disableReminderCommand = Util.CreateChatCommand("Don't remind me again", disableReminderAction);

				if (mLocDb.LastUpdate == DateTime.MinValue)
				{
					Util.Message(Util.PluginName + "'s location database may be out of date."
						+ "Would you like to update? " + updateCommand + " | " + disableReminderCommand);
				}
				else
				{
					Util.Message(Util.PluginName + "'s location database was last updated "
						+ ((int)timeSinceUpdate.TotalDays) + " days ago, it may be out of date. "
						+ "Would you like to update? " + updateCommand + " | " + disableReminderCommand);
				}
			}
			catch (Exception ex) { Util.HandleException(ex); }
		}
		#endregion

        #region VVS-specific

        bool VVSEnabled = false;
        int VVSHudBarButtons_Zorder;
        Dictionary<int, IManagedHud> VVSHudBarButtons_HandleToManagedHud = new Dictionary<int, IManagedHud>();
        Dictionary<int, object> VVSHudBarButtons_HandleToVVSButtonInfo = new Dictionary<int, object>();
        Dictionary<IManagedHud, int> VVSHudBarButtons_ManagedHudToHandle = new Dictionary<IManagedHud, int>();

        #region VVS Startup/shutdown

        private void Curtain_VVSEnabledStartup()
        {
            VVSEnabled = true;
            VVSHudBarButtons_Zorder = 100000;

            VirindiViewService.Service.HudBarInstance.Clicked += new VirindiViewService.HudBar.cHudBarHud.ClickedDelegate(HudBarInstance_Clicked);

            AddVVSButtonForManagedHud(mArrowHud, Icons.Toolbar.SimpleArrow, "GoArrow: Arrow");
            AddVVSButtonForManagedHud(mMapHud, Icons.Toolbar.Dereth, "GoArrow: Dereth Map");
            AddVVSButtonForManagedHud(mDungeonHud, Icons.Toolbar.Dungeon, "GoArrow: Dungeon Map");
        }

        private void Curtain_VVSEnabledShutdown()
        {
            VVSEnabled = false;

            VirindiViewService.Service.HudBarInstance.Clicked -= new VirindiViewService.HudBar.cHudBarHud.ClickedDelegate(HudBarInstance_Clicked);

            //Delete the VVS bar buttons
            foreach (KeyValuePair<int, IManagedHud> kp in VVSHudBarButtons_HandleToManagedHud)
            {
                kp.Value.VisibleChanged -= new EventHandler(managedhud_VisibleChanged);
                VirindiViewService.Service.HudBarInstance.RemoveHud(kp.Key);
            }
            VVSHudBarButtons_HandleToManagedHud.Clear();
            VVSHudBarButtons_HandleToVVSButtonInfo.Clear();
            VVSHudBarButtons_ManagedHudToHandle.Clear();
        }

        #endregion VVS Startup/shutdown

        #region VVS Bar hud buttons

        private void AddVVSButtonForManagedHud(IManagedHud managedhud, Bitmap icon, string name)
        {
            VirindiViewService.HudBar.sHudInfo info = new VirindiViewService.HudBar.sHudInfo();
            info.icon = new VirindiViewService.ACImage(icon);
            info.EntryName = name;
            info.zorder = ++VVSHudBarButtons_Zorder;
            info.hudvisible = managedhud.Visible;

            //Group with the main goarrow window...this is how VVS chooses a key internally
            info.group = System.Reflection.Assembly.GetExecutingAssembly().FullName.GetHashCode();
            if (info.group < (int.MinValue + 100))
                info.group += 100; //Reserved space

            int handle = VirindiViewService.Service.HudBarInstance.AddHud(info);
            VVSHudBarButtons_HandleToManagedHud[handle] = managedhud;
            VVSHudBarButtons_HandleToVVSButtonInfo[handle] = info;
            VVSHudBarButtons_ManagedHudToHandle[managedhud] = handle;

            managedhud.VisibleChanged += new EventHandler(managedhud_VisibleChanged);
        }

        void managedhud_VisibleChanged(object sender, EventArgs e)
        {
            IManagedHud myhud = sender as IManagedHud;
            if (!VVSHudBarButtons_ManagedHudToHandle.ContainsKey(myhud)) return;

            int myhandle = VVSHudBarButtons_ManagedHudToHandle[myhud];
            VirindiViewService.Service.HudBarInstance.SetHudEnabled(myhandle, myhud.Visible);
        }

        void HudBarInstance_Clicked(int handle)
        {
            if (!VVSHudBarButtons_HandleToManagedHud.ContainsKey(handle)) return;

            IManagedHud clickedhud = VVSHudBarButtons_HandleToManagedHud[handle];
            clickedhud.Visible = !clickedhud.Visible;
        }

        #endregion VVS Bar hud buttons

        #endregion VVS-specific

        private Coordinates PlayerCoords
		{
			get
			{
				try
				{
					return new Coordinates(Host.Actions.Landcell, Host.Actions.LocationY, Host.Actions.LocationX);
				}
				catch
				{
					// Sometimes causes error when accessed before login complete
					return Coordinates.NO_COORDINATES;
				}
			}
		}

		private void HudManager_ExceptionHandler(object sender, ExceptionEventArgs e)
		{
			Util.HandleException(e.Exception);
		}
	}
}
