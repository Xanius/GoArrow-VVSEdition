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
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;

namespace GoArrow.RouteFinding
{
	[Flags]
	public enum LocationType : uint
	{
		// Default
		_Unknown = 0x0,

		// Composite
		Any = 0xFFFFFFFF,
		AnyPortal = SettlementPortal | TownPortal | UndergroundPortal | WildernessPortal | Portal,

		// Defined in XML
		AllegianceHall = 0x1,
		Bindstone = 0x2,
		Dungeon = 0x4,
		Landmark = 0x8,
		Lifestone = 0x10,
		NPC = 0x20,
		Outpost = 0x40,
		Portal = 0x80,
		PortalHub = 0x100,
		SettlementPortal = 0x200,
		Village = 0x400, // Settlement
		Town = 0x800,
		TownPortal = 0x1000,
		UndergroundPortal = 0x2000,
		Vendor = 0x4000,
		WildernessPortal = 0x8000,

		// Custom
		PortalDevice = 0x8000000,
		Custom = 0x10000000,
		_StartPoint = 0x20000000,
		_EndPoint = 0x40000000,
	}

	public struct LocationTypeInfo
	{
		private int mIcon;
		private string mFriendlyName;
		private DatabaseType mShowFor;

		internal LocationTypeInfo(string friendlyName, int icon, DatabaseType showFor)
		{
			mFriendlyName = friendlyName;
			mIcon = icon;
			mShowFor = showFor;
		}

		public int Icon { get { return mIcon; } }
		public string FriendlyName { get { return mFriendlyName; } }
		public DatabaseType ShowForType { get { return mShowFor; } }

		public bool ShowFor(DatabaseType type)
		{
			return (mShowFor & type) != 0;
		}
	}

	/**
	 * The "ID space" for Location IDs is divided up as follows:
	 *   [0, 2 billion)             : IDs for locations supplied by Crossroads of Dereth
	 *   [2 billion, int.MaxValue]  : IDs for custom-made locations
	 *   (int.MinValue, -1]         : IDs used for temporary (internal) locations that are 
	 *                                lost when the program exits
	 *   [int.MinValue]             : ID for NO_LOCATION
	 */
	public class Location : IComparable<Location>, IEqualityComparer<Location>
	{
		public static readonly Location NO_LOCATION = new Location(int.MinValue, "[No Location]",
			LocationType._Unknown, Coordinates.NO_COORDINATES, "");

		private static readonly Regex HtmlTagsRegex = new Regex(@"<[^>]+>");
		private static readonly Regex BrTagRegex = new Regex(@"<(br|BR)\s*/?>");
		private static readonly Regex SignatureRegex = new Regex(@"<[Ii]>\s*(--[^<]*)</[Ii]>");
		private static readonly Regex DestinationRegex = new Regex(@"Destination: .*?\. ");

		/// <summary>All IDs of custom locations will be greater than this</summary>
		private const int CUSTOM_ID_OFFSET = 2000000000; // 2 billion
		private static int mNextCustomId = CUSTOM_ID_OFFSET;
		private static int mNextInternalId = -2;

		private readonly int mId;
		private string mName;
		private LocationType mType;
		private Coordinates mCoords;
		private Coordinates mExitCoords;
		private int mDungeonId;
		private string mNotes;
		private bool mIsFavorite;
		private bool mIsCustomized;
		private bool mIsRetired = false;
		private int mSpecialIcon = 0;
		private bool mUseInRouteFinding = true;

		/// <summary>Invoked when the IsFavorite property changes.</summary>
		public event EventHandler IsFavoriteChanged;

		public Location(int id, string Name, LocationType Type, Coordinates Coords, string Notes)
			: this(id, Name, Type, Coords, Notes, 0, Coordinates.NO_COORDINATES) { }

		public Location(int id, string Name, LocationType Type, Coordinates Coords, string Notes,
				Coordinates ExitCoords)
			: this(id, Name, Type, Coords, Notes, 0, ExitCoords) { }

		public Location(int id, string Name, LocationType Type, Coordinates Coords, string Notes,
				int DungeonID)
			: this(id, Name, Type, Coords, Notes, DungeonID, Coordinates.NO_COORDINATES) { }

