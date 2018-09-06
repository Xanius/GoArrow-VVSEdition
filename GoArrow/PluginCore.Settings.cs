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
using System.Drawing;
using System.IO;
using System.Xml;
using MouseButtons = System.Windows.Forms.MouseButtons;

using Decal.Adapter;
using Decal.Adapter.Wrappers;
using System.Globalization;

using GoArrow.Huds;
using GoArrow.RouteFinding;

namespace GoArrow
{
	public partial class PluginCore : PluginBase
	{
		private const int SettingsVersion = 2;
		private int mLoadedSettingsVersion = SettingsVersion;

		/// <summary>Set to FALSE until the LoadSettings() function is complete</summary>
		private bool mSettingsLoaded = false;

		private DateTime mSettingsLoadTime = DateTime.MinValue;

		private void LoadSettings()
		{
			bool arrowLoaded = false;
			FileInfo settingsFile;
			try
			{
				settingsFile = new FileInfo(Util.FullPath("settings.xml"));
				if (!settingsFile.Exists)
				{
					LoadArrowImage("Chrome");
					arrowLoaded = true;
					mSettingsLoaded = true;
					return;
				}
			}
			catch (Exception ex)
			{
				Util.HandleException(ex);
				LoadArrowImage("Chrome");
				arrowLoaded = true;
				mSettingsLoaded = true;
				return;
			}

			try
			{
				XmlDocument doc = new XmlDocument();
				doc.Load(settingsFile.FullName);

				if (doc.DocumentElement.HasAttribute("version"))
				{
					int.TryParse(doc.DocumentElement.GetAttribute("version"), out mLoadedSettingsVersion);
				}

				string val;
				int intVal;
				double dblVal;
				bool boolVal;
				Coordinates coords;
				Point pt;
				Rectangle rect;

				foreach (XmlElement ele in doc.DocumentElement.SelectNodes("setting"))
				{
					val = ele.GetAttribute("value");

					switch (ele.GetAttribute("name"))
					{
						case "ViewPosition":
							if (TryParsePoint(val, out pt))
							{
								SetViewLocation(pt);
							}
							break;
						case "nbkMain":
							if (int.TryParse(val, out intVal))
							{
								if (intVal > 0 && intVal < MainTab.COUNT) { nbkMain.ActiveTab = intVal; }
							}
							break;
						case "nbkHuds":
							if (int.TryParse(val, out intVal))
							{
								if (intVal > 0 && intVal < HudsTab.COUNT) { nbkHuds.ActiveTab = intVal; }
							}
							break;
						case "nbkAtlas":
							if (int.TryParse(val, out intVal))
							{
								if (intVal >= 0 && intVal < AtlasTab.COUNT) { nbkAtlas.ActiveTab = intVal; }
							}
							break;
						case "nbkSettings":
							if (int.TryParse(val, out intVal))
							{
								if (intVal > 0 && intVal < SettingsTab.COUNT) { nbkSettings.ActiveTab = intVal; }
							}
							break;
						case "nbkRouteSettings":
							if (int.TryParse(val, out intVal))
							{
								if (intVal > 0 && intVal < RouteSettingsTab.COUNT) { nbkRouteSettings.ActiveTab = intVal; }
							}
							break;

						//
						// HUDs > Arrow HUD Tab
						//
						case "ArrowHud.Visible":
							if (bool.TryParse(val, out boolVal)) { mArrowHud.Visible = boolVal; }
							break;
						case "ArrowHud.Location":
							if (TryParsePoint(val, out pt)) { mArrowHud.Location = pt; }
							break;

						// -- Old v
						case "ArrowHud.DestinationCoords":
							if (Coordinates.TryParse(val, out coords)) { mArrowHud.DestinationCoords = coords; }
							break;
						case "ArrowHud.DestinationLocation":
							if (int.TryParse(val, out intVal))
							{
								Location loc;
								if (mLocDb.TryGet(intVal, out loc) && loc.Coords == mArrowHud.DestinationCoords)
								{
									mArrowHud.DestinationLocation = loc;
								}
							}
							break;
						// -- Old ^

						case "ArrowHud.DisplayIndoors":
							if (bool.TryParse(val, out boolVal)) { mArrowHud.DisplayIndoors = boolVal; }
							break;
						case "ArrowHud.ShowDestinationOver":
							if (bool.TryParse(val, out boolVal)) { mArrowHud.ShowDestinationOver = boolVal; }
							break;
						case "ArrowHud.ShowDistanceUnder":
							if (bool.TryParse(val, out boolVal)) { mArrowHud.ShowDistanceUnder = boolVal; }
							break;
						case "ArrowHud.PositionLocked":
							if (bool.TryParse(val, out boolVal)) { mArrowHud.PositionLocked = boolVal; }
							break;
						case "ArrowHud.ArrowName":
							LoadArrowImage(val);
							arrowLoaded = true;
							break;
						case "ArrowHud.TextSize":
							if (int.TryParse(val, out intVal)) { mArrowHud.TextSize = intVal; }
							break;
						case "ArrowHud.TextColor":
							// Setting choTextColor.Selected calls its change event handler,
							// which changes mArrowHud's text color, unless the color is at 
							// index 0, which should always be white (the default for mArrowHud)
							for (int i = 0; i < choTextColor.Count; i++)
							{
								if (val == (string)choTextColor.Data[i])
								{
									choTextColor.Selected = i;
                                    choTextColor_Change(choTextColor, new MyClasses.MetaViewWrappers.MVIndexChangeEventArgs(0, i));
									break;
								}
							}
							break;
						case "ArrowHud.TextBold":
							if (bool.TryParse(val, out boolVal)) { mArrowHud.TextBold = boolVal; }
							break;

						//
						// HUDs > Dereth Map Tab
						//
						case "DerethMap.Region":
							if (TryParseRectangle(val, out rect)) { mMapHud.Region = rect; }
							break;
						case "DerethMap.Sticky":
							if (bool.TryParse(val, out boolVal)) { mMapHud.Sticky = boolVal; }
							break;
						case "DerethMap.CenterOnPlayer":
							if (bool.TryParse(val, out boolVal)) { mMapHud.CenterOnPlayer = boolVal; }
							break;
						case "DerethMap.ShowRoute":
							if (bool.TryParse(val, out boolVal)) { mMapHud.ShowRoute = boolVal; }
							break;
						case "DerethMap.ShowLocations":
							if (bool.TryParse(val, out boolVal)) { mMapHud.ShowLocations = boolVal; }
							break;
						case "DerethMap.ShowLocationsAllZooms":
							if (bool.TryParse(val, out boolVal)) { mMapHud.ShowLocationsAllZooms = boolVal; }
							break;
						case "DerethMap.ShowLabels":
							if (bool.TryParse(val, out boolVal)) { mMapHud.ShowLabels = boolVal; }
							break;

						// -- Old v
						case "DerethMap.AlphaActive":
							if (int.TryParse(val, out intVal)) { SetWindowAlpha(mMapHud, true, intVal); }
							break;
						case "DerethMap.AlphaInactive":
							if (int.TryParse(val, out intVal)) { SetWindowAlpha(mMapHud, false, intVal); }
							break;
						// -- Old ^

						case "DerethMap.Zoom":
							if (double.TryParse(val, out dblVal)) { mMapHud.Zoom = (float)dblVal; }
							break;
						case "DerethMap.DragButton":
							try { mMapHud.DragButton = (MouseButtons)Enum.Parse(typeof(MouseButtons), val, true); }
							catch { /* Ignore */ }
							break;
						case "DerethMap.SelectLocationButton":
							try { mMapHud.SelectLocationButton = (MouseButtons)Enum.Parse(typeof(MouseButtons), val, true); }
							catch { /* Ignore */ }
							break;
						case "DerethMap.ContextMenuButton":
							try { mMapHud.ContextMenuButton = (MouseButtons)Enum.Parse(typeof(MouseButtons), val, true); }
							catch { /* Ignore */ }
							break;
						case "DerethMap.DetailsButton":
							try { mMapHud.DetailsButton = (MouseButtons)Enum.Parse(typeof(MouseButtons), val, true); }
							catch { /* Ignore */ }
							break;

						//
						// HUDs > Dungeon Map Tab
						//
						case "DungeonMap.Region":
							if (TryParseRectangle(val, out rect)) { mDungeonHud.Region = rect; }
							break;
						case "DungeonMap.Sticky":
							if (bool.TryParse(val, out boolVal)) { mDungeonHud.Sticky = boolVal; }
							break;
						case "DungeonMap.CurrentDungeon":
							if (int.TryParse(val, out intVal)) { mDungeonHud.LoadDungeonById(intVal); }
							break;
						case "DungeonMap.AutoLoadMaps":
							if (bool.TryParse(val, out boolVal)) { mDungeonHud.AutoLoadMaps = boolVal; }
							break;
						case "DungeonMap.ShowCompass":
							if (bool.TryParse(val, out boolVal)) { mDungeonHud.ShowCompass = boolVal; }
							break;
						case "DungeonMap.AutoRotateMap":
							if (bool.TryParse(val, out boolVal)) { mDungeonHud.AutoRotateMap = boolVal; }
							break;
						case "DungeonMap.MoveWithPlayer":
							if (bool.TryParse(val, out boolVal)) { mDungeonHud.MoveWithPlayer = boolVal; }
							break;
						case "DungeonMap.DragButton":
							try { mDungeonHud.DragButton = (MouseButtons)Enum.Parse(typeof(MouseButtons), val, true); }
							catch { /* Ignore */ }
							break;

						//
						// HUDs > General Tab
						//
						case "Toolbar.Visible":
							if (bool.TryParse(val, out boolVal)) { mToolbar.Visible = boolVal; }
							break;
						case "Toolbar.Location":
							if (TryParsePoint(val, out pt)) { mToolbar.Location = pt; }
							break;
						case "Toolbar.Display":
							try { mToolbar.Display = (ToolbarDisplay)Enum.Parse(typeof(ToolbarDisplay), val, true); }
							catch { /* Ignore */ }
							break;
						case "Toolbar.Orientation":
							try { mToolbar.Orientation = (ToolbarOrientation)Enum.Parse(typeof(ToolbarOrientation), val, true); }
							catch { /* Ignore */ }
							break;
						case "MainViewToolButton.Visible":
							if (bool.TryParse(val, out boolVal)) { mMainViewToolButton.Visible = boolVal; }
							break;
						case "ArrowToolButton.Visible":
							if (bool.TryParse(val, out boolVal)) { mArrowToolButton.Visible = boolVal; }
							break;
						case "DerethToolButton.Visible":
							if (bool.TryParse(val, out boolVal)) { mDerethToolButton.Visible = boolVal; }
							break;
						case "DungeonToolButton.Visible":
							if (bool.TryParse(val, out boolVal)) { mDungeonToolButton.Visible = boolVal; }
							break;
						case "chkAllowBothMaps":
							if (bool.TryParse(val, out boolVal)) { chkAllowBothMaps.Checked = boolVal; }
							break;
						case "Huds.AlphaActive":
							if (int.TryParse(val, out intVal))
							{
								mArrowHud.Alpha = intVal;
								SetWindowAlpha(mMapHud, true, intVal);
								SetWindowAlpha(mDungeonHud, true, intVal);
							}
							break;
						case "Huds.AlphaInactive":
							if (int.TryParse(val, out intVal))
							{
								SetWindowAlpha(mMapHud, false, intVal);
								SetWindowAlpha(mDungeonHud, false, intVal);
							}
							break;

						//
						// Atlas > Update Tab
						//
						case "edtLocationsUrl":
							edtLocationsUrl.Text = val;
							break;
						case "chkUpdateRemind":
							if (bool.TryParse(val, out boolVal)) { chkUpdateRemind.Checked = boolVal; }
							break;

						//
						// Settings > Chat Tab
						//
						case "chkLinkCoords":
							if (bool.TryParse(val, out boolVal)) { chkLinkCoords.Checked = boolVal; }
							break;
						case "Util.DefaultTargetWindows":
							{
								ChatWindow windows;
								if (Util.TryParseEnum<ChatWindow>(val, out windows) && windows != ChatWindow.Default)
								{
									Util.DefaultWindow = windows;
								}
							}
							break;

						// -- Old v
						case "choWriteMessagesTo":
							if (int.TryParse(val, out intVal))
							{
								switch (intVal)
								{
									case 0: { Util.DefaultWindow = ChatWindow.MainChat; break; }
									case 1: { Util.DefaultWindow = ChatWindow.One; break; }
									case 2: { Util.DefaultWindow = ChatWindow.Two; break; }
									case 3: { Util.DefaultWindow = ChatWindow.Three; break; }
									case 4: { Util.DefaultWindow = ChatWindow.Four; break; }
								}
							}
							break;
						// -- Old ^

						case "chkAlwaysShowErrors":
							if (bool.TryParse(val, out boolVal))
							{
								chkAlwaysShowErrors.Checked = boolVal;
								Util.WriteErrorsToMainChat = boolVal;
							}
							break;
						case "edtChatCommand":
							edtChatCommand.Text = val;
							break;
						case "chkEnableCoordsCommand":
							if (bool.TryParse(val, out boolVal)) { chkEnableCoordsCommand.Checked = boolVal; }
							break;
						case "edtCoordsCommand":
							edtCoordsCommand.Text = val;
							break;
						case "chkEnableDestCommand":
							if (bool.TryParse(val, out boolVal)) { chkEnableDestCommand.Checked = boolVal; }
							break;
						case "edtDestCommand":
							edtDestCommand.Text = val;
							break;
						case "chkEnableFindCommand":
							if (bool.TryParse(val, out boolVal)) { chkEnableFindCommand.Checked = boolVal; }
							break;
						case "edtFindCommand":
							edtFindCommand.Text = val;
							break;
						case "chkTrackCorpses":
							if (bool.TryParse(val, out boolVal)) { chkTrackCorpses.Checked = boolVal; }
							break;

						//
						// Settings > Route Finding Tab
						//
						case "chkAutoUpdateRecalls":
							if (bool.TryParse(val, out boolVal)) { chkAutoUpdateRecalls.Checked = boolVal; }
							break;
						case "edtMaxRunDist":
							if (double.TryParse(val, out dblVal) && dblVal > 0)
							{
								edtMaxRunDist.Text = dblVal.ToString();
								RouteFinder.MaxRunDistance = dblVal;
							}
							break;
						case "edtPortalRunDist":
							if (double.TryParse(val, out dblVal) && dblVal > 0)
							{
								edtPortalRunDist.Text = dblVal.ToString();
								RouteFinder.PortalWeight = dblVal;
							}
							break;
						case "chkAutoDetectPortalDevices":
							if (bool.TryParse(val, out boolVal)) { chkAutoDetectPortalDevices.Checked = boolVal; }
							break;
					}
				}

				// Reset setting
				if (mLoadedSettingsVersion < 2)
				{
					chkAutoUpdateRecalls.Checked = true;
				}

				XmlElement arrowNode = doc.DocumentElement.SelectSingleNode("arrowTarget") as XmlElement;
				if (arrowNode != null)
				{
					mArrowHud.LoadDestinationXml(arrowNode, mLocDb);
				}

				XmlElement startLocationsNode = doc.DocumentElement.SelectSingleNode("startLocations") as XmlElement;
				if (startLocationsNode != null)
				{
					mStartLocations.Clear();
					foreach (XmlElement node in startLocationsNode.ChildNodes)
					{
						RouteStart loc;
						if (RouteStart.TryParseXml(node, out loc, Core.CharacterFilter.Id, Core.CharacterFilter.Name, MonarchId, MonarchName, Core.CharacterFilter.AccountName))
						{
							mStartLocations[loc.Name] = loc;
						}
					}
				}

				string portalDevicesXpath = "portalDevices/monarch[@guid='" + MonarchId.ToString("X") + "']";
				XmlElement portalDevicesMonarchNode = doc.DocumentElement.SelectSingleNode(portalDevicesXpath) as XmlElement;
				if (portalDevicesMonarchNode != null)
				{
					foreach (PortalDevice device in mPortalDevices.Values)
					{
						device.LoadSettingsXml(portalDevicesMonarchNode);
					}
				}

				XmlElement favoriteLocationsNode = doc.DocumentElement.SelectSingleNode("favoriteLocations") as XmlElement;
				if (favoriteLocationsNode != null)
				{
					foreach (XmlElement node in favoriteLocationsNode.GetElementsByTagName("favorite"))
					{
						Location loc = GetLocation(node.GetAttribute("id"));
						if (loc != null)
							loc.IsFavorite = true;
					}
				}

				XmlElement recentLocationsNode = doc.DocumentElement.SelectSingleNode("recentLocations") as XmlElement;
				if (recentLocationsNode != null)
				{
					lstRecent.Clear();
					int ct = 0;
					foreach (XmlElement node in recentLocationsNode.GetElementsByTagName("recent"))
					{
						Location loc = GetLocation(node.GetAttribute("id"));
						if (loc != null)
						{
							MyClasses.MetaViewWrappers.IListRow row = lstRecent.Add();
							row[LocationList.Icon][1] = loc.Icon;
							row[LocationList.Name][0] = loc.Name;
							row[LocationList.GoIcon][1] = GoIcon;
							row[LocationList.ID][0] = loc.Id.ToString();
						}
						if (ct++ >= MaxRecentLocations)
							break;
					}
					UpdateListCoords(lstRecent, chkRecentShowRelative.Checked);
				}

				XmlElement recentCoordsNode = doc.DocumentElement.SelectSingleNode("recentCoords") as XmlElement;
				if (recentCoordsNode != null)
				{
					lstRecentCoords.Clear();
					foreach (XmlElement node in recentCoordsNode.GetElementsByTagName("recent"))
					{
						if (Coordinates.TryParse(node.GetAttribute("coords"), out coords))
						{
							MyClasses.MetaViewWrappers.IListRow row = lstRecentCoords.Add();
							row[RecentCoordsList.Coords][0] = coords.ToString();
							if (node.HasAttribute("name"))
								row[RecentCoordsList.Name][0] = node.GetAttribute("name");
							if (node.HasAttribute("icon"))
							{
								int icon;
								string iconHex = node.GetAttribute("icon");
								if (int.TryParse(iconHex, NumberStyles.HexNumber, null, out icon))
								{
									row[RecentCoordsList.Icon][1] = icon;
								}
							}
						}
					}
				}

				if (!arrowLoaded)
					LoadArrowImage("Chrome");
			}
			catch (Exception ex)
			{
				Util.HandleException(ex, "Error encountered while loading settings.xml file", true);
				string errorPath = Util.FullPath("settings_error.xml");
				if (File.Exists(errorPath))
					File.Delete(errorPath);
				settingsFile.MoveTo(errorPath);
				Util.SevereError("The old settings.xml file has been renamed to settings_error.xml "
					+ "and a new settings.xml will be created with the defaults.");
			}
			finally
			{
				mSettingsLoaded = true;
				mSettingsLoadTime = DateTime.Now;
			}
		}

