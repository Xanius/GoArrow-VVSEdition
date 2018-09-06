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
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;

namespace GoArrow.RouteFinding
{
	public class LocationChangedEventArgs : EventArgs
	{
		private Location mLoc;
		public Location Loc { get { return mLoc; } }
		internal LocationChangedEventArgs(Location loc)
		{
			mLoc = loc;
		}
	}

	[Flags]
	public enum SearchField
	{
		Name = 0x1,
		Description = 0x2,
		Both = Name | Description,
	}

	[Flags]
	public enum DatabaseType
	{
		CrossroadsOfDereth = 0x1,
		ACSpedia = 0x2,
		Both = CrossroadsOfDereth | ACSpedia,
		Neither = 0x0,
	}

	public class LocationDatabase : IDisposable
	{
		private class CharComparerIgnoreCase : IComparer<char>, IEqualityComparer<char>
		{
			public int Compare(char x, char y) { return char.ToLower(x) - char.ToLower(y); }
			public bool Equals(char x, char y) { return char.ToLower(x) == char.ToLower(y); }
			public int GetHashCode(char c) { return char.ToLower(c); }
		}

		private Dictionary<int, Location> mLocations;
		private SortedList<char, List<Location>> mAlphaIndex;
		private List<Location> mFavorites;
		private List<Location> mPortalLocations;

		private string mSavePath;

		private bool mNeedsSave = false;
		private bool mDisposed = false;

		private DatabaseType mDatabaseType;
		private DateTime mLastUpdate = DateTime.MinValue;

		public event EventHandler<LocationChangedEventArgs> FavoritesListChanged;
		public event EventHandler<LocationChangedEventArgs> LocationAdded;
		public event EventHandler<LocationChangedEventArgs> LocationRemoved;

		public Location this[int id]
		{
			get { return mLocations[id]; }
		}

		public Dictionary<int, Location> Locations
		{
			get { return mLocations; }
		}

		public SortedList<char, List<Location>> AlphaIndex
		{
			get { return mAlphaIndex; }
		}

		public List<Location> Favorites
		{
			get { return mFavorites; }
		}

		public List<Location> PortalLocations
		{
			get { return mPortalLocations; }
		}

		public string SavePath
		{
			get { return mSavePath; }
			set { mSavePath = value; }
		}

		public bool NeedsSave
		{
			get { return mNeedsSave; }
			set { mNeedsSave = value; }
		}

		public bool Disposed
		{
			get { return mDisposed; }
		}

		public DatabaseType DatabaseType
		{
			get { return mDatabaseType; }
		}

		public DateTime LastUpdate
		{
			get { return mLastUpdate; }
		}

		public string LastUpdateString
		{
			get
			{
				if (mLastUpdate == DateTime.MinValue)
				{
					return "Unknown";
				}
				else
				{
					DateTime localTime = mLastUpdate.ToLocalTime();
					return localTime.ToShortDateString() + ", " + localTime.ToShortTimeString();
				}
			}
		}

		public LocationDatabase()
		{
			mLocations = new Dictionary<int, Location>();

			mAlphaIndex = new SortedList<char, List<Location>>(27, new CharComparerIgnoreCase());
			mAlphaIndex['#'] = new List<Location>();
			for (char i = 'A'; i <= 'Z'; i++)
				mAlphaIndex[i] = new List<Location>();

			mPortalLocations = new List<Location>();

			mFavorites = new List<Location>();

			mSavePath = Util.FullPath("locations.xml");
		}

		public LocationDatabase(DatabaseType type)
			: this()
		{
			mDatabaseType = type;
		}

		public LocationDatabase(XmlDocument locDoc)
			: this()
		{
			LoadLocationsXml(locDoc);
		}

		public LocationDatabase(string databaseFilePath)
			: this()
		{
			mSavePath = databaseFilePath;
			XmlDocument locDoc = new XmlDocument();
			locDoc.Load(databaseFilePath);
			LoadLocationsXml(locDoc);
		}

		public void Dispose()
		{
			if (mDisposed)
				return;

			if (NeedsSave)
				Save(SavePath);

			mDisposed = true;
		}

		private void LoadLocationsXml(XmlDocument locDoc)
		{
			mDatabaseType = DatabaseType.CrossroadsOfDereth;
			if (locDoc.DocumentElement.HasAttribute("type"))
			{
				try
				{
					mDatabaseType = (DatabaseType)Enum.Parse(typeof(DatabaseType), locDoc.DocumentElement.GetAttribute("type"));
				}
				catch { /* Ignore */ }
			}

			mLastUpdate = DateTime.MinValue;
			if (locDoc.DocumentElement.HasAttribute("updated"))
			{
				try
				{
					mLastUpdate = new DateTime(long.Parse(locDoc.DocumentElement.GetAttribute("updated")), DateTimeKind.Utc);
				}
				catch { /* Ignore */ }
			}

			foreach (XmlElement locNode in locDoc.DocumentElement.ChildNodes)
			{
				Location loc = Location.FromXml(locNode);

				if (!mLocations.ContainsKey(loc.Id))
				{
					mLocations[loc.Id] = loc;

					if (loc.HasExitCoords)
					{
						mPortalLocations.Add(loc);
					}

					GetAlphaIndex(loc.Name[0]).Add(loc);

					if (loc.IsFavorite)
					{
						mFavorites.Add(loc);
					}

					loc.IsFavoriteChanged -= Location_IsFavoriteChanged;
					loc.IsFavoriteChanged += new EventHandler(Location_IsFavoriteChanged);
				}
			}

			mPortalLocations.TrimExcess();

			foreach (List<Location> locList in mAlphaIndex.Values)
				locList.Sort();

			mFavorites.Sort();
		}

