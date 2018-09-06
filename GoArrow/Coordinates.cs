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
using System.Text.RegularExpressions;

namespace GoArrow
{
	public struct Coordinates
	{
		public static readonly Coordinates NO_COORDINATES = new Coordinates(double.NaN, double.NaN);
		public const string NO_COORDINATES_STRING = "<None>";
		public const string UNKNOWN_COORDINATES_STRING = "<Unknown>";

		// This is the only data stored by each instance of the struct
		public double NS, EW;

		public Coordinates(double NS, double EW)
		{
			this.NS = NS;
			this.EW = EW;
		}

		public Coordinates(int landcell, double yOffset, double xOffset)
		{
			this.NS = LandblockToNS(landcell, yOffset);
			this.EW = LandblockToEW(landcell, xOffset);
		}

		public Coordinates(int landcell, double yOffset, double xOffset, int precision)
		{
			this.NS = Math.Round(LandblockToNS(landcell, yOffset), precision);
			this.EW = Math.Round(LandblockToEW(landcell, xOffset), precision);
		}

		public Coordinates(Decal.Adapter.Wrappers.CoordsObject obj)
		{
			this.NS = obj.NorthSouth;
			this.EW = obj.EastWest;
		}

		public Coordinates(Decal.Adapter.Wrappers.CoordsObject obj, int precision)
		{
			this.NS = Math.Round(obj.NorthSouth, precision);
			this.EW = Math.Round(obj.EastWest, precision);
		}

		public static explicit operator Decal.Adapter.Wrappers.CoordsObject(Coordinates coords)
		{
			return new Decal.Adapter.Wrappers.CoordsObject(coords.NS, coords.EW);
		}

		public double AngleTo(Coordinates dest)
		{
			return Math.Atan2(dest.EW - EW, dest.NS - NS);
		}

		public double DistanceTo(Coordinates dest)
		{
			double x = dest.EW - EW;
			double y = dest.NS - NS;
			if (double.IsNaN(x + y))
				return double.PositiveInfinity;
			return Math.Sqrt(x * x + y * y);
		}

		public Coordinates RelativeTo(Coordinates dest)
		{
			return new Coordinates(dest.NS - NS, dest.EW - EW);
		}

		public static Coordinates Round(Coordinates coords, int precision)
		{
			return new Coordinates(Math.Round(coords.NS, precision), Math.Round(coords.EW, precision));
		}

		// Latitude
		public static double LandblockToNS(int landcell, double yOffset)
		{
			uint l = (uint)((landcell & 0x00FF0000) / 0x2000);
			return (l + yOffset / 24.0 - 1019.5) / 10.0;
		}

		// Longitude
		public static double LandblockToEW(int landcell, double xOffset)
		{
			uint l = (uint)((landcell & 0xFF000000) / 0x200000);
			return (l + xOffset / 24.0 - 1019.5) / 10.0;
		}

		#region String Parsing
		public static bool TryParse(string parseString, out Coordinates coords)
		{
			if (parseString == null)
			{
				coords = Coordinates.NO_COORDINATES;
				return false;
			}
			return FromRegexMatch(FindCoords(parseString), out coords);
		}

		public static bool TryParse(string parseString, bool allowNoCoords, out Coordinates coords)
		{
			if (allowNoCoords && parseString == "" || parseString == NO_COORDINATES_STRING
				|| parseString == UNKNOWN_COORDINATES_STRING)
			{
				coords = NO_COORDINATES;
				return true;
			}
			return FromRegexMatch(FindCoords(parseString), out coords);
		}

		public static bool FromRegexMatch(Match m, out Coordinates coords)
		{
			coords = NO_COORDINATES;
			try
			{
				if (!m.Success)
					return false;

				coords.NS = double.Parse(m.Groups["NSval"].Value, System.Globalization.CultureInfo.InvariantCulture.NumberFormat);
				if (m.Groups["NSchr"].Value.ToLower() == "s") { coords.NS = -coords.NS; }

				coords.EW = double.Parse(m.Groups["EWval"].Value);
				if (m.Groups["EWchr"].Value.ToLower() == "w") { coords.EW = -coords.EW; }
			}
			catch (Exception ex)
			{
				Util.HandleException(ex);
				return false;
			}
			return true;
		}

		// regExDouble matches a 0-3 digit decimal number with 
		// optionally 1-4 digits after the decimal place (ex: "139", "32.0132", ".16")
		private const string regExDouble = @"(\d{1,3}(\.\d{1,4})?)|(\.\d{1,4})";

		// Create a regular expression that's compiled at startup, 
		// rather than each time it's needed
		private static Regex CoordSearchRegex = new Regex(
			  @"(?<NSval>" + regExDouble + @")\s*(?<NSchr>[ns])" + @"[;/,\s]{0,4}\s*"
			+ @"(?<EWval>" + regExDouble + @")\s*(?<EWchr>[ew])",
			RegexOptions.Compiled | RegexOptions.IgnoreCase);

		/// <summary>
		/// Searches through the string for coordinates.
		/// </summary>
		/// <param name="parseString">The string to search through</param>
		/// <returns>A regular expression MatchCollection containing 0 or more matches.
		/// Each match will have four named groups:
		///	<para>"NSval" = The double value of the N/S part of the coordinate.</para>
		///	<para>"NSchr" = Either "n" or "s" (may be upper case).</para>
		/// <para>"EWval" = The double value of the E/W part of the coordinate.</para>
		/// <para>"EWchr" = Either "e" or "w" (may be upper case).</para></returns>
		public static MatchCollection FindAllCoords(string parseString)
		{
			return CoordSearchRegex.Matches(parseString);
		}