		private void LoadArrowImage(string name)
		{
			int foundIndex = -1, chromeIndex = -1;
			StringComparer cmp = StringComparer.OrdinalIgnoreCase;
			for (int i = 0; i < choArrowImage.Count; i++)
			{
				if (cmp.Equals(choArrowImage.Text[i], name))
				{
					foundIndex = i;
					break;
				}
				if (chromeIndex < 0 && cmp.Equals(choArrowImage.Text[i], "Chrome"))
					chromeIndex = i;
			}
			bool loaded = false;
			string errMsg;
			if (foundIndex >= 0)
			{
				choArrowImage.Selected = foundIndex;
				if (!(loaded = mArrowHud.LoadArrowImage(choArrowImage.Text[foundIndex], out errMsg)))
					Util.Error(errMsg);
			}
			if (!loaded && chromeIndex >= 0 && chromeIndex != foundIndex)
			{
				choArrowImage.Selected = chromeIndex;
				if (!(loaded = mArrowHud.LoadArrowImage(choArrowImage.Text[chromeIndex], out errMsg)))
					Util.Error(errMsg);
			}
			if (!loaded && choArrowImage.Count > 0)
			{
				choArrowImage.Selected = 0;
				if (!(loaded = mArrowHud.LoadArrowImage(choArrowImage.Text[0], out errMsg)))
					Util.Error(errMsg);
			}
			if (!loaded)
			{
				Util.SevereError("Could not load any arrow image. You should reinstall " + Util.PluginName + ".");
			}
		}

