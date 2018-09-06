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

using NumberStyles = System.Globalization.NumberStyles;

namespace GoArrow.RouteFinding
{
	public class PortalDevice
	{
		public static double MansionRunDistance = 1.0;

		private readonly int mIcon;
		private readonly string mName;
		private readonly Location mInfoLocation; // Location used for info about this device
		private readonly List<Location> mDestinations;

		private bool mEnabled;
		private Coordinates mCoords;

		private int mRow;

		public event EventHandler CoordsChanged;

		public static bool TryLoadXmlDefinition(XmlElement ele, out PortalDevice device)
		{
			device = null;
			int icon;
			string name;
			List<Location> destinations = new List<Location>(3);

			if (!ele.HasAttribute("name")
					|| !int.TryParse(ele.GetAttribute("icon"), NumberStyles.HexNumber, null, out icon))
			{
				return false;
			}
			name = ele.GetAttribute("name");

			string description = "";
			XmlElement descriptionEle = ele.SelectSingleNode("description") as XmlElement;
			if (descriptionEle != null)
				description = descriptionEle.InnerText.Trim();

			Location infoLocation = new Location(Location.GetNextInternalId(), name,
				LocationType.PortalDevice, Coordinates.NO_COORDINATES, description);
			infoLocation.Icon = icon;

			foreach (XmlElement destEle in ele.GetElementsByTagName("destination"))
			{
				Coordinates destCoords;
				string destName;
				if (!destEle.HasAttribute("name")
						|| !double.TryParse(destEle.GetAttribute("NS"), out destCoords.NS)
						|| !double.TryParse(destEle.GetAttribute("EW"), out destCoords.EW))
				{
					return false;
				}
				destName = destEle.GetAttribute("name");
				Location dest = new Location(Location.GetNextInternalId(), destName,
					LocationType.PortalDevice, Coordinates.NO_COORDINATES, description, destCoords);
				dest.Icon = icon;
				destinations.Add(dest);
			}
			if (destinations.Count == 1)
				infoLocation.ExitCoords = destinations[0].ExitCoords;

			device = new PortalDevice(icon, name, infoLocation, destinations);
			return true;
		}

		private PortalDevice(int icon, string name, Location infoLocation, List<Location> destinations)
		{
			mIcon = icon;
			mName = name;
			mInfoLocation = infoLocation;
			mDestinations = destinations;
			mEnabled = true;
			mCoords = Coordinates.NO_COORDINATES;
		}

		public void LoadSettingsXml(XmlElement monarchNode)
		{
			XmlElement ele = monarchNode.SelectSingleNode("device[@name=\"" + Name + "\"]") as XmlElement;
			if (ele != null)
			{
				double ns, ew;
				if (double.TryParse(ele.GetAttribute("NS"), out ns) &&
					double.TryParse(ele.GetAttribute("EW"), out ew))
				{
					Coords = new Coordinates(ns, ew);
				}

				bool tmpBool;
				if (bool.TryParse(ele.GetAttribute("enabled"), out tmpBool))
				{
					Enabled = tmpBool;
				}
			}
		}

		public void SaveSettingsXml(XmlElement monarchNode)
		{
			XmlElement ele = monarchNode.OwnerDocument.CreateElement("device");
			monarchNode.AppendChild(ele);

			ele.SetAttribute("name", Name);
			ele.SetAttribute("NS", Coords.NS.ToString());
			ele.SetAttribute("EW", Coords.EW.ToString());
			ele.SetAttribute("enabled", Enabled.ToString());
		}

		public int Icon
		{
			get { return mIcon; }
		}

		public string Name
		{
			get { return mName; }
		}

		public Location InfoLocation
		{
			get { return mInfoLocation; }
		}

		public List<Location> Destinations
		{
			get { return mDestinations; }
		}

		public int Row
		{
			get { return mRow; }
			set { mRow = value; }
		}

		public bool Enabled
		{
			get { return mEnabled; }
			set { mEnabled = value; }
		}

		public Coordinates Coords
		{
			get { return mCoords; }
			set
			{
				if (mCoords != value)
				{
					mCoords = value;
					if (CoordsChanged != null)
					{
						CoordsChanged(this, EventArgs.Empty);
					}
				}
				InfoLocation.Coords = value;
				foreach (Location dest in mDestinations)
				{
					dest.Coords = value;
				}
			}
		}

		public bool Detected
		{
			get { return Coords != Coordinates.NO_COORDINATES; }
		}

		public double RunDistance
		{
			get
			{
				return mDestinations.Count * (RouteFinder.PortalWeight + MansionRunDistance)
					- MansionRunDistance;
			}
		}
	}
}
