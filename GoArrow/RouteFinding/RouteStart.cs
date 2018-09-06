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
using System.Xml;

namespace GoArrow.RouteFinding
{
	public enum RouteStartType
	{
		Regular,
		LifestoneBind,
		LifestoneTie,
		PrimaryPortalTie,
		SecondaryPortalTie,
		HouseRecall,
		MansionRecall,
		AllegianceBindstone,
		_StartPoint,
		_EndPoint,
	}

	public enum SavesPer { All, Character, Account, Monarchy }

	public class RouteStart //: IDisposable
	{
		public const string XmlNodeName = "startLocation";
		private static readonly System.Globalization.NumberStyles Hex = System.Globalization.NumberStyles.HexNumber;

		public static readonly RouteStart StartPoint = new RouteStart("Start Point", RouteStartType._StartPoint,
			Location.LocationTypeInfo(LocationType._StartPoint).Icon, 0, Coordinates.NO_COORDINATES, SavesPer.All, true);

		//private static int msCharacterId = 0, msMonarchId = 0;
		//private static string msCharacterName = "", msMonarchName = "", msAccountName = "";

		//public static event EventHandler MonarchChanged;
		//public static event EventHandler CoordsChanged;

		//public static void Initialize(int currentPlayerGUID, string currentPlayerName,
		//    int currentMonarchGUID, string currentMonarchName, string accountName)
		//{
		//    msCharacterId = currentPlayerGUID;
		//    msCharacterName = currentPlayerName;
		//    msMonarchId = currentMonarchGUID;
		//    msMonarchName = currentMonarchName;
		//    msAccountName = accountName;
		//}

		//public static int MonarchId
		//{
		//    get { return msMonarchId; }
		//    set
		//    {
		//        if (msMonarchId != value)
		//        {
		//            msMonarchId = value;
		//            if (MonarchChanged != null)
		//                MonarchChanged(null, EventArgs.Empty);
		//        }
		//    }
		//}

		//public static string MonarchName
		//{
		//    get { return msMonarchName; }
		//    set { msMonarchName = value; }
		//}

		private readonly int mId;
		private string mName;
		private bool mEnabled;
		private RouteStartType mType;
		private int mIcon;
		private Coordinates mCoords;
		private SavesPer mSavesPer;
		private double mRunDistance = 0;

		private RouteStart(int id, string name, RouteStartType type, int icon,
				double runDistance, Coordinates coords, SavesPer savesPer, bool enabled)
		{
			mId = id;
			mEnabled = enabled;
			mName = name;
			mType = type;
			mIcon = icon;
			mRunDistance = runDistance;
			mCoords = coords;
			mSavesPer = savesPer;

			//MonarchChanged += new EventHandler(RouteStart_MonarchChanged);
		}

		public RouteStart(string name, RouteStartType type, int icon,
				double runDistance, Coordinates coords, SavesPer savesPer, bool enabled)
			: this(Location.GetNextInternalId(), name, type, icon, runDistance, coords, savesPer, enabled) { }

		//private void RouteStart_MonarchChanged(object sender, EventArgs e)
		//{
		//    // Reset the coordinates if this is a per-monarchy deal
		//    if (SavesPer == SavesPer.Monarchy)
		//    {
		//        Coords = Coordinates.NO_COORDINATES;
		//    }
		//}

		//public void Dispose()
		//{
		//    MonarchChanged -= RouteStart_MonarchChanged;
		//}

		public int Id
		{
			get { return mId; }
		}

		public string Name
		{
			get { return mName; }
			set { mName = value; }
		}

		public RouteStartType Type
		{
			get { return mType; }
			set { mType = value; }
		}

		public int Icon
		{
			get { return mIcon; }
			set { mIcon = value; }
		}

		public Coordinates Coords
		{
			get { return mCoords; }
			set
			{
				if (mCoords != value)
				{
					mCoords = value;
					//if (CoordsChanged != null)
					//    CoordsChanged(this, EventArgs.Empty);
				}
			}
		}

		public bool Enabled
		{
			get { return mEnabled; }
			set { mEnabled = value; }
		}

		public SavesPer SavesPer
		{
			get { return mSavesPer; }
			set { mSavesPer = value; }
		}

		public double RunDistance
		{
			get { return mRunDistance; }
			set { mRunDistance = value; }
		}

		public Location ToLocation(LocationDatabase locDb)
		{
			Location loc;
			if (locDb.TryGet(mId, out loc))
				return loc;

			loc = new Location(mId, Name, LocationType._StartPoint, Coords, "");
			loc.Icon = mIcon;
			return loc;
		}