		private void AddSetting(XmlDocument doc, string name, string value)
		{
			XmlElement ele = (XmlElement)doc.DocumentElement.AppendChild(doc.CreateElement("setting"));
			ele.SetAttribute("name", name);
			ele.SetAttribute("value", value);
		}
		private void AddSetting(XmlDocument doc, string name, bool value) { AddSetting(doc, name, value.ToString()); }
		private void AddSetting(XmlDocument doc, string name, int value) { AddSetting(doc, name, value.ToString()); }
		private void AddSetting(XmlDocument doc, string name, double value) { AddSetting(doc, name, value.ToString()); }
		private void AddSetting(XmlDocument doc, string name, Enum value) { AddSetting(doc, name, value.ToString()); }
		private void AddSetting(XmlDocument doc, string name, Coordinates value) { AddSetting(doc, name, value.ToString()); }
		private void AddSetting(XmlDocument doc, string name, Point value) { AddSetting(doc, name, PointToString(value)); }
		private void AddSetting(XmlDocument doc, string name, Rectangle value) { AddSetting(doc, name, RectangleToString(value)); }

		private void SaveSettings()
		{
			if (!mSettingsLoaded)
				return;

			// Reload start location settings from other characters
			XmlElement savedStartLocs = null;
			XmlElement savedPortalDevices = null;
			try
			{
				XmlDocument cur = new XmlDocument();
				cur.Load(Util.FullPath("settings.xml"));

				savedStartLocs = cur.DocumentElement.SelectSingleNode("startLocations") as XmlElement;
				savedPortalDevices = cur.DocumentElement.SelectSingleNode("portalDevices") as XmlElement;
			}
			catch { /* Ignore */ }

			try
			{
				XmlDocument doc = new XmlDocument();
				XmlElement root = (XmlElement)doc.AppendChild(doc.CreateElement("settings"));

				root.SetAttribute("version", SettingsVersion.ToString());

				// General
				AddSetting(doc, "ViewPosition", MyClasses.MetaViewWrappers.MVWireupHelper.GetDefaultView(this).Position.Location);
				AddSetting(doc, "nbkMain", nbkMain.ActiveTab);
				AddSetting(doc, "nbkHuds", nbkHuds.ActiveTab);
				AddSetting(doc, "nbkAtlas", nbkAtlas.ActiveTab);
				AddSetting(doc, "nbkSettings", nbkSettings.ActiveTab);
				AddSetting(doc, "nbkRouteSettings", nbkRouteSettings.ActiveTab);

				// HUDs > Arrow HUD Tab
				AddSetting(doc, "ArrowHud.Visible", mArrowHud.Visible);
				AddSetting(doc, "ArrowHud.Location", mArrowHud.Location);
				AddSetting(doc, "ArrowHud.DisplayIndoors", mArrowHud.DisplayIndoors);
				AddSetting(doc, "ArrowHud.ShowDestinationOver", mArrowHud.ShowDestinationOver);
				AddSetting(doc, "ArrowHud.ShowDistanceUnder", mArrowHud.ShowDistanceUnder);
				AddSetting(doc, "ArrowHud.PositionLocked", mArrowHud.PositionLocked);
				if (mArrowHud.ArrowName != "")
					AddSetting(doc, "ArrowHud.ArrowName", mArrowHud.ArrowName);
				else
					AddSetting(doc, "ArrowHud.ArrowName", choArrowImage.Text[choArrowImage.Selected]);
				AddSetting(doc, "ArrowHud.TextSize", mArrowHud.TextSize);
				AddSetting(doc, "ArrowHud.TextColor", (mArrowHud.TextColor.ToArgb() & 0xFFFFFF).ToString("X6"));
				AddSetting(doc, "ArrowHud.TextBold", mArrowHud.TextBold);

				// HUDs > Dereth Map Tab
				AddSetting(doc, "DerethMap.Region", mMapHud.Region);
				AddSetting(doc, "DerethMap.Sticky", mMapHud.Sticky);
				AddSetting(doc, "DerethMap.CenterOnPlayer", mMapHud.CenterOnPlayer);
				AddSetting(doc, "DerethMap.ShowRoute", mMapHud.ShowRoute);
				AddSetting(doc, "DerethMap.ShowLocations", mMapHud.ShowLocations);
				AddSetting(doc, "DerethMap.ShowLocationsAllZooms", mMapHud.ShowLocationsAllZooms);
				AddSetting(doc, "DerethMap.ShowLabels", mMapHud.ShowLabels);
				AddSetting(doc, "DerethMap.Zoom", mMapHud.Zoom);
				AddSetting(doc, "DerethMap.DragButton", mMapHud.DragButton);
				AddSetting(doc, "DerethMap.SelectLocationButton", mMapHud.SelectLocationButton);
				AddSetting(doc, "DerethMap.ContextMenuButton", mMapHud.ContextMenuButton);
				AddSetting(doc, "DerethMap.DetailsButton", mMapHud.DetailsButton);

				// HUDs > Dungeon Map Tab
				AddSetting(doc, "DungeonMap.Region", mDungeonHud.Region);
				AddSetting(doc, "DungeonMap.Sticky", mDungeonHud.Sticky);
				AddSetting(doc, "DungeonMap.CurrentDungeon", mDungeonHud.CurrentDungeon);
				AddSetting(doc, "DungeonMap.AutoLoadMaps", mDungeonHud.AutoLoadMaps);
				AddSetting(doc, "DungeonMap.ShowCompass", mDungeonHud.ShowCompass);
				AddSetting(doc, "DungeonMap.MoveWithPlayer", mDungeonHud.MoveWithPlayer);
				AddSetting(doc, "DungeonMap.ShowCompass", mDungeonHud.ShowCompass);
				AddSetting(doc, "DungeonMap.DragButton", mDungeonHud.DragButton);

				// HUDs > General Tab
				AddSetting(doc, "Toolbar.Visible", mToolbar.Visible);
				AddSetting(doc, "Toolbar.Location", mToolbar.Location);
				AddSetting(doc, "Toolbar.Display", mToolbar.Display);
				AddSetting(doc, "Toolbar.Orientation", mToolbar.Orientation);
				AddSetting(doc, "MainViewToolButton.Visible", mMainViewToolButton.Visible);
				AddSetting(doc, "ArrowToolButton.Visible", mArrowToolButton.Visible);
				AddSetting(doc, "DerethToolButton.Visible", mDerethToolButton.Visible);
				AddSetting(doc, "DungeonToolButton.Visible", mDungeonToolButton.Visible);

				AddSetting(doc, "chkAllowBothMaps", chkAllowBothMaps.Checked);

				AddSetting(doc, "Huds.AlphaActive", mMapHud.AlphaFrameActive);
				AddSetting(doc, "Huds.AlphaInactive", mMapHud.AlphaFrameInactive);

				// Atlas > Update Tab
				AddSetting(doc, "edtLocationsUrl", edtLocationsUrl.Text);
				AddSetting(doc, "chkUpdateRemind", chkUpdateRemind.Checked);

				// Settings > Chat Tab
				AddSetting(doc, "chkTrackCorpses", chkTrackCorpses.Checked);
				AddSetting(doc, "chkLinkCoords", chkLinkCoords.Checked);
				AddSetting(doc, "Util.DefaultTargetWindows", Util.DefaultWindow);
				AddSetting(doc, "chkAlwaysShowErrors", chkAlwaysShowErrors.Checked);
				AddSetting(doc, "edtChatCommand", edtChatCommand.Text);
				AddSetting(doc, "chkEnableCoordsCommand", chkEnableCoordsCommand.Checked);
				AddSetting(doc, "edtCoordsCommand", edtCoordsCommand.Text);
				AddSetting(doc, "chkEnableDestCommand", chkEnableDestCommand.Checked);
				AddSetting(doc, "edtDestCommand", edtDestCommand.Text);
				AddSetting(doc, "chkEnableFindCommand", chkEnableFindCommand.Checked);
				AddSetting(doc, "edtFindCommand", edtFindCommand.Text);

				// Settings > Route Finding Tab
				AddSetting(doc, "chkAutoUpdateRecalls", chkAutoUpdateRecalls.Checked);
				AddSetting(doc, "edtMaxRunDist", edtMaxRunDist.Text);
				AddSetting(doc, "edtPortalRunDist", edtPortalRunDist.Text);
				AddSetting(doc, "chkAutoDetectPortalDevices", chkAutoDetectPortalDevices.Checked);

				// Arrow Target
				XmlElement arrowNode = (XmlElement)root.AppendChild(doc.CreateElement("arrowTarget"));
				mArrowHud.SaveDestinationXml(arrowNode);

				// Start Locations
				XmlElement startLocationsNode = (XmlElement)root.AppendChild(doc.CreateElement("startLocations"));
				foreach (RouteStart startLocation in mStartLocations.Values)
				{
					startLocationsNode.AppendChild(startLocation.ToXml(doc, savedStartLocs, Core.CharacterFilter.Id, Core.CharacterFilter.Name, MonarchId, MonarchName, Core.CharacterFilter.AccountName));
				}

				// Portal Devices
				XmlElement portalDevicesNode = (XmlElement)root.AppendChild(doc.CreateElement("portalDevices"));
				string monarchHex = MonarchId.ToString("X");
				if (MonarchId != 0)
				{
					XmlElement monarchNode = (XmlElement)portalDevicesNode.AppendChild(doc.CreateElement("monarch"));
					monarchNode.SetAttribute("guid", monarchHex);
					monarchNode.SetAttribute("name", MonarchName);
					foreach (PortalDevice device in mPortalDevices.Values)
					{
						device.SaveSettingsXml(monarchNode);
					}
				}

				// Import portal device settings for other mansions
				if (savedPortalDevices != null)
				{
					XmlNodeList otherMonarchs = savedPortalDevices.SelectNodes("monarch[@guid!='" + monarchHex + "']");
					foreach (XmlElement otherMonarch in otherMonarchs)
					{
						portalDevicesNode.AppendChild(doc.ImportNode(otherMonarch, true));
					}
				}

				// Favorite Locations
				XmlElement favoriteLocationsNode = (XmlElement)root.AppendChild(doc.CreateElement("favoriteLocations"));
				foreach (Location loc in mLocDb.Favorites)
				{
					XmlElement favoriteNode = (XmlElement)favoriteLocationsNode.AppendChild(doc.CreateElement("favorite"));
					favoriteNode.SetAttribute("id", loc.Id.ToString());
					favoriteNode.SetAttribute("name", loc.Name);
				}

				// Recent Locations
				XmlElement recentLocationsNode = (XmlElement)root.AppendChild(doc.CreateElement("recentLocations"));
				for (int r = 0; r < lstRecent.RowCount; r++)
				{
					Location loc = GetLocation(lstRecent[r][LocationList.ID][0] as string);
					if (loc != null)
					{
						XmlElement recentNode = (XmlElement)recentLocationsNode.AppendChild(doc.CreateElement("recent"));
						recentNode.SetAttribute("id", loc.Id.ToString());
						recentNode.SetAttribute("name", loc.Name);
					}
				}

				// Recent Coordinates
				XmlElement recentCoordsNode = (XmlElement)root.AppendChild(doc.CreateElement("recentCoords"));
				for (int r = 0; r < lstRecentCoords.RowCount && r <= MaxRecentLocations; r++)
				{
					Coordinates coords;
					object coordsStr = lstRecentCoords[r][RecentCoordsList.Coords][0];
					object nameStr = lstRecentCoords[r][RecentCoordsList.Name][0];
					object iconInt = lstRecentCoords[r][RecentCoordsList.Icon][1];
					if ((coordsStr is string) && (nameStr is string) && (iconInt is int))
					{
						if (Coordinates.TryParse((string)coordsStr, out coords))
						{
							XmlElement node = (XmlElement)recentCoordsNode.AppendChild(doc.CreateElement("recent"));
							node.SetAttribute("name", (string)nameStr);
							node.SetAttribute("coords", coords.ToString());
							node.SetAttribute("icon", ((int)iconInt).ToString("X8"));
						}
					}
				}

				Util.SaveXml(doc, Util.FullPath("settings.xml"));
			}
			catch (Exception ex) { Util.HandleException(ex); }
		}

		private string PointToString(Point pt)
		{
			return pt.X + "," + pt.Y;
		}

		private bool TryParsePoint(string val, out Point pt)
		{
			string[] xy = val.Split(',');
			int x, y;
			if (xy.Length == 2
					&& int.TryParse(xy[0], out x) && x >= 0
					&& int.TryParse(xy[1], out y) && y >= 0)
			{
				pt = new Point(x, y);
				return true;
			}
			pt = new Point();
			return false;
		}

		private string RectangleToString(Rectangle r)
		{
			return r.X + "," + r.Y + ";" + r.Width + "," + r.Height;
		}

		private bool TryParseRectangle(string val, out Rectangle r)
		{
			string[] pt_sz = val.Split(';');
			Point pt, sz;
			if (pt_sz.Length == 2 && TryParsePoint(pt_sz[0], out pt) && TryParsePoint(pt_sz[1], out sz))
			{
				r = new Rectangle(pt, (Size)sz);
				return true;
			}
			r = new Rectangle();
			return false;
		}
	}
}