		public void Add(Location loc)
		{
			if (!mLocations.ContainsKey(loc.Id))
			{
				mLocations[loc.Id] = loc;

				List<Location> addTo = GetAlphaIndex(loc.Name[0]);
				int idx = addTo.BinarySearch(loc);
				if (idx < 0)
					addTo.Insert(~idx, loc);
				else
					addTo[idx] = loc;

				if (loc.IsFavorite)
				{
					idx = mFavorites.BinarySearch(loc);
					if (idx < 0)
						mFavorites.Insert(~idx, loc);
					else
						mFavorites[idx] = loc;
				}

				if (loc.HasExitCoords)
				{
					mPortalLocations.Remove(loc);
					mPortalLocations.Add(loc);
				}

				loc.IsFavoriteChanged -= Location_IsFavoriteChanged;
				loc.IsFavoriteChanged += new EventHandler(Location_IsFavoriteChanged);

				if (LocationAdded != null)
				{
					LocationAdded(this, new LocationChangedEventArgs(loc));
				}
			}
			else
			{
				throw new ArgumentException("Location already exists: " + loc, "loc");
			}
		}

		public void UpdatedNow()
		{
			mLastUpdate = DateTime.UtcNow;
			NeedsSave = true;
		}

		public bool Remove(int id)
		{
			Location loc;
			if (!mLocations.TryGetValue(id, out loc))
				return false;

			loc.IsFavoriteChanged -= Location_IsFavoriteChanged;

			mLocations.Remove(id);
			GetAlphaIndex(loc.Name[0]).Remove(loc);
			mFavorites.Remove(loc);
			mPortalLocations.Remove(loc);

			if (LocationRemoved != null)
			{
				LocationRemoved(this, new LocationChangedEventArgs(loc));
			}

			return true;
		}

		public bool Remove(Location loc)
		{
			loc.IsFavoriteChanged -= Location_IsFavoriteChanged;
			GetAlphaIndex(loc.Name[0]).Remove(loc);
			mFavorites.Remove(loc);
			mPortalLocations.Remove(loc);
			if (mLocations.Remove(loc.Id))
			{
				if (LocationRemoved != null)
				{
					LocationRemoved(this, new LocationChangedEventArgs(loc));
				}
				return true;
			}
			return false;
		}

		public bool Contains(int id)
		{
			return mLocations.ContainsKey(id);
		}

		public bool TryGet(int id, out Location loc)
		{
			if (!mLocations.TryGetValue(id, out loc))
			{
				loc = Location.NO_LOCATION;
				return false;
			}
			return true;
		}

		public bool TryGet(string exactName, out Location loc)
		{
			List<Location> lst = GetAlphaIndex(exactName[0]);
			int idx = BinarySearch(lst, exactName);
			loc = (idx >= 0) ? lst[idx] : Location.NO_LOCATION;
			return (idx >= 0);
		}

		public List<Location> GetAlphaIndex(char nameFirstLetter)
		{
			if (char.IsLetter(nameFirstLetter))
				return mAlphaIndex[nameFirstLetter];
			return mAlphaIndex['#'];
		}

		public List<Location> SearchNameBeginsWith(string nameBeginsWith)
		{
			return SearchNameBeginsWith(nameBeginsWith, LocationType.Any);
		}

		public List<Location> SearchNameBeginsWith(string nameBeginsWith, LocationType limitToType)
		{
			List<Location> foundLocations = new List<Location>();

			List<Location> lst = GetAlphaIndex(nameBeginsWith[0]);
			int i = BinarySearch(lst, nameBeginsWith);
			if (i < 0) { i = ~i; }

			for (; i < lst.Count; i++)
			{
				if (lst[i].Name.StartsWith(nameBeginsWith, StringComparison.OrdinalIgnoreCase))
				{
					if (lst[i].TypeMatches(limitToType))
						foundLocations.Add(lst[i]);
				}
				else
				{
					break;
				}
			}

			return foundLocations;
		}

		public int BinarySearch(List<Location> lst, string name)
		{
			int left = 0, right = lst.Count - 1, mid = 0;
			while (left <= right)
			{
				mid = (left + right) / 2;
				int cmp = StringComparer.OrdinalIgnoreCase.Compare(name, lst[mid].Name);
				if (cmp == 0)
					return mid;
				if (cmp < 0)
					right = mid - 1;
				else
					left = mid + 1;
			}

			if (mid < lst.Count && StringComparer.OrdinalIgnoreCase.Compare(name, lst[mid].Name) > 0)
				mid++;

			return ~mid;
		}