		public XmlElement ToXml(XmlDocument ownerDoc, XmlElement savedStartLocs,
			int characterId, string characterName, int monarchId, string monarchName, string accountName)
		{
			XmlElement myNode = ownerDoc.CreateElement(XmlNodeName);
			myNode.SetAttribute("name", Name);
			myNode.SetAttribute("type", Type.ToString());
			myNode.SetAttribute("icon", Icon.ToString("X8"));
			myNode.SetAttribute("runDistance", RunDistance.ToString());

			if (SavesPer == SavesPer.All)
			{
				myNode.SetAttribute("coords", Coords.ToString());
				myNode.SetAttribute("enabled", Enabled.ToString());
			}
			else
			{
				string eleName, playerName, idString;
				switch (SavesPer)
				{
					case SavesPer.Account:
						eleName = "account";
						playerName = accountName;
						idString = accountName;
						break;
					case SavesPer.Monarchy:
						eleName = "monarch";
						playerName = monarchName;
						idString = monarchId.ToString("X");
						break;
					default:
						eleName = "character";
						playerName = characterName;
						idString =characterId.ToString("X");
						break;
				}

				// Don't save monarchy info for characters not in a monarchy
				if (idString != "0")
				{
					XmlElement ele = (XmlElement)myNode.AppendChild(ownerDoc.CreateElement(eleName));
					ele.SetAttribute("guid", idString);
					ele.SetAttribute("name", playerName);
					ele.SetAttribute("coords", Coords.ToString());
					ele.SetAttribute("enabled", Enabled.ToString());
				}

				// Import settings from other characters/monarchies/accounts
				if (savedStartLocs != null)
				{
					string tmpName = Name.Contains("'") ? ('"' + Name + '"') : ("'" + Name + "'");
					XmlElement savedInfo = savedStartLocs.SelectSingleNode(XmlNodeName + "[@name=" + tmpName + "]") as XmlElement;
					if (savedInfo != null)
					{
						foreach (XmlElement otherNode in savedInfo.SelectNodes(eleName + "[@guid!='" + idString + "']"))
						{
							myNode.AppendChild(ownerDoc.ImportNode(otherNode, true));
						}
					}
				}
			}

			myNode.SetAttribute("savesPer", SavesPer.ToString());
			return myNode;
		}

		public static bool TryParseXml(XmlElement node, out RouteStart startLocation,
			int characterId, string characterName, int monarchId, string monarchName, string accountName)
		{
			startLocation = null;

			string name;
			RouteStartType type;
			double runDistance;
			int icon;
			Coordinates coords;
			SavesPer savesPer;
			bool enabled;

			if (!node.HasAttribute("name"))
				return false;
			name = node.GetAttribute("name");

			if (!Util.TryParseEnum(node.GetAttribute("type"), out type))
				return false;

			if (!double.TryParse(node.GetAttribute("runDistance"), out runDistance))
				return false;

			if (!int.TryParse(node.GetAttribute("icon"), Hex, null, out icon) || ((icon >> 16) != 0x0600))
				icon = Location.LocationTypeInfo(LocationType.Custom).Icon;

			if (node.HasAttribute("coords") && node.HasAttribute("enabled"))
			{
				Coordinates.TryParse(node.GetAttribute("coords"), true, out coords);
				bool.TryParse(node.GetAttribute("enabled"), out enabled);
				savesPer = SavesPer.All;
			}
			else
			{
				if (!Util.TryParseEnum(node.GetAttribute("savesPer"), out savesPer))
					savesPer = SavesPer.Character;

				string xpath;
				switch (savesPer)
				{
					case SavesPer.Account:
						xpath = "account[@guid='" + accountName + "']";
						break;
					case SavesPer.Monarchy:
						xpath = "monarch[@guid='" + monarchId.ToString("X") + "']";
						break;
					default:
						xpath = "character[@guid='" + characterId.ToString("X") + "']";
						break;
				}

				XmlElement charNode = node.SelectSingleNode(xpath) as XmlElement;
				if (charNode != null)
				{
					Coordinates.TryParse(charNode.GetAttribute("coords"), out coords);
					bool.TryParse(charNode.GetAttribute("enabled"), out enabled);
				}
				else
				{
					coords = Coordinates.NO_COORDINATES;
					enabled = true;
				}
			}

			startLocation = new RouteStart(name, type, icon, runDistance, coords, savesPer, enabled);
			return true;
		}

		public static RouteStart FromLocation(Location loc)
		{
			return new RouteStart(loc.Id, loc.Name, RouteStartType._StartPoint, loc.Icon, 0, loc.Coords, SavesPer.All, true);
		}
	}
}