		public static Match FindCoords(string parseString)
		{
			return CoordSearchRegex.Match(parseString);
		}

#if false
		internal static WorldObject player = null;

		public Coordinates(string parseString) {
			try {
				Match m = FindCoords(parseString);
				if (m.Success) {
					NS = double.Parse(m.Groups["NSval"].Value);
					if (m.Groups["NSchr"].Value.ToLower() == "s") { NS = -NS; }

					EW = double.Parse(m.Groups["EWval"].Value);
					if (m.Groups["EWchr"].Value.ToLower() == "w") { EW = -EW; }
				}
				else {
					/* 
					 * No regex matches.  Parse using old, less-reliable parsing
					 * code that doesn't require N/S, E/W specified.
					 */

					// Get the player's current coordniates
					double playerNS = 0, playerEW = 0;
					if (player != null)
						player.Coordinates(ref playerNS, ref playerEW);

					int i = 0;
					parseString = parseString.ToLower();
					NS = OldParseHelper(ref parseString, ref i, 'n', 's', playerNS < 0);
					EW = OldParseHelper(ref parseString, ref i, 'e', 'w', playerEW < 0);
				}
			}
			catch (Exception ex) {
				NS = EW = 0;
				Util.HandleException(ex);
			}
		}

		public Coordinates(Match coordsSearchMatch) {
			if (coordsSearchMatch.Success) {
				NS = double.Parse(coordsSearchMatch.Groups["NSval"].Value);
				if (coordsSearchMatch.Groups["NSchr"].Value.ToLower() == "s") { NS = -NS; }

				EW = double.Parse(coordsSearchMatch.Groups["EWval"].Value);
				if (coordsSearchMatch.Groups["EWchr"].Value.ToLower() == "w") { EW = -EW; }
			}
			else {
				NS = 0;
				EW = 0;
			}
		}

		private static double OldParseHelper(ref string s, ref int i, char chNE, char chSW, bool playerIsSW) {
			double retVal = 0;
			for (; i < s.Length; i++) {
				// Check if the first char of the string is a number
				// (or second if first is a decimal point)
				int idx = (s[i] == '.' && s.Length > 1) ? i + 1 : i;
				if (s[idx] >= '0' && s[idx] <= '9') {
					// Find the length of the current number
					int len = s.Length - i;
					bool decimalPointFound = false;
					for (int j = i; j < s.Length; j++) {
						if (s[j] == '.' && !decimalPointFound)
							decimalPointFound = true;
						else if (!(s[j] >= '0' && s[j] <= '9')) {
							len = j - i;
							break;
						}
					}
					// Parse the number part
					retVal = double.Parse(s.Substring(i, len));
					// Advance i past the number
					i = i + len;
					// Flip sign if south or west
					if (i >= s.Length || (s[i] != chNE && s[i] != chSW)) {
						if (playerIsSW)
							retVal = -retVal;
					}
					else if (s[i] == chSW)
						retVal = -retVal;

					break;
				}
			}
			return retVal;
		}
#endif
		#endregion

		public override string ToString()
		{
			return ToString(false);
		}

		public string ToString(bool useUnknownString)
		{
			if (double.IsNaN(NS) || double.IsNaN(EW))
				return useUnknownString ? UNKNOWN_COORDINATES_STRING : NO_COORDINATES_STRING;

			// If one of the numbers has more than 1 decimal place of precision, 
			// write both with 2 decimal places.  Otherwise, just use 1.
			double ns10 = NS * 10, ew10 = EW * 10;
			if (Math.Floor(ns10) != ns10 || Math.Floor(ew10) != ew10)
				return ToString("0.00", useUnknownString);
			else
				return ToString("0.0", useUnknownString);
		}

		public string ToString(string numberFormat)
		{
			return ToString(numberFormat, false);
		}

		public string ToString(string numberFormat, bool useUnknownString)
		{
			if (double.IsNaN(NS) || double.IsNaN(EW))
				return useUnknownString ? UNKNOWN_COORDINATES_STRING : NO_COORDINATES_STRING;

			return Math.Abs(NS).ToString(numberFormat) + (NS >= 0 ? "N" : "S") + ", "
				 + Math.Abs(EW).ToString(numberFormat) + (EW >= 0 ? "E" : "W");
		}

		public static bool operator ==(Coordinates a, Coordinates b)
		{
			return (a.NS == b.NS && a.EW == b.EW)
				|| (double.IsNaN(a.NS) && double.IsNaN(b.NS));
		}

		public static bool operator !=(Coordinates a, Coordinates b)
		{
			return !(a == b);
		}

		public override bool Equals(object obj)
		{
			if (obj is Coordinates)
			{
				Coordinates c = (Coordinates)obj;
				return (this == c);
			}
			return false;
		}

		public override int GetHashCode()
		{
			if (double.IsNaN(NS) || double.IsNaN(EW))
				return int.MaxValue;
			return (int)(NS * 1000000) ^ (int)(EW * 10000);
		}
	}
}