		public List<Location> Search(string fieldContains, SearchField field)
		{
			return Search(fieldContains, field, LocationType.Any);
		}

		public List<Location> Search(string fieldContains, SearchField field, LocationType limitToType)
		{
			List<Location> foundLocations = new List<Location>();

			fieldContains = fieldContains.ToLower();
			foreach (List<Location> locList in AlphaIndex.Values)
			{
				foreach (Location loc in locList)
				{
					if (!loc.TypeMatches(limitToType))
						continue;

					string searchField;
					switch (field)
					{
						case SearchField.Name:
							searchField = loc.Name.ToLower();
							break;
						case SearchField.Description:
							searchField = loc.Notes.ToLower();
							break;
						case SearchField.Both:
							searchField = (loc.Name + loc.Notes).ToLower();
							break;
						default:
							searchField = loc.Name.ToLower();
							break;
					}
					if (searchField.Contains(fieldContains))
					{
						foundLocations.Add(loc);
					}
				}
			}

			return foundLocations;
		}

		public List<Location> Search(Coordinates nearThis, double maxDistance)
		{
			return Search(nearThis, maxDistance, LocationType.Any);
		}

		public List<Location> Search(Coordinates nearThis, double maxDistance, LocationType limitToType)
		{
			List<Location> foundLocations = new List<Location>();

			foreach (List<Location> locList in AlphaIndex.Values)
			{
				foreach (Location loc in locList)
				{
					if (loc.TypeMatches(limitToType) && loc.Coords.DistanceTo(nearThis) <= maxDistance)
					{
						foundLocations.Add(loc);
					}
				}
			}

			return foundLocations;
		}

		public List<Location> Search(string fieldContains, SearchField field,
				Coordinates nearThis, double maxDistance)
		{
			return Search(fieldContains, field, nearThis, maxDistance, LocationType.Any);
		}

		public List<Location> Search(string fieldContains, SearchField field,
				Coordinates nearThis, double maxDistance, LocationType limitToType)
		{
			List<Location> foundLocations = new List<Location>();

			fieldContains = fieldContains.ToLower();
			foreach (List<Location> locList in AlphaIndex.Values)
			{
				foreach (Location loc in locList)
				{
					if (!loc.TypeMatches(limitToType) || loc.Coords.DistanceTo(nearThis) > maxDistance)
						continue;

					string searchField;
					switch (field)
					{
						case SearchField.Name:
							searchField = loc.Name.ToLower();
							break;
						case SearchField.Description:
							searchField = loc.Notes.ToLower();
							break;
						case SearchField.Both:
							searchField = (loc.Name + loc.Notes).ToLower();
							break;
						default:
							searchField = loc.Name.ToLower();
							break;
					}
					if (searchField.Contains(fieldContains))
					{
						foundLocations.Add(loc);
					}
				}
			}

			return foundLocations;
		}

		public Location GetLocationAt(Coordinates coords)
		{
			List<Location> results = Search(coords, 0.049);
			if (results.Count == 1)
			{
				return results[0];
			}
			else if (results.Count > 0)
			{
				foreach (Location loc in results)
				{
					if (loc.Type == LocationType.PortalHub)
						return loc;
				}
				return results[0];
			}
			return null;
		}

		public void Save(string path)
		{
			XmlDocument locDoc = new XmlDocument();
			locDoc.AppendChild(locDoc.CreateElement("locations"));
			locDoc.DocumentElement.SetAttribute("type", DatabaseType.ToString());
			locDoc.DocumentElement.SetAttribute("updated", mLastUpdate.Ticks.ToString());

			foreach (Location loc in Locations.Values)
			{
				locDoc.DocumentElement.AppendChild(loc.ToXml(locDoc));
			}

			Util.SaveXml(locDoc, path);
			NeedsSave = false;
		}

		private void Location_IsCustomizedChanged(object sender, EventArgs e)
		{
			NeedsSave = true;
		}

		private void Location_IsFavoriteChanged(object sender, EventArgs e)
		{
			Location loc = (Location)sender;
			if (loc.IsFavorite)
			{
				if (!mFavorites.Contains(loc))
				{
					int idx = mFavorites.BinarySearch(loc);
					if (idx < 0)
						mFavorites.Insert(~idx, loc);
					else
						mFavorites[idx] = loc;
					NeedsSave = true;
					if (FavoritesListChanged != null)
						FavoritesListChanged(this, new LocationChangedEventArgs(loc));
				}
			}
			else
			{
				if (mFavorites.Remove(loc))
				{
					NeedsSave = true;
					if (FavoritesListChanged != null)
						FavoritesListChanged(this, new LocationChangedEventArgs(loc));
				}
			}
		}
	}
}