		public Location(int id, string Name, LocationType Type, Coordinates Coords, string Notes,
				int DungeonID, Coordinates ExitCoords)
		{
			this.mId = id;
			this.mName = Name;
			this.mType = Type;
			this.mCoords = Coords;
			this.mNotes = (Notes == null) ? "" : Notes;
			this.mExitCoords = ExitCoords;
			this.mDungeonId = DungeonID;

			if (id >= mNextCustomId)
				mNextCustomId = id + 1;
		}

		/// <summary>Copy constructor.</summary>
		/// <param name="newId">The ID for the copy of loc.</param>
		/// <param name="loc">The location to copy from.</param>
		public Location(int newId, Location loc)
			: this(newId, loc.Name, loc.Type, loc.Coords, loc.Notes, loc.DungeonId, loc.ExitCoords) { }

		public int Id { get { return mId; } }

		public string Name
		{
			get { return mName; }
			set { mName = value; }
		}

		public LocationType Type
		{
			get { return mType; }
			set { mType = value; }
		}

		public Coordinates Coords
		{
			get { return mCoords; }
			set { mCoords = value; }
		}

		public Coordinates ExitCoords
		{
			get { return mExitCoords; }
			set { mExitCoords = value; }
		}

		public bool HasExitCoords
		{
			get { return mExitCoords != Coordinates.NO_COORDINATES; }
		}

		public int DungeonId
		{
			get { return mDungeonId; }
			set { mDungeonId = value; }
		}

		public string Notes
		{
			get { return mNotes; }
			set { mNotes = value; }
		}

		public bool IsFavorite
		{
			get { return mIsFavorite; }
			set
			{
				if (mIsFavorite != value)
				{
					mIsFavorite = value;
					if (IsFavoriteChanged != null)
						IsFavoriteChanged(this, EventArgs.Empty);
				}
			}
		}

		public bool IsCustomized
		{
			get { return mIsCustomized; }
			set { mIsCustomized = value; }
		}

		public bool IsRetired
		{
			get { return mIsRetired; }
		}

		public bool IsInternalLocation
		{
			get { return IsInternalId(Id); }
		}

		public bool UseInRouteFinding
		{
			get { return mUseInRouteFinding; }
			set { mUseInRouteFinding = value; }
		}

		public int Icon
		{
			get { return GetIcon(true); }
			set { mSpecialIcon = value; }
		}

		public int GetIcon(bool useFavIcon)
		{
			if (mIsFavorite && useFavIcon)
				return FavoriteIcon;

			if (mSpecialIcon != 0)
				return mSpecialIcon;

			if (mType == LocationType.Village)
			{
				if (mName.Contains("Mansion"))
					return AcIcons.Mansion;
				if (mName.Contains("Villa"))
					return AcIcons.Villa1;
			}

			return LocationTypeInfo(mType).Icon;
		}

		public void ClearSpecializedIcon()
		{
			mSpecialIcon = 0;
		}

		public bool HasSpecializedIcon
		{
			get { return mSpecialIcon != 0; }
		}

		public string GetTypeString()
		{
			return LocationTypeInfo(this.Type).FriendlyName;
		}

		public bool TypeMatches(LocationType type)
		{
			return this.Type == type || (this.Type & type) != 0;
		}

		public static bool IsInternalId(int id)
		{
			return id < 0;
		}

		public XmlElement ToXml(XmlDocument ownerDoc)
		{
			XmlElement ele = ownerDoc.CreateElement("loc");
			ele.SetAttribute("id", Id.ToString());
			ele.SetAttribute("name", Name);
			ele.SetAttribute("type", Type.ToString());
			ele.SetAttribute("NS", Coords.NS.ToString());
			ele.SetAttribute("EW", Coords.EW.ToString());
			if (HasExitCoords)
			{
				ele.SetAttribute("exitNS", ExitCoords.NS.ToString());
				ele.SetAttribute("exitEW", ExitCoords.EW.ToString());
				if (!UseInRouteFinding)
					ele.SetAttribute("use", UseInRouteFinding.ToString());
			}
			if (DungeonId != 0)
			{
				ele.SetAttribute("dungeonId", DungeonId.ToString());
			}
			if (IsCustomized)
			{
				ele.SetAttribute("customized", IsCustomized.ToString());
			}
			if (HasSpecializedIcon)
			{
				ele.SetAttribute("icon", GetIcon(false).ToString("X8"));
			}

			ele.InnerText = Notes;
			return ele;
		}

		public static Location FromXml(XmlElement locEle)
		{
			return FromXml(locEle, false);
		}

		public static Location FromXml(XmlElement locEle, bool useInternalId)
		{
			int id = useInternalId ? GetNextInternalId() : Convert.ToInt32(locEle.GetAttribute("id"));
			string name = locEle.GetAttribute("name");
			LocationType type = (LocationType)Enum.Parse(typeof(LocationType), locEle.GetAttribute("type"));
			Coordinates coords = new Coordinates(
				Convert.ToDouble(locEle.GetAttribute("NS")),
				Convert.ToDouble(locEle.GetAttribute("EW")));
			Coordinates exitCoords = Coordinates.NO_COORDINATES;
			if (locEle.HasAttribute("exitNS"))
			{
				exitCoords = new Coordinates(
					Convert.ToDouble(locEle.GetAttribute("exitNS")),
					Convert.ToDouble(locEle.GetAttribute("exitEW")));
			}
			int dungeonId = 0;
			if (locEle.HasAttribute("dungeonId"))
			{
				dungeonId = Convert.ToInt32(locEle.GetAttribute("dungeonId"));
			}
			string notes = locEle.InnerText;
			Location loc = new Location(id, name, type, coords, notes, dungeonId, exitCoords);
			if (locEle.HasAttribute("customized"))
			{
				loc.IsCustomized = Convert.ToBoolean(locEle.GetAttribute("customized"));
			}
			if (locEle.HasAttribute("use"))
			{
				loc.UseInRouteFinding = Convert.ToBoolean(locEle.GetAttribute("use"));
			}
			int icon;
			if (locEle.HasAttribute("icon") && int.TryParse(locEle.GetAttribute("icon"), NumberStyles.HexNumber, null, out icon))
			{
				loc.Icon = icon;
			}
			return loc;
		}

		public override string ToString() { return Name + " [" + Coords + "]"; }

		public static bool operator ==(Location a, Location b)
		{
			if (object.Equals(a, null))
				return object.Equals(b, null);
			else
				return a.Equals(b);
		}

		public static bool operator !=(Location a, Location b)
		{
			return !(a == b);
		}

		public override bool Equals(object obj)
		{
			if (obj is Location)
				return ((Location)obj).Id == Id;
			return false;
		}

		public bool Equals(Location x, Location y)
		{
			return x == y;
		}

		public override int GetHashCode()
		{
			return Id;
		}

		public int GetHashCode(Location loc)
		{
			if (loc == null)
				return 0;
			return loc.GetHashCode();
		}

		public int CompareTo(Location other)
		{
			if (other == null)
				return 1;
			int result = StringComparer.OrdinalIgnoreCase.Compare(this.Name, other.Name);
			if (result != 0)
				return result;
			return this.Id - other.Id;
		}

		private static Dictionary<string, LocationType> strToLocWarcry;
		private static Dictionary<LocationType, LocationTypeInfo> msLocationTypeInfo;
		//private static Dictionary<LocationType, int> locationIcons;
		private static readonly int FavoriteIcon;

		public static int GetNextCustomId()
		{
			return checked(mNextCustomId++);
		}

		public static int GetNextInternalId()
		{
			return checked(mNextInternalId--);
		}

		public static Location FromXmlWarcry(XmlElement locEle)
		{
			// Lazily create the strToLocWarcry dictionary
			if (strToLocWarcry == null)
			{
				strToLocWarcry = new Dictionary<string, LocationType>(StringComparer.OrdinalIgnoreCase);
				strToLocWarcry.Add("Allegiance Hall", LocationType.AllegianceHall);
				strToLocWarcry.Add("Bindstone", LocationType.Bindstone);
				strToLocWarcry.Add("Dungeon", LocationType.Dungeon);
				strToLocWarcry.Add("Landmark", LocationType.Landmark);
				strToLocWarcry.Add("Lifestone", LocationType.Lifestone);
				strToLocWarcry.Add("NPC", LocationType.NPC);
				strToLocWarcry.Add("Outpost", LocationType.Outpost);
				strToLocWarcry.Add("Portal Hub", LocationType.PortalHub);
				strToLocWarcry.Add("Settlement Portal", LocationType.SettlementPortal);
				strToLocWarcry.Add("Town", LocationType.Town);
				strToLocWarcry.Add("Town Portal", LocationType.TownPortal);
				strToLocWarcry.Add("Underground Portal", LocationType.UndergroundPortal);
				strToLocWarcry.Add("Vendor", LocationType.Vendor);
				strToLocWarcry.Add("Village", LocationType.Village);
				strToLocWarcry.Add("Wilderness Portal", LocationType.WildernessPortal);
				// Not in XML:
				strToLocWarcry.Add("Any", LocationType.Any);
				strToLocWarcry.Add("Portal", LocationType.Portal);
				strToLocWarcry.Add("Start Point", LocationType._StartPoint);
				strToLocWarcry.Add("End Point", LocationType._EndPoint);
				strToLocWarcry.Add("Custom", LocationType.Custom);
			}

			double NS = 0, EW = 0, exitNS = 0, exitEW = 0;
			string name = "";
			int id = -1, dungeonId = 0;
			LocationType type = LocationType._Unknown;
			string notes = "";
			string restrictions = "";
			bool retired = false;
			Coordinates exitCoords = Coordinates.NO_COORDINATES;

			foreach (XmlElement field in locEle.ChildNodes)
			{
				switch (field.Name)
				{
					case "id":
						id = Convert.ToInt32(field.InnerText);
						break;
					case "name":
						name = field.InnerText.Trim();
						break;
					case "type":
						if (!strToLocWarcry.TryGetValue(field.InnerText, out type))
							type = LocationType._Unknown;
						break;
					case "latitude":
						// NOTE: North is negative in CoD atlas
						NS = -Convert.ToDouble(field.InnerText);
						break;
					case "longitude":
						EW = Convert.ToDouble(field.InnerText);
						break;
					case "arrival_latitude":
						if (field.InnerText != "")
							// NOTE: North is negative in CoD atlas
							exitNS = -Convert.ToDouble(field.InnerText);
						break;
					case "arrival_longitude":
						if (field.InnerText != "")
							exitEW = Convert.ToDouble(field.InnerText);
						break;
					case "dungeon_id":
						// TryParse will set dungeonId to 0 if the number can't be parsed
						int.TryParse(field.InnerText, NumberStyles.HexNumber, null, out dungeonId);
						break;
					case "description":
						notes = field.InnerText.Trim();
						break;
					case "restrictions":
						restrictions = field.InnerText.Replace("<br>", "\n").Trim();
						string restrictionsLcase = restrictions.ToLower();
						if (restrictions.Equals("None", StringComparison.OrdinalIgnoreCase)
							|| restrictions.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
						{
							restrictions = "";
						}
						break;
					case "retired":
						retired = field.InnerText == "Y" || field.InnerText == "y";
						break;
				}
			}

			if (id < 0)
			{
				throw new ArgumentException("Location '" + name + "' has an invalid ID: " + id);
			}

			if (retired)
				notes = "THIS LOCATION IS RETIRED\n" + notes;
			if (restrictions.Length > 0)
			{
				if (!restrictions.StartsWith("Purchase Item"))
					restrictions = "Restrictions: " + restrictions;
				notes += "\n\n" + restrictions;
			}
			notes = HtmlTagsRegex.Replace(notes, "");
			notes = notes.Replace("&lt;", "<").Replace("&gt;", ">")
				.Replace("&quot;", "\"").Replace("&amp;", "&").Trim();

			Location ret;
			if (exitNS != 0 && exitEW != 0)
			{
				ret = new Location(id, name, type, new Coordinates(NS, EW), notes, dungeonId,
					new Coordinates(exitNS, exitEW));
			}
			else
			{
				ret = new Location(id, name, type, new Coordinates(NS, EW), notes, dungeonId);
			}

			ret.mIsRetired = retired;
			return ret;
		}

		private static Dictionary<string, LocationType> strToLocAcSpedia;
		private static Regex ACSpediaParse = new Regex(
			@"(\d+),(.*),(.*),[^,\d]*([\d\. ]+(?:N|S|n|s)[, ]+[\d\. ]+(?:E|W|e|w))[^,\d]*,"
			+ @"[^,]*,-?\d+,-?\d+,(?:[Ff]alse|[Tt]rue),<level>(.*)</level><exit>(.*)</exit>(.*)",
			RegexOptions.Multiline | RegexOptions.Compiled);

		private struct ACSpediaParseIndex
		{
			public const int ID = 1, Name = 2, Type = 3, Coords = 4, Level = 5, ExitCoords = 6, Description = 7;
		}
		public static bool TryParseACSpedia(string locStr, out Location loc)
		{
			// Lazily create the strToLocAcSpedia dictionary
			if (strToLocAcSpedia == null)
			{
				strToLocAcSpedia = new Dictionary<string, LocationType>(StringComparer.OrdinalIgnoreCase);
				strToLocAcSpedia.Add("Community", LocationType.Village);
				strToLocAcSpedia.Add("Dungeon", LocationType.Dungeon);
				strToLocAcSpedia.Add("Interest", LocationType.Landmark);
				strToLocAcSpedia.Add("Lifestone", LocationType.Lifestone);
				strToLocAcSpedia.Add("NPC", LocationType.NPC);
				strToLocAcSpedia.Add("Portal", LocationType.Portal);
				strToLocAcSpedia.Add("Random Portal", LocationType.Portal);
				strToLocAcSpedia.Add("Shop", LocationType.Vendor);
				strToLocAcSpedia.Add("Town", LocationType.Town);
				// Not in XML:
				strToLocAcSpedia.Add("Any", LocationType.Any);
				strToLocAcSpedia.Add("Start Point", LocationType._StartPoint);
				strToLocAcSpedia.Add("End Point", LocationType._EndPoint);
				strToLocAcSpedia.Add("Custom", LocationType.Custom);
			}

			Match parsed = ACSpediaParse.Match(locStr);
			loc = Location.NO_LOCATION;
			if (!parsed.Success)
			{
				return false;
			}
			int id;
			if (!int.TryParse(parsed.Groups[ACSpediaParseIndex.ID].Value, out id))
			{
				return false;
			}
			string name = parsed.Groups[ACSpediaParseIndex.Name].Value.Trim();
			LocationType type;
			if (!strToLocAcSpedia.TryGetValue(parsed.Groups[ACSpediaParseIndex.Type].Value.Trim(), out type))
			{
				type = LocationType._Unknown;
			}
			if (type == LocationType.Town && name.Contains("Outpost") && !name.Contains("Danby"))
			{
				type = LocationType.Outpost;
			}
			if (type == LocationType.Landmark && (name.EndsWith("Bindstone") || name.EndsWith("bindstone")))
			{
				type = LocationType.Bindstone;
			}

			Coordinates coords, exitCoords;
			if (!Coordinates.TryParse(parsed.Groups[ACSpediaParseIndex.Coords].Value, out coords))
			{
				return false;
			}
			if (!Coordinates.TryParse(parsed.Groups[ACSpediaParseIndex.ExitCoords].Value, out exitCoords))
			{
				exitCoords = Coordinates.NO_COORDINATES;
			}

			string description = parsed.Groups[ACSpediaParseIndex.Description].Value;
			description = DestinationRegex.Replace(description, "");
			description = BrTagRegex.Replace(description, "\n");
			description = SignatureRegex.Replace(description, "\n$1\n");
			description = HtmlTagsRegex.Replace(description, "");
			description = description.Trim();

			if (parsed.Groups[ACSpediaParseIndex.Level].Value != "")
			{
				description += "\n\nLevel: " + parsed.Groups[ACSpediaParseIndex.Level].Value;
			}

			loc = new Location(id, name, type, coords, description, exitCoords);
			return true;
		}

		static Location()
		{
			msLocationTypeInfo = new Dictionary<LocationType, LocationTypeInfo>(24);
			msLocationTypeInfo[LocationType._Unknown] = new LocationTypeInfo("Unknown", AcIcons.QuestionMarkLever, DatabaseType.Both);
			msLocationTypeInfo[LocationType.Any] = new LocationTypeInfo("Any", AcIcons.QuestionMarkLever, DatabaseType.Both);
			msLocationTypeInfo[LocationType.AnyPortal] = new LocationTypeInfo("Any Portal", AcIcons.PortalRecall, DatabaseType.CrossroadsOfDereth);
			msLocationTypeInfo[LocationType.AllegianceHall] = new LocationTypeInfo("Allegiance Hall", AcIcons.LugianCrest, DatabaseType.CrossroadsOfDereth);
			msLocationTypeInfo[LocationType.Bindstone] = new LocationTypeInfo("Allegiance Bindstone", AcIcons.HouseStone, DatabaseType.Both);
			msLocationTypeInfo[LocationType.Dungeon] = new LocationTypeInfo("Dungeon", AcIcons.DungeonPortal, DatabaseType.Both);
			msLocationTypeInfo[LocationType.Landmark] = new LocationTypeInfo("Landmark", AcIcons.TreeAndPond, DatabaseType.Both);
			msLocationTypeInfo[LocationType.Lifestone] = new LocationTypeInfo("Lifestone", AcIcons.Lifestone, DatabaseType.Both);
			msLocationTypeInfo[LocationType.NPC] = new LocationTypeInfo("NPC", AcIcons.Person, DatabaseType.Both);
			msLocationTypeInfo[LocationType.Outpost] = new LocationTypeInfo("Outpost", AcIcons.OutpostSign, DatabaseType.Both);
			msLocationTypeInfo[LocationType.Portal] = new LocationTypeInfo("Portal", AcIcons.PortalRecall, DatabaseType.ACSpedia);
			msLocationTypeInfo[LocationType.PortalHub] = new LocationTypeInfo("Portal Hub", AcIcons.BluePortal, DatabaseType.CrossroadsOfDereth);
			msLocationTypeInfo[LocationType.SettlementPortal] = new LocationTypeInfo("Settlement Portal", AcIcons.PortalRecall, DatabaseType.CrossroadsOfDereth);
			msLocationTypeInfo[LocationType.Town] = new LocationTypeInfo("Town", AcIcons.CandethKeep, DatabaseType.Both);
			msLocationTypeInfo[LocationType.TownPortal] = new LocationTypeInfo("Town Portal", AcIcons.PortalRecall, DatabaseType.CrossroadsOfDereth);
			msLocationTypeInfo[LocationType.UndergroundPortal] = new LocationTypeInfo("Underground Portal", AcIcons.PortalRecall, DatabaseType.CrossroadsOfDereth);
			msLocationTypeInfo[LocationType.Vendor] = new LocationTypeInfo("Vendor", AcIcons.Person, DatabaseType.Both);
			msLocationTypeInfo[LocationType.Village] = new LocationTypeInfo("Settlement", AcIcons.Cottage1, DatabaseType.Both);
			msLocationTypeInfo[LocationType.WildernessPortal] = new LocationTypeInfo("Wilderness Portal", AcIcons.PortalRecall, DatabaseType.CrossroadsOfDereth);
			msLocationTypeInfo[LocationType.PortalDevice] = new LocationTypeInfo("Portal Device", AcIcons.BlackmirePortalDevice, DatabaseType.Neither);
			msLocationTypeInfo[LocationType._StartPoint] = new LocationTypeInfo("Start Point", AcIcons.AegisGreen, DatabaseType.Neither);
			msLocationTypeInfo[LocationType._EndPoint] = new LocationTypeInfo("End Point", AcIcons.AegisRed, DatabaseType.Neither);
			msLocationTypeInfo[LocationType.Custom] = new LocationTypeInfo("Custom", AcIcons.BlueGlobe, DatabaseType.Both);
			FavoriteIcon = AcIcons.HeartGoldFrills;
		}

		private static void InitLTI(LocationType type, string friendlyName, int icon, DatabaseType showForType)
		{
			msLocationTypeInfo[type] = new LocationTypeInfo(friendlyName, icon, showForType);
		}

		public static LocationTypeInfo LocationTypeInfo(LocationType type)
		{
			return msLocationTypeInfo[type];
		}

#if false
		public static int LocationTypeToIcon(LocationType type) {
			return locationIcons[type];
		}

		public static string LocationTypeToString(LocationType type) {
			switch (type) {
				case LocationType._Unknown:
					return "Unknown";
				case LocationType.AllegianceHall:
					return "Allegiance Hall";
				case LocationType.Bindstone:
					return "Allegiance Bindstone";
				case LocationType.PortalHub:
					return "Portal Hub";
				case LocationType.SettlementPortal:
					return "Settlement Portal";
				case LocationType.Village:
					return "Settlement";
				case LocationType.TownPortal:
					return "Town Portal";
				case LocationType.UndergroundPortal:
					return "Underground Portal";
				case LocationType.WildernessPortal:
					return "Wilderness Portal";
				case LocationType._StartPoint:
					return "Start Point";
				case LocationType._EndPoint:
					return "End Point";
				case LocationType.Custom:
					return "Custom";
				case LocationType.AnyPortal:
					return "Any Portal";
				default:
					return type.ToString();
			}
		}

#endif
	}
}
